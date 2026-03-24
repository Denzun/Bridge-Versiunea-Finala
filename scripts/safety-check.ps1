# 🛡️ Script de Verificare Pre/Post Modificare
# Rulează acest script înainte și după orice modificare majoră

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("pre", "post")]
    [string]$Mode = "post"
)

$ErrorActionPreference = "Continue"
$projectRoot = "d:\Proiecte Cursor\POS Bridge"
$passed = 0
$failed = 0
$warnings = 0

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    $color = switch($Type) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        default { "White" }
    }
    Write-Host $Message -ForegroundColor $color
}

function Test-Check {
    param([string]$Name, [scriptblock]$Check)
    Write-Host "`n[CHECK] $Name" -ForegroundColor Cyan
    try {
        $result = & $Check
        if ($result) {
            Write-Status "  ✓ PASS" "Success"
            $script:passed++
            return $true
        } else {
            Write-Status "  ✗ FAIL" "Error"
            $script:failed++
            return $false
        }
    }
    catch {
        Write-Status "  ✗ ERROR: $_" "Error"
        $script:failed++
        return $false
    }
}

Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  🛡️  DEVELOPMENT SAFETY CHECK" -ForegroundColor Magenta
Write-Host "  Mode: $Mode" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta

cd $projectRoot

# ==================== PRE-MODIFICATION CHECKS ====================

if ($Mode -eq "pre") {
    Write-Host "`n📋 PRE-MODIFICATION CHECKS`n" -ForegroundColor Yellow
    
    Test-Check "Git repository is clean (no uncommitted changes)" {
        $status = git status --porcelain
        if ($status) {
            Write-Status "  ⚠ You have uncommitted changes. Consider committing before modifying." "Warning"
            $script:warnings++
            git status --short
        }
        return $true
    }
    
    Test-Check "Application compiles successfully" {
        $output = dotnet build POSBridge.sln 2>&1
        $success = $LASTEXITCODE -eq 0
        if (-not $success) {
            Write-Status "  Build errors found:" "Error"
            $output | Select-String "error" | ForEach-Object { Write-Host "    $_" }
        }
        return $success
    }
    
    Test-Check "Application can start" {
        $exePath = "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
        if (-not (Test-Path $exePath)) {
            Write-Status "  EXE not found at $exePath" "Error"
            return $false
        }
        
        # Start and quickly check if it crashes
        $proc = Start-Process -FilePath $exePath -PassThru
        Start-Sleep -Seconds 3
        $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        
        if ($running) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            return $true
        } else {
            Write-Status "  Application crashed on startup" "Error"
            return $false
        }
    }
    
    Test-Check "COM ports are available" {
        $ports = [System.IO.Ports.SerialPort]::getportnames()
        if ($ports.Count -gt 0) {
            Write-Status "  Available ports: $($ports -join ', ')" "Success"
            return $true
        } else {
            Write-Status "  ⚠ No COM ports detected" "Warning"
            $script:warnings++
            return $true
        }
    }
    
    Write-Host "`n" -NoNewline
    Write-Status "✓ Pre-modification checks complete!" "Success"
    Write-Status "  You can now make your modifications." "Info"
    Write-Status "  Run this script with -Mode post after changes." "Info"
}

# ==================== POST-MODIFICATION CHECKS ====================

