namespace POSBridge.Abstractions.Models;

/// <summary>
/// Informații despre bonul fiscal curent
/// CRITICAL: Folosit pentru a preveni bonuri blocate (problema #1)
/// </summary>
public class ReceiptInfo
{
    /// <summary>
    /// Este un bon deschis în acest moment?
    /// </summary>
    public bool IsReceiptOpened { get; set; }
    
    /// <summary>
    /// Este bon fiscal (vs non-fiscal)?
    /// </summary>
    public bool IsFiscal { get; set; }
    
    /// <summary>
    /// Număr de articole (vânzări) pe bon
    /// </summary>
    public int SalesCount { get; set; }
    
    /// <summary>
    /// Subtotal curent al bonului (lei)
    /// </summary>
    public decimal SubtotalAmount { get; set; }
    
    /// <summary>
    /// A fost inițiată plata?
    /// </summary>
    public bool PaymentInitiated { get; set; }
    
    /// <summary>
    /// Este plata finalizată?
    /// </summary>
    public bool PaymentFinalized { get; set; }
}
