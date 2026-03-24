namespace POSBridge.Core.Models;

public enum ReceiptCommandType
{
    Sale,
    VoidSale,
    DiscountPercent,
    DiscountValue,
    MarkupPercent,
    MarkupValue,
    Subtotal,
    TextLine,
    Payment,
    CashIn,
    CashOut,
    FiscalCode,
    XReport,
    ZReport,
    CancelReceipt,
    OpenDrawer,
    NonFiscalText,
    Barcode,
    ClientDisplay,
    PosAmount
}

public class ReceiptCommand
{
    public ReceiptCommandType Type { get; set; }

    public string? Text { get; set; }
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }

    public decimal? Price { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public int? TaxGroup { get; set; }
    public int? Department { get; set; }

    public decimal? Value { get; set; }
    public int? PaymentType { get; set; }

    public string? Barcode { get; set; }
    public int? BarcodeType { get; set; }
}

public class ReceiptCommandFile
{
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public List<ReceiptCommand> Commands { get; set; } = new();
    public bool IsNonFiscal { get; set; }
    public bool IsDisplay { get; set; }
}
