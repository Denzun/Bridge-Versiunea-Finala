namespace POSBridge.Core.Models;

/// <summary>
/// Information retrieved from the fiscal device after connection.
/// Fields show "N/A" when not available or on error.
/// </summary>
public class DeviceInfo
{
    // Identification
    public string SerialNumber { get; set; } = "N/A";
    public string FiscalNumber { get; set; } = "N/A";
    public string TAXnumber { get; set; } = "N/A";
    public string DeviceName { get; set; } = "N/A";
    public string FirmwareVersion { get; set; } = "N/A";
    public string FirmwareDate { get; set; } = "N/A";

    // Status
    public string DateTime { get; set; } = "N/A";
    public string LastFiscalRecordDate { get; set; } = "N/A";
    public string ReceiptOpen { get; set; } = "N/A";
    public string ReceiptNumber { get; set; } = "N/A";
    public string ZReportNumber { get; set; } = "N/A";
    public string ReportsLeft { get; set; } = "N/A";

    // Fiscal
    public string Fiscalized { get; set; } = "N/A";
    public string TaxA { get; set; } = "N/A";
    public string TaxB { get; set; } = "N/A";
    public string TaxC { get; set; } = "N/A";
    public string TaxD { get; set; } = "N/A";
    public string TaxE { get; set; } = "N/A";

    // Cash
    public string CashSum { get; set; } = "N/A";
    public string CashIn { get; set; } = "N/A";
    public string CashOut { get; set; } = "N/A";

    // Header
    public string Headerline1 { get; set; } = "N/A";
    public string Headerline2 { get; set; } = "N/A";

    public string LastError { get; set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(LastError);
}
