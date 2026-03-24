namespace POSBridge.Abstractions.Enums;

/// <summary>
/// Tipuri de dispozitive fiscale suportate
/// </summary>
public enum DeviceType
{
    /// <summary>
    /// Datecs (DP-25, DP-35, etc.) - DUDE COM
    /// </summary>
    Datecs = 1,
    
    /// <summary>
    /// Incotex (181, 133, etc.) - Serial/USB COM
    /// </summary>
    Incotex = 2,
    
    /// <summary>
    /// Tremol (FP-700X, FP-05, etc.) - XML-based - COMING SOON
    /// </summary>
    Tremol = 3,
    
    /// <summary>
    /// Elcom - Reserved pentru viitor
    /// </summary>
    Elcom = 4,
    
    /// <summary>
    /// SmartPay/Ingenico (iCT, iPP, Desk series) - Serial/Bluetooth
    /// </summary>
    SmartPay = 5
}
