# Backup and Recovery

## Where data lives

By default: `%LOCALAPPDATA%\WindowsCleanupManager`, containing `Data\cleanup.db`, `Logs` and `Quarantine`.
With `portable.marker` beside the program, the same folders sit beside the program.

## Back up

Close WCM, then copy the whole data folder. The database and `Quarantine` belong together: quarantine records
point to files in that folder, so restoring one without the other leaves records that can't be restored.

## Restore

Close WCM, then replace the data folder with your copy.

## If WCM says the database can't be used

WCM stops rather than guess, and leaves the file in place. Restore a backup. If you have none, move
`Data\cleanup.db` somewhere safe and restart to create a new database. A new database has no quarantine
records, so items in `Quarantine` then need restoring by hand. Each `op-N\manifest.json` lists original paths.

## After a crash or power loss

WCM checks interrupted items when the Dashboard first opens, or when you select Check interrupted. It never
deletes or overwrites during this check. Items marked Recovery required may exist in two places, or neither.
Compare them by hand using the paths shown.
