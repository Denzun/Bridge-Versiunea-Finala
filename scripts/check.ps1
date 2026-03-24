# Safety Check Script - Simple Version
# Run this after making changes to verify everything works

param([switch]$SkipAppStart)

$root = "d:\Proiecte Cursor\POS Bridge"
cd $root

Write-Host "`n====== SAFETY CHECK ======`n" -ForegroundColor Cyan

# 1. Build
Write-Host "[1/7] Building..." -ForegroundColor Yellow
$build = dotnet build POSBridge.sln 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  PASS: Build successful" -ForegroundColor Green
} else {
    Write-Host "  FAIL: Build errors" -ForegroundColor Red
    $build | Select-String "error"
    exit 1
}

# 2. XAML
Write-Host "[2/7] Validating XAML..." -ForegroundColor Yellow
try {
    [xml]$x = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
    Write-Host "  PASS: XAML valid" -ForegroundColor Green
} catch {
    Write-Host "  FAIL: XAML error - $_" -ForegroundColor Red
    exit 1
}

# 3. Duplicates
Write-Host "[3/7] Checking duplicates..." -ForegroundColor Yellow
$dups = Select-String -Path "POSBridge.WPF\MainWindow.xaml" -Pattern 'x:Name="[^"]*"' | 
        ForEach-Object { $_.Matches.Value } | Group-Object | Where-Object { $_.Count -gt 1 }
if ($dups) {
    Write-Host "  FAIL: Duplicates found" -ForegroundColor Red
    $dups | ForEach-Object { Write-Host "    $($_.Name)" }
    exit 1
} else {
    Write-Host "  PASS: No duplicates" -ForegroundColor Green
}

# 4. Controls
Write-Host "[4/7] Checking controls..." -ForegroundColor Yellow
$req = @("ConnectButton", "DisconnectButton", "PortComboBox", "BaudComboBox")
$content = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
$miss = $req | Where-Object { $content -notmatch "x:Name=`"$_`"" }
if ($miss) {
    Write-Host "  FAIL: Missing controls" -ForegroundColor Red
    $miss | ForEach-Object { Write-Host "    $_" }
    exit 1
} else {
    Write-Host "  PASS: All controls present" -ForegroundColor Green
}

# 5. EXE
Write-Host "[5/7] Checking EXE..." -ForegroundColor Yellow
$exe = "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
if (Test-Path $exe) {
    Write-Host "  PASS: EXE exists" -ForegroundColor Green
} else {
    Write-Host "  FAIL: EXE not found" -ForegroundColor Red
    exit 1
}

# 6. COM Ports
Write-Host "[6/7] Checking COM ports..." -ForegroundColor Yellow
$ports = [System.IO.Ports.SerialPort]::getportnames()
if ($ports) {
    Write-Host "  PASS: Found $($ports -join ', ')" -ForegroundColor Green
} else {
    Write-Host "  WARN: No COM ports" -ForegroundColor Yellow
}

# 7. App Start
if (-not $SkipAppStart) {
    Write-Host "[7/7] Testing app start (5s)..." -ForegroundColor Yellow
    Stop-Process -Name "POSBridge.WPF" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    
    $proc = Start-Process -FilePath $exe -PassThru
    Start-Sleep -Seconds 5
    
    if (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) {
        Write-Host "  PASS: App started OK" -ForegroundColor Green
        Stop-Process -Id $proc.Id -Force
    } else {
        Write-Host "  FAIL: App crashed" -ForegroundColor Red
        $crash = "POSBridge.WPF\bin\Debug\net8.0-windows\crash.log"
        if (Test-Path $crash) {
            Get-Content $crash | Select -First 10
        }
        exit 1
    }
} else {
    Write-Host "[7/7] Skipped app start test" -ForegroundColor Yellow
}

Write-Host "`n====== ALL CHECKS PASSED ======`n" -ForegroundColor Green
Write-Host "Ready to test manually and commit!" -ForegroundColor Cyan
