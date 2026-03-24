# Test Plan - DUDE Auto-Launch

## Funcționalitate Implementată

Aplicația POSBridge verifică automat la pornire dacă DUDE rulează și îl lansează dacă este necesar. La închidere, POSBridge închide automat procesul DUDE.

## Modificări Efectuate

### 1. App.xaml.cs
- ✅ Adăugat proprietate statică `DudeProcess` pentru referință proces
- ✅ Adăugat metodă `LaunchDudeIfNeeded()` care:
  - Verifică dacă DUDE rulează deja (`Process.GetProcessesByName("DUDE")`)
  - Încarcă calea DUDE din settings
  - Lansează DUDE dacă nu rulează
  - Așteaptă 3 secunde pentru inițializare
  - Loghează toate acțiunile
  - Afișează mesaj de eroare dacă DUDE nu poate fi lansat, dar continuă pornirea aplicației

### 2. MainWindow.xaml.cs
- ✅ Adăugat câmp `_dudePath` cu valoare default
- ✅ Modificat `LoadSettings()` pentru a citi `DudePath` din settings.txt
- ✅ Modificat `SaveSettings()` pentru a salva `DudePath` în settings.txt
- ✅ Modificat `OnClosing()` pentru a închide procesul DUDE:
  - Verifică dacă `App.DudeProcess` există și nu s-a închis
  - Încearcă închidere grațioasă cu `CloseMainWindow()`
  - Așteaptă 5 secunde pentru închidere
  - Forțează închidere cu `Kill()` dacă este necesar
  - Loghează toate acțiunile

## Scenarii de Test

### Scenariu 1: DUDE nu rulează
**Pași:**
1. Asigură-te că DUDE nu rulează (Task Manager)
2. Pornește POSBridge.WPF.exe
3. Verifică în Task Manager că DUDE a fost lansat
4. Verifică în log-uri (Logs/app_YYYYMMDD.log) că apare mesajul "✓ DUDE lansat cu succes"

**Rezultat așteptat:**
- DUDE se lansează automat
- POSBridge se conectează la imprimantă după 3 secunde

### Scenariu 2: DUDE rulează deja
**Pași:**
1. Lansează manual DUDE
2. Pornește POSBridge.WPF.exe
3. Verifică în log-uri că apare "✓ DUDE rulează deja"

**Rezultat așteptat:**
- DUDE rămâne deschis (nu se lansează al doilea)
- POSBridge se conectează normal

### Scenariu 3: Închidere aplicație
**Pași:**
1. Pornește POSBridge (care va lansa DUDE)
2. Închide POSBridge prin butonul X sau Exit din tray
3. Verifică în Task Manager că DUDE s-a închis
4. Verifică în log-uri că apare "✓ Proces DUDE închis cu succes"

**Rezultat așteptat:**
- DUDE se închide automat
- Nu rămân procese orfane

### Scenariu 4: DUDE nu există la calea configurată
**Pași:**
1. Modifică în settings.txt: `DudePath=C:\Invalid\Path\DUDE.exe`
2. Pornește POSBridge
3. Verifică că apare MessageBox cu avertisment
4. Verifică că aplicația continuă să pornească

**Rezultat așteptat:**
- MessageBox cu eroare: "DUDE nu a fost găsit la calea configurată"
- Aplicația continuă să pornească (fără DUDE)

### Scenariu 5: Cale personalizată DUDE
**Pași:**
1. Creează fișier settings.txt cu:
   ```
   DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
   BonFolder=D:\Proiecte Cursor\POS Bridge\Bon
   OperatorCode=1
   OperatorPassword=0000
   RunAtStartup=1
   ComPort=COM7
   BaudRate=115200
   ```
2. Pornește POSBridge
3. Verifică în log că calea corectă este utilizată

**Rezultat așteptat:**
- DUDE se lansează de la calea specificată
- Settings sunt persistate corect

## Verificare Log-uri

### Log-uri în App (Logs/app_YYYYMMDD.log)
Căutați următoarele mesaje:
```
[HH:mm:ss] Verificare proces DUDE...
[HH:mm:ss] ✓ DUDE rulează deja
SAU
[HH:mm:ss] Lansare DUDE de la: C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
[HH:mm:ss] ✓ DUDE lansat cu succes (PID: 12345)
[HH:mm:ss] ✓ Așteptare inițializare DUDE completă
```

### Log-uri în MainWindow (Logs/log_YYYYMMDD.txt)
La închidere, căutați:
```
[HH:mm:ss] Închidere proces DUDE (PID: 12345)...
[HH:mm:ss] ✓ Proces DUDE închis cu succes
```

## Configurare Settings

Fișierul `settings.txt` implicit va conține:
```
BonFolder=D:\Proiecte Cursor\POS Bridge\Bon
OperatorCode=1
OperatorPassword=0000
RunAtStartup=1
ComPort=COM7
BaudRate=115200
DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

## Compilare

Proiectul compilează cu succes:
```
Build succeeded.
0 Error(s)
8 Warning(s) (pre-existente, nu legate de modificări)
```

## Note Importante

1. **Cale DUDE**: Default `C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe`
2. **Timp așteptare**: 3 secunde după lansare pentru inițializare DUDE
3. **Închidere grațioasă**: 5 secunde timeout pentru `CloseMainWindow()` înainte de `Kill()`
4. **Erori non-fatale**: Dacă DUDE nu poate fi lansat/închis, aplicația continuă funcționarea
5. **Logging complet**: Toate operațiunile DUDE sunt logate pentru debugging

## Probleme Cunoscute

Niciuna - implementarea este completă și testată prin compilare.

## Următori Pași

1. Testare manuală pe mașină cu DUDE instalat
2. Verificare comportament cu/fără DUDE
3. Testare scenarii de eroare (cale invalidă, DUDE blocat, etc.)
