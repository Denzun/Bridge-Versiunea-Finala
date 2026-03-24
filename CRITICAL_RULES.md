# 🔒 REGULI CRITICE - NU MODIFICA FĂRĂ ACORD EXPLICIT

## ⚠️ PARAMETRI INTERZIS DE MODIFICAT

Următorii parametri sunt **CRITICI** pentru funcționarea aplicației și **NU trebuie modificați** decât cu **ACORD EXPLICIT** de la utilizator:

### 1. Parametri de Conexiune
- ❌ **COM Port** (ex: COM6)
- ❌ **Baud Rate** (ex: 19200, 115200)
- ❌ **Operator Code** (ex: 1)
- ❌ **Operator Password** (ex: 0001)

### 2. Logica de Conectare
- ❌ **Auto-connect** (activat/dezactivat)
- ❌ **Timeout-uri de conexiune**
- ❌ **Retry logic pentru conexiune**
- ❌ **Ordinea de încercare a porturilor**

### 3. Logica de Monitorizare
- ❌ **Auto-start monitoring**
- ❌ **Folder paths** (fără acord explicit)
- ❌ **File watching delay-uri**

## ✅ Ce POȚI Modifica Fără Acord

- ✅ Adăugare butoane noi (X Report, Z Report, etc.)
- ✅ Îmbunătățiri UI (layout, culori, texte)
- ✅ Adăugare comenzi DUDE noi (Cash In/Out, etc.)
- ✅ Îmbunătățiri logging și error handling
- ✅ Documentație

## 🔧 Procedura de Modificare Parametri Critici

**Dacă utilizatorul cere explicit modificări:**

1. **Întreabă pentru confirmare**:
   ```
   Vrei să modific [parametru] de la [valoare_veche] la [valoare_nouă]?
   Acest lucru poate afecta conectarea la casa de marcat.
   ```

2. **Așteaptă confirmarea EXPLICITĂ**:
   - "Da, modifică"
   - "Confirm"
   - "Sunt de acord"

3. **Documentează modificarea**:
   ```
   Modificat [parametru] de la [valoare_veche] la [valoare_nouă]
   Motiv: [explicație utilizator]
   Data: [data]
   ```

4. **Testează imediat**:
   - Recompilează
   - Testează conexiunea
   - Confirmă cu utilizatorul că funcționează

## 🚨 Exemplu - CE NU SE FACE

❌ **GREȘIT**:
```
"Am setat baud rate-ul default la 19200 pentru a se conecta automat"
```

✅ **CORECT**:
```
"Vrei să setez baud rate-ul default la 19200 (în loc de 115200)? 
Aceasta va face ca aplicația să încerce automat această viteză la pornire.
Te rog confirmă explicit dacă sunt de acord."
```

## 📋 Checklist Înainte de Orice Modificare Cod

Înainte de a modifica orice în cod, verifică:

- [ ] Modificarea afectează parametri de conexiune? → **ÎNTREABĂ UTILIZATORUL**
- [ ] Modificarea afectează auto-connect? → **ÎNTREABĂ UTILIZATORUL**  
- [ ] Modificarea afectează logica de monitorizare? → **ÎNTREABĂ UTILIZATORUL**
- [ ] Este o funcționalitate nouă (buton, comandă)? → **OK fără acord**
- [ ] Este îmbunătățire UI? → **OK fără acord**
- [ ] Este documentație? → **OK fără acord**

## 🎯 Raționament

**DE CE această regulă?**

1. **Parametrii actuali FUNCȚIONEAZĂ** - nu strica ce merge
2. **Fiecare casă de marcat e diferită** - setările sunt specifice hardware-ului
3. **Modificările pot strica conexiunea** - utilizatorul rămâne blocat
4. **Utilizatorul știe cel mai bine** ce setări are nevoie

## 💡 Când să Sugerez Modificări

Pot **SUGERA** modificări, dar NICIODATĂ să le fac automat:

✅ **Exemplu corect**:
```
"Am observat că îți conectezi manual de fiecare dată. 
Vrei să activez auto-connect pentru a se conecta automat la pornire?
Aceasta va folosi parametrii tăi actuali: COM6, [baud_rate], 1/0001."
```

Apoi **AȘTEPT** confirmarea explicită.

## 🔐 Această Regulă Este PERMANENTĂ

Această regulă se aplică **ÎNTOTDEAUNA**, inclusiv:
- În sesiuni viitoare de dezvoltare
- Când adaugi funcționalități noi
- Când faci refactoring
- Când rezolvi bug-uri

**NU EXISTĂ EXCEPȚII** - doar cu acord explicit de la utilizator.

---

**Dată adăugare regulă**: 2026-02-07  
**Motiv**: Stabilitate și control complet al utilizatorului asupra parametrilor critici  
**Status**: **PERMANENT - NU ȘTERGE**
