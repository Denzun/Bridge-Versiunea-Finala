using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace POSBridge.Devices.Incotex;

[SupportedOSPlatform("windows")]
internal static class IncotexComPortFinder
{
    internal const int IncotexVendorId = 0x0483;
    internal const int IncotexProductId = 0x5740;

    internal static string? TryFindIncotexComPort()
        => TryFindComPortByVidPid(IncotexVendorId, IncotexProductId);

    internal static bool IsIncotexPresent()
        => IsDevicePresentByVidPid(IncotexVendorId, IncotexProductId);

    /// <summary>
    /// Returns the WinUSB/CDC driver service name for the Incotex device, or null.
    /// Typical values: "WinUSB", "usbser", "WUDFRd".
    /// </summary>
    internal static string? GetIncotexDriverService()
    {
        if (!OperatingSystem.IsWindows()) return null;

        string vid = IncotexVendorId.ToString("X4");
        string pid = IncotexProductId.ToString("X4");
        string query =
            "SELECT Service, PNPDeviceID " +
            "FROM Win32_PnPEntity " +
            $"WHERE PNPDeviceID LIKE '%VID_{vid}&PID_{pid}%'";
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject result in results)
            {
                string? svc = result["Service"] as string;
                if (!string.IsNullOrWhiteSpace(svc))
                    return svc;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Check if FiscalNet (or similar competing app) is currently running.
    /// </summary>
    internal static bool IsFiscalNetRunning()
    {
        try
        {
            var names = new[] { "FiscalNet", "fiscalnet", "FiscalNetClient", "FiscalNetService" };
            foreach (string name in names)
            {
                if (Process.GetProcessesByName(name).Length > 0)
                    return true;
            }
        }
        catch { }
        return false;
    }

    internal static string? TryFindComPortByVidPid(int vendorId, int productId)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // Preferred: Win32_SerialPort gives us the COM name directly (DeviceID = "COM7")
        string vid = vendorId.ToString("X4");
        string pid = productId.ToString("X4");
        string querySerialPort =
            "SELECT DeviceID, PNPDeviceID " +
            "FROM Win32_SerialPort " +
            $"WHERE PNPDeviceID LIKE '%VID_{vid}&PID_{pid}%'";

        try
        {
            using var searcher = new ManagementObjectSearcher(querySerialPort);
            using var results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                string? deviceId = result["DeviceID"] as string; // e.g. "COM7"
                if (string.IsNullOrWhiteSpace(deviceId))
                    continue;

                if (deviceId.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    return deviceId.ToUpperInvariant();
            }
        }
        catch
        {
            // WMI can fail on some systems / permissions; just treat as not found.
        }

        // Fallback: some drivers don't populate Win32_SerialPort reliably, so use PnPEntity and parse "(COMx)".
        // We keep this as a best-effort fallback only.
        try
        {
            string queryPnP =
                "SELECT Name, PNPDeviceID " +
                "FROM Win32_PnPEntity " +
                $"WHERE PNPDeviceID LIKE '%VID_{vid}&PID_{pid}%'";

            using var searcher = new ManagementObjectSearcher(queryPnP);
            using var results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                string? name = result["Name"] as string; // e.g. "USB Serial Device (COM7)"
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                int start = name.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                    continue;

                int end = name.IndexOf(')', start);
                if (end < 0)
                    continue;

                string inside = name.Substring(start + 1, end - start - 1); // "COM7"
                if (inside.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                    return inside.ToUpperInvariant();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    internal static bool IsDevicePresentByVidPid(int vendorId, int productId)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        string vid = vendorId.ToString("X4");
        string pid = productId.ToString("X4");

        // Any PnP entry containing this VID/PID is enough to say it's connected.
        string query =
            "SELECT PNPDeviceID " +
            "FROM Win32_PnPEntity " +
            $"WHERE PNPDeviceID LIKE '%VID_{vid}&PID_{pid}%'";

        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject _ in results)
                return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }
}

