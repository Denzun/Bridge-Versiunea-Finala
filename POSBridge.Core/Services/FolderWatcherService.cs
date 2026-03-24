using System.Collections.Concurrent;
using System.Collections.Generic;
using POSBridge.Core.Models;

namespace POSBridge.Core.Services;

/// <summary>
/// Service that monitors the "Bon" folder for new receipt files.
/// Processes files serially (one at a time) to avoid concurrent access to fiscal device.
/// </summary>
public class FolderWatcherService : IDisposable
{
    private readonly string _watchFolder;
    private readonly string _errorFolder;
    private readonly string _historyFolder;
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _processSemaphore = new(1, 1);
    private readonly ConcurrentQueue<string> _fileQueue = new();
    private bool _isProcessing;
    private bool _disposed;

    public event EventHandler<BonProcessingResult>? BonProcessed;
    public event EventHandler<string>? LogMessage;

    public FolderWatcherService(string watchFolder)
    {
        _watchFolder = watchFolder;
        _errorFolder = Path.Combine(_watchFolder, "Erori");
        _historyFolder = Path.Combine(_watchFolder, "Istoric");

        // Create folders if they don't exist
        Directory.CreateDirectory(_watchFolder);
        Directory.CreateDirectory(_errorFolder);
        Directory.CreateDirectory(_historyFolder);
    }

    /// <summary>
    /// Starts monitoring the folder.
    /// </summary>
    public void Start()
    {
        if (_watcher != null)
            return;

        Log("Starting folder watcher...");

        _watcher = new FileSystemWatcher(_watchFolder)
        {
            Filter = "*.txt",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Changed += OnFileChanged;

        Log($"Watching folder: {_watchFolder}");

        // Process any existing files
        ProcessExistingFiles();
    }

    /// <summary>
    /// Stops monitoring the folder.
    /// </summary>
    public void Stop()
    {
        if (_watcher == null)
            return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Changed -= OnFileChanged;
        _watcher.Dispose();
        _watcher = null;

        Log("Folder watcher stopped.");
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Log($"New file detected: {Path.GetFileName(e.FullPath)}");
        if (ShouldIgnoreFile(e.FullPath))
        {
            Log($"Ignored file: {Path.GetFileName(e.FullPath)}");
            return;
        }
        EnqueueFile(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Ignore change events (we only process on Created)
    }

    private void EnqueueFile(string filePath)
    {
        if (ShouldIgnoreFile(filePath))
            return;

        _fileQueue.Enqueue(filePath);
        
        if (!_isProcessing)
        {
            _ = Task.Run(ProcessQueueAsync);
        }
    }

    private async Task ProcessQueueAsync()
    {
        _isProcessing = true;

        try
        {
            while (_fileQueue.TryDequeue(out string? filePath))
            {
                await _processSemaphore.WaitAsync();
                
                try
                {
                    // Wait 500ms before processing (allow file to be fully written)
                    await Task.Delay(500);
                    
                    await ProcessFileAsync(filePath);
                }
                finally
                {
                    _processSemaphore.Release();
                }
            }
        }
        finally
        {
            _isProcessing = false;
        }
    }

    /// <summary>
    /// Attempts to read a file with retry logic for locked files.
    /// Tries up to 5 times with increasing delays: 500ms, 1000ms, 2000ms, 3000ms, 5000ms.
    /// </summary>
    private async Task<string> ReadFileWithRetryAsync(string filePath, string fileName)
    {
        int maxRetries = 5;
        int[] delayMs = { 500, 1000, 2000, 3000, 5000 };
        bool wasLocked = false;
        int emptyRetries = 0;
        const int maxEmptyRetries = 3;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Try to open the file exclusively to check if it's locked
                if (IsFileLocked(filePath))
                {
                    wasLocked = true;
                    if (attempt < maxRetries - 1)
                    {
                        Log($"File is locked: {fileName}, retry {attempt + 1}/{maxRetries} after {delayMs[attempt]}ms...");
                        await Task.Delay(delayMs[attempt]);
                        continue;
                    }
                    else
                    {
                        throw new IOException($"File remains locked after {maxRetries} attempts");
                    }
                }
                
                // File is not locked, read it
                string content = await File.ReadAllTextAsync(filePath);
                
                // If file was previously locked but is now empty, wait and retry
                // (the writing process may have unlocked it before finishing writing)
                if (wasLocked && string.IsNullOrWhiteSpace(content) && emptyRetries < maxEmptyRetries)
                {
                    emptyRetries++;
                    Log($"File was locked but is empty: {fileName}, retry {emptyRetries}/{maxEmptyRetries} after 1000ms...");
                    await Task.Delay(1000);
                    continue;
                }
                
                return content;
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                Log($"IOException reading {fileName}, retry {attempt + 1}/{maxRetries} after {delayMs[attempt]}ms: {ex.Message}");
                await Task.Delay(delayMs[attempt]);
            }
        }
        
        // Final attempt without catching
        return await File.ReadAllTextAsync(filePath);
    }

