namespace POSBridge.Core.Models;

/// <summary>
/// Represents a fiscal receipt request parsed from file.
/// File format: NumeProdus|Pret|Cantitate|CotaTVA (one item per line)
/// </summary>
public class BonRequest
{
    public string FileName { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public List<BonItem> Items { get; set; } = new();
}

public class BonItem
{
    public string NumeProdus { get; set; } = string.Empty;
    public decimal Pret { get; set; }
    public decimal Cantitate { get; set; } = 1;
    public int CotaTVA { get; set; } = 19; // Default 19%
    
    /// <summary>
    /// Maps VAT rate to TaxGroup for DUDE.
    /// 1=A(19%), 2=B(9%), 3=C(5%), 4=D(0%)
    /// </summary>
    public int GetTaxGroup()
    {
        return CotaTVA switch
        {
            19 => 1, // Group A
            9 => 2,  // Group B
            5 => 3,  // Group C
            0 => 4,  // Group D
            _ => 1   // Default to A (19%)
        };
    }
}

public class BonProcessingResult
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ReceiptNumber { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ProcessedAt { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
}
