# Implementation Gap Report

Compared with `WCM_Master_Plan.md` on 2026-11-06.

## Not verified

| Item | Why | Plan reference |
|---|---|---|
| Disposable Windows 11 VM runs | No VM was available. Everything ran on one development PC. | 15.1 |
| Read-only executable location with redirected data | Not run | Phase 12 task 6 |
| Elevated operation boundaries | Only tested without elevation | Phase 12 task 7 |
| Running with the network disabled | Not run. A source search finds no network code. | Phase 12 task 8 |
| Mouse-driven use | UI checks used UI Automation, not a person | 12 |
| Screens at small window sizes | Not inspected | 12 |
| Other Windows versions, DPI and locales | Not tested | NFR-002 |

## Implemented differently

| Item | What exists | Plan reference |
|---|---|---|
| Storage Explorer tree | A drill-down list with Open folder and Up, limited to 500 rows | 12.7 |
| Executable metadata | Used inside providers, not a separate provider | 8.1 |
| Quarantine progress | Reported per item. A running move can't be cancelled from the UI. | Phase 10 |
| Protected paths storage | Built-in policy plus a settings list. The `ProtectedPaths` table is unused. | 7.1 |
| Rule storage | JSON file plus enabled choices in settings. The `CleanupRules` table is unused. | 7.1 |
| Manifest | Saved per operation from the item records. It lacks original attributes and ACL details. | 13.3 |

## Deferred

- Incremental rescans, allocated disk size and pausing a scan (FR-002, 9.2).
- User-configurable evidence weights (FR-004).
- Importing settings, which the plan allows only "if safe" (Phase 12 task 3).
- Updater data rules: the category exists, but no rule ships, because none has been verified.
- Browser cache cleanup: browser profiles stay fully protected.
- More application signatures: only 7 ship.

## Known minor issues

- Disabled checkboxes and scrollbars keep the default light look in dark mode.
- The launch that applies a migration can show no window for over 20 seconds on a large database.
- Evidence labels often read "Weak/unknown" on correct links, because the plan's weights give low scores.
- A full scan adds about 278 MB to the database. Space is reused but the file doesn't shrink.
- Sensitive-content checks block a whole item if any file has a database or key extension. This is deliberate,
  but it means many real caches will be blocked.

## Known critical defects

None known. This is not a guarantee: see "Not verified".
