# 🛡️ Script de Verificare Rapidă
# Rulează după modificări pentru a verifica că totul funcționează

param(
    [switch]$SkipAppStart
)

$projectRoot = "d:\Proiecte Cursor\POS Bridge"
cd $projectRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  🛡️  QUICK SAFETY CHECK" -ForegroundColor Cyan  
Write-Host "========================================" -ForegroundColor Cyan

# 1. Build Check
Write-Host "`n[1/7] Checking build..." -ForegroundColor Yellow
$buildOutput = dotnet build POSBridge.sln 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build successful" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build failed!" -ForegroundColor Red
    $buildOutput | Select-String "error" | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
}

# 2. XAML Check
Write-Host "`n[2/7] Checking XAML..." -ForegroundColor Yellow
try {
    [xml]$xaml = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
    Write-Host "  ✓ XAML is valid XML" -ForegroundColor Green
} catch {
    Write-Host "  ✗ XAML parsing error: $_" -ForegroundColor Red
    exit 1
}

# 3. Duplicate Names Check
Write-Host "`n[3/7] Checking for duplicate x:Name..." -ForegroundColor Yellow
$duplicates = Select-String -Path "POSBridge.WPF\MainWindow.xaml" -Pattern 'x:Name="[^"]*"' | 
              ForEach-Object { $_.Matches.Value } | 
              Group-Object | 
              Where-Object { $_.Count -gt 1 }
if ($duplicates) {
    Write-Host "  ✗ Duplicates found:" -ForegroundColor Red
    $duplicates | ForEach-Object { Write-Host "    $($_.Name) x$($_.Count)" -ForegroundColor Red }
    exit 1
} else {
    Write-Host "  ✓ No duplicates" -ForegroundColor Green
}

# 4. Critical Controls Check
Write-Host "`n[4/7] Checking critical controls..." -ForegroundColor Yellow
$critical = @("ConnectButton", "DisconnectButton", "PortComboBox", "BaudComboBox")
$xamlContent = Get-Content "POSBridge.WPF\MainWindow.xaml" -Raw
$missing = $critical | Where-Object { $xamlContent -notmatch "x:Name=`"$_`"" }
if ($missing) {
    Write-Host "  ✗ Missing controls:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
    exit 1
} else {
    Write-Host "  ✓ All critical controls present" -ForegroundColor Green
}

# 5. EXE Check
Write-Host "`n[5/7] Checking executable..." -ForegroundColor Yellow
$exePath = "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
if (Test-Path $exePath) {
    $exe = Get-Item $exePath
    Write-Host "  ✓ EXE exists ($($exe.Length) bytes)" -ForegroundColor Green
} else {
    Write-Host "  ✗ EXE not found" -ForegroundColor Red
    exit 1
}

# 6. COM Ports Check
Write-Host "`n[6/7] Checking COM ports..." -ForegroundColor Yellow
$ports = [System.IO.Ports.SerialPort]::getportnames()
if ($ports.Count -gt 0) {
    Write-Host "  ✓ Available: $($ports -join ', ')" -ForegroundColor Green
} else {
    Write-Host "  ⚠ No COM ports detected" -ForegroundColor Yellow
}

# 7. App Start Check
if (-not $SkipAppStart) {
    Write-Host "`n[7/7] Testing application start..." -ForegroundColor Yellow
    Write-Host "  Starting app for 5 seconds..." -ForegroundColor Gray
    
    Stop-Process -Name "POSBridge.WPF" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    
    $proc = Start-Process -FilePath $exePath -PassThru
    Start-Sleep -Seconds 5
    
    $running = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    
    if ($running) {
        Write-Host "  ✓ Application started successfully (PID: $($proc.Id))" -ForegroundColor Green
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "  ✗ Application crashed!" -ForegroundColor Red
        
        $crashLog = "POSBridge.WPF\bin\Debug\net8.0-windows\crash.log"
        if (Test-Path $crashLog) {
            Write-Host "`n  Crash log:" -ForegroundColor Red
            Get-Content $crashLog | Select-Object -First 15 | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        }
        exit 1
    }
} else {
    Write-Host "`n[7/7] App start test skipped" -ForegroundColor Yellow
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  ✓ ALL CHECKS PASSED!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`n📝 Ready to:" -ForegroundColor Cyan
Write-Host "  1. Test manually (start app, connect, test features)"
Write-Host "  2. Commit changes: git add . && git commit -m 'your message'"
