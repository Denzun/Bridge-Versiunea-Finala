namespace POSBridge.Abstractions.Models;

/// <summary>
/// Definește capabilitățile unui dispozitiv fiscal
/// Folosit pentru a determina ce features sunt disponibile
/// </summary>
public class DeviceCapabilities
{
    // Connection Types Supported
    /// <summary>
    /// Suportă conexiune RS232 serial
    /// </summary>
    public bool SupportsRS232 { get; set; } = true;
    
    /// <summary>
    /// Suportă conexiune Ethernet (TCP/IP wired)
    /// </summary>
    public bool SupportsEthernet { get; set; }
    
    /// <summary>
    /// Suportă conexiune USB serial
    /// </summary>
    public bool SupportsUSB { get; set; }
    
    /// <summary>
    /// Suportă conexiune WiFi/LAN
    /// </summary>
    public bool SupportsWiFi { get; set; }
    
    /// <summary>
    /// Suportă conexiune Bluetooth
    /// </summary>
    public bool SupportsBluetooth { get; set; }
    
    /// <summary>
    /// Suportă conexiune GPRS
    /// </summary>
    public bool SupportsGPRS { get; set; }
    
    // Critical Features (din analiza FiscalNet)
    /// <summary>
    /// Suportă citire status bon curent (ReadCurrentReceiptInfo)
    /// CRITICAL: Previne bonuri blocate
    /// </summary>
    public bool SupportsReceiptInfo { get; set; }
    
    /// <summary>
    /// Suportă subtotal cu return value (nu doar print)
    /// CRITICAL: Permite validări business
    /// </summary>
    public bool SupportsSubtotalReturn { get; set; }
    
    /// <summary>
    /// Suportă citire sume zilnice (ReadDailyAvailableAmounts)
    /// CRITICAL: Reconciliere cash automată
    /// </summary>
    public bool SupportsDailyAmounts { get; set; }
    
    /// <summary>
    /// Suportă tipărire duplicat instant ultimul bon
    /// </summary>
    public bool SupportsLastReceiptDuplicate { get; set; }
    
    /// <summary>
    /// Suportă Cash In/Out cu descriere text
    /// </summary>
    public bool SupportsCashDescription { get; set; }
    
    // Advanced Features
    /// <summary>
    /// Suportă configurare WiFi remote
    /// </summary>
    public bool SupportsWiFiConfiguration { get; set; }
    
    /// <summary>
    /// Suportă configurare GPRS
    /// </summary>
    public bool SupportsGPRSConfiguration { get; set; }
    
    /// <summary>
    /// Suportă programare dispozitiv (PLU, departamente, etc.)
    /// </summary>
    public bool SupportsDeviceProgramming { get; set; }
    
    // Device Limits
    /// <summary>
    /// Lungimea maximă nume articol (caractere)
    /// </summary>
    public int MaxItemNameLength { get; set; } = 30;
    
    /// <summary>
    /// Număr maxim operatori
    /// </summary>
    public int MaxOperators { get; set; } = 20;
    
    /// <summary>
    /// Număr maxim departamente
    /// </summary>
    public int MaxDepartments { get; set; } = 20;
    
    /// <summary>
    /// Număr maxim tipuri de plată
    /// </summary>
    public int MaxPaymentTypes { get; set; } = 10;
}
