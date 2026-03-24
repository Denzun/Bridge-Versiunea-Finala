using POSBridge.Abstractions;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// Operațiuni GUI pentru Incotex Succes M7: Introducere/Scoatere numerar, Raport X, Raport Z.
/// Conform Protocol Comunicatie.txt: CMD 70 (46h) Numerar, CMD 69 (45h) Raport zilnic.
/// Utilizat de interfața principală când dispozitivul selectat este Incotex.
/// Nu afectează comunicarea cu Datecs.
/// </summary>
public static class IncotexOperations
{
    /// <summary>
    /// Introducere numerar în sertar. CMD 70 (46h) cu semn +.
    /// Protocol: Date: &lt;[Sign]Amount&gt;, [OpNumber], [Text]
    /// </summary>
    /// <param name="device">Dispozitiv Incotex conectat</param>
    /// <param name="amount">Suma în lei</param>
    /// <param name="description">Motiv opțional (max 38 caractere)</param>
    public static async Task CashInAsync(IFiscalDevice device, decimal amount, string description = "")
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));
        if (!device.IsConnected)
            throw new InvalidOperationException("Dispozitivul Incotex nu este conectat.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Suma trebuie să fie > 0.");

        await device.CashInAsync(amount, description);
    }

    /// <summary>
    /// Scoatere numerar din sertar. CMD 70 (46h) cu semn -.
    /// Protocol: Suma introdusă pentru "Plată din sertar" nu poate depăși numerarul din sertar.
    /// </summary>
    /// <param name="device">Dispozitiv Incotex conectat</param>
    /// <param name="amount">Suma în lei</param>
    /// <param name="description">Motiv opțional</param>
    public static async Task CashOutAsync(IFiscalDevice device, decimal amount, string description = "")
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));
        if (!device.IsConnected)
            throw new InvalidOperationException("Dispozitivul Incotex nu este conectat.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Suma trebuie să fie > 0.");

        await device.CashOutAsync(amount, description);
    }

    /// <summary>
    /// Raport X (intermediar). CMD 69 (45h) cu Option 2 sau 3.
    /// Nu resetează contoarele zilnice. Poate fi tipărit oricând.
    /// </summary>
    /// <param name="device">Dispozitiv Incotex conectat</param>
    public static async Task PrintXReportAsync(IFiscalDevice device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));
        if (!device.IsConnected)
            throw new InvalidOperationException("Dispozitivul Incotex nu este conectat.");

        await device.PrintDailyReportAsync("X");
    }

    /// <summary>
    /// Raport Z (zilnic cu zerare). CMD 69 (45h) cu Option 0 sau 1.
    /// Resetează contoarele zilnice. Operațiune finală de zi.
    /// </summary>
    /// <param name="device">Dispozitiv Incotex conectat</param>
    public static async Task PrintZReportAsync(IFiscalDevice device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));
        if (!device.IsConnected)
            throw new InvalidOperationException("Dispozitivul Incotex nu este conectat.");

        await device.PrintDailyReportAsync("Z");
    }
}
