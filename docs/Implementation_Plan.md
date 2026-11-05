# Implementation Plan

WCM_Master_Plan.md section 14 defines each phase.

- [x] Phase 0: Repository and implementation controls
- [x] Phase 1: WPF foundation and DI
- [x] Phase 2: SQLite and migrations
- [x] Phase 3: Application discovery
- [ ] Phase 4 onward: not started

## Phase 3 notes

- Packaged apps are read from the per-user package repository in the registry. This avoids PowerShell
  and a target framework change, and does not list packages installed only for other users.
- Executable metadata is read by `ApplicationMetadataReader` and used by the providers, instead of
  being a separate provider.
- `NotDetected` is recorded only after a complete discovery, and doesn't mean uninstalled.
- The Applications screen is a basic list. Phase 8 adds filters and details.
