namespace POSBridge.Abstractions.Enums;

/// <summary>
/// Tipuri de conexiune suportate
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// Serial RS232/USB (COM port)
    /// </summary>
    Serial = 1,
    
    /// <summary>
    /// Ethernet TCP/IP (wired LAN)
    /// </summary>
    Ethernet = 2,
    
    /// <summary>
    /// WiFi/Wireless TCP/IP
    /// </summary>
    WiFi = 3,
    
    /// <summary>
    /// Bluetooth
    /// </summary>
    Bluetooth = 4,
    
    /// <summary>
    /// GPRS/Mobile network
    /// </summary>
    GPRS = 5
}
