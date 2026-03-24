# E2E Local Guide (GUI + Datecs)

## Scope
- Real device Datecs + DUDE COM.
- GUI remains visible during B/C scenarios.
- Current default suite: A (connection + credentials), B (fiscal success), C (parser invalid file).

## Prerequisites
- DUDE installed and COM server registered.
- Device connected and powered on.
- Projects built as x86 where required.
- Operator code available.

## Run
From repo root:

```powershell
.\scripts\run-e2e-local.ps1 -ScenarioSet ABC -KeepGuiOpen
```

## Useful options
- `-ComPort COM6`
- `-BaudRate 115200`
- `-OperatorCode 1`
- `-OperatorPassword 0000`
- `-AutoFindPassword` (enabled by default)
- `-PasswordCandidates "0000,1234,1111,0001"`
- `-ConnectionRetries 3`
- `-ConnectionRetryDelayMs 1500`
- `-TimeoutSeconds 120`
- `-GuiWarmupSeconds 5`

## What each scenario validates
- A: Device connection + operator credentials (includes retry and optional password auto-discovery).
- B: End-to-end fiscal processing from BON file to `Procesate` + `BONOK=1` + `NRBON`.
- C: Invalid file path to parser error, file moved to `Erori`, response `BONOK=0`, and `.log` created.

## Outputs
- Live console timeline from runner.
- GUI window remains open for inspection (when `-KeepGuiOpen`).
- Report saved in `<WatchFolder>\E2E\report_yyyyMMdd_HHmmss.txt`.

## Failure workflow
1. Inspect report + `Raspuns` + `.log` in `Erori`.
2. Fix code or connection/credentials.
3. Rerun failed scenario only (example: `-ScenarioSet B`).
4. Rerun full suite (`-ScenarioSet ABC`) to confirm no regressions.

## Safety notes
- Fiscal success scenario prints a real fiscal receipt.
- Run Z report only when explicitly planned (not included in ABC baseline).