    /// <summary>
    /// Checks if a file is locked by another process.
    /// </summary>
    private bool IsFileLocked(string filePath)
    {
        try
        {
            using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // File opened successfully, it's not locked
            }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ProcessFileAsync(string filePath)
    {
        var startTime = DateTime.Now;
        string fileName = Path.GetFileName(filePath);

        try
        {
            Log($"Processing: {fileName}...");

            // Check if file still exists
            if (!File.Exists(filePath))
            {
                Log($"File no longer exists: {fileName}");
                return;
            }

            // Ask user whether to execute (if callback provided)
            if (ConfirmFileAsync != null)
            {
                bool shouldExecute = await ConfirmFileAsync(filePath);
                if (!shouldExecute)
                {
                    MoveToHistory(filePath, fileName);
                    Log($"Skipped by user: {fileName} -> Istoric");
                    return;
                }
            }

            // Read raw content for special commands with retry mechanism
            string rawContent = await ReadFileWithRetryAsync(filePath, fileName);
            string trimmed = rawContent.Trim();

            // Validate that file is not empty
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Log($"✗ ERROR: {fileName} is empty or contains only whitespace");
                var emptyResult = new BonProcessingResult
                {
                    Success = false,
                    FileName = fileName,
                    ErrorMessage = "File is empty or contains only whitespace",
                    ErrorCode = -1,
                    ProcessedAt = DateTime.Now,
                    ProcessingDuration = DateTime.Now - startTime
                };
                MoveToError(filePath, fileName, emptyResult);
                BonProcessed?.Invoke(this, emptyResult);
                return;
            }

            BonProcessingResult result;
            // Check for standalone special commands (X^, Z^, I^VALOARE^, O^VALOARE^)
            if (IsSpecialCommand(trimmed) && ProcessSpecialCommandAsync != null)
            {
                Log($"Detected special command: {trimmed}");
                result = await ProcessSpecialCommandAsync(trimmed);
            }
            else
            {
                // Parse file into commands
                ReceiptCommandFile commandFile = await Task.Run(() => ReceiptCommandParser.ParseFile(filePath));
                Log($"Parsed {commandFile.Commands.Count} commands from {fileName}");

                if (ProcessCommandFileAsync == null)
                    throw new InvalidOperationException("ProcessCommandFileAsync is not configured.");

                result = await ProcessCommandFileAsync(commandFile);
            }

            // Move file based on result
            if (result.Success)
            {
                MoveToHistory(filePath, fileName);
                Log($"✓ SUCCESS: {fileName} -> Istoric, Receipt #{result.ReceiptNumber} ({result.ProcessingDuration.TotalSeconds:F1}s)");
                WriteResponseFile(result);
            }
            else
            {
                MoveToError(filePath, fileName, result);
                Log($"✗ ERROR: {fileName} -> Code {result.ErrorCode}: {result.ErrorMessage}");
            }

            // Notify listeners
            BonProcessed?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            Log($"✗ EXCEPTION: {fileName} -> {ex.Message}");
            
            var result = new BonProcessingResult
            {
                Success = false,
                FileName = fileName,
                ErrorMessage = ex.Message,
                ProcessedAt = DateTime.Now,
                ProcessingDuration = DateTime.Now - startTime
            };

            MoveToError(filePath, fileName, result);
            BonProcessed?.Invoke(this, result);
        }
    }

    /// <summary>
    /// Delegate for processing command files - should be set by caller.
    /// </summary>
    public Func<ReceiptCommandFile, Task<BonProcessingResult>>? ProcessCommandFileAsync { get; set; }

    /// <summary>
    /// Delegate for processing special commands (e.g. X^ / Z^).
    /// </summary>
    public Func<string, Task<BonProcessingResult>>? ProcessSpecialCommandAsync { get; set; }

