using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using System.Diagnostics;
using POSBridge.Core.Services;

namespace POSBridge.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static Process? DudeProcess { get; set; }
    /// <summary>True if device is activated/licensed; false = demo mode.</summary>
    public static bool IsDeviceActivated { get; internal set; }
    private DeviceActivationService? _activationService;
    private string _deviceSerialNumber = string.Empty;
    private string _tenantCode = "demo";
    
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Global exception handler
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            var errorLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(errorLog, $"CRASH: {DateTime.Now}\n\n{ex?.ToString() ?? "Unknown error"}");
            System.Windows.MessageBox.Show($"Fatal error:\n\n{ex?.Message}\n\nSee crash.log for details", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        
        DispatcherUnhandledException += (sender, args) =>
        {
            var errorLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.WriteAllText(errorLog, $"UI CRASH: {DateTime.Now}\n\n{args.Exception}");
            System.Windows.MessageBox.Show($"UI error:\n\n{args.Exception.Message}\n\nSee crash.log for details", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        
        // Step 1: Check device activation before anything else
        WriteLog("=== POS Bridge Startup ===");
        WriteLog("Verificare activare dispozitiv...");
        
        var activationSuccess = await CheckDeviceActivationAsync();
        IsDeviceActivated = activationSuccess;
        
        if (!activationSuccess)
            WriteLog("Dispozitiv neactivat. Mod demo - 30 zile.");
        else
            WriteLog("Dispozitiv activat. Continuare pornire...");
        
        // Step 2: Launch DUDE if needed (only for Datecs devices)
        LaunchDudeIfNeeded();
        
        // Step 3: Show main window
        var mainWindow = new MainWindow();
        mainWindow.Show();
        
        // Step 4: Start periodic heartbeat
        StartHeartbeatTimer();
    }
    
    /// <summary>
    /// Checks if the device is activated via the activation service.
    /// Returns true if activated, false otherwise (shows payment window).
    /// </summary>
    private async Task<bool> CheckDeviceActivationAsync()
    {
        try
        {
            _activationService = new DeviceActivationService();
            
            // Get device serial number from hardware or settings
            _deviceSerialNumber = GetDeviceSerialNumber();
            _tenantCode = GetTenantCode();
            
            WriteLog($"Serial Number: {_deviceSerialNumber}");
            WriteLog($"Tenant Code: {_tenantCode}");
            
            // Check activation status on server
            var result = await _activationService.CheckActivationAsync(_deviceSerialNumber);
            
            // Device is enabled (activated)
            if (result.IsActivated)
            {
                WriteLog($"Dispozitiv activat: {result.Model} (Serie fiscala: {result.FiscalPrinterSeries})");
                IsDeviceActivated = true;
                return true;
            }
            
            // Device not registered - auto-register on first run
            if (result.NeedsRegistration)
            {
                WriteLog("Dispozitiv nou detectat. Inregistrare automata...");
                var (model, fiscalSeries) = GetDeviceModelAndSeriesFromSettings();
                
                var registerResult = await _activationService.RegisterDeviceAsync(
                    _deviceSerialNumber,
                    _tenantCode,
                    fiscalPrinterSeries: fiscalSeries,
                    model: model
                );
                
                WriteLog($"Rezultat inregistrare: {registerResult.Message}");
                
                if (registerResult.Status == "REGISTERED" || registerResult.Status == "ALREADY_REGISTERED")
                {
                    // Device registered but disabled by default - needs manual enabling
                    WriteLog("Dispozitiv inregistrat. Asteapta activare manuala din panoul de administrare.");
                    // Store info for UI display
                    IsDeviceActivated = false;
                    return false;
                }
            }
            
            // Device registered but not enabled, or other activation issues
            WriteLog($"Dispozitiv neactivat: {result.Message}");
            IsDeviceActivated = false;
            return false;
        }
        catch (Exception ex)
        {
            WriteLog($"Eroare verificare activare: {ex.Message}");
            // Continue with local mode (demo) if server check fails
            IsDeviceActivated = false;
            return false;
        }
    }
    
    /// <summary>
    /// Gets the device serial number from hardware or settings.
    /// </summary>
    private string GetDeviceSerialNumber()
    {
        try
        {
            // Priority 1: Try to get from settings file
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DeviceSerialNumber=", StringComparison.OrdinalIgnoreCase))
                    {
                        var serial = trimmed.Substring("DeviceSerialNumber=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(serial) && serial != "AUTO")
                        {
                            WriteLog("Serial number loaded from settings");
                            return serial;
                        }
                    }
                }
            }
            
            // Priority 2: Try to get from machine-specific identifier
            // Use machine name + processor ID for a stable but unique identifier
            var machineName = Environment.MachineName;
            var processorId = GetProcessorId();
            var generatedSerial = $"POS_{machineName}_{processorId}".ToUpperInvariant();
            
            WriteLog("Serial number generated from hardware info");
            return generatedSerial;
        }
        catch (Exception ex)
        {
            WriteLog($"Eroare la obținerea serial number: {ex.Message}");
            
            // Fallback: use machine name only
            return $"POS_{Environment.MachineName}".ToUpperInvariant();
        }
    }
    
    /// <summary>
    /// Gets a processor ID for hardware identification.
    /// </summary>
    private string GetProcessorId()
    {
        try
        {
            var mc = new System.Management.ManagementClass("win32_processor");
            var moc = mc.GetInstances();
            foreach (var mo in moc)
            {
                var processorId = mo.Properties["processorID"].Value?.ToString();
                if (!string.IsNullOrEmpty(processorId))
                {
                    // Take first 8 characters for brevity
                    return processorId.Substring(0, Math.Min(8, processorId.Length));
                }
            }
        }
        catch
        {
            // Ignore errors - will use fallback
        }
        
        // Fallback: use a hash of machine name
        return Environment.MachineName.GetHashCode().ToString("X8");
    }
    
    /// <summary>
    /// Gets device model and fiscal printer series from settings (saved when device connects).
    /// </summary>
    private (string model, string fiscalPrinterSeries) GetDeviceModelAndSeriesFromSettings()
    {
        string model = "N/A";
        string fiscalSeries = "N/A";
        try
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                foreach (var line in File.ReadAllLines(settingsPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DeviceModel=", StringComparison.OrdinalIgnoreCase))
                    {
                        model = trimmed.Substring("DeviceModel=".Length).Trim();
                        if (string.IsNullOrWhiteSpace(model)) model = "N/A";
                    }
                    else if (trimmed.StartsWith("FiscalPrinterSeries=", StringComparison.OrdinalIgnoreCase))
                    {
                        fiscalSeries = trimmed.Substring("FiscalPrinterSeries=".Length).Trim();
                        if (string.IsNullOrWhiteSpace(fiscalSeries)) fiscalSeries = "N/A";
                    }
                }
            }
        }
        catch { }
        return (model, fiscalSeries);
    }

    /// <summary>
    /// Gets the tenant code from settings or default.
    /// </summary>
    private string GetTenantCode()
    {
        try
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("TenantCode=", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenant = trimmed.Substring("TenantCode=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(tenant))
                        {
                            return tenant;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        
        return "demo";
    }
    
    /// <summary>
    /// Starts a timer for periodic heartbeat check-ins.
    /// </summary>
    private void StartHeartbeatTimer()
    {
        if (string.IsNullOrEmpty(_deviceSerialNumber))
            return;
            
        // Check every 5 minutes; also send device model/serial if available
        var timer = new System.Threading.Timer(async _ =>
        {
            try
            {
                if (_activationService != null)
                {
                    await _activationService.SendHeartbeatAsync(_deviceSerialNumber);
                    var (model, fiscalSeries) = GetDeviceModelAndSeriesFromSettings();
                    if (model != "N/A" || fiscalSeries != "N/A")
                        await _activationService.SendDeviceInfoAsync(_deviceSerialNumber, model, fiscalSeries, _tenantCode);
                }
            }
            catch
            {
                // Silent fail - heartbeat is not critical
            }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        
        WriteLog("Heartbeat timer started (5 minute interval)");
    }
    
    private async void LaunchDudeIfNeeded()
    {
        try
        {
            // DUDE is only needed for Datecs devices; skip for Incotex/others
            if (!IsDatecsDeviceSelected())
            {
                WriteLog("DUDE nu este necesar (dispozitiv non-Datecs selectat)");
                return;
            }

            var dudeProcesses = Process.GetProcessesByName("DUDE");
            if (dudeProcesses.Length > 0)
            {
                WriteLog("✓ DUDE rulează deja");
                return;
            }
            
            string dudePath = LoadDudePath();
            
            if (!File.Exists(dudePath))
            {
                WriteLog($"⚠ DUDE nu a fost găsit la: {dudePath}");
                System.Windows.MessageBox.Show(
                    $"DUDE nu a fost găsit la calea configurată:\n{dudePath}\n\n" +
                    "Aplicația va continua, dar comunicarea cu imprimanta fiscală poate să nu funcționeze.\n\n" +
                    "Verificați calea în settings.txt sau instalați DUDE.",
                    "DUDE nu a fost găsit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            
            WriteLog($"Lansare DUDE de la: {dudePath}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = dudePath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(dudePath)
            };
            
            DudeProcess = Process.Start(startInfo);
            
            if (DudeProcess != null)
            {
                WriteLog($"✓ DUDE lansat cu succes (PID: {DudeProcess.Id})");
                
                // Wait for DUDE to initialize
                await Task.Delay(3000);
                WriteLog("✓ Așteptare inițializare DUDE completă");
            }
            else
            {
                WriteLog("✗ Nu s-a putut lansa DUDE (Process.Start a returnat null)");
            }
        }
        catch (Exception ex)
        {
            WriteLog($"✗ Eroare la lansarea DUDE: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Eroare la lansarea DUDE:\n\n{ex.Message}\n\n" +
                "Aplicația va continua, dar comunicarea cu imprimanta fiscală poate să nu funcționeze.",
                "Eroare lansare DUDE",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    
    private bool IsDatecsDeviceSelected()
    {
        string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
        try
        {
            if (File.Exists(settingsPath))
            {
                foreach (var line in File.ReadAllLines(settingsPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DeviceType=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = trimmed.Substring("DeviceType=".Length).Trim();
                        return val.Equals("Datecs", StringComparison.OrdinalIgnoreCase)
                            || val.Equals("1", StringComparison.Ordinal);
                    }
                }
            }
        }
        catch { }
        return true; // default to Datecs if setting not found
    }

    private string LoadDudePath()
    {
        string defaultPath = @"C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe";
        string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
        
        try
        {
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DudePath=", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring("DudePath=".Length).Trim();
                    }
                }
            }
        }
        catch
        {
            // Ignore errors, use default
        }
        
        return defaultPath;
    }
    
    private void WriteLog(string message)
    {
        try
        {
            string logFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logFolder);
            
            string logFile = Path.Combine(logFolder, $"app_{DateTime.Now:yyyyMMdd}.log");
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            
            File.AppendAllText(logFile, logEntry);
        }
        catch
        {
            // Ignore logging errors
        }
    }
}
