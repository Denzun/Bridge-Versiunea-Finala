using POSBridge.Abstractions.Enums;

namespace POSBridge.Abstractions.Models;

/// <summary>
/// Setări de conexiune pentru dispozitivul fiscal
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Tipul conexiunii
    /// </summary>
    public ConnectionType Type { get; set; } = ConnectionType.Serial;
    
    // Serial/USB Settings
    /// <summary>
    /// Port serial (ex: COM7)
    /// </summary>
    public string Port { get; set; } = "COM7";
    
    /// <summary>
    /// Baud rate pentru serial (ex: 115200)
    /// </summary>
    public int BaudRate { get; set; } = 115200;
    
    // WiFi/TCP Settings
    /// <summary>
    /// Adresa IP pentru conexiune WiFi/TCP
    /// </summary>
    public string IpAddress { get; set; } = "";
    
    /// <summary>
    /// Port TCP pentru WiFi (ex: 9100 - standard Datecs/Ethernet)
    /// </summary>
    public int TcpPort { get; set; } = 9100;
    
    // Bluetooth Settings
    /// <summary>
    /// Adresa Bluetooth a dispozitivului
    /// </summary>
    public string BluetoothAddress { get; set; } = "";
    
    // General Settings
    /// <summary>
    /// Timeout pentru operații (secunde)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
