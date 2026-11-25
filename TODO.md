# TODO

## Subito
- [ ] Committare il redesign della GUI e le modifiche di libreria in sospeso (vedi PROJECT_STATUS.md).
- [ ] Verificare a schermo: riquadro avvisi nella cronologia, dialog password, finestre strette (<1180 px).

## Funzionalità previste dal mockup, non implementate
- [ ] Ripristino dalla GUI (oggi solo da CLI).
- [ ] Filtri nella lista job («Con avvisi», «Mai eseguiti»).
- [ ] Elenco delle fasi (stepper) nella pagina di esecuzione.
- [ ] Scelta del servizio con chip nelle impostazioni (oggi ComboBox).

## Correzioni
- [ ] CLI: `RestoreAction` ignora `Success` del restore, quindi un restore fallito non viene segnalato.
- [ ] Credenziali di sorgente/destinazione (`Job.Position.Password`) salvate in chiaro in `settings.json`.
- [ ] Test `FileManagers`: renderli espliciti/saltabili senza credenziali (oggi bloccano la suite).
- [ ] Sincronizzazioni: `LastResult` distingue solo Completato/Errore/Annullato (nessun avviso).
