# POS Bridge - Distribution Package
## Version 1.1.0 - Cash In/Out Support

---

## 📦 CONȚINUT PACHET

Acest folder conține aplicația POS Bridge compilată și gata de utilizare.

```
POSBridge/
├── POSBridge.WPF.exe          ← Aplicația principală
├── POSBridge.Core.dll
├── POSBridge.Devices.Datecs.dll
├── POSBridge.Devices.Incotex.dll
├── Bon/                       ← Folder monitorizat (bonuri de printat)
│   ├── Procesate/             ← Bonuri tipărite cu succes
│   ├── Erori/                 ← Bonuri cu erori
│   ├── Istoric/               ← Arhivă
│   └── README_CASHIN_CASHOUT.txt
├── *.dll                      ← Dependențe .NET
└── runtimes/                  ← Runtime components
```

---

## 🚀 INSTALARE

### Cerințe:
- Windows 10/11
- .NET 8.0 Runtime (se instalează automat sau manual)
- Imprimantă fiscală Datecs compatibilă
- DUDE COM Server instalat

### Pași:
1. Copiază tot folder-ul `POSBridge/` pe calculatorul țintă
2. Plasează-l într-o locație permanentă (ex: `C:\Program Files\POSBridge\`)
3. Rulează `POSBridge.WPF.exe`
4. Configurează portul COM și credențialele operatorului
5. Pornește monitorizarea

---

## ⚡ PORNIRE RAPIDĂ

1. **Pornește aplicația**: Dublu-click pe `POSBridge.WPF.exe`

2. **Configurează conexiunea**:
   - Selectează portul COM (ex: COM7)
   - Selectează baud rate (implicit: 115200)
   - Introdu codul operatorului (implicit: 1)
   - Introdu parola operatorului (implicit: 0000)

3. **Conectează**:
   - Click pe butonul "Conectare dispozitiv"
   - Așteaptă confirmarea conexiunii

4. **Pornește monitorizarea**:
   - Click pe "Pornește monitorizarea"
   - Aplicația monitorizează automat folder-ul `Bon/`

5. **Procesează bonuri/comenzi**:
   - Adaugă fișiere .txt în folder-ul `Bon/`
   - Aplicația le procesează automat
   - Verifică în jurnal statusul procesării

---

## 🆕 NOI în v1.1.0: Cash In / Cash Out

### INTRODUCERE NUMERAR (Cash In):
Creează fișier `introducere.txt`:
```
I^150.00^
```
→ Introduce 150 lei în casa de marcat

### SCOATERE NUMERAR (Cash Out):
Creează fișier `scoatere.txt`:
```
O^50.00^
```
→ Scoate 50 lei din casa de marcat

**Documentație completă**: Vezi `Bon/README_CASHIN_CASHOUT.txt` după prima rulare

---

## 📁 STRUCTURĂ FOLDERE

După prima rulare, aplicația creează automat:

```
[Locație aplicație]/
├── POSBridge.WPF.exe
├── settings.txt              ← Setări salvate automat
├── Logs/                     ← Jurnale zilnice
│   └── log_2026-02-16.txt
└── Bon/                      ← Folder monitorizat
    ├── [fisiere noi]         ← Aici se plasează comenzile
    ├── Procesate/            ← Comenzi procesate cu succes
    ├── Erori/                ← Comenzi cu erori
    └── Istoric/              ← Arhivă
```

**IMPORTANT**: Nu șterge folder-ul `Bon/` - este necesar pentru funcționare!

---

## 📝 FORMAT COMENZI

### Bon Fiscal Simplu:
```
S^Produs Test^10.00^1^buc^1^1^
P^1^10.00^
```

### Cu Discount:
```
S^Produs^100.00^1^buc^1^1^
DP^10^
ST^
P^1^90.00^
```

### Rapoarte:
```
X^          → Raport X (intermediar)
Z^          → Raport Z (cu zerare)
```

### Cash Operations (NOU v1.1.0):
```
I^150.00^   → Introduce 150 lei
O^50.00^    → Scoate 50 lei
```

---

## ⚙️ CONFIGURARE AVANSATĂ

### settings.txt
Acest fișier se creează automat și salvează:
- Calea folder-ului monitorizat
- Codul și parola operatorului
- Portul COM și baud rate
- Pornire automată cu Windows

### Pornire la Startup Windows:
- Bifează checkbox-ul "Pornește automat cu Windows" din aplicație
- Aplicația se adaugă automat în registru
- La pornirea Windows, se conectează automat și pornește monitorizarea

---

## 🔧 DEPANARE

### Aplicația nu pornește:
- Verifică că ai .NET 8.0 Runtime instalat
- Rulează ca Administrator (dacă e necesar)
- Verifică antivirus-ul (poate bloca .exe-ul)

### Nu se conectează la imprimantă:
- Verifică că DUDE COM Server este instalat
- Verifică portul COM în Device Manager
- Verifică că imprimanta este pornită
- Testează conexiunea cu butonul "Test conexiune"

### Fișierele nu se procesează:
- Verifică că monitorizarea este pornită (LED verde)
- Verifică că fișierele sunt în folder-ul `Bon/` (nu în subfoldere)
- Verifică formatul comenzilor (vezi documentația)
- Consultă jurnalul din `Logs/` pentru detalii

### Erori la procesare:
- Fișierele cu erori se mută în `Bon/Erori/`
- Verifică fișierul `.log` asociat pentru detalii eroare
- Corectează și reîncearcă (copiază din Erori/ înapoi în Bon/)

---

## 📊 MONITORIZARE

### Jurnal Aplicație:
- Vizibil în aplicație în panoul "Jurnal"
- Salvat zilnic în `Logs/log_[data].txt`
- Curățare automată: păstrează ultimele 30 zile

### Statistici:
Aplicația afișează în timp real:
- Total procesate
- Succese
- Erori
- Ultimul fișier procesat

---

## 🔒 SECURITATE

### Credențiale Operator:
- Stocate în `settings.txt` (plain text)
- Folosite pentru autentificare la dispozitiv
- Modifică din UI → se salvează automat

### Permisiuni:
- Aplicația rulează cu permisiunile utilizatorului curent
- Nu necesită drepturi de Administrator (în condiții normale)
- Portul COM trebuie să fie accesibil

---

## 📈 PERFORMANȚĂ

### Specificații:
- Procesare serială (thread-safe)
- ~2-3 secunde per bon (depinde de complexitate)
- Delay 500ms între detectare și procesare
- Suportă sute de fișiere în coadă

### Optimizări:
- Nu crea fișiere foarte mari (> 1000 linii)
- Folosește fișiere separate pentru comenzi multiple
- Curăță periodic folder-ul `Procesate/`

---

## 🆘 SUPORT

### Documentație:
- `Bon/README_CASHIN_CASHOUT.txt` - Ghid Cash In/Out
- `RELEASE_NOTES_v1.1.0.md` - Detalii versiune
- `CHANGELOG_CASHIN_CASHOUT.md` - Istoric modificări

### Log-uri:
- Aplicație: `Logs/log_[data].txt`
- Erori procesare: `Bon/Erori/[fisier].txt.log`

### Contact:
Pentru suport tehnic, consultă documentația sau contactează administratorul sistemului.

---

## 📋 CHECKLIST INSTALARE

- [ ] DUDE COM Server instalat
- [ ] .NET 8.0 Runtime instalat
- [ ] Imprimantă conectată și pornită
- [ ] Aplicație copiată într-o locație permanentă
- [ ] Port COM identificat (Device Manager)
- [ ] Credențiale operator cunoscute
- [ ] Aplicația pornește fără erori
- [ ] Conexiune la imprimantă reușită
- [ ] Monitorizare pornită
- [ ] Test cu un bon simplu - SUCCESS
- [ ] Test Cash In - SUCCESS (opțional)
- [ ] Test raport X - SUCCESS

---

## 🎉 MULȚUMIRI

Mulțumim pentru utilizarea POS Bridge!

**Version:** 1.2.0  
**Build Date:** 25 Februarie 2026  
**Platform:** Windows x86 (.NET 8.0)  

---

**© 2026 POS Bridge - All Rights Reserved**
