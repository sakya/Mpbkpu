# TODO

## Now
- [ ] Check the Windows animations by hand (minimize, maximize, restore).
- [ ] Commit the secret store change (see PROJECT_STATUS.md).
- [ ] Check on screen: warnings box in the history, password dialog, narrow windows (<1180 px).

## Mockup features not implemented
- [ ] Restore from the GUI (today only from the CLI).
- [ ] Job list filters ("With warnings", "Never run").
- [ ] Phase list (stepper) on the running page.
- [ ] Service picker with chips in the settings (today a ComboBox).

## Fixes
- [ ] CLI: `RestoreAction` ignores the restore `Success`, so a failed restore is not reported.
- [ ] `FileManagers` tests: make them explicit/skippable without credentials (today they block the suite).
- [ ] Synchronizations: `LastResult` only distinguishes Completed/Error/Cancelled (no warnings).
