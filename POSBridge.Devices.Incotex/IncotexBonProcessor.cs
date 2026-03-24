using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Exceptions;
using POSBridge.Core.Models;
using POSBridge.Devices.Incotex;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// Processes ReceiptCommandFile (format Datecs) on Incotex Succes M7 via IFiscalDevice.
/// Same bon structure as Datecs - translates to Incotex protocol.
/// </summary>
public static class IncotexBonProcessor
{
    public static async Task<BonProcessingResult> ProcessCommandFileAsync(
        ReceiptCommandFile commandFile,
        IFiscalDevice device,
        int operatorCode,
        string operatorPassword)
    {
        var startTime = DateTime.Now;
        var result = new BonProcessingResult
        {
            FileName = commandFile.FileName,
            ProcessedAt = startTime
        };

        try
        {
            if (!device.IsConnected)
                throw new InvalidOperationException("Incotex device not connected.");

            // Display-only
            if (commandFile.IsDisplay)
            {
                foreach (var cmd in commandFile.Commands.Where(c => c.Type == ReceiptCommandType.ClientDisplay))
                {
                    await device.DisplayTextAsync(cmd.Line1 ?? "", cmd.Line2 ?? "");
                }
                result.Success = true;
                result.ProcessingDuration = DateTime.Now - startTime;
                return result;
            }

            // Non-fiscal: single call with all lines (device opens once, prints each, closes)
            if (commandFile.IsNonFiscal)
            {
                var lines = string.Join("\n", commandFile.Commands
                    .Where(c => c.Type == ReceiptCommandType.NonFiscalText)
                    .Select(c => c.Text ?? ""));
                await device.PrintNonFiscalTextAsync(lines);
                result.Success = true;
                result.ProcessingDuration = DateTime.Now - startTime;
                return result;
            }

            // X/Z Report
            if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.XReport || c.Type == ReceiptCommandType.ZReport))
            {
                if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.ZReport))
                    await device.PrintDailyReportAsync("Z");
                else
                    await device.PrintDailyReportAsync("X");
                result.Success = true;
                result.ReceiptNumber = "Report";
                result.ProcessingDuration = DateTime.Now - startTime;
                return result;
            }

            // Standalone CancelReceipt
            if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.CancelReceipt))
            {
                await device.CancelReceiptAsync();
                result.Success = true;
                result.ReceiptNumber = "Cancelled";
                result.ProcessingDuration = DateTime.Now - startTime;
                return result;
            }

            // Standalone CashIn/CashOut
            if (commandFile.Commands.Count == 1 &&
                (commandFile.Commands[0].Type == ReceiptCommandType.CashIn ||
                 commandFile.Commands[0].Type == ReceiptCommandType.CashOut))
            {
                var cmd = commandFile.Commands[0];
                // ParseMoney returnează deja lei (ex: "30000" → 300.00). Nu se mai împarte la 100.
                decimal amountInLei = cmd.Value ?? 0m;
                if (amountInLei <= 0)
                    throw new InvalidOperationException($"{cmd.Type} amount must be > 0");

                if (cmd.Type == ReceiptCommandType.CashIn)
                    await device.CashInAsync(amountInLei);
                else
                    await device.CashOutAsync(amountInLei);

                result.Success = true;
                result.ReceiptNumber = cmd.Type == ReceiptCommandType.CashIn
                    ? $"Cash In: {amountInLei:F2} lei"
                    : $"Cash Out: {amountInLei:F2} lei";
                result.ProcessingDuration = DateTime.Now - startTime;
                return result;
            }

            // Fiscal receipt
            try
            {
                await device.CancelReceiptAsync();
            }
            catch { /* ignore if no open receipt */ }

            // Check for Invoice (FiscalCode/CUI) -- requires CMD 57 before CMD 48 with option I
            var fiscalCodeCmd = commandFile.Commands.FirstOrDefault(c => c.Type == ReceiptCommandType.FiscalCode);
            bool isInvoice = fiscalCodeCmd != null && !string.IsNullOrWhiteSpace(fiscalCodeCmd.Text);
            
            if (isInvoice && device is IncotexDevice incotexDev)
            {
                await incotexDev.OpenReceiptInvoiceAsync(
                    operatorCode,
                    operatorPassword ?? "0000",
                    "",
                    fiscalCodeCmd!.Text!,
                    "");
            }
            else
            {
                await device.OpenReceiptAsync(operatorCode, operatorPassword ?? "0000");
            }

            var reordered = ReorderCommands(commandFile.Commands);
            var paymentCommands = new List<ReceiptCommand>();
            decimal lastSubtotalAmount = 0m;

            // Incotex: reordonăm vânzările înaintea textului NUMAI dacă există TL/CB înainte de prima vânzare.
            // Dacă textul este deja după vânzări, păstrăm ordinea originală pentru a nu separa S de DP/DV/MP/MV.
            int firstSalePos = reordered.FindIndex(c => c.Type == ReceiptCommandType.Sale || c.Type == ReceiptCommandType.VoidSale);
            int firstTextPos = reordered.FindIndex(c => c.Type == ReceiptCommandType.TextLine || c.Type == ReceiptCommandType.Barcode);
            bool needsSaleReorder = firstSalePos >= 0 && firstTextPos >= 0 && firstTextPos < firstSalePos;
            if (needsSaleReorder)
            {
                // Construim grupuri (vânzare + discount/adaos imediat următor) și le mutăm înaintea textului.
                var saleGroups = new List<ReceiptCommand>();
                var nonSaleNonPay = new List<ReceiptCommand>();
                var pays = new List<ReceiptCommand>();
                for (int k = 0; k < reordered.Count; k++)
                {
                    var c = reordered[k];
                    if (c.Type == ReceiptCommandType.Sale || c.Type == ReceiptCommandType.VoidSale)
                    {
                        saleGroups.Add(c);
                        if (k + 1 < reordered.Count && IsDiscountOrMarkup(reordered[k + 1].Type))
                        {
                            saleGroups.Add(reordered[k + 1]);
                            k++;
                        }
                    }
                    else if (c.Type == ReceiptCommandType.Payment)
                        pays.Add(c);
                    else
                        nonSaleNonPay.Add(c);
                }
                reordered = saleGroups.Concat(nonSaleNonPay).Concat(pays).ToList();
            }

            for (int i = 0; i < reordered.Count; i++)
            {
                var cmd = reordered[i];

                if (cmd.Type == ReceiptCommandType.Payment)
                {
                    paymentCommands.Add(cmd);
                    continue;
                }

                switch (cmd.Type)
                {
                    case ReceiptCommandType.Sale:
                    case ReceiptCommandType.VoidSale:
                        decimal price = cmd.Price ?? 0m;
                        if (cmd.Type == ReceiptCommandType.VoidSale)
                            price = -Math.Abs(price);
                        decimal? percent = null, absVal = null;
                        if (i + 1 < reordered.Count)
                        {
                            var next = reordered[i + 1];
                            if (next.Type == ReceiptCommandType.DiscountPercent) { percent = -Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.DiscountValue) { absVal = -Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.MarkupPercent) { percent = Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.MarkupValue) { absVal = Math.Abs(next.Value ?? 0); i++; }
                        }
                        // Pentru Incotex, discount/adaos per articol se embedează în CMD 49.
                        // Nu se trimite CMD 51 (subtotal) separat pentru discount per linie.
                        if (device is IncotexDevice incotexDevSale)
                        {
                            await incotexDevSale.AddSaleWithDiscountAsync(
                                cmd.Text ?? "", price, cmd.Quantity ?? 1m,
                                cmd.TaxGroup ?? 1, cmd.Department ?? 1,
                                unit: cmd.Unit,
                                percentDiscount: percent,
                                absDiscount: absVal);
                        }
                        else
                        {
                            await device.AddSaleAsync(
                                cmd.Text ?? "",
                                price,
                                cmd.Quantity ?? 1m,
                                cmd.TaxGroup ?? 1,
                                cmd.Department ?? 1);
                            if (percent.HasValue)
                                await device.AddDiscountAsync(percent.Value, true);
                            if (absVal.HasValue)
                                await device.AddDiscountAsync(absVal.Value, false);
                        }
                        break;

                    case ReceiptCommandType.Subtotal:
                        decimal? stPercent = null, stAbs = null;
                        if (i + 1 < reordered.Count)
                        {
                            var next = reordered[i + 1];
                            if (next.Type == ReceiptCommandType.DiscountPercent) { stPercent = -Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.DiscountValue) { stAbs = -Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.MarkupPercent) { stPercent = Math.Abs(next.Value ?? 0); i++; }
                            else if (next.Type == ReceiptCommandType.MarkupValue) { stAbs = Math.Abs(next.Value ?? 0); i++; }
                        }
                        var stRes = await device.SubtotalAsync(true);
                        lastSubtotalAmount = stRes.Amount;
                        if (stPercent.HasValue)
                            await device.AddDiscountAsync(stPercent.Value, true);
                        if (stAbs.HasValue)
                            await device.AddDiscountAsync(stAbs.Value, false);
                        break;

                    case ReceiptCommandType.TextLine:
                        if (device is IncotexDevice incotex)
                            await incotex.PrintTextInFiscalReceiptAsync(cmd.Text ?? "");
                        break;

                    case ReceiptCommandType.OpenDrawer:
                        await device.OpenCashDrawerAsync();
                        break;

                    case ReceiptCommandType.Barcode:
                        // CMD 84 - skip for now or implement
                        break;

                    case ReceiptCommandType.ClientDisplay:
                        await device.DisplayTextAsync(cmd.Line1 ?? "", cmd.Line2 ?? "");
                        break;

                    case ReceiptCommandType.CashIn:
                    case ReceiptCommandType.CashOut:
                        // ParseMoney returnează deja lei. Nu se mai împarte la 100.
                        decimal amt = cmd.Value ?? 0m;
                        if (cmd.Type == ReceiptCommandType.CashIn)
                            await device.CashInAsync(amt);
                        else
                            await device.CashOutAsync(amt);
                        break;

                    case ReceiptCommandType.FiscalCode:
                        // CF (CUI) este deja procesat înainte de OpenReceipt (CMD 57 + CMD 48 cu opțiunea I).
                        // Nu mai facem nimic cu el în buclă.
                        break;

                    case ReceiptCommandType.DiscountPercent:
                    case ReceiptCommandType.DiscountValue:
                    case ReceiptCommandType.MarkupPercent:
                    case ReceiptCommandType.MarkupValue:
                        break;
                }
            }

            // Dacă nu am avut ST explicit, adăugăm subtotal implicit. Capturăm amount din răspuns.
            if (paymentCommands.Count > 0 && reordered.Any(c => c.Type == ReceiptCommandType.Sale || c.Type == ReceiptCommandType.VoidSale) 
                && !reordered.Any(c => c.Type == ReceiptCommandType.Subtotal))
            {
                var stResult = await device.SubtotalAsync(true);
                lastSubtotalAmount = stResult.Amount;
                LogPaymentDiagnostic($"Subtotal implicit from device: {lastSubtotalAmount:F2}");
            }

            await Task.Delay(100);

            bool hasMultiplePayments = paymentCommands.Count > 1;
            for (int pi = 0; pi < paymentCommands.Count; pi++)
            {
                var payment = paymentCommands[pi];
                var pt = MapPaymentType(payment.PaymentType);
                decimal payAmount = payment.Value ?? 0m;
                bool isLastPayment = pi == paymentCommands.Count - 1;

                // Normalize exact single payment to match AMEF subtotal
                if (paymentCommands.Count == 1 && lastSubtotalAmount > 0 && Math.Abs(payAmount - lastSubtotalAmount) < 0.02m)
                    payAmount = lastSubtotalAmount;

                // Use explicit amount when:
                // 1. Bon cu rest (single payment > total)
                // 2. Multiple payments (each partial payment needs explicit amount)
                bool isBonCuRest = lastSubtotalAmount > 0 && payAmount > lastSubtotalAmount + 0.02m;
                bool useExplicit = isBonCuRest || hasMultiplePayments;
                LogPaymentDiagnostic($"Payment[{pi}]: type={pt}, amount={payAmount:F2} (lastSubtotal={lastSubtotalAmount:F2}) bonCuRest={isBonCuRest} explicit={useExplicit}");

                if (useExplicit && device is IncotexDevice incotexDevPay)
                {
                    var payResult = await incotexDevPay.AddExplicitPaymentAsync(pt, payAmount);
                    // Partial payment accepted (D + remaining > 0) — continue to next payment
                    if (payResult.IsPartial)
                    {
                        LogPaymentDiagnostic($"Payment[{pi}] partial: remaining={payResult.Remaining:F2}, continuing...");
                        continue;
                    }
                    // Full payment (D=0 or R=change) — no more payments needed
                    if (payResult.Change > 0.001m)
                        LogPaymentDiagnostic($"Payment[{pi}] cu rest: change={payResult.Change:F2}");
                    break;
                }
                else
                {
                    await device.AddPaymentAsync(pt, payAmount);
                }
            }

            var closeResult = await device.CloseReceiptAsync();
            result.Success = true;
            result.ReceiptNumber = closeResult.ReceiptNumber;
            result.ProcessingDuration = DateTime.Now - startTime;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ErrorCode = ex is FiscalDeviceException ? -1 : null;
            try { await device.CancelReceiptAsync(); } catch { }
        }

        result.ProcessingDuration = DateTime.Now - startTime;
        return result;
    }

    private static List<ReceiptCommand> ReorderCommands(List<ReceiptCommand> commands)
    {
        int? stIdx = null;
        for (int i = 0; i < commands.Count; i++)
        {
            if (commands[i].Type == ReceiptCommandType.Subtotal)
            {
                stIdx = i;
                break;
            }
        }

        if (!stIdx.HasValue)
            return new List<ReceiptCommand>(commands);

        var result = new List<ReceiptCommand>();
        int j = 0;
        for (; j <= stIdx.Value; j++)
            result.Add(commands[j]);
        if (j < commands.Count && IsDiscountOrMarkup(commands[j].Type))
        {
            result.Add(commands[j]);
            j++;
        }
        var textAndBarcode = new List<ReceiptCommand>();
        for (; j < commands.Count; j++)
        {
            var c = commands[j];
            if (c.Type == ReceiptCommandType.Barcode || c.Type == ReceiptCommandType.TextLine)
                textAndBarcode.Add(c);
            else if (c.Type == ReceiptCommandType.Payment)
            {
                result.AddRange(textAndBarcode);
                textAndBarcode.Clear();
                for (int k = j; k < commands.Count; k++)
                    result.Add(commands[k]);
                return result;
            }
            else
                result.Add(c);
        }
        result.AddRange(textAndBarcode);
        return result;
    }

    private static bool IsDiscountOrMarkup(ReceiptCommandType t) =>
        t == ReceiptCommandType.DiscountPercent || t == ReceiptCommandType.DiscountValue ||
        t == ReceiptCommandType.MarkupPercent || t == ReceiptCommandType.MarkupValue;

    private static void LogPaymentDiagnostic(string message)
    {
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "payment_debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    /// <summary>
    /// Map bon file TipPlata (1-9) to PaymentType per Structura fisierului bonuri.
    /// Protocol Incotex: 1=Numerar(P), 2=Card(N), 3=Credit(I), 4=Tichet Masa(C), 5=Tichet Valoric(D), 6=Voucher(B).
    /// </summary>
    private static PaymentType MapPaymentType(int? paymentType)
    {
        return paymentType switch
        {
            0 or 1 => PaymentType.Cash,
            2 => PaymentType.Card,
            3 => PaymentType.Credit,
            4 => PaymentType.TicketMeal,   // Tichet masă
            5 => PaymentType.TicketValue,  // Tichet valoric / Bon valoric
            6 => PaymentType.Voucher,
            7 => PaymentType.Cash,        // Plata moderna → default Numerar
            _ => PaymentType.Cash
        };
    }
}
