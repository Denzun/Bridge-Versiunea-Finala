# CHANGELOG - POS Bridge

## [Unreleased] - 2026-02-16

### Added - Funcționalități Cash In / Cash Out prin Folder Monitorizat

#### Noi Funcționalități
- **Cash In prin fișier**: Posibilitatea de a introduce numerar în casa de marcat prin creare fișier cu comanda `I^VALOARE^`
- **Cash Out prin fișier**: Posibilitatea de a scoate numerar din casa de marcat prin creare fișier cu comanda `O^VALOARE^`
- Operațiunile se execută automat la detectarea fișierelor în folderul monitorizat
- Comenzile sunt procesate ca "special commands" standalone (fără deschidere bon fiscal)

#### Modificări Tehnice

**POSBridge.Core/Services/FolderWatcherService.cs**
- Adăugată metoda `IsSpecialCommand()` pentru detectarea comenzilor speciale (X^, Z^, I^, O^)
- Modificată logica în `ProcessFileAsync()` pentru a redirecționa comenzile speciale către handler-ul corespunzător
- Suport pentru comenzi cu format: `I^valoare^` și `O^valoare^`

**POSBridge.WPF/MainWindow.xaml.cs**
- Extins `ProcessSpecialCommandAsync()` pentru a procesa comenzile Cash In (`I^`) și Cash Out (`O^`)
- Adăugat parsing și validare pentru valoarea numerară:
  - Verificare format numeric
  - Validare sumă > 0
  - Suport pentru format cu punct zecimal (ex: 150.00) sau fără (ex: 150)
- Mesaje de eroare clare pentru validări eșuate
- Actualizat mesajul de log la pornirea monitorizării pentru a include noile comenzi

**POSBridge.Devices.Datecs/FiscalEngine.cs**
- Adăugată verificare pentru comenzi standalone CashIn/CashOut în `ProcessCommandFile()`
- Comenzile I^ și O^ standalone se execută FĂRĂ a deschide bon fiscal
- Early return pentru procesare corectă (nu intră în fluxul normal de bon fiscal)
- Returnează `ReceiptNumber` specific cu suma procesată (ex: "Cash In: 150.00 lei")

#### Documentație
- Creat `Bon/README_CASHIN_CASHOUT.txt` cu ghid complet de utilizare
- Inclus exemple de comenzi și format
- Documentate erori comune și soluții
- Adăugat checklist de verificare după execuție

#### Fișiere de Test
- Creat exemple practice în `Bon/De tiparit/`:
  - `EXEMPLU_introducere_150lei.txt` - Exemplu Cash In
  - `EXEMPLU_scoatere_50lei.txt` - Exemplu Cash Out
  - `EXEMPLU_introducere_1000lei.txt` - Exemplu format simplu

#### Plan de Testare
- Creat `.cursor/TEST_PLAN_CASHIN_CASHOUT.txt` cu:
  - 10 scenarii de testare complete
  - Teste de validare pentru cazuri de eroare
  - Teste de integrare și compatibilitate
  - Checklist final pentru verificare

### Format Comenzi

#### Cash In (Introducere Numerar)
```
I^VALOARE^
```
**Exemple:**
- `I^150.00^` - Introduce 150.00 lei
- `I^50^` - Introduce 50 lei
- `I^1000.50^` - Introduce 1000.50 lei

#### Cash Out (Scoatere Numerar)
```
O^VALOARE^
```
**Exemple:**
- `O^50.00^` - Scoate 50.00 lei
- `O^25.50^` - Scoate 25.50 lei
- `O^100^` - Scoate 100 lei

### Comportament

1. **Detectare Automată**: Fișierele create în folderul monitorizat sunt detectate automat
2. **Procesare Serială**: Comenzile sunt procesate serial (thread-safe) prin `SemaphoreSlim`
3. **Validare**: 
   - Suma trebuie să fie > 0
   - Format numeric valid
   - Fișierul trebuie să conțină DOAR comanda I^ sau O^
4. **Rezultat**:
   - **Success**: Fișier mutat în `Bon/Procesate/`
   - **Eroare**: Fișier mutat în `Bon/Erori/` cu log de eroare
5. **Înregistrare**: Operațiunile apar în rapoartele fiscale X și Z

### Compatibilitate

- ✅ Compatibil backwards cu toate comenzile existente
- ✅ Nu afectează procesarea bonurilor normale
- ✅ Butoanele UI pentru CashIn/CashOut continuă să funcționeze normal
- ✅ Comenzile I^ și O^ în cadrul bonurilor fiscale sunt procesate diferit (în contextul bonului)

### Notițe Importante

- Comenzile I^ și O^ standalone **NU** deschid bon fiscal
- Sunt operațiuni de serviciu separate
- Se înregistrează în memoria fiscală
- Sunt incluse în rapoartele zilnice
- Procesarea este instant (fără confirmare utilizator)

### TODO (Următorii Pași)

- [ ] Testare cu dispozitiv fiscal real
- [ ] Verificare în rapoarte X și Z
- [ ] Testare scenarii multiple simultane
- [ ] Validare în producție

---

## Versiuni Anterioare

### [1.0.0] - Initial Release
- Implementare inițială POS Bridge
- Suport comenzi fiscale de bază
- Monitorizare folder pentru bonuri
- Rapoarte X/Z
- Comenzi speciale X^ și Z^
