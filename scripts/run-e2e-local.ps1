param(
    [string]$ComPort = "",
    [int]$BaudRate = 0,
    [int]$OperatorCode = 1,
    [string]$OperatorPassword = "0000",
    [string]$ScenarioSet = "ABC",
    [int]$TimeoutSeconds = 120,
    [int]$GuiWarmupSeconds = 5,
    [int]$ConnectionRetries = 3,
    [int]$ConnectionRetryDelayMs = 1500,
    [string]$PasswordCandidates = "",
    [string]$WatchFolder = "",
    [switch]$AutoFindPassword = $true,
    [switch]$KeepGuiOpen
)

$ErrorActionPreference = "Stop"

Write-Host "[E2E] Closing previous GUI instances (if any)..."
Get-Process -Name "POSBridge.WPF" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "[E2E] Building WPF and E2E runner..."
dotnet build "POSBridge.WPF/POSBridge.WPF.csproj" --configuration Debug | Out-Host
dotnet build "POSBridge.E2E.Local/POSBridge.E2E.Local.csproj" --configuration Debug | Out-Host

$runnerArgs = @("--scenario-set", $ScenarioSet, "--operator-code", $OperatorCode, "--operator-password", $OperatorPassword, "--timeout-seconds", $TimeoutSeconds, "--gui-warmup-seconds", $GuiWarmupSeconds, "--connection-retries", $ConnectionRetries, "--connection-retry-delay-ms", $ConnectionRetryDelayMs)
if (-not [string]::IsNullOrWhiteSpace($ComPort)) { $runnerArgs += @("--com-port", $ComPort) }
if ($BaudRate -gt 0) { $runnerArgs += @("--baud-rate", $BaudRate) }
if ($KeepGuiOpen.IsPresent) { $runnerArgs += @("--keep-gui-open", "true") }
if (-not $AutoFindPassword.IsPresent) { $runnerArgs += @("--auto-find-password", "false") }
if (-not [string]::IsNullOrWhiteSpace($PasswordCandidates)) { $runnerArgs += @("--password-candidates", $PasswordCandidates) }
if (-not [string]::IsNullOrWhiteSpace($WatchFolder)) { $runnerArgs += @("--watch-folder", $WatchFolder) }

Write-Host "[E2E] Running local suite with GUI visibility..."
Write-Host "[E2E] Arguments: $($runnerArgs -join ' ')"

dotnet run --project "POSBridge.E2E.Local/POSBridge.E2E.Local.csproj" --configuration Debug -- @runnerArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "[E2E] Suite failed with exit code $LASTEXITCODE. Fix code and rerun failed scenario, then rerun ABC." -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "[E2E] Suite completed successfully." -ForegroundColor Green
