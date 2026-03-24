using System.Diagnostics;
using System.Management;
using Microsoft.Win32;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Helper class to detect and install Ingenico USB drivers.
/// Creates virtual COM port for SmartPay ECR Link communication.
/// </summary>
public static class SmartPayDriverInstaller
{
    // Ingenico USB VID/PID (common values - may vary by model)
    private const int IngenicoVendorId = 0x0B00;  // Ingenico
    private const int IngenicoProductId = 0x0068; // Common iCT/iPP series
    
    // Alternative PIDs for different models
    private static readonly (int Vid, int Pid, string Model)[] KnownDevices = new[]
    {
        (0x0B00, 0x0068, "iCT220/iCT250"),
        (0x0B00, 0x0069, "iPP320/iPP350"),
        (0x0B00, 0x0070, "Desk/5000"),
        (0x0B00, 0x0071, "Lane/7000"),
        (0x0B00, 0x0072, "iUN series"),
        (0x0B00, 0x0073, "Self series"),
    };

    /// <summary>
    /// Checks if Ingenico device is connected but driver is missing.
    /// </summary>
    public static DriverStatus CheckDriverStatus()
    {
        try
        {
            // Check for Ingenico device in WMI
            bool deviceFound = false;
            bool driverInstalled = false;
            string devicePath = "";
            string detectedModel = "";

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%'");

            foreach (ManagementObject device in searcher.Get())
            {
                string deviceId = device["DeviceID"]?.ToString() ?? "";
                string name = device["Name"]?.ToString() ?? "";
                string status = device["Status"]?.ToString() ?? "";

                foreach (var (vid, pid, model) in KnownDevices)
                {
                    string vidPid = $"VID_{vid:X4}&PID_{pid:X4}";
                    if (deviceId.Contains(vidPid, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceFound = true;
                        detectedModel = model;
                        devicePath = deviceId;
                        
                        // Check if driver is installed (device has COM port or proper name)
                        if (name.Contains("COM", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Ingenico", StringComparison.OrdinalIgnoreCase) ||
                            status == "OK")
                        {
                            driverInstalled = true;
                        }
                        break;
                    }
                }
            }

            return new DriverStatus
            {
                DeviceConnected = deviceFound,
                DriverInstalled = driverInstalled,
                DevicePath = devicePath,
                DetectedModel = detectedModel,
                ComPort = FindIngenicoComPort()
            };
        }
        catch (Exception ex)
        {
            return new DriverStatus
            {
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Finds the COM port assigned to Ingenico device.
    /// </summary>
    public static string? FindIngenicoComPort()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%(COM%)%'");

            foreach (ManagementObject device in searcher.Get())
            {
                string name = device["Name"]?.ToString() ?? "";
                string deviceId = device["DeviceID"]?.ToString() ?? "";

                // Check if it's an Ingenico device
                foreach (var (vid, pid, _) in KnownDevices)
                {
                    string vidPid = $"VID_{vid:X4}&PID_{pid:X4}";
                    if (deviceId.Contains(vidPid, StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract COM port from name like "Ingenico USB Serial Port (COM3)"
                        int start = name.IndexOf("(COM");
                        if (start >= 0)
                        {
                            int end = name.IndexOf(")", start);
                            if (end > start)
                            {
                                return name.Substring(start + 1, end - start - 1);
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Installs the Ingenico driver from bundled resources or Windows Update.
    /// </summary>
    public static async Task<InstallResult> InstallDriverAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Checking device connection...");
        
        var status = CheckDriverStatus();
        
        if (!status.DeviceConnected)
        {
            return new InstallResult
            {
                Success = false,
                Message = "Ingenico device not detected.\n\nPlease:\n1. Connect the device via USB\n2. Power it on\n3. Try again"
            };
        }

        if (status.DriverInstalled && !string.IsNullOrEmpty(status.ComPort))
        {
            return new InstallResult
            {
                Success = true,
                Message = $"Driver already installed.\nDevice detected on {status.ComPort}",
                ComPort = status.ComPort
            };
        }

        progress?.Report("Device found. Installing driver...");

        try
        {
            // Try bundled driver first
            string driverPath = FindBundledDriver();
            
            if (!string.IsNullOrEmpty(driverPath))
            {
                progress?.Report("Installing bundled driver...");
                return await InstallBundledDriverAsync(driverPath, progress);
            }

            // Fallback: Try Windows Update
            progress?.Report("Searching Windows Update for driver...");
            return await InstallViaWindowsUpdateAsync(progress);
        }
        catch (Exception ex)
        {
            return new InstallResult
            {
                Success = false,
                Message = $"Installation failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Finds bundled driver in the application directory.
    /// </summary>
    private static string FindBundledDriver()
    {
        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] possiblePaths = new[]
        {
            Path.Combine(appDir, "Drivere", "SmartPay", "Ingenico_Driver.exe"),
            Path.Combine(appDir, "Drivere", "SmartPay", "setup.exe"),
            Path.Combine(appDir, "Drivere", "SmartPay", "Install.bat"),
            Path.Combine(appDir, "SmartPay_Driver", "setup.exe"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        // Check for INF file (manual install)
        string infPath = Path.Combine(appDir, "Drivere", "SmartPay", "Ingenico.inf");
        if (File.Exists(infPath))
            return infPath;

        return "";
    }

    /// <summary>
    /// Installs bundled driver executable.
    /// </summary>
    private static async Task<InstallResult> InstallBundledDriverAsync(string driverPath, IProgress<string>? progress)
    {
        string ext = Path.GetExtension(driverPath).ToLower();
        
        if (ext == ".inf")
        {
            // Manual INF install using pnputil
            return await InstallInfDriverAsync(driverPath, progress);
        }
        else
        {
            // Run installer
            var psi = new ProcessStartInfo
            {
                FileName = driverPath,
                Arguments = "/S /SILENT", // Silent install flags
                UseShellExecute = true,
                Verb = "runas", // Admin required
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new InstallResult
                {
                    Success = false,
                    Message = "Failed to start driver installer."
                };
            }

            progress?.Report("Installing... Please wait...");
            await Task.Run(() => process.WaitForExit());

            // Check if installation succeeded
            await Task.Delay(3000); // Give Windows time to recognize device
            var status = CheckDriverStatus();

            if (status.DriverInstalled && !string.IsNullOrEmpty(status.ComPort))
            {
                return new InstallResult
                {
                    Success = true,
                    Message = $"Driver installed successfully!\nDevice is now on {status.ComPort}",
                    ComPort = status.ComPort
                };
            }
            else
            {
                return new InstallResult
                {
                    Success = false,
                    Message = "Driver installation completed but device not detected.\nPlease restart the application."
                };
            }
        }
    }

    /// <summary>
    /// Installs driver using Windows INF file and pnputil.
    /// </summary>
    private static async Task<InstallResult> InstallInfDriverAsync(string infPath, IProgress<string>? progress)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = $"/add-driver \"{infPath}\" /install",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new InstallResult
            {
                Success = false,
                Message = "Failed to start driver installation."
            };
        }

        progress?.Report("Installing driver via Windows...");
        await Task.Run(() => process.WaitForExit());

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        // Check if installation succeeded
        await Task.Delay(2000);
        var status = CheckDriverStatus();

        if (status.DriverInstalled && !string.IsNullOrEmpty(status.ComPort))
        {
            return new InstallResult
            {
                Success = true,
                Message = $"Driver installed successfully!\nDevice is now on {status.ComPort}",
                ComPort = status.ComPort
            };
        }
        else
        {
            return new InstallResult
            {
                Success = false,
                Message = $"Installation result:\n{output}\n{error}"
            };
        }
    }

    /// <summary>
    /// Tries to install driver via Windows Update.
    /// </summary>
    private static async Task<InstallResult> InstallViaWindowsUpdateAsync(IProgress<string>? progress)
    {
        // Use pnputil to scan for hardware changes
        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = "/scan-devices",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true
        };

        progress?.Report("Scanning for hardware changes...");
        
        using (var process = Process.Start(psi))
        {
            if (process != null)
            {
                await Task.Run(() => process.WaitForExit());
            }
        }

        // Wait and check
        await Task.Delay(5000);
        var status = CheckDriverStatus();

        if (status.DriverInstalled && !string.IsNullOrEmpty(status.ComPort))
        {
            return new InstallResult
            {
                Success = true,
                Message = $"Driver installed via Windows Update!\nDevice is now on {status.ComPort}",
                ComPort = status.ComPort
            };
        }

        return new InstallResult
        {
            Success = false,
            Message = "Windows Update could not find a driver automatically.\n\n" +
                     "Please download the driver manually from:\n" +
                     "https://www.ingenico.com/support/download-center"
        };
    }

    /// <summary>
    /// Gets installation instructions for manual installation.
    /// </summary>
    public static string GetManualInstallInstructions()
    {
        return @"Manual Driver Installation Instructions:

1. Download the driver from Ingenico support website:
   https://www.ingenico.com/support/download-center

2. Connect your Ingenico terminal via USB

3. Power on the terminal

4. Run the downloaded driver installer

5. After installation, check Device Manager:
   - Look under 'Ports (COM & LPT)'
   - You should see 'Ingenico USB Serial Port (COMx)'

6. Note the COM port number and use it in POS Bridge

Alternative: Windows may install the driver automatically
when you connect the device for the first time.
";
    }
}

/// <summary>
/// Represents the driver status.
/// </summary>
public class DriverStatus
{
    public bool DeviceConnected { get; set; }
    public bool DriverInstalled { get; set; }
    public string DevicePath { get; set; } = "";
    public string DetectedModel { get; set; } = "";
    public string? ComPort { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Represents the installation result.
/// </summary>
public class InstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ComPort { get; set; }
}
