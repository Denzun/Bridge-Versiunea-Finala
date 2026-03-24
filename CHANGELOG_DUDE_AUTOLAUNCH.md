# Changelog - DUDE Auto-Launch

## [v1.2.0] - 2026-02-16

### Added
- **Auto-lansare DUDE**: POSBridge verifică și lansează automat aplicația DUDE la pornire
  - Verificare proces DUDE la startup
  - Lansare automată dacă nu rulează
  - Așteptare 3 secunde pentru inițializare
  - Logging complet în `Logs/app_YYYYMMDD.log`

- **Auto-închidere DUDE**: POSBridge închide automat DUDE la ieșire
  - Închidere grațioasă cu `CloseMainWindow()`
  - Timeout 5 secunde cu fallback la `Kill()`
  - Logging în `Logs/log_YYYYMMDD.txt`

- **Configurare cale DUDE**: Posibilitate de personalizare cale DUDE în settings
  - Parametru nou `DudePath` în `settings.txt`
  - Valoare default: `C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe`
  - Citire/salvare automată în settings

- **Gestionare erori robustă**:
  - MessageBox informativ dacă DUDE nu este găsit
  - Aplicația continuă să funcționeze chiar dacă DUDE nu poate fi lansat
  - Logging complet pentru debugging

### Changed
- **App.xaml.cs**:
  - Adăugat proprietate statică `DudeProcess` pentru referință proces
  - Adăugat metodă `LaunchDudeIfNeeded()` în `OnStartup`
  - Adăugat metodă `LoadDudePath()` pentru citire settings
  - Adăugat metodă `WriteLog()` pentru logging

- **MainWindow.xaml.cs**:
  - Adăugat câmp privat `_dudePath` cu valoare default
  - Modificat `LoadSettings()` pentru citire `DudePath` (tuple extins cu 1 element)
  - Modificat `SaveSettings()` pentru salvare `DudePath`
  - Modificat `OnClosing()` pentru închidere DUDE înainte de salvare settings

### Technical Details

#### App.xaml.cs
```csharp
// Nou: Proprietate statică pentru referință proces DUDE
public static Process? DudeProcess { get; set; }

// Nou: Verificare și lansare DUDE în OnStartup
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    // ... exception handlers ...
    LaunchDudeIfNeeded();
}
```

#### MainWindow.xaml.cs
```csharp
// Nou: Câmp pentru calea DUDE
private string _dudePath = @"C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe";

// Modificat: LoadSettings returnează tuple cu DudePath
private (string?, int, string, bool, string, int, string) LoadSettings()

// Modificat: SaveSettings salvează DudePath
private void SaveSettings()
{
    // ... include linie nouă: $"DudePath={_dudePath}"
}

// Modificat: OnClosing închide DUDE
protected override void OnClosing(CancelEventArgs e)
{
    // ... după deconectare și eliberare COM ...
    // Închide proces DUDE dacă există
}
```

### Files Modified
- `POSBridge.WPF/App.xaml.cs` - Adăugat logică lansare DUDE
- `POSBridge.WPF/MainWindow.xaml.cs` - Adăugat logică închidere DUDE și settings

### Files Added
- `README_DUDE_AUTOLAUNCH.md` - Documentație utilizator
- `IMPLEMENTATION_DUDE_AUTOLAUNCH.md` - Detalii tehnice implementare
- `TEST_DUDE_AUTOLAUNCH.md` - Plan de testare
- `CHANGELOG_DUDE_AUTOLAUNCH.md` - Acest fișier

### Breaking Changes
Niciuna. Funcționalitatea este backward compatible.

### Migration Guide
Nu este necesară migrare. La prima rulare, `DudePath` va fi adăugat automat în `settings.txt` cu valoarea default.

### Settings Format
```
BonFolder=D:\Proiecte Cursor\POS Bridge\Bon
OperatorCode=1
OperatorPassword=0000
RunAtStartup=1
ComPort=COM7
BaudRate=115200
DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

### Known Issues
Niciuna.

### Testing
- ✅ Compilare reușită (0 erori)
- ✅ Verificare linter (fără erori noi)
- ⏳ Testare manuală necesară (vezi TEST_DUDE_AUTOLAUNCH.md)

### Notes
- Timeout inițializare DUDE: 3 secunde
- Timeout închidere grațioasă DUDE: 5 secunde
- Toate operațiunile sunt logate pentru debugging
- Erorile sunt non-fatale - aplicația continuă funcționarea
