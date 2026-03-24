# 🧪 Testare Rapidă - POS Bridge Folder Watcher

## ⚡ Quick Start (5 Minute Test)

### Pasul 1: Verificare Build

```powershell
cd "D:\Proiecte Cursor\POS Bridge"

# Build aplicația
dotnet build POSBridge.WPF\POSBridge.WPF.csproj --configuration Debug

# Verifică executabil
Test-Path "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
```

---

### Pasul 2: Pornire Aplicație

```powershell
# Lansează aplicația
Start-Process "POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe"
```

**UI Check**:
- ✅ Fereastra se deschide
- ✅ Header: "POS Bridge - Folder Watcher"
- ✅ Status LED roșu: "Not Connected"
- ✅ Log gol

---

### Pasul 3: Test Conexiune DUDE

**Click pe "Connect Device"**

#### ✅ Scenariu SUCCESS:
```
Log arată:
[14:30:45] Initializing DUDE COM Server...
[14:30:45] ✓ Connected to Datecs fiscal printer on COM5
[14:30:45] Device connected. Click 'Start Monitoring' to begin...
```
- LED devine VERDE
- Butoane active: Disconnect, Start Monitoring, Test Connection

#### ❌ Scenariu FAIL (DUDE nu este instalat):
```
Error Dialog:
"Failed to connect to fiscal printer:

Failed to initialize DUDE COM Server. Ensure DUDE is installed 
and application runs as x86.

Ensure:
- DUDE COM Server is installed
- Application is compiled as x86
- Device is connected to COM5"
```

**→ Continuă cu "Opțiunea B: Serial Direct" (vezi mai jos)**

---

### Pasul 4: Test Folder Watcher

**Dacă conexiunea a reușit:**

1. **Click "Start Monitoring"**
   ```
   Log:
   [14:30:46] ═══════════════════════════════════════
   [14:30:46] Starting folder watcher service...
   [14:30:46] ✓ Folder watcher is now active
   [14:30:46] Drop .txt files into: D:\Proiecte...\Bon
   [14:30:46] Format: NumeProdus|Pret|Cantitate|CotaTVA
   [14:30:46] ═══════════════════════════════════════
   ```

2. **Click "Open Folder"**
   - Se deschide Explorer în `.\Bon\`

3. **Creează fișier test**: `TEST_BON.txt`
   ```
   Produs Test|10.50|1|19
   Alt Produs|5.00|2|9
   ```

4. **Drop fișierul în Bon/**
   
5. **Verifică log-ul**:
   ```
   [14:30:47] New file detected: TEST_BON.txt
   [14:30:48] Processing: TEST_BON.txt...
   [14:30:48] Parsed 2 items from TEST_BON.txt
   [14:30:51] ✓ SUCCESS: TEST_BON.txt -> Receipt #123456 (2.8s)
   ```

6. **Verifică statistici**:
   - Total Processed: 1
   - Success: 1
   - Errors: 0

7. **Verifică folder Procesate/**:
   - Fișierul `TEST_BON.txt` este mutat aici

---

## 🔀 Opțiunea B: Testare cu Serial Direct (fără DUDE)

**Dacă DUDE nu este instalat**, ai două opțiuni:

### Opțiunea B1: Instalează DUDE (Recomandat Long-Term)

**Pas 1**: Descarcă DUDE
- **Locație**: `Drivere\DUDE\` (verifică dacă există installer)
- **Sau**: [Datecs Website](http://www.datecs.bg/en/products/fiscal-printers)

**Pas 2**: Instalează
```powershell
# Admin PowerShell
cd "D:\Proiecte Cursor\POS Bridge\Drivere\DUDE"
.\dude.exe  # Rulează installer
```

**Pas 3**: Verifică înregistrare COM
```powershell
Get-Item "HKLM:\SOFTWARE\Classes\Datecs.FiscalDevice" -ErrorAction SilentlyContinue
```

**Pas 4**: Restart aplicația POSBridge.WPF și reîncearcă Connect

---

### Opțiunea B2: Revert la Serial Direct (Funcțional ACUM)

**Avantaj**: Funcționează IMEDIAT, fără DUDE  
**Dezavantaj**: Pierde beneficiile oficiale ale DUDE (error handling robust)

#### Modificări Necesare:

**1. Creează `SerialFiscalEngine.cs`** (implementare directă serial)

```csharp
// Copiază din implementarea anterioară (din transcript)
// Folosește System.IO.Ports.SerialPort direct
```

**2. Modifică `MainWindow.xaml.cs`**:

```csharp
// În loc de:
private readonly FiscalEngine _fiscalEngine = FiscalEngine.Instance;

