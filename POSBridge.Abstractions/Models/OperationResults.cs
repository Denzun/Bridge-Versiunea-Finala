namespace POSBridge.Abstractions.Models;

/// <summary>
/// Rezultat de bază pentru operații dispozitiv fiscal
/// </summary>
public class OperationResult
{
    /// <summary>
    /// Operația a avut succes?
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Mesaj de eroare (dacă Success = false)
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Cod eroare (din dispozitiv)
    /// </summary>
    public int? ErrorCode { get; set; }
}

/// <summary>
/// Rezultat operație deschidere bon
/// </summary>
public class ReceiptResult : OperationResult
{
}

/// <summary>
/// Rezultat operație vânzare
/// </summary>
public class SaleResult : OperationResult
{
    /// <summary>
    /// Număr bon curent (dacă disponibil)
    /// </summary>
    public string? ReceiptNumber { get; set; }
}

/// <summary>
/// Rezultat subtotal
/// CRITICAL: Include valoarea subtotalului (nu doar success)
/// </summary>
public class SubtotalResult : OperationResult
{
    /// <summary>
    /// Valoarea subtotalului (lei)
    /// </summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// Rezultat operație discount/adaos
/// </summary>
public class DiscountResult : OperationResult
{
}

/// <summary>
/// Rezultat operație plată
/// </summary>
public class PaymentResult : OperationResult
{
    /// <summary>
    /// Rest de dat clientului (lei) — când client plătește mai mult decât totalul.
    /// </summary>
    public decimal Change { get; set; }

    /// <summary>
    /// Sumă rămasă de plată (lei) — > 0 pentru plăți parțiale (multiple forme de plată).
    /// PCode='D' cu amount1 > 0 de la AMEF.
    /// </summary>
    public decimal Remaining { get; set; }

    /// <summary>
    /// True dacă plata este parțială (mai sunt sume de achitat).
    /// </summary>
    public bool IsPartial => Remaining > 0.001m;
}

/// <summary>
/// Rezultat închidere bon
/// </summary>
public class CloseResult : OperationResult
{
    /// <summary>
    /// Număr bon
    /// </summary>
    public string? ReceiptNumber { get; set; }
    
    /// <summary>
    /// Număr fiscal complet
    /// </summary>
    public string? FiscalNumber { get; set; }
    
    /// <summary>
    /// Total bon (lei)
    /// </summary>
    public decimal TotalAmount { get; set; }
}
