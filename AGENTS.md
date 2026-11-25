# Note per gli agenti

## Struttura
- `MpbkpuLib/`: logica (job in `Jobs/`, file manager in `FileManagers/`, database SQLite EF Core in `BackupFile.cs`).
- `Mpbkpu/`: CLI (`Arguments.cs`, `Actions/`).
- `MpbkpuGUI/`: GUI Avalonia 12 + AvaloniaSingleWindow (pagine in `Pages/`, tema in `App.axaml`, testi in `Assets/i18n/`).
- `Tests/`: NUnit; `Regressions.cs` contiene i test di regressione della logica di backup.

## Comandi
- Build: `dotnet build Mpbkpu.sln`
- Test (senza i FileManagers remoti, ~3-8 min): `dotnet test Tests/Tests.csproj --filter "FullyQualifiedName!~FileManagers"`
- I test `FileManagers` richiedono `filemanagers-settings.json` con credenziali reali: non eseguirli senza chiedere.

## Regole
- Serializzazione JSON con **Newtonsoft**: per escludere un campo usare `Newtonsoft.Json.JsonIgnore`, mai quello di
  `System.Text.Json`. Le password non devono finire in log, database o `settings.json`.
- GUI: niente binding compilati né campi generati per i nomi; usare `FindControl` e i binding a riflessione.
- GUI: ogni testo nuovo va in **entrambi** `it-IT.json` e `en-US.json`; i pulsanti con sola icona hanno
  `ToolTip.Tip` e `AutomationProperties.Name`.
- GUI: usare stili e risorse di `App.axaml` (classi `card`, `chip`, `h1`, `h2`, `muted`, `mono`, `hint`, `accent`,
  `danger`, `dangerHover`, `icon`, `nav`, `log`) invece di colori locali.
- Fine riga CRLF (`.gitattributes`).
- Scrivi commenti e documentazione sempre in inglese
- Non duplicare codice. Riusa funziona comuni ed utilizza pattern OOP.

## Verifica della GUI
Computer use non controlla l'exe di sviluppo. Funziona così: avviare `MpbkpuGUI\bin\Debug\net10.0\mpbkpugui.exe`,
catturare la finestra con `PrintWindow` (flag 2) e cliccare i pulsanti con UI Automation (`InvokePattern` per nome o
AutomationId, `TogglePattern` per gli Expander, `ScrollPattern` per scorrere). Le impostazioni sono in
`%LOCALAPPDATA%\MpbkpuGUI\settings.json`: salvarne una copia prima di usare dati di prova e ripristinarla dopo.