// Folosește:
private readonly SerialFiscalEngine _fiscalEngine = new();
```

**3. Rebuild și testează**

**Vrei să fac aceste modificări pentru Serial Direct?**
- ✅ PRO: Funcționează ACUM (5 minute)
- ❌ CON: Renunți la DUDE (dar poți reveni mai târziu)

---

## 🐛 Testare Erori

### Test 1: Format Invalid

**Fișier**: `ERROR_FORMAT.txt`
```
ProdusInvalid   // Lipsește pretul!
```

**Așteptat**:
- ❌ Eroare de parsing
- Fișier mutat în `Erori/`
- Creat `ERROR_FORMAT.txt.log` cu detalii

---

### Test 2: Nume Produs Prea Lung

**Fișier**: `ERROR_LENGTH.txt`
```
Produs Cu Nume Foarte Foarte Foarte Lung Care Depășește 30 Caractere|10.00|1|19
```

**Așteptat**:
- ✅ SUCCESS (nume truncat automat la 30 char)
- Bon emis cu: "Produs Cu Nume Foarte Foarte..."

---

### Test 3: Dispozitiv Deconectat Mid-Processing

**Pași**:
1. Start monitoring
2. Drop bon în Bon/
3. Deconectează USB-Serial în timpul procesării

**Așteptat**:
- ❌ Eroare COM
- Fișier în `Erori/` cu log: "Device disconnected"

---

## 📊 Testare Performanță

### Test Load: 10 Bonuri Simultan

**Script PowerShell**:
```powershell
# Creează 10 bonuri simultan
1..10 | ForEach-Object {
    $content = "Produs $_ |10.00|1|19"
    $content | Out-File "Bon\BON_$_.txt" -Encoding UTF8
}
```

**Așteptat**:
- Procesare SERIALIZATĂ (unul câte unul)
- Durată totală: ~20-30s pentru 10 bonuri
- Statistici: Total=10, Success=10, Errors=0

---

## ✅ Checklist Final

### Testare Completă

- [ ] Aplicația pornește fără erori
- [ ] Conexiunea DUDE reușește (sau Serial Direct)
- [ ] Folder watcher detectează fișiere noi
- [ ] Bonuri valide procesate cu succes
- [ ] Bonuri invalide mutate în Erori/ cu log
- [ ] Statistici actualizate corect
- [ ] Log în timp real funcțional
- [ ] "Open Folder" deschide Explorer
- [ ] "Test Connection" verifică dispozitivul
- [ ] "Clear Log" șterge log-ul
- [ ] "Stop Monitoring" oprește watcher-ul
- [ ] "Disconnect" deconectează dispozitivul

---

## 🆘 Troubleshooting Rapid

### Eroare: "FileSystemWatcher access denied"

**Soluție**: Rulează ca Administrator

```powershell
Start-Process "POSBridge.WPF.exe" -Verb RunAs
```

---

### Eroare: "File is being used by another process"

**Cauză**: Delay 500ms insuficient

**Soluție**: Crește delay în `FolderWatcherService.cs`:
```csharp
await Task.Delay(1000);  // Crește la 1000ms
```

---

### Log nu se actualizează

**Verifică**: Dispatcher thread

**Soluție**: Asigură-te că `Log()` folosește `Dispatcher.Invoke`

---

## 📞 Raportare Bug

**Template**:
```
Titlu: [BUG] Descriere scurtă

Pași de reproducere:
1. ...
2. ...

Comportament așteptat:
...

Comportament actual:
...

Log (copiat din aplicație):
[14:30:45] ...

Sistem:
- OS: Windows 11
- .NET: 8.0
- DUDE: 1.1.0.3
- Casa: Datecs DP-25MX
```

---

**Document creat**: 06 Feb 2026  
**Ultima actualizare**: 06 Feb 2026  
**Next Steps**: Alege între DUDE sau Serial Direct
