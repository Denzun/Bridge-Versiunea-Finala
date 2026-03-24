using System.Diagnostics;
using System.Text;
using System.Text.Json;
using POSBridge.Devices.Datecs;

return await E2ELocalRunner.RunAsync(args);

internal static class E2ELocalRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        RunnerConfig config;
        try
        {
            config = RunnerConfig.FromArgs(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Invalid arguments: {ex.Message}");
            return 2;
        }

        var timestamp = DateTime.Now;
        var reportLines = new List<string>
        {
            $"POS Bridge E2E Local Report - {timestamp:yyyy-MM-dd HH:mm:ss}",
            $"ScenarioSet={config.ScenarioSet}",
            $"ComPort={config.ComPort}, BaudRate={config.BaudRate}, OperatorCode={config.OperatorCode}",
            $"WatchFolder={config.WatchFolder}",
            string.Empty
        };

        PrintStep("E2E local test started.");
        PrintStep($"Scenario set: {config.ScenarioSet}");
        PrintStep($"Watch folder: {config.WatchFolder}");

        var results = new List<ScenarioResult>();
        Process? guiProcess = null;

        try
        {
            EnsureFolderStructure(config.WatchFolder);

            ScenarioResult? resultA = null;
            var runPrecheckForFileScenarios = !config.ScenarioSet.Contains('A') &&
                                              config.ScenarioSet.IndexOfAny(new[] { 'B', 'C', 'D', 'E' }) >= 0;
            var shouldRunA = config.ScenarioSet.Contains('A') || runPrecheckForFileScenarios;
            if (shouldRunA)
            {
                resultA = await RunConnectionScenarioAsync(config);
                var aId = runPrecheckForFileScenarios ? "A*" : "A";
                var aName = runPrecheckForFileScenarios ? "ConnectionPrecheckForFileScenarios" : "ConnectionCheck";
                var normalizedA = resultA with { ScenarioId = aId, Name = aName };
                results.Add(normalizedA);
                reportLines.Add(FormatResult(normalizedA));
            }

            var requiresGui = config.ScenarioSet.IndexOfAny(new[] { 'B', 'C', 'D', 'E' }) >= 0 && !config.NoGui;
            if (requiresGui)
            {
                _ = EnsureGuiUsesWatchFolder(config);
                guiProcess = StartGuiProcess(config.WpfExePath);
                PrintStep($"GUI started. PID={guiProcess.Id}");
                PrintStep($"Waiting {config.GuiWarmupSeconds}s for GUI initialization...");
                await Task.Delay(TimeSpan.FromSeconds(config.GuiWarmupSeconds));
            }

            if (config.ScenarioSet.Contains('B'))
            {
                if (resultA is { Passed: false })
                {
                    var skippedB = new ScenarioResult(
                        "B",
                        "FiscalSuccessCashExact",
                        false,
                        $"Skipped due to connection precheck failure: {resultA.Details}");
                    results.Add(skippedB);
                    reportLines.Add(FormatResult(skippedB));
                }
                else
                {
                    var resultB = await RunFileScenarioAsync(
                        config,
                        scenarioId: "B",
                        scenarioName: "FiscalSuccessCashExact",
                        templatePath: Path.Combine(config.ScenariosFolder, "fiscal_success_cash_exact.txt"),
                        fallbackContent: "S^E2E PRODUS TEST^100^1000^buc^1^1" + Environment.NewLine + "P^1^100",
                        expectedBonOk: "1",
                        expectedFolder: "Procesate",
                        requireErrorLog: false);

                    results.Add(resultB);
                    reportLines.Add(FormatResult(resultB));
                }
            }

            if (config.ScenarioSet.Contains('D'))
            {
                if (resultA is { Passed: false })
                {
                    var skippedD = new ScenarioResult(
                        "D",
                        "FiscalCashOverpayChange",
                        false,
                        $"Skipped due to connection precheck failure: {resultA.Details}");
                    results.Add(skippedD);
                    reportLines.Add(FormatResult(skippedD));
                }
                else
                {
                    var resultD = await RunFileScenarioAsync(
                        config,
                        scenarioId: "D",
                        scenarioName: "FiscalCashOverpayChange",
                        templatePath: Path.Combine(config.ScenariosFolder, "fiscal_cash_overpay_change.txt"),
                        fallbackContent: "S^E2E PRODUS REST^13500^1000^buc^1^1" + Environment.NewLine + "P^1^20000",
                        expectedBonOk: "1",
                        expectedFolder: "Procesate",
                        requireErrorLog: false);

                    results.Add(resultD);
                    reportLines.Add(FormatResult(resultD));
                }
            }

            if (config.ScenarioSet.Contains('E'))
            {
                if (resultA is { Passed: false })
                {
                    var skippedE = new ScenarioResult(
                        "E",
                        "FiscalMixedCardCash",
                        false,
                        $"Skipped due to connection precheck failure: {resultA.Details}");
                    results.Add(skippedE);
                    reportLines.Add(FormatResult(skippedE));
                }
                else
                {
                    var resultE = await RunFileScenarioAsync(
                        config,
                        scenarioId: "E",
                        scenarioName: "FiscalMixedCardCash",
                        templatePath: Path.Combine(config.ScenariosFolder, "fiscal_mixed_card_cash.txt"),
                        fallbackContent: "S^E2E MIXT^2000^1000^buc^1^1" + Environment.NewLine + "P^2^1000" + Environment.NewLine + "P^1^1000",
                        expectedBonOk: "1",
                        expectedFolder: "Procesate",
                        requireErrorLog: false);

                    results.Add(resultE);
                    reportLines.Add(FormatResult(resultE));
                }
            }

            if (config.ScenarioSet.Contains('C'))
            {
                var resultC = await RunFileScenarioAsync(
                    config,
                    scenarioId: "C",
                    scenarioName: "ParserInvalidFile",
                    templatePath: Path.Combine(config.ScenariosFolder, "fiscal_invalid_format.txt"),
                    fallbackContent: "INVALID_LINE_WITHOUT_COMMAND_SEPARATOR",
                    expectedBonOk: "0",
                    expectedFolder: "Erori",
                    requireErrorLog: true);

                results.Add(resultC);
                reportLines.Add(FormatResult(resultC));
            }
        }
        catch (Exception ex)
        {
            var fatal = new ScenarioResult("FATAL", "UnexpectedFatalError", false, ex.Message);
            results.Add(fatal);
            reportLines.Add(FormatResult(fatal));
            PrintError($"Fatal error: {ex.Message}");
        }
        finally
        {
            if (guiProcess is not null && !config.KeepGuiOpen && !guiProcess.HasExited)
            {
                try
                {
                    guiProcess.Kill(entireProcessTree: true);
                    PrintStep("GUI process closed by runner.");
                }
                catch
                {
                    // best effort cleanup
                }
            }
        }

        var passed = results.All(r => r.Passed);
        var summary = passed
            ? $"E2E suite finished: PASS ({results.Count} scenario(s))."
            : $"E2E suite finished: FAIL ({results.Count(r => !r.Passed)} failed scenario(s) of {results.Count}).";

        reportLines.Add(string.Empty);
        reportLines.Add(summary);
        PrintStep(summary);

        var reportPath = WriteReport(config.WatchFolder, reportLines);
        PrintStep($"Report written: {reportPath}");

        if (config.KeepGuiOpen && guiProcess is not null && !guiProcess.HasExited)
            PrintStep("GUI remains open for live inspection.");

        return passed ? 0 : 1;
    }

    private static async Task<ScenarioResult> RunConnectionScenarioAsync(RunnerConfig config)
    {
        var started = DateTime.Now;
        PrintStep("[A] ConnectionCheck started.");

        var engine = FiscalEngine.Instance;
        engine.OperatorCode = config.OperatorCode;
        engine.OperatorPassword = config.OperatorPassword;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= config.ConnectionRetries; attempt++)
        {
            try
            {
                await Task.Run(() => engine.Initialize(config.ComPort, config.BaudRate));
                var connected = await Task.Run(engine.TestConnection);
                if (!connected)
                    throw new InvalidOperationException("Device not responding to Get_Status.");

                var pwdResult = await ValidateOrDiscoverPasswordAsync(engine, config);
                if (!pwdResult.IsValid)
                    throw new InvalidOperationException(pwdResult.Message);

                await Task.Run(engine.Disconnect);
                var details = $"Connected on {config.ComPort}@{config.BaudRate}. {pwdResult.Message}";
                return new ScenarioResult("A", "ConnectionCheck", true, details, started, DateTime.Now);
            }
            catch (Exception ex)
            {
                lastError = ex;
                PrintStep($"[A] Attempt {attempt}/{config.ConnectionRetries} failed: {ex.Message}");
                try { await Task.Run(engine.Disconnect); } catch { }

                if (attempt < config.ConnectionRetries)
                    await Task.Delay(config.ConnectionRetryDelayMs);
            }
        }

        return new ScenarioResult("A", "ConnectionCheck", false, lastError?.Message ?? "Unknown connection error.", started, DateTime.Now);
    }

    private static async Task<PasswordCheckResult> ValidateOrDiscoverPasswordAsync(FiscalEngine engine, RunnerConfig config)
    {
        try
        {
            var ok = await Task.Run(() => engine.TestOperatorCredentials(config.OperatorCode, config.OperatorPassword));
            if (ok)
                return new PasswordCheckResult(true, $"Operator credentials valid ({config.OperatorCode}/****).");
        }
        catch
        {
            // continue to fallback logic
        }

        if (!config.AutoFindPassword)
            return new PasswordCheckResult(false, "Operator credentials invalid and auto-discovery disabled.");

        foreach (var candidate in config.PasswordCandidates)
        {
            try
            {
                var ok = await Task.Run(() => engine.TestOperatorCredentials(config.OperatorCode, candidate));
                if (!ok)
                    continue;

                config.OperatorPassword = candidate;
                PrintStep($"[A] Auto-discovered operator password candidate: {candidate}");
                return new PasswordCheckResult(true, $"Operator password auto-discovered for code {config.OperatorCode}.");
            }
            catch
            {
                // try next
            }
        }

        return new PasswordCheckResult(false, "Operator credentials invalid. Auto-discovery exhausted candidate list.");
    }

    private static async Task<ScenarioResult> RunFileScenarioAsync(
        RunnerConfig config,
        string scenarioId,
        string scenarioName,
        string templatePath,
        string fallbackContent,
        string expectedBonOk,
        string expectedFolder,
        bool requireErrorLog)
    {
        var started = DateTime.Now;
        var fileName = $"E2E_{scenarioId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.txt";
        var sourcePath = Path.Combine(config.WatchFolder, fileName);
        var responsePath = Path.Combine(config.WatchFolder, "Raspuns", fileName);

        PrintStep($"[{scenarioId}] {scenarioName} started.");
        try
        {
            var content = File.Exists(templatePath) ? await File.ReadAllTextAsync(templatePath) : fallbackContent;
            await File.WriteAllTextAsync(sourcePath, content, Encoding.UTF8);
            PrintStep($"[{scenarioId}] Test file injected: {sourcePath}");

            var timeoutAt = DateTime.Now.AddSeconds(config.TimeoutSeconds);
            while (DateTime.Now < timeoutAt)
            {
                if (File.Exists(responsePath))
                    break;
                await Task.Delay(350);
            }

            if (!File.Exists(responsePath))
                return new ScenarioResult(scenarioId, scenarioName, false, $"Timeout waiting response file: {responsePath}", started, DateTime.Now);

            var response = ParseResponse(await File.ReadAllLinesAsync(responsePath));
            response.TryGetValue("BONOK", out var bonOk);

            if (!string.Equals(bonOk, expectedBonOk, StringComparison.OrdinalIgnoreCase))
            {
                response.TryGetValue("ERRCODE", out var errCode);
                response.TryGetValue("ERRINFO", out var errInfo);
                var mismatchDetails = $"Unexpected BONOK. Expected={expectedBonOk}, Actual={bonOk ?? "<missing>"}";
                if (!string.IsNullOrWhiteSpace(errCode) || !string.IsNullOrWhiteSpace(errInfo))
                    mismatchDetails += $"; ERRCODE={errCode ?? "<missing>"}; ERRINFO={errInfo ?? "<missing>"}";

                return new ScenarioResult(
                    scenarioId,
                    scenarioName,
                    false,
                    mismatchDetails,
                    started,
                    DateTime.Now);
            }

            var movedPath = FindMovedFile(Path.Combine(config.WatchFolder, expectedFolder), fileName);
            if (movedPath is null)
            {
                return new ScenarioResult(
                    scenarioId,
                    scenarioName,
                    false,
                    $"Source file not found in expected folder '{expectedFolder}'.",
                    started,
                    DateTime.Now);
            }

            if (requireErrorLog)
            {
                var logPath = movedPath + ".log";
                if (!File.Exists(logPath))
                {
                    return new ScenarioResult(
                        scenarioId,
                        scenarioName,
                        false,
                        $"Expected error log not found: {logPath}",
                        started,
                        DateTime.Now);
                }
            }

            if (!requireErrorLog && (!response.TryGetValue("NRBON", out var nrbon) || string.IsNullOrWhiteSpace(nrbon)))
            {
                return new ScenarioResult(
                    scenarioId,
                    scenarioName,
                    false,
                    "Missing NRBON in success response.",
                    started,
                    DateTime.Now);
            }

            var details = $"BONOK={bonOk}; moved={Path.GetFileName(movedPath)}";
            return new ScenarioResult(scenarioId, scenarioName, true, details, started, DateTime.Now);
        }
        catch (Exception ex)
        {
            return new ScenarioResult(scenarioId, scenarioName, false, ex.Message, started, DateTime.Now);
        }
    }

    private static Process StartGuiProcess(string wpfExePath)
    {
        if (!File.Exists(wpfExePath))
            throw new FileNotFoundException($"WPF executable not found: {wpfExePath}");

        var psi = new ProcessStartInfo
        {
            FileName = wpfExePath,
            WorkingDirectory = Path.GetDirectoryName(wpfExePath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start WPF GUI process.");
    }

    private static Dictionary<string, string> ParseResponse(IEnumerable<string> lines)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length == 2)
                dict[parts[0].Trim()] = parts[1].Trim();
        }
        return dict;
    }

    private static string? FindMovedFile(string folder, string originalFileName)
    {
        if (!Directory.Exists(folder))
            return null;

        var exact = Path.Combine(folder, originalFileName);
        if (File.Exists(exact))
            return exact;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFileName);
        var ext = Path.GetExtension(originalFileName);
        var candidates = Directory.GetFiles(folder, $"{nameWithoutExt}*{ext}");
        return candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    private static void EnsureFolderStructure(string watchFolder)
    {
        Directory.CreateDirectory(watchFolder);
        Directory.CreateDirectory(Path.Combine(watchFolder, "Procesate"));
        Directory.CreateDirectory(Path.Combine(watchFolder, "Erori"));
        Directory.CreateDirectory(Path.Combine(watchFolder, "Raspuns"));
        Directory.CreateDirectory(Path.Combine(watchFolder, "Istoric"));
    }

    private static string EnsureGuiUsesWatchFolder(RunnerConfig config)
    {
        var settingsDir = Path.GetDirectoryName(config.SettingsPath);
        if (!string.IsNullOrWhiteSpace(settingsDir))
            Directory.CreateDirectory(settingsDir);

        var lines = new List<string>
        {
            $"BonFolder={config.WatchFolder}",
            $"OperatorCode={config.OperatorCode}",
            $"OperatorPassword={config.OperatorPassword}"
        };
        File.WriteAllLines(config.SettingsPath, lines);

        PrintStep($"GUI settings redirected to watch folder: {config.WatchFolder}");
        return config.SettingsPath;
    }

    private static string WriteReport(string watchFolder, IEnumerable<string> lines)
    {
        var reportFolder = Path.Combine(watchFolder, "E2E");
        Directory.CreateDirectory(reportFolder);
        var reportPath = Path.Combine(reportFolder, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllLines(reportPath, lines);
        return reportPath;
    }

    private static string FormatResult(ScenarioResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        var duration = result.EndedAt.HasValue && result.StartedAt.HasValue
            ? $" ({(result.EndedAt.Value - result.StartedAt.Value).TotalSeconds:F2}s)"
            : string.Empty;
        return $"{status} [{result.ScenarioId}] {result.Name}{duration} - {result.Details}";
    }

    private static void PrintStep(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private static void PrintError(string message)
    {
        Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}

internal sealed record ScenarioResult(
    string ScenarioId,
    string Name,
    bool Passed,
    string Details,
    DateTime? StartedAt = null,
    DateTime? EndedAt = null);

internal sealed class RunnerConfig
{
    public string ComPort { get; private set; } = "COM6";
    public int BaudRate { get; private set; } = 115200;
    public int OperatorCode { get; private set; } = 1;
    public string OperatorPassword { get; set; } = "0000";
    public string ScenarioSet { get; private set; } = "ABC";
    public int TimeoutSeconds { get; private set; } = 120;
    public int GuiWarmupSeconds { get; private set; } = 5;
    public bool KeepGuiOpen { get; private set; } = true;
    public bool NoGui { get; private set; }
    public bool AutoFindPassword { get; private set; } = true;
    public int ConnectionRetries { get; private set; } = 3;
    public int ConnectionRetryDelayMs { get; private set; } = 1500;
    public List<string> PasswordCandidates { get; private set; } = new()
    {
        "0000", "1234", "1111", "0001", "9999", "1000", "0100", "00000", "12345", "11111", "99999"
    };

    public string RepoRoot { get; private set; } = string.Empty;
    public string WpfExePath { get; private set; } = string.Empty;
    public string SettingsPath { get; private set; } = string.Empty;
    public string WatchFolder { get; private set; } = string.Empty;
    public string ScenariosFolder { get; private set; } = string.Empty;

    public static RunnerConfig FromArgs(string[] args)
    {
        var config = new RunnerConfig();
        var map = ParseArgs(args);

        config.RepoRoot = ResolveRepoRoot();
        var deviceDefaults = LoadDeviceDefaults(Path.Combine(config.RepoRoot, "devices.json"));

        config.ComPort = ReadString(map, "--com-port", deviceDefaults.ComPort ?? config.ComPort);
        config.BaudRate = ReadInt(map, "--baud-rate", deviceDefaults.BaudRate ?? config.BaudRate);
        config.OperatorCode = ReadInt(map, "--operator-code", config.OperatorCode);
        config.OperatorPassword = ReadString(map, "--operator-password", config.OperatorPassword);
        config.ScenarioSet = NormalizeScenarioSet(ReadString(map, "--scenario-set", config.ScenarioSet));
        config.TimeoutSeconds = ReadInt(map, "--timeout-seconds", config.TimeoutSeconds);
        config.GuiWarmupSeconds = ReadInt(map, "--gui-warmup-seconds", config.GuiWarmupSeconds);
        config.KeepGuiOpen = ReadBool(map, "--keep-gui-open", true);
        config.NoGui = ReadBool(map, "--no-gui", false);
        config.AutoFindPassword = ReadBool(map, "--auto-find-password", true);
        config.ConnectionRetries = Math.Max(1, ReadInt(map, "--connection-retries", config.ConnectionRetries));
        config.ConnectionRetryDelayMs = Math.Max(250, ReadInt(map, "--connection-retry-delay-ms", config.ConnectionRetryDelayMs));

        if (map.TryGetValue("--password-candidates", out var rawCandidates) && !string.IsNullOrWhiteSpace(rawCandidates))
        {
            config.PasswordCandidates = rawCandidates
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        config.WpfExePath = Path.Combine(
            config.RepoRoot,
            "POSBridge.WPF",
            "bin",
            "Debug",
            "net8.0-windows",
            "POSBridge.WPF.exe");

        config.SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "POSBridge",
            "settings.txt");

        var baseWatchFolder = TryReadBonFolderFromSettings() ??
            Path.Combine(Path.GetDirectoryName(config.WpfExePath) ?? config.RepoRoot, "Bon");

        var normalizedBase = baseWatchFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var defaultWatchFolder = Path.GetFileName(normalizedBase).Equals("E2E_Run", StringComparison.OrdinalIgnoreCase)
            ? normalizedBase
            : Path.Combine(normalizedBase, "E2E_Run");

        config.WatchFolder = ReadString(map, "--watch-folder", defaultWatchFolder);
        config.ScenariosFolder = Path.Combine(config.RepoRoot, "POSBridge.E2E.Local", "Scenarios");

        return config;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                map[key] = args[i + 1];
                i++;
            }
            else
            {
                map[key] = "true";
            }
        }
        return map;
    }

    private static string ReadString(Dictionary<string, string> map, string key, string fallback)
        => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

    private static int ReadInt(Dictionary<string, string> map, string key, int fallback)
        => map.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;

    private static bool ReadBool(Dictionary<string, string> map, string key, bool fallback)
    {
        if (!map.TryGetValue(key, out var value))
            return fallback;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeScenarioSet(string raw)
    {
        var chars = raw.ToUpperInvariant().Where(c => c is 'A' or 'B' or 'C' or 'D' or 'E').Distinct().ToArray();
        if (chars.Length == 0)
            return "ABCDE";
        return new string(chars);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "POSBridge.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate POSBridge.sln from current execution path.");
    }

    private static (string? ComPort, int? BaudRate) LoadDeviceDefaults(string devicesJsonPath)
    {
        try
        {
            if (!File.Exists(devicesJsonPath))
                return (null, null);

            var json = File.ReadAllText(devicesJsonPath);
            var devices = JsonSerializer.Deserialize<List<DeviceConfig>>(json);
            var first = devices?.FirstOrDefault();
            if (first is null)
                return (null, null);
            return (first.ComPort, first.BaudRate);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? TryReadBonFolderFromSettings()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "POSBridge",
                "settings.txt");

            if (!File.Exists(settingsPath))
                return null;

            foreach (var line in File.ReadAllLines(settingsPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("BonFolder=", StringComparison.OrdinalIgnoreCase))
                    return trimmed.Split('=', 2)[1].Trim();
            }
        }
        catch
        {
            // ignore and fallback
        }

        return null;
    }
}

internal sealed class DeviceConfig
{
    public string? Name { get; set; }
    public string? ComPort { get; set; }
    public int? BaudRate { get; set; }
}

internal sealed record PasswordCheckResult(bool IsValid, string Message);

