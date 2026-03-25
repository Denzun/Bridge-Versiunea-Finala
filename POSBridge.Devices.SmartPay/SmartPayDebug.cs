namespace POSBridge.Devices.SmartPay;

public static class SmartPayDebug
{
    private static readonly object _logLock = new();
    private static readonly List<string> _logBuffer = new();
    private static string _logPath = "";
    private static bool _initialized = false;
    
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        
        // Use Desktop - always writable
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        _logPath = Path.Combine(desktop, "POSBridge-SmartPay-Debug.txt");
        
        try
        {
            File.WriteAllText(_logPath, $"=== SmartPay Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===" + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // If desktop fails, try temp
            _logPath = Path.Combine(Path.GetTempPath(), "POSBridge-SmartPay-Debug.txt");
            try 
            { 
                File.WriteAllText(_logPath, $"=== SmartPay Log Started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===" + Environment.NewLine);
            }
            catch 
            {
                _logPath = "C:\\POSBridge-SmartPay-Debug.txt";
                try { File.WriteAllText(_logPath, "Log started"); } catch { }
            }
        }
        
        _initialized = true;
    }
    
    public static void Log(string message)
    {
        EnsureInitialized();
        
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string logLine = $"[{timestamp}] {message}";
        
        lock (_logLock)
        {
            _logBuffer.Add(logLine);
        }
        
        // Write to file immediately
        try 
        { 
            File.AppendAllText(_logPath, logLine + Environment.NewLine);
        }
        catch { }
    }

    public static string GetLogContents()
    {
        lock (_logLock)
        {
            return string.Join("\n", _logBuffer);
        }
    }

    public static void Clear()
    {
        EnsureInitialized();
        lock (_logLock)
        {
            _logBuffer.Clear();
            try 
            { 
                File.WriteAllText(_logPath, $"=== SmartPay Log {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===" + Environment.NewLine);
            }
            catch { }
        }
    }
    
    public static void FlushToFile()
    {
        EnsureInitialized();
        try
        {
            File.WriteAllText(_logPath, GetLogContents());
        }
        catch { }
    }
    
    public static string GetLogFilePath()
    {
        EnsureInitialized();
        return _logPath;
    }
}
