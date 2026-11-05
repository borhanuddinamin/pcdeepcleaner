# Implementation Plan

WCM_Master_Plan.md section 14 defines each phase.

- [x] Phase 0: Repository and implementation controls
- [x] Phase 1: WPF foundation and DI
- [x] Phase 2: SQLite and migrations
- [x] Phase 3: Application discovery
- [x] Phase 4: Storage scanning
- [ ] Phase 5 onward: not started

## Phase 3 notes

- Packaged apps are read from the per-user package repository in the registry. This avoids PowerShell
  and a target framework change, and does not list packages installed only for other users.
- Executable metadata is read by `ApplicationMetadataReader` and used by the providers, instead of
  being a separate provider.
- `NotDetected` is recorded only after a complete discovery, and doesn't mean uninstalled.
- The Applications screen is a basic list. Phase 8 adds filters and details.

## Phase 4 notes

- All scanner services from section 9.1 live in `StorageScanner` and `PathNormalizer`; split them when a
  second caller needs one.
- Only the latest scan keeps item rows, unless a cleanup plan refers to an older row. Session history stays.
- Normal starts run `quick_check`; the full integrity and foreign-key checks run after a migration.
- A scan session left Running is marked Interrupted when the next scan starts.
- Deferred from FR-002 and section 9.2: incremental rescans (every scan is full), allocated disk size
  (sizes are logical), and pause (cancel is supported).
- OneDrive-style placeholder files are reparse points, so they are recorded as links with size 0.