    /// <summary>
    /// Delegate for user confirmation before executing a file.
    /// </summary>
    public Func<string, Task<bool>>? ConfirmFileAsync { get; set; }

    private void MoveToError(string sourceFile, string fileName, BonProcessingResult result)
    {
        try
        {
            string destPath = Path.Combine(_errorFolder, fileName);
            
            // Add timestamp if file exists
            if (File.Exists(destPath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                destPath = Path.Combine(_errorFolder, $"{nameWithoutExt}_{timestamp}{ext}");
            }
            
            MoveFileWithRetry(sourceFile, destPath, fileName);

            // Create error log file
            string logPath = destPath + ".log";
            string logContent = $@"Error Log for: {fileName}
Processed At: {result.ProcessedAt:yyyy-MM-dd HH:mm:ss}
Duration: {result.ProcessingDuration.TotalSeconds:F2}s
Error Code: {result.ErrorCode}
Error Message: {result.ErrorMessage}
";
            File.WriteAllText(logPath, logContent);
        }
        catch (Exception ex)
        {
            Log($"Failed to move file to Erori: {ex.Message}");
        }
    }

    private void MoveToHistory(string sourceFile, string fileName)
    {
        try
        {
            string destPath = Path.Combine(_historyFolder, fileName);

            if (File.Exists(destPath))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                destPath = Path.Combine(_historyFolder, $"{nameWithoutExt}_{timestamp}{ext}");
            }

            MoveFileWithRetry(sourceFile, destPath, fileName);
        }
        catch (Exception ex)
        {
            Log($"Failed to move file to Istoric: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to move a file with retry logic for locked files.
    /// Tries up to 3 times with delays: 500ms, 1000ms, 2000ms.
    /// </summary>
    private void MoveFileWithRetry(string sourceFile, string destPath, string fileName)
    {
        int maxRetries = 3;
        int[] delayMs = { 500, 1000, 2000 };
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Move(sourceFile, destPath);
                return; // Success
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Log($"File move blocked for {fileName}, retry {attempt + 1}/{maxRetries} after {delayMs[attempt]}ms...");
                Thread.Sleep(delayMs[attempt]);
            }
        }
        
        // Final attempt without catching
        File.Move(sourceFile, destPath);
    }

    private void ProcessExistingFiles()
    {
        try
        {
            var existingFiles = Directory.GetFiles(_watchFolder, "*.txt");
            
            foreach (var file in existingFiles)
            {
                if (ShouldIgnoreFile(file))
                    continue;
                Log($"Found existing file: {Path.GetFileName(file)}");
                EnqueueFile(file);
            }
        }
        catch (Exception ex)
        {
            Log($"Error processing existing files: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    /// <summary>
    /// Scrie în Raspuns doar când bonul a fost tipărit corect (bon.txt cu BONOK=1).
    /// </summary>
    private void WriteResponseFile(BonProcessingResult result)
    {
        try
        {
            // Raspuns folder is at the same level as the Bon folder (sibling, not child)
            string? parentFolder = Path.GetDirectoryName(_watchFolder);
            if (string.IsNullOrEmpty(parentFolder))
                parentFolder = _watchFolder;

            string responseFolder = Path.Combine(parentFolder, "Raspuns");
            Directory.CreateDirectory(responseFolder);

            string bonPath = Path.Combine(responseFolder, "bon.txt");
            File.WriteAllText(bonPath, "BONOK=1");
        }
        catch (Exception ex)
        {
            Log($"Failed to write response file: {ex.Message}");
        }
    }

    private bool ShouldIgnoreFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);
        if (fileName.Equals("README.txt", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.StartsWith("README_", StringComparison.OrdinalIgnoreCase))
            return true;
        // Ignore bon.txt - often created empty by management software
        if (fileName.Equals("bon.txt", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Checks if the command is a standalone special command (X^, Z^, I^value^, O^value^).
    /// </summary>
    private bool IsSpecialCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var trimmed = command.Trim();

        // X^ and Z^ reports
        if (trimmed.Equals("X^", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Z^", StringComparison.OrdinalIgnoreCase))
            return true;

        // Cash In (I^value^) and Cash Out (O^value^)
        if (trimmed.StartsWith("I^", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("O^", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('^');
            // Must have format: I^value^ or O^value^
            return parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]);
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _processSemaphore.Dispose();
        _disposed = true;
    }
}
