# Implementation Plan

WCM_Master_Plan.md section 14 defines each phase.

- [x] Phase 0: Repository and implementation controls
- [x] Phase 1: WPF foundation and DI
- [x] Phase 2: SQLite and migrations
- [x] Phase 3: Application discovery
- [x] Phase 4: Storage scanning
- [x] Phase 5: Mapping and ownership evidence
- [x] Phases 6 to 11: see IMPLEMENTATION_STATUS.md
- [ ] Phase 12: portable build verified; VM, elevation and offline runs are open (see the gap report)

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

## Phase 5 notes

- Candidates are folders up to two levels below a scan root, each development tool's own root, install
  folders and signature paths. Deeper folders are covered by their parent's protection in Phase 9.
- A publisher match counts only alongside a folder-name match, so `Microsoft` alone links to nothing.
- Evidence weights are constants in `OwnershipScorer`. Making them user-configurable is deferred.
- Every mapping is saved with `IsProtected = 1`. Decisions change the state and never the protection.
- A parent of an install folder is not owned by the apps inside it, so it stays unknown and protected.
- The launch that applies a migration runs the full database checks, which can take over 20 seconds on a
  large database with no window showing yet. Later launches use the quick check.
