# Decisioni

## Piattaforma e dipendenze
- **Avalonia 12 con binding a riflessione e generatore di nomi disattivato** (`AvaloniaUseCompiledBindingsByDefault=false`,
  `AvaloniaNameGeneratorIsEnabled=false`). Il codice usa `FindControl` e binding senza `x:DataType`; i campi generati
  nascondevano `MainWindowBase.Container` e `StyledElement.Name`.
- **Material.Icons.Avalonia** al posto di Projektanker/FontAwesome: Projektanker non supporta Avalonia 12.
- **EF Core 10**, non 11: la 11 richiede .NET 11.
- **Font Inter** da `Avalonia.Fonts.Inter` (`avares://Avalonia.Fonts.Inter/Assets#Inter`).

## Backup
- **Differenziale confrontato con l'ultimo full**; i record di cancellazione hanno `OriginalRelativePath`.
- **Catene**: un full avvia sempre una nuova catena; dopo un full non completato il backup successivo è un full; cambio
  di tipo incrementale/differenziale → nuovo full. Così la ricostruzione esistente (`GetBackupItems`) resta valida
  anche per i backup vecchi.
- **Conservazione** (`KeepChains`): eseguita dopo un full completato riusando `DeleteJob`; un errore è solo un avviso.
- **Scritture sicure**: restore su file temporaneo + rinomina dopo la verifica dell'hash; database copiato come
  `mpbkpu.db.tmp` e poi rinominato; `DeleteJob` cancella i file dallo storage solo dopo commit e copia del database.
- **Niente VSS** (dipendenza nativa + privilegi admin): file bloccati saltati con avviso, file modificati durante la
  copia ripetuti; avvisi in `BackupWarnings`, esito `CompletedWithWarnings` (trattato come completato).
- **Cifratura v2**: AES-256-CBC + HMAC-SHA256 (encrypt-then-MAC), header `MPBKPU_ENCRYPT02`; chiavi derivate una volta
  per istanza di `EncryptDecrypt`. Il formato v1 resta leggibile.
- **Percorsi locali senza lettera di unità** (relativi, UNC) → file system del sistema operativo corrente.
- **Password mai serializzate**: `[JsonIgnore]` di Newtonsoft (impostazioni dei job, `OAuth2Settings.AppSecret`,
  `BackupJob.Password` della GUI). Il serializzatore usato ovunque è Newtonsoft.

## GUI
- **Navigazione SingleWindow invariata** (pagine animate in `Container`, freccia indietro nella TitleBar); la barra
  laterale è solo nella lista job.
- **Card con `ItemsControl`** invece di `ItemsList` per lista job e cronologia: `ItemsList` colora lo sfondo delle righe
  e confligge con le card. `ItemsList` resta nel browser dei file (serve la selezione).
- **Stile centralizzato** in `App.axaml` tramite override delle risorse Fluent e classi (`card`, `chip`, `h1`, `accent`,
  `danger`, `icon`, `nav`, `log`…).
- **Esito dell'ultima esecuzione** salvato nel job (`LastResult`) a ogni run, anche in caso di errore o annullamento.