if ($Mode -eq "post") {
    Write-Host "`n📋 POST-MODIFICATION CHECKS`n" -ForegroundColor Yellow
    
    Test-Check "Solution builds without errors" {
        $output = dotnet build POSBridge.sln 2>&1
        $success = $LASTEXITCODE -eq 0
        if (-not $success) {
            Write-Status "  Build errors found:" "Error"
            $output | Select-String "error" | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        }
        
        # Check for warnings
        $warningCount = ($output | Select-String "Warning\(s\)" | Select-String "(\d+) Warning").Matches.Groups[1].Value
        if ($warningCount -and [int]$warningCount -gt 0) {
            Write-Status "  ⚠ $warningCount warning(s) found" "Warning"
            $script:warnings++
        }
        
        return $success
    }
    
    Test-Check "XAML is valid XML" {
        try {
            [xml]$xaml = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
            Write-Status "  XAML parsed successfully" "Success"
            return $true
        }
        catch {
            Write-Status "  XAML parsing error: $_" "Error"
            return $false
        }
    }
    
    Test-Check "No duplicate x:Name in XAML" {
        $duplicates = Select-String -Path "POSBridge.WPF\MainWindow.xaml" -Pattern 'x:Name="[^"]*"' | 
                      ForEach-Object { $_.Matches.Value } | 
                      Group-Object | 
                      Where-Object { $_.Count -gt 1 }
        
        if ($duplicates) {
            Write-Status "  Duplicate x:Name found:" "Error"
            $duplicates | ForEach-Object { Write-Host "    $($_.Name) appears $($_.Count) times" }
            return $false
        }
        return $true
    }
    
    Test-Check "Critical controls exist in XAML" {
        $criticalControls = @(
            "ConnectButton",
            "DisconnectButton",
            "PortComboBox",
            "BaudComboBox",
            "OperatorCodeBox",
            "OperatorPasswordBox",
            "ActivityLogBox",
            "StatusBarText"
        )
        
        $xaml = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
        $missing = @()
        
        foreach ($control in $criticalControls) {
            if ($xaml -notmatch "x:Name=`"$control`"") {
                $missing += $control
            }
        }
        
        if ($missing.Count -gt 0) {
            Write-Status "  Missing critical controls:" "Error"
            $missing | ForEach-Object { Write-Host "    $_" }
            return $false
        }
        return $true
    }
    
    Test-Check "Auto-connect is enabled in code" {
        $content = Get-Content "POSBridge.WPF\MainWindow.xaml.cs" -Raw
        $autoConnectEnabled = $content -match 'await ConnectAndMaybeStartWatcherAsync\(autoStartWatcher: true\);' -and
                             $content -notmatch '//\s*await ConnectAndMaybeStartWatcherAsync'
        
        if (-not $autoConnectEnabled) {
            Write-Status "  ⚠ Auto-connect appears to be disabled/commented" "Warning"
            $script:warnings++
        }
        return $true
    }
    
    Test-Check "No crash.log exists from previous run" {
        $crashLog = "POSBridge.WPF\bin\Debug\net8.0-windows\crash.log"
        if (Test-Path $crashLog) {
            Write-Status "  ⚠ crash.log found - application may have crashed:" "Warning"
            Get-Content $crashLog | Select-Object -First 10 | ForEach-Object { Write-Host "    $_" }
            Remove-Item $crashLog -Force
            $script:warnings++
        }
        return $true
    }
    
    Test-Check "Application executable exists" {
        $exePath = "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
        if (Test-Path $exePath) {
            $exe = Get-Item $exePath
            Write-Status "  EXE: $($exe.Name) ($($exe.Length) bytes, modified: $($exe.LastWriteTime))" "Success"
            return $true
        } else {
            Write-Status "  EXE not found at $exePath" "Error"
            return $false
        }
    }
    
    Test-Check "Application starts without crashing" {
        Write-Status "  Starting application for 5 seconds..." "Info"
        $exePath = "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
        
        # Stop any existing instances
        Stop-Process -Name "POSBridge.WPF" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        
        $proc = Start-Process -FilePath $exePath -PassThru
        Start-Sleep -Seconds 5
        
        $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        
        if ($running) {
            Write-Status "  Application is running (PID: $($proc.Id))" "Success"
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            return $true
        } else {
            Write-Status "  Application crashed within 5 seconds" "Error"
            
            # Check for crash log
            $crashLog = "POSBridge.WPF\bin\Debug\net8.0-windows\crash.log"
            if (Test-Path $crashLog) {
                Write-Status "  Crash log contents:" "Error"
                Get-Content $crashLog | Select-Object -First 20 | ForEach-Object { Write-Host "    $_" }
            }
            return $false
        }
    }
    
    Test-Check "Bon folder structure exists" {
        $bonPath = "POSBridge.WPF\bin\Debug\net8.0-windows\Bon"
        $requiredFolders = @("Erori", "Procesate")
        
        if (-not (Test-Path $bonPath)) {
            Write-Status "  Bon folder not found at $bonPath" "Error"
            return $false
        }
        
        $missing = @()
        foreach ($folder in $requiredFolders) {
            $fullPath = Join-Path $bonPath $folder
            if (-not (Test-Path $fullPath)) {
                $missing += $folder
            }
        }
        
        if ($missing.Count -gt 0) {
            Write-Status "  ⚠ Missing subfolders: $($missing -join ', ')" "Warning"
            $script:warnings++
        }
        
        return $true
    }
}

# ==================== SUMMARY ====================

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  RESULTS SUMMARY" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "✓ Passed:   $passed" -ForegroundColor Green
Write-Host "✗ Failed:   $failed" -ForegroundColor Red
Write-Host "⚠ Warnings: $warnings" -ForegroundColor Yellow

if ($failed -eq 0) {
    Write-Host "`n🎉 All critical checks passed!" -ForegroundColor Green
    
    if ($Mode -eq "post") {
        Write-Host "`n📝 Next steps:" -ForegroundColor Cyan
        Write-Host "  1. Test connection manually (COM6, 115200, Op: 1/0001)"
        Write-Host "  2. Test a simple fiscal receipt"
        Write-Host "  3. Test new functionality"
        Write-Host "  4. Commit your changes:"
        Write-Host "     git add ."
        Write-Host "     git commit -m 'feat: your description'"
    }
    
    exit 0
} else {
    Write-Host "`n❌ Some checks failed. Please fix issues before proceeding." -ForegroundColor Red
    exit 1
}
