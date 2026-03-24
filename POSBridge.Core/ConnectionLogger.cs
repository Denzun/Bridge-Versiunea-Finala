namespace POSBridge.Core;

/// <summary>
/// Fișier dedicat pentru log-uri de conexiune - detalii despre IP, port, erori DUDE, etc.
/// Salvat în Logs/connection_log_YYYY-MM-DD.txt
/// </summary>
public static class ConnectionLogger
{
    private static readonly object Lock = new();
    private static string LogFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            string logFile = Path.Combine(LogFolder, $"connection_log_{DateTime.Now:yyyy-MM-dd}.txt");
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            lock (Lock)
            {
                File.AppendAllText(logFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignoră erorile de scriere
        }
    }

    public static void WriteSection(string title)
    {
        Write($"═══════════════════════════════════════");
        Write($"  {title}");
        Write($"═══════════════════════════════════════");
    }
}
