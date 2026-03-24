namespace POSBridge.Abstractions.Enums;

/// <summary>
/// Tipuri de plată
/// </summary>
public enum PaymentType
{
    /// <summary>
    /// Numerar
    /// </summary>
    Cash = 0,
    
    /// <summary>
    /// Card bancar
    /// </summary>
    Card = 1,
    
    /// <summary>
    /// Credit/Voucher
    /// </summary>
    Credit = 2,
    
    /// <summary>
    /// Voucher/Tichet
    /// </summary>
    Voucher = 3,
    
    /// <summary>
    /// Cec
    /// </summary>
    Check = 4,

    /// <summary>
    /// Tichet masă (protocol Incotex: C)
    /// </summary>
    TicketMeal = 5,

    /// <summary>
    /// Bon valoric / Tichet valoric (protocol Incotex: D)
    /// </summary>
    TicketValue = 6
}
