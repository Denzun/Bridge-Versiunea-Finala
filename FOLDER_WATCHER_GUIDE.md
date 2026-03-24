# 📂 POS Bridge - Folder Watcher Guide

**Versiune:** 1.0.0 - Middleware Fiscal Datecs  
**Data:** 06 Februarie 2026  
**Autor:** POSBridge Development Team

---

## 🎯 Conceptul Aplicației

**POS Bridge Folder Watcher** este un middleware fiscal care automatizează procesarea bonurilor fiscale prin monitorizarea unui folder local. 

### Principiul de Funcționare

```
Sistem Gestiune → Crează fișier .txt → Folder "Bon/" → Watcher → Procesare Serializată → Casa Datecs
                                           ↓
                                    [Succes → Procesate/]
                                    [Eroare → Erori/ + log]
```

---

## 🏗️ Arhitectura Tehnică

### Componente Principale

1. **FiscalEngine** (Singleton)
   - Gestionează conexiunea la DUDE COM Server
   - Thread-safe cu lock intern
   - Procesează bonurile serializat (unul câte unul)

2. **FolderWatcherService**
   - Monitorizează folderul `.\Bon\` cu `FileSystemWatcher`
   - Queue serial cu `SemaphoreSlim(1,1)`
   - Delay 500ms înainte de citire (evită conflicte de acces)

3. **BonParser**
   - Parsează fișierele .txt în format: `NumeProdus|Pret|Cantitate|CotaTVA`
   - Validare și conversie automată la taxe DUDE

4. **UI WPF**
   - Log în timp real (Consolas font)
   - Statistici: Total/Success/Errors
   - Control Panel: Connect/Start/Stop

---

## 📝 Format Fișier BON

### Template Standard

```text
# Comentarii opționale (linie începe cu # sau //)

NumeProdus|Pret|Cantitate|CotaTVA
```

### Exemplu Concret

```text
Cafea Espresso|5.50|2|19
Croissant|4.00|1|19
Suc Natural|8.50|1|9
Apă Plată|2.00|3|9
Pâine Integrală|3.50||5
```

### Reguli de Validare

| Câmp | Tip | Obligatoriu | Limită | Default |
|------|-----|-------------|--------|---------|
| **NumeProdus** | String | ✅ DA | Max 30 char | - |
| **Pret** | Decimal | ✅ DA | > 0 | - |
| **Cantitate** | Decimal | ❌ NU | > 0 | 1.000 |
| **CotaTVA** | Integer | ❌ NU | 0/5/9/19% | 19% |

### Mapare Cote TVA → Tax Group

| Cotă TVA | Tax Group DUDE | Descriere |
|----------|----------------|-----------|
| 19% | 1 (Grupa A) | TVA Standard |
| 9% | 2 (Grupa B) | TVA Redus |
| 5% | 3 (Grupa C) | TVA Super-redus |
| 0% | 4 (Grupa D) | Fără TVA |

---

## 🚀 Workflow Complet

### 1. Inițializare

```
1. Pornire Aplicație
   ↓
2. Click "Connect Device"
   ↓
3. Inițializare DUDE COM (COM5, 115200 baud)
   ↓
4. Test Conexiune → LED Verde
   ↓
5. Click "Start Monitoring"
   ↓
6. FileSystemWatcher ACTIV pe .\Bon\
```

### 2. Procesare Automată

```
Fișier .txt apare în Bon/
   ↓
FileSystemWatcher detectează (Created event)
   ↓
Queue → SemaphoreSlim(1,1) asigură procesare serializată
   ↓
Delay 500ms (evită lock-uri de scriere)
   ↓
BonParser citește și validează
   ↓
FiscalEngine procesează:
   ├── Fiscal_Open (Cmd 48)
   ├── Fiscal_Sale (Cmd 49) × n items
   └── Fiscal_Total (Cmd 53)
   ↓
[SUCCES]                    [EROARE]
   ↓                           ↓
Mută în Procesate/         Mută în Erori/
                           Creează .log cu detalii
