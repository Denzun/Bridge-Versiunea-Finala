namespace POSBridge.Abstractions.Models;

/// <summary>
/// Informații generale despre dispozitivul fiscal
/// </summary>
public class DeviceInfo
{
    /// <summary>
    /// Nume producător (ex: Datecs, Tremol)
    /// </summary>
    public string VendorName { get; set; } = "";
    
    /// <summary>
    /// Model dispozitiv (ex: DP-25, FP-700X)
    /// </summary>
    public string ModelName { get; set; } = "";
    
    /// <summary>
    /// Număr de serie
    /// </summary>
    public string SerialNumber { get; set; } = "";
    
    /// <summary>
    /// Număr fiscal ANAF
    /// </summary>
    public string FiscalNumber { get; set; } = "";
    
    /// <summary>
    /// Cod fiscal (CUI) programat în AMEF
    /// </summary>
    public string FiscalCode { get; set; } = "";

    /// <summary>
    /// Versiune firmware
    /// </summary>
    public string FirmwareVersion { get; set; } = "";
    
    /// <summary>
    /// Este dispozitivul fiscalizat?
    /// </summary>
    public bool IsFiscalized { get; set; }
}
