# Stato del progetto

_Aggiornato: 2026-11-25_

## Obiettivo
Mpbkpu: utility di backup multipiattaforma con cifratura e compressione, su file system locali e remoti
(FTP, SFTP, SMB, OneDrive, Dropbox, Mega, Nextcloud, MediaFire). Tre progetti: libreria `MpbkpuLib`,
CLI `Mpbkpu`, GUI Avalonia `MpbkpuGUI`, più `Tests` (NUnit).

## Completato e verificato
| Attività | Come è stato verificato |
|---|---|
| Fine riga normalizzate (`.gitattributes`: `* text=auto eol=crlf`) | committato (`55e3a7c`) |
| .NET 10, pacchetti NuGet aggiornati, EF Core 10, Graph 6, FluentFTP 55, SSH.NET 2026 | build della soluzione |
| Avalonia 12.2 + AvaloniaSingleWindow 1.2.0 + Material.Icons (al posto di Projektanker/FontAwesome) | build + app avviata |
| Logica di backup: corretti i problemi critici, alti, medi e bassi dell'analisi (vedi DECISIONS.md) | 45 test non remoti passano (`Tests/Regressions.cs` + test esistenti) |
| Catene di backup: full in qualsiasi momento, `FullBackupEveryDays`, `KeepChains` (lib, CLI, GUI) | test `TypeChangeStartsNewChain`, `KeepChainsDeletesOlderChains`, ecc. |
| Cifratura v2 con HMAC, chiave derivata una volta per job, lettura dei file v1 | test `TamperedOrWrongPasswordIsRejected`, `V1EncryptedDataCanBeDecrypted` |
| File bloccati/modificati durante la copia → avvisi salvati, esito `CompletedWithWarnings` | test `LockedFileIsSkippedWithWarning`, `FileChangedDuringCopyIsRetried` |
| Redesign della GUI (mockup: https://claude.ai/artifact/HpYrM1fJ6XE4of8AhhtrrS) | screenshot di ogni pagina: lista, esecuzione, impostazioni, cronologia, verifica, sfoglia, eliminazione, info, dialog di conferma |
| Pagina di eliminazione backup implementata (prima era vuota) | eliminazione eseguita da GUI, cronologia ricaricata |

## Modifiche non ancora committate
- GUI: `App.axaml` (tema), `Views/MainWindow.*`, `Controls/*` (TitleBar, JobListItem, OkCancelButtons, PositionDetail,
  ErrorContainer, Spinner), `Pages/*` (tutte), `Dialogs/*`, `Models/BackupJob.cs`, `Models/BackupOpenPageModel.cs`,
  `Abstracts/Job.cs` (`LastResult`), `Converters/EnumToLocalizedStringConverter.cs` (nuovo), `DateTimeConverter.cs`,
  `Assets/i18n/*.json`, `MpbkpuGUI.csproj` (Avalonia.Fonts.Inter).
- Libreria: `BackupJobJobResult.Result`, password delle impostazioni e `OAuth2Settings.AppSecret` con `[JsonIgnore]`.
- Tutto compila; i 45 test non remoti passano con queste modifiche.

## Stato attuale
Redesign della GUI applicato e controllato a schermo; ultime correzioni di allineamento (checkbox, campi numerici)
verificate. In attesa di commit.

## Problemi aperti e tentativi già fatti
- Test `Tests/FileManagers`: usano credenziali reali (`filemanagers-settings.json`) e si bloccano (OneDrive attende
  il login interattivo) → si escludono con `--filter "FullyQualifiedName!~FileManagers"`. Non risolto.
- Non verificati a schermo: riquadro avvisi nella cronologia (nessun backup di prova con avvisi), dialog password,
  finestre più strette di 1180×780.
- Computer use non riesce a controllare l'exe di sviluppo (non è un'app installata): per le verifiche GUI si usano
  PrintWindow + UI Automation (vedi AGENTS.md).
- I backup creati prima della correzione con percorsi sorgente relativi hanno `RelativePath` errati nel database:
  non recuperabili automaticamente.

## Prossimo passo
Committare il redesign della GUI e le modifiche di libreria in sospeso, poi verificare a schermo il riquadro avvisi
della cronologia con un backup `CompletedWithWarnings` (es. file bloccato con `FileShare.None`).