```

### 3. Gestiunea Erorilor

**Erori COM/Driver (negative)**:
- `-1`: COM Server not initialized
- `-2`: Connection failed
- `-3`: Command execution error

**Erori Device (positive)**:
- Consultă `Drivere\DUDE\DOCUMENTATION\ErrorCodes.xls`
- Exemplu: `30` = Hârtie terminată

**Log Format** (creat automat în `Erori/`):
```
Error Log for: BON_123.txt
Processed At: 2026-02-06 14:30:45
Duration: 1.23s
Error Code: 30
Error Message: Printer out of paper
```

---

## 🔧 Cerințe Tehnice

### Software

| Componență | Versiune | Scop |
|------------|----------|------|
| **.NET Runtime** | 8.0 (x86) | Framework aplicație |
| **DUDE COM Server** | 1.1.0.3 | Driver fiscal Datecs |
| **Windows** | 10/11 | OS suportat |

### Hardware

- **Casa de marcat**: Datecs DP-25MX / FP-2000 / FP-700X
- **Port Serial**: COM5 (configurabil)
- **Baud Rate**: 115200 (standard Datecs)

---

## ⚙️ Configurare Inițială

### Instalare DUDE COM Server

**IMPORTANT**: Aplicația necesită DUDE instalat și înregistrat ca server COM!

#### Opțiune 1: Auto-Instalare (Recomandat)

1. Rulează `dude.exe` din folderul instalare
2. Server-ul se auto-înregistrează automat
3. Verificare: Caută în Registry `HKEY_CLASSES_ROOT\Datecs.FiscalDevice`

#### Opțiune 2: Manual Registration

```powershell
# Admin PowerShell
regsvr32 "C:\Path\To\Datecs\dude.dll"
```

#### Verificare Instalare

```powershell
# Test registry key
Get-Item "HKLM:\SOFTWARE\Classes\Datecs.FiscalDevice" -ErrorAction SilentlyContinue
```

### Configurare COM Port

**Implicit**: COM5, 115200 baud

**Modificare** (în `FiscalEngine.cs`):
```csharp
_fiscalEngine.Initialize("COM3", 9600);  // Exemplu: COM3, 9600 baud
```

---

## 🖥️ Interfața Utilizator

### Status LED

| Culoare | Status | Descriere |
|---------|--------|-----------|
| 🔴 **Roșu** | Disconnected | Dispozitiv deconectat |
| 🟠 **Portocaliu** | Connected | Conectat, monitoring oprit |
| 🟢 **Verde** | Monitoring | Procesare activă |

### Statistici Live

- **Total Processed**: Număr total bonuri procesate
- **Success**: Bonuri emise cu succes
- **Errors**: Bonuri cu erori (mutate în Erori/)

### Log în Timp Real

```
[14:30:45] Starting folder watcher service...
[14:30:45] ✓ Folder watcher is now active
[14:30:47] New file detected: BON_001.txt
[14:30:48] Processing: BON_001.txt...
[14:30:48] Parsed 3 items from BON_001.txt
[14:30:51] ✓ SUCCESS: BON_001.txt -> Receipt #123456 (2.8s)
```

---

## 📚 Exemple de Utilizare

### Exemplu 1: Bon Standard Restaurant

**Fișier:** `BON_MASA_01.txt`
```
Supă de Legume|12.50|2|19
Paste Carbonara|28.00|1|19
Salată Caesar|18.50|1|19
Apă Minerală|5.00|2|9
```

**Rezultat**: Bon fiscal cu 4 produse, Total = ~86.00 LEI

---

### Exemplu 2: Bon Supermarket

**Fișier:** `CASA_03_20260206_1430.txt`
```
Pâine Albă|3.50|2|9
Lapte 3.5%|5.80|3|9
Telemea Vaci|22.00|0.350|9
Sare 1kg|2.50|1|5
Detergent Ariel|35.00|1|19
```

**Rezultat**: Bon fiscal mixt (produse cu TVA diferit)

---

### Exemplu 3: Bon cu Eroare

**Fișier:** `BON_ERROR.txt`
```
Produs Foarte Lung Care Depășește Limita de 30 Caractere|10.00|1|19
```

**Rezultat**: 
- ❌ Eroare: Nume prea lung (truncat automat la 30 char)
- ✅ Procesat cu nume: "Produs Foarte Lung Care Dep"

---

## 🛠️ Troubleshooting

### Problema 1: "DUDE COM Server not initialized"

**Cauze**:
- DUDE nu este instalat
- Aplicația nu rulează ca x86

**Soluții**:
1. Instalează DUDE din `Drivere\DUDE\`
2. Verifică Platform Target în `.csproj`:
   ```xml
   <PlatformTarget>x86</PlatformTarget>
   ```

---

### Problema 2: "Device not responding"

**Cauze**:
- Casa nu este conectată la COM5
- Baud rate incorect
- Cablu serial defect

**Soluții**:
1. Verifică în Device Manager: COM5 activ?
2. Test conexiune: `mode COM5` în CMD
3. Schimbă cablu USB-Serial

---

### Problema 3: Fișiere nu sunt procesate

**Cauze**:
- Monitoring nu este pornit
- Format fișier invalid
- Permisiuni folder

**Soluții**:
1. Click "Start Monitoring" după conectare
2. Verifică format: `NumeProdus|Pret|Cantitate|CotaTVA`
3. Rulează ca Administrator

---

### Problema 4: Eroare "Fiscal memory full"

**Cauză**: Memoria fiscală plină (necesită raport Z)

**Soluție**:
```csharp
// În FiscalEngine, adaugă metodă pentru raport Z
_dude.ExecuteSafe("PrintZReport");
```

---

## 📊 Performanță

### Metrici Tipice

- **Parsing**: ~10ms per bon
- **Procesare fiscală**: 1-3s per bon (depinde de nr. produse)
- **Throughput**: ~20-30 bonuri/minut (cu delay 500ms)

### Optimizări

**Nu recomandabil**:
- ❌ Procesare paralelă (casa nu suportă comenzi simultane)
- ❌ Reducere delay sub 500ms (risc lock-uri fișiere)

**Recomandabil**:
- ✅ Batch processing la ore cu trafic mic
- ✅ Pre-validare fișiere înainte de drop în Bon/

---

## 🔐 Securitate

### Accesul la Folder

**Recomandări**:
1. Folder `Bon/` doar pentru aplicație și sistem gestiune
2. Permisiuni: `Read/Write` pentru `SYSTEM`, `Read` pentru users
3. Audit trail: Log toate operațiunile în `activity.log`

### Backup

**Important**: Păstrează bonurile procesate!

```powershell
# Backup zilnic automat
$source = ".\Bon\Procesate"
$dest = "D:\Backup\Bonuri\$(Get-Date -Format 'yyyyMMdd')"
Copy-Item -Recurse $source $dest
```

---

## 🚦 Deployment Production

### Checklist Pre-Producție

- [ ] DUDE COM Server instalat și testat
- [ ] Casa de marcat configurată (COM port, baud rate)
- [ ] Test procesare 10+ bonuri mock
- [ ] Verificat folder Procesate/ și Erori/
- [ ] Backup plan pentru memoria fiscală
- [ ] Training operator (Connect → Start → Monitor)

### Instalare pe Workstation

1. **Copy binaries**:
   ```
   xcopy /E /I "bin\Debug\net8.0-windows" "C:\POSBridge\"
   ```

2. **Create shortcut Desktop**:
   - Target: `C:\POSBridge\POSBridge.WPF.exe`
   - Start in: `C:\POSBridge\`
   - Run as: Administrator (dacă e necesar)

3. **Configurare Autostart** (opțional):
   ```
   HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
   POSBridge = "C:\POSBridge\POSBridge.WPF.exe"
   ```

---

## 📞 Suport Tehnic

### Loguri de Debug

**Locație**: `.\Logs\` (va fi adăugat în versiunea următoare)

**Pentru suport**, trimite:
1. Screenshot aplicație (cu log vizibil)
2. Fișier bon care a eșuat (din `Erori/`)
3. Log file asociat (`.txt.log`)
4. Versiune DUDE: `dude.exe /version`

---

## 🔄 Versiuni

### v1.0.0 (2026-02-06) - Initial Release
- ✅ FolderWatcher cu FileSystemWatcher
- ✅ Procesare serializată cu SemaphoreSlim
- ✅ Integrare DUDE COM Server
- ✅ UI WPF modern cu log real-time
- ✅ Management automat fișiere (Procesate/Erori)
- ✅ Parser robust cu validare

### Roadmap

- [ ] **v1.1**: Configurare COM port din UI
- [ ] **v1.2**: Export statistici în Excel
- [ ] **v1.3**: REST API pentru interogare status
- [ ] **v1.4**: Notificări email la erori
- [ ] **v2.0**: Suport multi-dispozitiv (load balancing)

---

## 📄 Licență

**Proprietar** - © 2026 POSBridge Development Team  
Utilizare exclusivă pentru clienți autorizați.

---

**Documentație generată**: 06 Feb 2026  
**Ultima actualizare**: 06 Feb 2026  
**Contact**: support@posbridge.local
