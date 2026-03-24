# Script pentru repornire sigura POSBridge
# Inchide toate instantele aplicatiei si DUDE

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "RESTART POSBridge - Curatare procese" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Inchide toate instantele POSBridge.WPF
Write-Host "`n[1/3] Verificare si inchidere POSBridge.WPF..." -ForegroundColor Yellow
$posBridgeProcesses = Get-Process -Name "POSBridge.WPF" -ErrorAction SilentlyContinue
if ($posBridgeProcesses) {
    $count = ($posBridgeProcesses | Measure-Object).Count
    Write-Host "      Gasite $count instanta(e) POSBridge.WPF" -ForegroundColor White
    foreach ($proc in $posBridgeProcesses) {
        Write-Host "      Inchidere PID: $($proc.Id)..." -ForegroundColor Gray
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 500
    Write-Host "      OK POSBridge.WPF inchis" -ForegroundColor Green
} else {
    Write-Host "      OK Nicio instanta POSBridge.WPF gasita" -ForegroundColor Green
}

# 2. Inchide toate instantele DUDE
Write-Host "`n[2/3] Verificare si inchidere DUDE procese..." -ForegroundColor Yellow
$dudeProcesses = Get-Process -Name "DUDE" -ErrorAction SilentlyContinue
if ($dudeProcesses) {
    $count = ($dudeProcesses | Measure-Object).Count
    Write-Host "      Gasite $count instanta(e) DUDE" -ForegroundColor White
    foreach ($proc in $dudeProcesses) {
        Write-Host "      Inchidere DUDE PID: $($proc.Id)..." -ForegroundColor Gray
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 500
    Write-Host "      OK DUDE inchis" -ForegroundColor Green
} else {
    Write-Host "      OK Nicio instanta DUDE gasita" -ForegroundColor Green
}

# 3. Verificare finala - asigura-te ca procesele sunt inchise
Write-Host "`n[3/3] Verificare finala..." -ForegroundColor Yellow
Start-Sleep -Milliseconds 300

$remainingPOS = Get-Process -Name "POSBridge.WPF" -ErrorAction SilentlyContinue
$remainingDUDE = Get-Process -Name "DUDE" -ErrorAction SilentlyContinue

if ($remainingPOS -or $remainingDUDE) {
    Write-Host "      ATENTIE: Procese ramase active!" -ForegroundColor Red
    if ($remainingPOS) {
        Write-Host "        - POSBridge.WPF: $($remainingPOS.Count) proces(e)" -ForegroundColor Red
    }
    if ($remainingDUDE) {
        Write-Host "        - DUDE: $($remainingDUDE.Count) proces(e)" -ForegroundColor Red
    }
} else {
    Write-Host "      OK Toate procesele au fost inchise cu succes" -ForegroundColor Green
}

# 4. Pornire aplicatie
Write-Host "`n[4/4] Pornire POSBridge..." -ForegroundColor Yellow
Start-Sleep -Milliseconds 500

Start-Process "Distributie\POSBridge\POSBridge.WPF.exe" -WorkingDirectory "Distributie\POSBridge"
Start-Sleep -Milliseconds 1000

$newProcess = Get-Process -Name "POSBridge.WPF" -ErrorAction SilentlyContinue
if ($newProcess) {
    Write-Host "      OK POSBridge pornit cu succes (PID: $($newProcess.Id))" -ForegroundColor Green
} else {
    Write-Host "      ATENTIE: POSBridge pornit, dar procesul nu este vizibil imediat" -ForegroundColor Yellow
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "RESTART COMPLET" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
