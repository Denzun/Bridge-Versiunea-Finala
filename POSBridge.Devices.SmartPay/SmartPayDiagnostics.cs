using System.Management;
using System.Text;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Diagnostic tools for SmartPay/Ingenico device detection
/// </summary>
public static class SmartPayDiagnostics
{
    /// <summary>
    /// Scans all USB devices and returns detailed information
    /// </summary>
    public static List<UsbDeviceInfo> ScanAllUsbDevices()
    {
        var devices = new List<UsbDeviceInfo>();
        
        try
        {
            // Method 1: Win32_PnPEntity
            using var searcher1 = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%' OR DeviceID LIKE '%COM%'");
            
            foreach (ManagementObject device in searcher1.Get())
            {
                string deviceId = device["DeviceID"]?.ToString() ?? "";
                string name = device["Name"]?.ToString() ?? "";
                string status = device["Status"]?.ToString() ?? "";
                string service = device["Service"]?.ToString() ?? "";
                
                devices.Add(new UsbDeviceInfo
                {
                    Source = "Win32_PnPEntity",
                    DeviceId = deviceId,
                    Name = name,
                    Status = status,
                    Service = service
                });
            }
        }
        catch (Exception ex)
        {
            devices.Add(new UsbDeviceInfo
            {
                Source = "Error",
                Name = $"Win32_PnPEntity error: {ex.Message}"
            });
        }

        try
        {
            // Method 2: Win32_SerialPort (for COM ports)
            using var searcher2 = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
            
            foreach (ManagementObject port in searcher2.Get())
            {
                string deviceId = port["DeviceID"]?.ToString() ?? "";
                string name = port["Name"]?.ToString() ?? "";
                string description = port["Description"]?.ToString() ?? "";
                string pnpDeviceId = port["PNPDeviceID"]?.ToString() ?? "";
                
                devices.Add(new UsbDeviceInfo
                {
                    Source = "Win32_SerialPort",
                    DeviceId = deviceId,
                    PnpDeviceId = pnpDeviceId,
                    Name = name,
                    Description = description
                });
            }
        }
        catch (Exception ex)
        {
            devices.Add(new UsbDeviceInfo
            {
                Source = "Error",
                Name = $"Win32_SerialPort error: {ex.Message}"
            });
        }

        try
        {
            // Method 3: USB Hub devices
            using var searcher3 = new ManagementObjectSearcher(
                "SELECT * FROM Win32_USBHub");
            
            foreach (ManagementObject hub in searcher3.Get())
            {
                string deviceId = hub["DeviceID"]?.ToString() ?? "";
                string name = hub["Name"]?.ToString() ?? "";
                
                devices.Add(new UsbDeviceInfo
                {
                    Source = "Win32_USBHub",
                    DeviceId = deviceId,
                    Name = name
                });
            }
        }
        catch (Exception ex)
        {
            devices.Add(new UsbDeviceInfo
            {
                Source = "Error",
                Name = $"Win32_USBHub error: {ex.Message}"
            });
        }

        return devices;
    }

    /// <summary>
    /// Generates a diagnostic report
    /// </summary>
    public static string GenerateReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SmartPay/Ingenico Device Diagnostic Report ===");
        sb.AppendLine($"Generated: {DateTime.Now}");
        sb.AppendLine();

        var devices = ScanAllUsbDevices();
        
        // Group by source
        var bySource = devices.GroupBy(d => d.Source).ToList();
        
        foreach (var group in bySource)
        {
            sb.AppendLine($"--- {group.Key} ---");
            foreach (var device in group)
            {
                if (!string.IsNullOrEmpty(device.Name))
                    sb.AppendLine($"  Name: {device.Name}");
                if (!string.IsNullOrEmpty(device.DeviceId))
                    sb.AppendLine($"  DeviceID: {device.DeviceId}");
                if (!string.IsNullOrEmpty(device.PnpDeviceId))
                    sb.AppendLine($"  PNPDeviceID: {device.PnpDeviceId}");
                if (!string.IsNullOrEmpty(device.Description))
                    sb.AppendLine($"  Description: {device.Description}");
                if (!string.IsNullOrEmpty(device.Status))
                    sb.AppendLine($"  Status: {device.Status}");
                if (!string.IsNullOrEmpty(device.Service))
                    sb.AppendLine($"  Service: {device.Service}");
                
                // Check if it looks like Ingenico
                if (IsLikelyIngenico(device))
                {
                    sb.AppendLine("  *** POSSIBLE INGENICO DEVICE ***");
                }
                
                sb.AppendLine();
            }
        }

        // Check for known VID/PIDs
        sb.AppendLine("--- Known Ingenico VID/PID Patterns ---");
        sb.AppendLine("Looking for VID_0B00 (Ingenico)...");
        var ingenicoDevices = devices.Where(d => 
            d.DeviceId.Contains("VID_0B00", StringComparison.OrdinalIgnoreCase) ||
            d.DeviceId.Contains("VID_0b00", StringComparison.OrdinalIgnoreCase) ||
            d.PnpDeviceId.Contains("VID_0B00", StringComparison.OrdinalIgnoreCase)).ToList();
        
        if (ingenicoDevices.Any())
        {
            sb.AppendLine($"Found {ingenicoDevices.Count} device(s) with Ingenico VID:");
            foreach (var dev in ingenicoDevices)
            {
                sb.AppendLine($"  - {dev.Name}: {dev.DeviceId}");
            }
        }
        else
        {
            sb.AppendLine("No devices found with VID_0B00");
        }

        // Check other payment terminal VIDs
        sb.AppendLine();
        sb.AppendLine("--- Other Payment Terminal VIDs ---");
        string[] otherVids = new[] { "VID_11CA", "VID_079B", "VID_0A89", "VID_152A" };
        foreach (var vid in otherVids)
        {
            var matches = devices.Where(d => 
                d.DeviceId.Contains(vid, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Any())
            {
                sb.AppendLine($"{vid}: Found {matches.Count} device(s)");
                foreach (var m in matches)
                    sb.AppendLine($"  - {m.Name}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Tries to detect if a device is likely an Ingenico terminal
    /// </summary>
    private static bool IsLikelyIngenico(UsbDeviceInfo device)
    {
        string[] keywords = new[] { 
            "ingenico", "ict", "ipp", "desk", "lane", "iun", "self",
            "payment", "terminal", "pos", "eft"
        };
        
        string searchText = $"{device.Name} {device.Description} {device.DeviceId} {device.PnpDeviceId}".ToLower();
        
        return keywords.Any(k => searchText.Contains(k));
    }

    /// <summary>
    /// Gets available COM ports with detailed info
    /// </summary>
    public static List<ComPortInfo> GetComPortDetails()
    {
        var ports = new List<ComPortInfo>();
        
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
            
            foreach (ManagementObject port in searcher.Get())
            {
                ports.Add(new ComPortInfo
                {
                    PortName = port["DeviceID"]?.ToString() ?? "",
                    Description = port["Description"]?.ToString() ?? "",
                    Manufacturer = port["Manufacturer"]?.ToString() ?? "",
                    PnpDeviceId = port["PNPDeviceID"]?.ToString() ?? ""
                });
            }
        }
        catch { }

        return ports;
    }
}

public class UsbDeviceInfo
{
    public string Source { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Service { get; set; } = "";
}

public class ComPortInfo
{
    public string PortName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string PnpDeviceId { get; set; } = "";
}
