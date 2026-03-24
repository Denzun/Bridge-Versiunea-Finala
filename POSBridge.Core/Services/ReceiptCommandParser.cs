using POSBridge.Core.Models;

namespace POSBridge.Core.Services;

public static class ReceiptCommandParser
{
    public static ReceiptCommandFile ParseFile(string filePath)
    {
        var result = new ReceiptCommandFile
        {
            FileName = Path.GetFileName(filePath),
            FullPath = filePath
        };

        string fileName = result.FileName;
        if (fileName.StartsWith("nf_", StringComparison.OrdinalIgnoreCase))
            result.IsNonFiscal = true;
        if (fileName.StartsWith("display_", StringComparison.OrdinalIgnoreCase))
            result.IsDisplay = true;

        var lines = File.ReadAllLines(filePath);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("#") || line.StartsWith("//"))
                continue;
            if (line.All(c => c == '=') || line.All(c => c == '-'))
                continue;

            var parts = line.Split('^');
            var cmd = parts[0].Trim().ToUpperInvariant();

            switch (cmd)
            {
                case "S":
                {
                    // S^DENUMIRE^PRET^CANTITATE^UM^GRTVA^GRDEP
                    if (parts.Length < 7)
                        throw new FormatException($"Invalid S command: '{line}'");
                    var unit = parts[4].Trim();
                    if (string.IsNullOrWhiteSpace(unit))
                        unit = "BUC";
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.Sale,
                        Text = parts[1].Trim(),
                        Price = ParseMoney(parts[2]),
                        Quantity = ParseQuantity(parts[3]),
                        Unit = unit,
                        TaxGroup = ParseInt(parts[5]),
                        Department = ParseInt(parts[6], defaultValue: 1)
                    });
                    break;
                }
                case "VS":
                {
                    // VS^DENUMIRE^PRET^CANTITATE^UM^GRTVA^GRDEP
                    if (parts.Length < 7)
                        throw new FormatException($"Invalid VS command: '{line}'");
                    var unit = parts[4].Trim();
                    if (string.IsNullOrWhiteSpace(unit))
                        unit = "BUC";
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.VoidSale,
                        Text = parts[1].Trim(),
                        Price = ParseMoney(parts[2]),
                        Quantity = ParseQuantity(parts[3]),
                        Unit = unit,
                        TaxGroup = ParseInt(parts[5]),
                        Department = ParseInt(parts[6], defaultValue: 1)
                    });
                    break;
                }
                case "DP":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.DiscountPercent,
                        Value = ParsePercent(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "DV":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.DiscountValue,
                        Value = ParseMoney(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "MP":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.MarkupPercent,
                        Value = ParsePercent(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "MV":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.MarkupValue,
                        Value = ParseMoney(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "ST":
                    result.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.Subtotal });
                    break;
                case "TL":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = result.IsNonFiscal ? ReceiptCommandType.NonFiscalText : ReceiptCommandType.TextLine,
                        Text = parts.ElementAtOrDefault(1)?.Trim() ?? string.Empty
                    });
                    break;
                case "P":
                {
                    // P^TipPlata^VALOARE
                    // TipPlata: 1=Numerar, 2=Card, 3=Credit, 4=Tichet masa, 5=Tichet valoric, 
                    //           6=Voucher, 7=Plata moderna, 8-9=Alte modalitati
                    if (parts.Length < 3)
                        throw new FormatException($"Invalid P command: '{line}'");
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.Payment,
                        PaymentType = ParsePaymentType(parts[1]),
                        Value = ParseMoney(parts[2])
                    });
                    break;
                }
                case "I":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.CashIn,
                        Value = ParseMoney(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "O":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.CashOut,
                        Value = ParseMoney(parts.ElementAtOrDefault(1))
                    });
                    break;
                case "CF":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.FiscalCode,
                        Text = parts.ElementAtOrDefault(1)?.Trim() ?? string.Empty
                    });
                    break;
                case "X":
                    result.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.XReport });
                    break;
                case "Z":
                    result.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.ZReport });
                    break;
                case "VB":
                    result.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.CancelReceipt });
                    break;
                case "DS":
                    result.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.OpenDrawer });
                    break;
                case "CB":
                {
                    // CB^CodDeBare^TipCod
                    if (parts.Length < 3)
                        throw new FormatException($"Invalid CB command: '{line}'");
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.Barcode,
                        Barcode = parts[1].Trim(),
                        BarcodeType = ParseInt(parts[2])
                    });
                    break;
                }
                case "MD":
                {
                    // MD^Rand1^Rand2
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.ClientDisplay,
                        Line1 = parts.ElementAtOrDefault(1)?.Trim() ?? string.Empty,
                        Line2 = parts.ElementAtOrDefault(2)?.Trim() ?? string.Empty
                    });
                    break;
                }
                case "POS":
                    result.Commands.Add(new ReceiptCommand
                    {
                        Type = ReceiptCommandType.PosAmount,
                        Value = ParseMoney(parts.ElementAtOrDefault(1))
                    });
                    break;
                default:
                    // In non-fiscal receipts, treat unknown commands as text lines
                    if (result.IsNonFiscal)
                    {
                        // Treat the entire line as text (no command prefix needed)
                        result.Commands.Add(new ReceiptCommand
                        {
                            Type = ReceiptCommandType.NonFiscalText,
                            Text = line // Use the original line as-is
                        });
                    }
                    else
                    {
                        throw new FormatException($"Unknown command: '{cmd}'");
                    }
                    break;
            }
        }

        return result;
    }

    private static decimal ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Missing money value.");
        
        var trimmed = raw.Trim();
        
        // Check if it contains decimal separator (. or ,)
        if (trimmed.Contains('.') || trimmed.Contains(','))
        {
            // Parse as decimal number (e.g., "5.50" or "5,50")
            var normalized = trimmed.Replace(',', '.');
            if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.AllowDecimalPoint, 
                System.Globalization.CultureInfo.InvariantCulture, out var decimalVal))
                throw new FormatException($"Invalid money value: '{raw}'");
            return decimalVal;
        }
        else
        {
            // Parse as integer in cents (e.g., "550" = 5.50)
            if (!long.TryParse(trimmed, out var val))
                throw new FormatException($"Invalid money value: '{raw}'");
            return val / 100m;
        }
    }

    private static decimal ParsePercent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Missing percent value.");
        
        var trimmed = raw.Trim();
        
        // Check if it contains decimal separator (. or ,)
        if (trimmed.Contains('.') || trimmed.Contains(','))
        {
            // Parse as decimal percentage (e.g., "15.5" = 15.5%)
            var normalized = trimmed.Replace(',', '.');
            if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.AllowDecimalPoint, 
                System.Globalization.CultureInfo.InvariantCulture, out var decimalVal))
                throw new FormatException($"Invalid percent value: '{raw}'");
            return decimalVal;
        }
        else
        {
            // Parse as integer in hundredths (e.g., "1550" = 15.50%)
            if (!long.TryParse(trimmed, out var val))
                throw new FormatException($"Invalid percent value: '{raw}'");
            return val / 100m;
        }
    }

    private static decimal ParseQuantity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new FormatException("Missing quantity value.");
        
        var trimmed = raw.Trim();
        
        // Check if it contains decimal separator (. or ,)
        if (trimmed.Contains('.') || trimmed.Contains(','))
        {
            // Parse as decimal number (e.g., "1.250" or "2,5")
            var normalized = trimmed.Replace(',', '.');
            if (!decimal.TryParse(normalized, System.Globalization.NumberStyles.AllowDecimalPoint, 
                System.Globalization.CultureInfo.InvariantCulture, out var decimalVal))
                throw new FormatException($"Invalid quantity value: '{raw}'");
            return decimalVal;
        }
        else
        {
            // Parse as integer in thousandths (e.g., "1250" = 1.250)
            if (!long.TryParse(trimmed, out var val))
                throw new FormatException($"Invalid quantity value: '{raw}'");
            return val / 1000m;
        }
    }

    private static int ParseInt(string? raw, int? defaultValue = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (defaultValue.HasValue)
                return defaultValue.Value;
            throw new FormatException("Missing integer value.");
        }

        if (!int.TryParse(raw.Trim(), out int val))
            throw new FormatException($"Invalid integer value: '{raw}'");
        return val;
    }

    private static int ParsePaymentType(string? raw)
    {
        int paymentType = ParseInt(raw);

        // Backward compatibility with older samples using P^0^... for cash.
        if (paymentType == 0)
            return 1;

        if (paymentType < 1 || paymentType > 9)
            throw new FormatException($"Invalid payment type '{raw}'. Supported values are 1..9.");

        return paymentType;
    }
}
