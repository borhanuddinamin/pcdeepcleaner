# Architecture

WCM_Master_Plan.md is the authoritative specification. This file records decisions that refine it.

## ADR-001: Desktop composition root

The plan says `WCM.Desktop` depends on `WCM.Application`, but the WPF process must also register
implementations for dependency injection. `WCM.Desktop` may therefore reference `WCM.Infrastructure`
(and `WCM.CleanupEngine` from Phase 9), and only files in `WCM.Desktop/Composition` may use those
namespaces. `ArchitectureTests` enforces this.

## ADR-002: Local SQLite only

The plan requires fully offline operation (FR-012, NFR-001, NFR-005). Hosted databases are not used.

## ADR-003: Data root selection

The data root is resolved before the database exists, so it lives in a small JSON file at
`%LOCALAPPDATA%\WindowsCleanupManager\bootstrap.json`. Order of precedence: a `portable.marker` file
beside the executable, then the saved location, then `%LOCALAPPDATA%\WindowsCleanupManager`. An
unreadable settings file stops startup rather than silently switching data location.
