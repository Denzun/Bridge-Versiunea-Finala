namespace POSBridge.Core.Models;

/// <summary>
/// Represents the current status of the fiscal device.
/// </summary>
public class DeviceStatus
{
    public bool IsConnected { get; set; }
    public bool PaperOut { get; set; }
    public bool CoverOpen { get; set; }
    public bool FiscalMemoryFull { get; set; }
    public int LastErrorCode { get; set; }
    public string LastErrorMessage { get; set; } = string.Empty;
    public DateTime LastChecked { get; set; }
}
