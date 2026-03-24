=============================================================================
  GHID: Operațiuni Cash In / Cash Out prin Folder Monitorizat
=============================================================================

DESCRIERE:
  Aceste comenzi permit introducerea și scoaterea numerarului din casa de 
  marcat FĂRĂ a deschide un bon fiscal. Sunt operațiuni standalone care
  se înregistrează în rapoartele fiscale.

=============================================================================
COMANDA: Cash In (Introducere Numerar)
=============================================================================

FORMAT:
  I^VALOARE^

PARAMETRI:
  - VALOARE: Suma în lei (format: 100.00 sau 100)

EXEMPLE:
  I^150.00^        → Introduce 150.00 lei în casa de marcat
  I^50^            → Introduce 50.00 lei în casa de marcat
  I^1000.50^       → Introduce 1000.50 lei în casa de marcat

UTILIZARE:
  1. Creează un fișier .txt în folderul Bon/ (ex: introducere_numerar.txt)
  2. Scrie o singură linie: I^VALOARE^
  3. Salvează fișierul
  4. Aplicația va detecta automat fișierul și va executa operațiunea
  5. După execuție, fișierul va fi mutat în Bon/Procesate/ sau Bon/Erori/

=============================================================================
COMANDA: Cash Out (Scoatere Numerar)
=============================================================================

FORMAT:
  O^VALOARE^

PARAMETRI:
  - VALOARE: Suma în lei (format: 100.00 sau 100)

EXEMPLE:
  O^50.00^         → Scoate 50.00 lei din casa de marcat
  O^25.50^         → Scoate 25.50 lei din casa de marcat
  O^100^           → Scoate 100.00 lei din casa de marcat

UTILIZARE:
  1. Creează un fișier .txt în folderul Bon/ (ex: scoatere_numerar.txt)
  2. Scrie o singură linie: O^VALOARE^
  3. Salvează fișierul
  4. Aplicația va detecta automat fișierul și va executa operațiunea
  5. După execuție, fișierul va fi mutat în Bon/Procesate/ sau Bon/Erori/

=============================================================================
IMPORTANT:
=============================================================================

  ✓ Aceste comenzi se execută IMEDIAT (fără confirmare)
  ✓ Operațiunile sunt înregistrate în rapoartele fiscale
  ✓ Suma trebuie să fie mai mare decât 0
  ✓ Fișierul trebuie să conțină DOAR comanda I^ sau O^ (nu combina cu alte comenzi)
  ✓ Pentru operațiuni CashIn/CashOut în cadrul unui bon fiscal, 
    folosește comenzile în interiorul bonului

=============================================================================
ERORI COMUNE:
=============================================================================

  ✗ I^0^              → Eroare: suma trebuie > 0
  ✗ O^-50^            → Eroare: suma trebuie > 0
  ✗ I^abc^            → Eroare: valoare invalidă
  ✗ I^100.00^         → Eroare: lipsește ^ de la final
    S^Produs^10^1^buc^1^1^
    I^50^             → Eroare: nu combina cu alte comenzi (folosește fișiere separate)

=============================================================================
VERIFICARE:
=============================================================================

  După execuție, verifică:
  1. Fișierul este mutat în Bon/Procesate/ (success) sau Bon/Erori/ (eroare)
  2. În jurnalul aplicației apare mesajul de succes/eroare
  3. Casa de marcat a înregistrat operațiunea
  4. Operațiunea apare în raportul X sau Z

=============================================================================
