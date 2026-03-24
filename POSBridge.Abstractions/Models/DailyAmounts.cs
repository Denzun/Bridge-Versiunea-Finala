namespace POSBridge.Abstractions.Models;

/// <summary>
/// Sume zilnice disponibile în casă pe tipuri de plată
/// CRITICAL: Folosit pentru reconciliere cash automată (problema #2)
/// </summary>
public class DailyAmounts
{
    /// <summary>
    /// Total numerar în casă (lei)
    /// </summary>
    public decimal Cash { get; set; }
    
    /// <summary>
    /// Total plăți cu card (lei)
    /// </summary>
    public decimal Card { get; set; }
    
    /// <summary>
    /// Total plăți credit/voucher (lei)
    /// </summary>
    public decimal Credit { get; set; }
    
    /// <summary>
    /// Total vouchere/tichete (lei)
    /// </summary>
    public decimal Voucher { get; set; }
    
    /// <summary>
    /// Total general (sumă toate tipurile)
    /// </summary>
    public decimal Total => Cash + Card + Credit + Voucher;
}
