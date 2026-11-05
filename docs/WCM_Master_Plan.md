# Windows Cleanup Manager (WCM)
## Master Architecture, SRS, Database Design, UI Specification & AI Implementation Plan

**Document version:** 1.0  
**Status:** Baseline implementation specification  
**Target platform:** Windows 11 x64  
**Application type:** Portable, local desktop utility  
**Technology baseline:** C# / .NET 10 LTS / WPF / MVVM / SQLite  
**Operating mode:** Fully offline, single-user  
**Primary safety policy:** Protect installed applications and their data by default. Unknown ownership is never sufficient evidence for deletion.

---

# 1. Purpose

Build a Windows 11 desktop application that discovers installed applications, scans configured storage locations, associates folders/files with applications, identifies potential remnants of uninstalled applications, classifies cache and temporary data, and lets the user review and quarantine selected items.

The application must be application-centric: selecting an application should show all discovered storage locations associated with it, the data type of each location, ownership evidence, confidence score, protection status, and potential cleanup eligibility.

The application must be useful without an internet connection. Application identification rules and all scan data must remain local.

## 1.1 Primary goals

1. Discover installed applications, including registry-based, packaged and detectable portable applications.
2. Scan configured application and temporary-data locations.
3. Map applications to one or more storage locations.
4. Support shared storage with multiple application owners.
5. Detect potential orphaned data using current and historical inventory.
6. Identify recognized cache and temporary data using explicit local rules.
7. Show transparent evidence and an explainable confidence score.
8. Protect installed application data, unknown data, shared data and sensitive user data.
9. Provide user-controlled cleanup planning and preview.
10. Quarantine approved files/folders and support restoration.
11. Permit permanent deletion only from quarantine after explicit confirmation.
12. Maintain a local audit trail and reports.

## 1.2 Non-goals for v1

- Uninstalling applications.
- Modifying Windows Registry entries.
- Cleaning Windows component store, WinSxS or system restore data.
- Removing browser profiles, passwords, cookies or saved sessions.
- Deleting source code, Git repositories, project files or user documents.
- Removing Docker volumes, container data or database files.
- Automatically deleting whole application directories.
- Cloud-based application identification or telemetry.
- Automatic cleanup without user approval.

---

# 2. Product principles and safety invariants

These rules are mandatory and take precedence over all cleanup rules, scores, user-interface state and AI-generated suggestions.

## 2.1 Safety invariants

- **Fail closed:** if ownership, path safety, permissions, database state or operation state is uncertain, block cleanup.
- **Installed means protected:** any storage location associated with an installed application is protected by default.
- **Unknown means preserved:** unknown ownership is never automatically classified as an orphan.
- **Shared means protected:** a path used by more than one application is protected as a whole. Only independently identified child items with explicit rules may be considered.
- **Settings and user data are not cache:** preferences, credentials, databases, profiles, extensions, projects and user documents must not be classified as disposable cache.
- **No registry mutation:** the application may read discovery data but must not modify uninstall entries or other registry values.
- **No system-folder cleanup:** protected Windows/system locations are excluded.
- **No link traversal:** do not follow symbolic links, junctions or other reparse points during recursive scanning or cleanup. Record them as links and skip their targets.
- **Revalidate immediately before action:** never trust a stale scan result or UI checkbox as authorization to move a file.
- **No silent overwrite:** restore conflicts must be presented to the user.
- **Auditable operations:** every plan, approval, move, verification, restore, rollback and permanent deletion must be journaled.
- **No irreversible cleanup of original data:** permanent deletion is allowed only for already-quarantined items after explicit confirmation.

## 2.2 Installed application protection

An installed application association must override confidence scores. A folder with a weak name match to an installed app must not be cleaned. A folder with strong evidence of being cache may only be cleaned when an application-specific rule explicitly permits it, the application is closed, and the selected cache scope is narrowly defined.

## 2.3 Administrator privileges

Run normally without elevation. Request elevation only for a specific operation that requires it. Do not elevate the whole application by default. If a protected location cannot be scanned, report access denied rather than attempting privilege escalation automatically.

---

# 3. Scope and scan locations

The scanner must support configurable scan roots and exclusions.

## 3.1 Required roots

| Category | Default path / environment variable | Default policy |
|---|---|---|
| Local application data | `%LOCALAPPDATA%` | Scan; classify by rule |
| Roaming application data | `%APPDATA%` | Scan; protect settings/data |
| LocalLow application data | `%USERPROFILE%\AppData\LocalLow` | Scan; classify by rule |
| Machine-wide application data | `%PROGRAMDATA%` | Scan; strict protection |
| Program Files | `%PROGRAMFILES%` | Discovery and mapping; no cleanup by default |
| Program Files x86 | `%PROGRAMFILES(X86)%` | Discovery and mapping; no cleanup by default |
| User profile hidden folders | Selected hidden folders under `%USERPROFILE%` | Scan only configured/recognized roots |
| User temporary files | `%TEMP%` | Scan; stale-file rules |
| Windows temporary files | `%WINDIR%\Temp` | Restricted; requires elevation and dedicated rules |

## 3.2 Development tool locations

Recognize these as development data, not automatically disposable:

- `.nuget`
- `.npm`
- `.gradle`
- `.docker`
- `.vscode`
- `.cursor`
- `.claude`
- `.config`
- `.cache`
- `.local`
- `.m2`
- `.cargo`
- `.rustup`
- `.git`
- Other user-configured tool locations.

Development caches can be expensive to rebuild and may include credentials, configuration, packages, containers or important local data. Provide specific cleanup rules for individual subdirectories only. Never treat a top-level development folder as cache based on its name alone.

## 3.3 Exclusions and protected roots

At minimum, exclude or protect:

- `%WINDIR%` except a specifically enabled and narrowly scoped Temp rule.
- Windows component stores and system-managed folders.
- User Documents, Desktop, Pictures, Videos and Downloads by default.
- Git repositories and source-code directories.
- Browser profile roots.
- Credential stores and authentication databases.
- Database data directories.
- Docker volumes and container storage.
- Application installation directories.
- Shared application data.
- Reparse points and their targets.
- Any user-defined protected path.

The UI must show excluded roots and explain why they are excluded.

---

# 4. Architecture

## 4.1 Architectural style

Use a **modular monolith**. Do not create a web server, cloud backend or separate microservices. The WPF process orchestrates local services through dependency injection.

Logical layers:

1. Presentation: WPF, MVVM, navigation, dialogs and UI state.
2. Application: use cases, orchestration, commands, queries and DTOs.
3. Domain: entities, value objects, ownership policies, rules and invariants.
4. Infrastructure: filesystem, registry, Windows package discovery, SQLite and logging.
5. Cleanup engine: planning, validation, quarantine, restore, deletion and crash recovery.

## 4.2 Solution projects

```text
WindowsCleanupManager/
├── WindowsCleanupManager.sln
├── src/
│   ├── WCM.Desktop/
│   │   ├── App.xaml
│   │   ├── MainWindow.xaml
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Converters/
│   │   ├── Themes/
│   │   └── Navigation/
│   ├── WCM.Application/
│   │   ├── Interfaces/
│   │   ├── Services/
│   │   ├── Commands/
│   │   ├── Queries/
│   │   ├── DTOs/
│   │   └── DependencyInjection.cs
│   ├── WCM.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── ValueObjects/
│   │   ├── Rules/
│   │   ├── Policies/
│   │   └── Exceptions/
│   ├── WCM.Infrastructure/
│   │   ├── Registry/
│   │   ├── Appx/
│   │   ├── FileSystem/
│   │   ├── Discovery/
│   │   ├── SQLite/
│   │   ├── Logging/
│   │   └── Windows/
│   ├── WCM.CleanupEngine/
│   │   ├── Planning/
│   │   ├── Validation/
│   │   ├── Quarantine/
│   │   ├── Restore/
│   │   ├── Deletion/
│   │   └── Recovery/
│   └── WCM.Tests/
│       ├── Unit/
│       ├── Integration/
│       ├── Safety/
│       └── Fixtures/
├── Rules/
├── Data/
├── Logs/
├── Quarantine/
└── docs/
    ├── WCM_Master_Plan.md
    ├── Architecture.md
    ├── SRS.md
    ├── Database_Design.md
    ├── UI_Specification.md
    ├── Implementation_Plan.md
    └── Test_Plan.md
```

## 4.3 Dependency direction

- `WCM.Desktop` depends on `WCM.Application`.
- `WCM.Application` depends on `WCM.Domain` abstractions.
- `WCM.Infrastructure` implements application/domain interfaces.
- `WCM.CleanupEngine` depends on domain and application abstractions.
- `WCM.Tests` tests all projects.
- Domain must not depend on WPF, SQLite, filesystem APIs or UI frameworks.
- No file-moving or deletion logic may exist in a ViewModel.

## 4.4 Technology choices

| Concern | Technology |
|---|---|
| Language | C# |
| Runtime | .NET 10 LTS |
| Desktop | WPF |
| UI architecture | MVVM |
| MVVM toolkit | CommunityToolkit.Mvvm |
| DI and hosting | Microsoft.Extensions.Hosting |
| Database | SQLite / Microsoft.Data.Sqlite |
| Data access | Dapper |
| Logging | Serilog |
| JSON | System.Text.Json |
| Tests | xUnit |
| Publishing | Self-contained win-x64 |

Use package versions compatible with .NET 10 and pin versions in the project. Avoid unnecessary dependencies.

---

# 5. Functional requirements

## FR-001 — Installed application discovery

The system shall:

- Read HKLM, HKCU and WOW6432Node uninstall registry entries.
- Discover packaged AppX/MSIX applications using supported Windows APIs or PowerShell-free APIs where practical.
- Detect application name, publisher, version, install location, executable path, architecture and source.
- Deduplicate records from multiple discovery sources.
- Identify portable applications when evidence is available.
- Mark missing or inconsistent installation paths for review.
- Preserve inventory snapshots across scans.

Registry records are evidence, not absolute truth. Stale uninstall entries and portable apps must be handled.

## FR-002 — Storage scanning

The system shall:

- Scan the configured roots from Section 3.
- Enumerate folders and files with bounded concurrency.
- Record size, item count, timestamps, attributes and errors.
- Support cancellation and progress reporting.
- Handle access-denied and disappearing files without crashing.
- Detect reparse points and skip their targets.
- Avoid loading the entire filesystem inventory into memory.
- Support incremental rescans based on metadata while retaining a full-scan option.

## FR-003 — Application-to-storage mapping

The system shall:

- Map one application to multiple storage locations.
- Support a storage location shared by multiple applications.
- Record evidence for each mapping.
- Support known application signatures and versioned local rules.
- Use publisher, executable metadata, installation paths and historical inventory.
- Treat generic name similarity as weak evidence only.
- Allow the user to confirm, reject or correct mappings.
- Protect shared locations.

## FR-004 — Ownership confidence and classification

The system shall calculate a transparent score from evidence, but scores must not override safety invariants.

Initial evidence weights (configurable and documented):

| Evidence | Points |
|---|---:|
| Exact application signature | +30 |
| Matching executable metadata | +25 |
| Matching publisher | +15 |
| Known application-specific path | +15 |
| Historical inventory match | +10 |
| Matching installation path | +10 |
| Generic folder-name similarity | +5 |
| Conflicting ownership | -30 |
| Shared active ownership | -40 |

Normalize to 0–100. These are heuristic evidence scores, not probabilities.

Initial display bands:

- 90–100: Strong evidence.
- 70–89: High evidence.
- 40–69: Possible.
- 0–39: Weak/unknown.

Every result must show its supporting and conflicting evidence. An installed association, shared ownership, unknown ownership or protected path blocks cleanup regardless of score.

## FR-005 — Orphan detection

The system shall:

- Compare current inventory with historical snapshots.
- Detect previously installed applications no longer detected.
- Search for storage associated with those applications.
- Identify potential remnants even when no previous snapshot exists, but label them as unconfirmed candidates.
- Distinguish confirmed historical evidence from inferred evidence.
- Never claim an application was uninstalled based solely on a missing registry entry.
- Require manual approval before an orphan candidate becomes cleanup eligible.

## FR-006 — Cache analysis

The system shall:

- Use explicit, versioned, local application-specific rules.
- Separate cache, temporary files, updater data, logs, package caches, settings, credentials, profiles and user data.
- Mark whether an application must be closed.
- Identify rebuild cost where known.
- Calculate estimated recoverable space.
- Exclude unknown and unsupported cache paths.
- Never classify an entire `.nuget`, `.npm`, `.gradle`, `.docker`, `.vscode`, `.cursor` or similar root as disposable based only on its name.

## FR-007 — UI selection and preview

The system shall allow users to:

- Select an application and inspect all associated locations.
- Select individual supported cache/leftover items.
- Exclude individual files or folders.
- Filter by status, category, size and confidence.
- View the exact source and quarantine destination.
- View protected/blocked reasons.
- Generate a dry-run plan without filesystem changes.
- Approve each cleanup plan explicitly.

## FR-008 — Quarantine

The system shall:

- Quarantine only items that pass fresh validation.
- Preserve original path and relative structure in a manifest.
- Use same-volume rename/move when safe and supported.
- Use copy-verify-remove for cross-volume operations.
- Never silently overwrite quarantine destinations.
- Record file metadata, operation status and integrity information.
- Support rollback and recovery after interrupted operations.
- Verify that the original path is not recreated or changed unexpectedly before removing an original after copy.
- Treat ACLs, alternate data streams, timestamps and other relevant metadata carefully; if they cannot be preserved, warn and block unsupported items.

## FR-009 — Restore

The system shall:

- Restore one item or an entire operation.
- Validate original paths.
- Detect destination conflicts.
- Never silently overwrite.
- Verify restored content.
- Keep records until restoration is confirmed.
- Support recovery after interrupted restore.

## FR-010 — Permanent deletion

The system shall:

- Delete only quarantined items.
- Require explicit selection and confirmation.
- Verify no restore is in progress.
- Journal the deletion.
- Report failures and partial results.
- Retain audit metadata after file deletion.

## FR-011 — Reporting and history

Generate local CSV and JSON reports for:

- Application inventory.
- Storage scan.
- Ownership evidence.
- Orphan candidates.
- Cleanup preview.
- Quarantine and restore history.
- Permanent deletion history.
- Estimated and actual recovered space.

## FR-012 — Offline operation

- No telemetry.
- No cloud services.
- No network dependency.
- No remote rule download.
- All data, rules, logs and database stored locally.
- Any future update capability must be explicitly opt-in and outside v1.

---

# 6. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-001 | Must operate without internet |
| NFR-002 | Must run on Windows 11 x64 |
| NFR-003 | Must remain responsive during scans |
| NFR-004 | Must use bounded memory and concurrency |
| NFR-005 | Must use local SQLite persistence |
| NFR-006 | Must use structured local logs |
| NFR-007 | Must fail closed on uncertainty |
| NFR-008 | Must support cancellation |
| NFR-009 | Must recover interrupted operations |
| NFR-010 | Must support portable self-contained deployment |
| NFR-011 | Must run without elevation for normal user scans |
| NFR-012 | Must request elevation only for specific privileged operations |
| NFR-013 | Must retain an audit history |
| NFR-014 | Must support database schema migrations |

---

# 7. Database design

Use SQLite. Store UTC timestamps in ISO-8601 format and file sizes as 64-bit integers. Use migrations; do not alter production tables manually.

## 7.1 Tables

1. `Applications` — current application identity and status.
2. `ApplicationSnapshots` — historical inventory.
3. `StorageLocations` — unique discovered paths.
4. `ApplicationStorageMappings` — many-to-many ownership mapping.
5. `OwnershipEvidence` — evidence for each mapping.
6. `ScanSessions` — scan execution metadata.
7. `ScanItems` — discovered files and directories.
8. `CleanupRules` — local versioned cleanup rules.
9. `ProtectedPaths` — protected path patterns.
10. `CleanupPlans` — preview and approval.
11. `CleanupPlanItems` — individual plan selections.
12. `QuarantineOperations` — quarantine transaction.
13. `QuarantineItems` — moved item manifest and status.
14. `RestoreOperations` — restoration history.
15. `DeletionOperations` — permanent deletion history.
16. `AuditLogs` — security and operational events.
17. `AppSettings` — user settings.

## 7.2 Initial SQL schema

```sql
PRAGMA foreign_keys = ON;

CREATE TABLE Applications (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AppKey TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    Publisher TEXT,
    Version TEXT,
    InstallLocation TEXT,
    ExecutablePath TEXT,
    DetectionSource TEXT NOT NULL,
    Status TEXT NOT NULL,
    FirstDetectedAt TEXT NOT NULL,
    LastDetectedAt TEXT NOT NULL
);

CREATE TABLE ApplicationSnapshots (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationId INTEGER NOT NULL,
    SnapshotAt TEXT NOT NULL,
    Status TEXT NOT NULL,
    InstallLocation TEXT,
    FOREIGN KEY(ApplicationId) REFERENCES Applications(Id)
);

CREATE TABLE StorageLocations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Path TEXT NOT NULL UNIQUE,
    DataType TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL DEFAULT 0,
    FileCount INTEGER NOT NULL DEFAULT 0,
    LastModifiedAt TEXT,
    OwnershipStatus TEXT NOT NULL DEFAULT 'Unknown'
);

CREATE TABLE ApplicationStorageMappings (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationId INTEGER NOT NULL,
    StorageLocationId INTEGER NOT NULL,
    ConfidenceScore INTEGER NOT NULL
        CHECK(ConfidenceScore BETWEEN 0 AND 100),
    OwnershipStatus TEXT NOT NULL,
    IsProtected INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY(ApplicationId) REFERENCES Applications(Id),
    FOREIGN KEY(StorageLocationId) REFERENCES StorageLocations(Id),
    UNIQUE(ApplicationId, StorageLocationId)
);

CREATE TABLE OwnershipEvidence (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    MappingId INTEGER NOT NULL,
    EvidenceType TEXT NOT NULL,
    Description TEXT NOT NULL,
    Score INTEGER NOT NULL,
    ObservedAt TEXT NOT NULL,
    FOREIGN KEY(MappingId) REFERENCES ApplicationStorageMappings(Id)
);

CREATE TABLE ScanSessions (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    StartedAt TEXT NOT NULL,
    CompletedAt TEXT,
    Status TEXT NOT NULL,
    FilesScanned INTEGER NOT NULL DEFAULT 0,
    FoldersScanned INTEGER NOT NULL DEFAULT 0,
    Errors INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE ScanItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ScanSessionId INTEGER NOT NULL,
    StorageLocationId INTEGER,
    Path TEXT NOT NULL,
    ItemType TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL DEFAULT 0,
    LastModifiedAt TEXT,
    IsLocked INTEGER NOT NULL DEFAULT 0,
    IsProtected INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(ScanSessionId) REFERENCES ScanSessions(Id),
    FOREIGN KEY(StorageLocationId) REFERENCES StorageLocations(Id)
);

CREATE TABLE CleanupRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AppKey TEXT NOT NULL,
    RelativePathPattern TEXT NOT NULL,
    DataType TEXT NOT NULL,
    CleanupAllowed INTEGER NOT NULL DEFAULT 0,
    RequiresAppClosed INTEGER NOT NULL DEFAULT 1,
    RuleVersion TEXT NOT NULL,
    UNIQUE(AppKey, RelativePathPattern)
);

CREATE TABLE ProtectedPaths (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PathPattern TEXT NOT NULL UNIQUE,
    Reason TEXT NOT NULL,
    ProtectionType TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE CleanupPlans (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CreatedAt TEXT NOT NULL,
    Status TEXT NOT NULL,
    SelectedItems INTEGER NOT NULL DEFAULT 0,
    EstimatedBytes INTEGER NOT NULL DEFAULT 0,
    ApprovedAt TEXT
);

CREATE TABLE CleanupPlanItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CleanupPlanId INTEGER NOT NULL,
    ScanItemId INTEGER NOT NULL,
    Selected INTEGER NOT NULL DEFAULT 0,
    ValidationStatus TEXT NOT NULL,
    FOREIGN KEY(CleanupPlanId) REFERENCES CleanupPlans(Id),
    FOREIGN KEY(ScanItemId) REFERENCES ScanItems(Id)
);

CREATE TABLE QuarantineOperations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CleanupPlanId INTEGER NOT NULL,
    CreatedAt TEXT NOT NULL,
    Status TEXT NOT NULL,
    FOREIGN KEY(CleanupPlanId) REFERENCES CleanupPlans(Id)
);

CREATE TABLE QuarantineItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    OperationId INTEGER NOT NULL,
    OriginalPath TEXT NOT NULL,
    QuarantinePath TEXT NOT NULL,
    SizeBytes INTEGER NOT NULL,
    VerificationHash TEXT,
    Status TEXT NOT NULL,
    FOREIGN KEY(OperationId) REFERENCES QuarantineOperations(Id)
);

CREATE TABLE RestoreOperations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    StartedAt TEXT NOT NULL,
    CompletedAt TEXT,
    Status TEXT NOT NULL
);

CREATE TABLE DeletionOperations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    QuarantineItemId INTEGER NOT NULL,
    DeletedAt TEXT,
    Status TEXT NOT NULL,
    FOREIGN KEY(QuarantineItemId) REFERENCES QuarantineItems(Id)
);

CREATE TABLE AuditLogs (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Timestamp TEXT NOT NULL,
    Action TEXT NOT NULL,
    TargetPath TEXT,
    Result TEXT NOT NULL,
    Details TEXT
);

CREATE TABLE AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE INDEX IX_Applications_Status ON Applications(Status);
CREATE INDEX IX_Mappings_ApplicationId
    ON ApplicationStorageMappings(ApplicationId);
CREATE INDEX IX_Mappings_StorageLocationId
    ON ApplicationStorageMappings(StorageLocationId);
CREATE INDEX IX_ScanItems_SessionId ON ScanItems(ScanSessionId);
CREATE INDEX IX_QuarantineItems_OperationId
    ON QuarantineItems(OperationId);
```

Add schema migrations, foreign-key validation, unique normalized path handling, appropriate indexes and database integrity checks. Filesystem operations must not be held inside long-running SQLite transactions.

---

# 8. Application discovery design

## 8.1 Providers

Create provider abstractions:

```csharp
public interface IApplicationDiscoveryProvider
{
    string ProviderName { get; }

    Task<IReadOnlyList<ApplicationInfo>> DiscoverAsync(
        CancellationToken cancellationToken);
}
```

Implement:

- `RegistryApplicationProvider`
- `AppxApplicationProvider`
- `StartMenuShortcutProvider`
- `PortableApplicationProvider`
- `ExecutableMetadataProvider`

Use a stable application key derived from available identity evidence. Do not use display name alone as a unique key.

## 8.2 Discovery DTO

```csharp
public sealed record ApplicationInfo(
    string AppKey,
    string Name,
    string? Publisher,
    string? Version,
    string? InstallLocation,
    string? ExecutablePath,
    string DetectionSource,
    string Status);
```

Deduplicate records carefully. If sources conflict, retain the evidence and mark the record for review rather than silently merging unrelated applications.

---

# 9. Storage scanner design

## 9.1 Scanner interface

```csharp
public interface IStorageScanner
{
    Task<ScanResult> ScanAsync(
        ScanConfiguration configuration,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken);
}
```

Supporting services:

- `DirectoryEnumerator`
- `FileMetadataReader`
- `FolderSizeCalculator`
- `PathNormalizer`
- `ReparsePointDetector`
- `ScanErrorCollector`
- `IncrementalScanService`

## 9.2 Scanner rules

- Use asynchronous orchestration and bounded workers.
- Use streaming enumeration.
- Handle files disappearing during enumeration.
- Record access-denied and I/O errors.
- Do not follow directory links or junctions.
- Normalize paths using Windows-aware path comparison.
- Do not assume case-sensitive path semantics.
- Distinguish logical size from allocated size when available.
- Avoid hashing during normal inventory scans.
- Support pause/cancel where practical.

---

# 10. Ownership and orphan detection

## 10.1 Mapping strategies

1. Exact known application signature.
2. Executable product metadata.
3. Publisher match.
4. Installation path relationship.
5. Known application-specific storage path.
6. Historical inventory association.
7. Generic folder-name similarity (weak only).

Each mapping must persist evidence, not just a score.

## 10.2 Ownership states

- `InstalledProtected`
- `PotentialOrphan`
- `ConfirmedOrphanByHistory`
- `SharedProtected`
- `CacheCandidate`
- `UnknownPreserved`
- `UserConfirmedOwnership`
- `UserRejectedMapping`

Do not make `ConfirmedOrphanByHistory` cleanup eligible automatically. User approval and cleanup rule validation remain mandatory.

## 10.3 Historical inventory

At every successful application discovery, save a snapshot. When an application disappears from the current inventory:

- Check whether the prior record was reliable.
- Check whether the application is packaged, portable or per-user.
- Check for stale registry records.
- Search only configured roots for related storage.
- Record evidence and last-seen timestamp.
- Present a potential orphan candidate for manual review.

---

# 11. Cache rule catalogue

Rules must be local, versioned and explicit. A rule must include:

- Rule ID and version.
- Application key or supported generic cache type.
- Relative path pattern.
- Data classification.
- Cleanup permission.
- Required process-closed condition.
- Rebuildability description.
- Sensitive-data warning.
- Minimum age, if appropriate.
- Validation method.

Example only:

```json
{
  "ruleId": "example-app-cache-001",
  "ruleVersion": "1.0",
  "applicationKey": "example-app",
  "relativePath": "Cache",
  "dataType": "ApplicationCache",
  "cleanupAllowed": false,
  "requiresApplicationClosed": true,
  "minimumAgeDays": 7,
  "description": "Example rule. Must be validated for the actual application."
}
```

Default all newly introduced rules to `cleanupAllowed: false`. A rule must be reviewed and tested before being enabled.

---

# 12. WPF UI specification

## 12.1 Main navigation

1. Dashboard
2. Applications
3. Orphaned Data
4. Cache Analyzer
5. Storage Explorer
6. Cleanup Center
7. Quarantine & Restore
8. Scan History
9. Settings

## 12.2 Dashboard

Show:

- Installed application count.
- Potential orphan count.
- Cache candidate count.
- Total scanned size.
- Protected data size.
- Estimated eligible cleanup size.
- Quarantine size.
- Last scan date and status.
- Scan progress and errors.

Never label all candidate data as recoverable. Distinguish:
- Discovered bytes.
- Candidate bytes.
- Validated eligible bytes.
- Quarantined bytes.
- Actually recovered disk space.

## 12.3 Applications screen

Columns:

- Application name.
- Publisher.
- Version.
- Installed status.
- Detection source.
- Associated storage size.
- Number of locations.
- Confidence/evidence status.
- Protection status.

Filters:
- All.
- Installed.
- Potentially uninstalled.
- Portable.
- Unknown.
- Shared ownership.

## 12.4 Application details

Display:

- Application identity and detection evidence.
- Install path and executable.
- All mapped storage locations.
- Size and file count.
- Data type.
- Ownership confidence and evidence.
- Protection reason.
- Cleanup rules applicable to each child item.

## 12.5 Orphaned Data

Display:

- Potential application.
- Evidence of previous installation.
- Last detected timestamp.
- Associated folders.
- Total size.
- Confidence score.
- Conflicting evidence.
- User classification controls.

## 12.6 Cache Analyzer

Categories:
- Application cache.
- Temporary files.
- Updater data.
- Logs/crash dumps.
- Package caches.
- Build caches.

Display rebuild cost and application-closed requirements. Do not automatically select any item.

## 12.7 Storage Explorer

Use a folder tree with:
- Path.
- Size.
- Owner(s).
- Data type.
- Confidence.
- Protection state.
- Last modified.
- Scan errors.

## 12.8 Cleanup Center

Show an exact preview:
- Selected paths.
- Item count.
- Estimated bytes.
- Ownership evidence.
- Protection checks.
- Running processes.
- Destination quarantine.
- Warnings.
- Final approval control.

## 12.9 Quarantine & Restore

Display operation ID, application, original path, quarantine path, bytes, date, verification status and restore/deletion availability.

## 12.10 Settings

- Scan roots and exclusions.
- Maximum scan concurrency.
- Database/data directory.
- Quarantine directory.
- Retention policy.
- Local cleanup rules.
- Protected paths.
- Logging level.
- Export settings.

---

# 13. Cleanup engine and transaction design

## 13.1 Planning

`CleanupPlanner.CreatePlanAsync` must be read-only. It must not move, rename or delete files.

## 13.2 Validation

Immediately before execution, validate:

1. User approval exists.
2. Plan is current and not already executed.
3. Source path remains within approved roots.
4. Path has not been redirected through a reparse point.
5. Ownership is still eligible.
6. No installed application association exists.
7. No shared ownership exists.
8. A valid cleanup rule explicitly allows the operation.
9. Required application processes are closed.
10. File is not locked or in use.
11. Destination is safe and has enough space.
12. Database and journal are writable.

Any failed check blocks the affected item. No force-delete fallback.

## 13.3 Quarantine manifest

Each operation must include:

- Operation ID.
- Application identity if known.
- Original absolute path.
- Relative path.
- Quarantine path.
- File/folder type.
- Size.
- Original timestamps.
- Attributes.
- Relevant ACL/security metadata where supported.
- Alternate data stream handling status.
- Hash/verification status.
- Operation timestamps.
- Per-item result.

## 13.4 Crash recovery

Persist operation states such as:

- `Planned`
- `Approved`
- `InProgress`
- `Quarantined`
- `VerificationFailed`
- `RollbackRequired`
- `RecoveryRequired`
- `Restored`
- `PermanentlyDeleted`
- `Failed`

On startup, inspect incomplete operations and offer safe recovery. Never automatically delete or overwrite files during recovery.

## 13.5 Cross-volume quarantine

A cross-volume move is not atomic. Use:

1. Copy to a unique quarantine staging path.
2. Verify content and metadata as far as supported.
3. Revalidate the original source.
4. Remove the original only if it is unchanged and verification succeeded.
5. Commit the quarantine record.
6. If interrupted, preserve both copies and report recovery required.

If safe metadata preservation cannot be guaranteed, block the item.

---

# 14. Implementation plan — phase by phase

Each phase must be implemented, built, tested and reviewed before moving to the next. Do not generate the entire project in one step.

## Phase 0 — Repository and implementation controls

Tasks:
1. Create solution and project structure.
2. Add project references with correct dependency direction.
3. Configure nullable reference types and analyzers.
4. Configure package versions.
5. Add `.editorconfig`.
6. Add test projects.
7. Add documentation structure.
8. Add a development-only test data fixture system.
9. Add a rule catalogue with all cleanup disabled.
10. Create a baseline build pipeline using local commands.

Files:
- `WindowsCleanupManager.sln`
- `Directory.Build.props`
- `Directory.Packages.props`
- `src/*/*.csproj`
- `.editorconfig`
- `docs/*`

Acceptance:
- Solution builds.
- Tests execute.
- No cleanup operation exists yet.
- Dependency direction is correct.

## Phase 1 — WPF foundation and DI

Tasks:
1. Create WPF application.
2. Configure Generic Host and DI.
3. Configure MVVM Toolkit.
4. Create main navigation shell.
5. Add theme resources.
6. Configure Serilog file logging.
7. Add startup/shutdown lifecycle.
8. Add local data-directory resolution.
9. Add a first-run settings screen.

Files:
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindowViewModel.cs`
- `DependencyInjection.cs`
- `LoggingConfiguration.cs`

Acceptance:
- App launches offline.
- Navigation works.
- Logs are local.
- No administrator rights required.
- No filesystem changes are made.

## Phase 2 — SQLite and migrations

Tasks:
1. Configure Microsoft.Data.Sqlite.
2. Implement database connection factory.
3. Implement migration runner.
4. Create schema from Section 7.
5. Add indexes and constraints.
6. Add repository interfaces.
7. Implement repositories with Dapper.
8. Add database integrity checks.
9. Test database recovery and backup.

Files:
- `SqliteConnectionFactory.cs`
- `DatabaseInitializer.cs`
- `MigrationRunner.cs`
- `Migrations/001_InitialSchema.sql`
- `ApplicationRepository.cs`
- `ScanRepository.cs`
- `AuditRepository.cs`

Acceptance:
- Database initializes.
- Migrations are repeatable and versioned.
- Foreign keys are enforced.
- No database file is stored in a protected executable directory.

## Phase 3 — Application discovery

Tasks:
1. Implement registry provider for HKLM/HKCU/WOW6432Node.
2. Implement packaged-app provider.
3. Implement Start Menu shortcut discovery.
4. Implement portable app evidence provider.
5. Read executable version metadata.
6. Normalize application identities.
7. Deduplicate while retaining source evidence.
8. Save current inventory.
9. Save historical snapshots.

Files:
- `IApplicationDiscoveryProvider.cs`
- `RegistryApplicationProvider.cs`
- `AppxApplicationProvider.cs`
- `StartMenuShortcutProvider.cs`
- `PortableApplicationProvider.cs`
- `ApplicationMetadataReader.cs`
- `ApplicationDiscoveryService.cs`

Acceptance:
- Installed app list is available.
- Duplicate records are controlled.
- Missing paths are reported.
- Historical snapshot is stored.
- No cleanup actions exist.

## Phase 4 — Storage scanning

Tasks:
1. Add configurable scan roots.
2. Implement streaming enumeration.
3. Implement bounded concurrency.
4. Implement directory and file size calculation.
5. Handle access denied, I/O and disappeared files.
6. Detect reparse points.
7. Record scan errors.
8. Add progress and cancellation.
9. Persist scan sessions and items.
10. Add scan summary.

Files:
- `IStorageScanner.cs`
- `StorageScanner.cs`
- `DirectoryEnumerator.cs`
- `FolderSizeCalculator.cs`
- `PathNormalizer.cs`
- `ReparsePointDetector.cs`
- `ScanProgress.cs`
- `ScanRepository.cs`

Acceptance:
- All configured roots are scanned as permitted.
- No link target is traversed.
- Cancellation works.
- UI is not blocked.
- Scan errors do not terminate the entire scan.

## Phase 5 — Mapping and ownership evidence

Tasks:
1. Define application signature format.
2. Implement exact signature matching.
3. Implement executable metadata matching.
4. Implement publisher matching.
5. Implement installation path matching.
6. Implement historical association.
7. Add weak folder-name matching.
8. Add shared ownership detection.
9. Calculate and persist scores.
10. Display evidence explanations.
11. Add manual confirm/reject controls.

Files:
- `IApplicationMapper.cs`
- `ApplicationMapper.cs`
- `OwnershipScorer.cs`
- `SharedStorageDetector.cs`
- `KnownApplicationSignature.cs`
- `OwnershipEvidenceRepository.cs`

Acceptance:
- Mapping is many-to-many.
- Every score is explainable.
- Shared data is protected.
- Installed app associations override scores.
- Unknown data remains protected.

## Phase 6 — Orphan detection

Tasks:
1. Compare current and historical inventories.
2. Identify missing application records.
3. Validate package and portable-app cases.
4. Search for associated storage.
5. Assess historical evidence.
6. Distinguish confirmed history from inferred candidates.
7. Add user ownership corrections.
8. Persist classification and reasons.

Files:
- `IOrphanDetectionService.cs`
- `OrphanDetectionService.cs`
- `HistoricalInventoryAnalyzer.cs`
- `RegistryDifferenceAnalyzer.cs`
- `OrphanClassificationEngine.cs`

Acceptance:
- Orphans are candidates, not automatic deletion targets.
- No missing registry entry alone can confirm uninstall.
- Unknown and shared paths are blocked.
- Evidence is displayed.

## Phase 7 — Cache analysis and rule catalogue

Tasks:
1. Define versioned rule schema.
2. Add rule validation.
3. Load local JSON rules.
4. Implement application-specific cache providers.
5. Implement temporary file rules.
6. Implement updater data rules.
7. Implement package cache rules individually.
8. Add process-closed checks.
9. Calculate eligible bytes.
10. Keep all new rules disabled by default.

Files:
- `CleanupRule.cs`
- `RuleCatalogueLoader.cs`
- `RuleValidator.cs`
- `ICacheAnalyzer.cs`
- `CacheAnalyzer.cs`
- `ApplicationCacheProvider.cs`
- `RunningProcessDetector.cs`
- `Rules/application-rules.json`

Acceptance:
- Unknown rules are rejected.
- Invalid rules fail closed.
- Top-level development directories are not treated as cache.
- Cache candidates are visible separately from eligible items.

## Phase 8 — WPF screens

Tasks:
1. Build dashboard.
2. Build application inventory.
3. Build application details.
4. Build orphaned data screen.
5. Build cache analyzer.
6. Build storage explorer.
7. Build scan history.
8. Build settings.
9. Add search, filters and sorting.
10. Add progress, error and empty states.

Acceptance:
- Data binds to ViewModels.
- Long-running operations use background tasks.
- UI remains responsive.
- Protected items cannot be selected for cleanup.
- All warnings are understandable.

## Phase 9 — Cleanup planner and dry-run validator

Tasks:
1. Create plan entities.
2. Implement selection validation.
3. Revalidate current ownership.
4. Check path containment.
5. Check reparse points.
6. Check processes and file locks.
7. Check shared ownership and protected rules.
8. Calculate exact selected size.
9. Build preview.
10. Persist approved plan.

Files:
- `ICleanupPlanner.cs`
- `CleanupPlanner.cs`
- `CleanupValidator.cs`
- `PathSafetyValidator.cs`
- `CleanupPreviewBuilder.cs`
- `CleanupPlanRepository.cs`

Acceptance:
- Dry-run creates no filesystem changes.
- Every blocked item has a reason.
- Stale plans cannot execute.
- Installed/unknown/shared data cannot pass validation.

## Phase 10 — Quarantine engine

Tasks:
1. Define operation state machine.
2. Implement operation journal.
3. Implement manifest writer.
4. Implement same-volume quarantine.
5. Implement cross-volume staging workflow.
6. Verify data.
7. Revalidate original before removal.
8. Implement rollback and recovery.
9. Update database state.
10. Add progress and cancellation handling.

Acceptance:
- Only approved eligible files are moved.
- Partial failures are visible.
- Interrupted operations can be recovered.
- Original data is not removed before verification.
- No silent overwrite occurs.

## Phase 11 — Restore and permanent deletion

Tasks:
1. List quarantine operations.
2. Implement item-level restore.
3. Implement group restore.
4. Add conflict resolution.
5. Verify restored content.
6. Implement permanent deletion from quarantine only.
7. Require explicit confirmation.
8. Record deletion result.
9. Add audit views.

Acceptance:
- Restore is safe and verified.
- Conflicts never overwrite silently.
- Permanent deletion cannot target original source paths.
- Audit history remains.

## Phase 12 — Reports, portable packaging and hardening

Tasks:
1. Add CSV/JSON exports.
2. Add scan and cleanup summaries.
3. Add application settings export/import if safe.
4. Publish self-contained win-x64.
5. Test from a non-protected writable directory.
6. Test from a read-only executable location with data redirected.
7. Test normal and elevated operation boundaries.
8. Test no-network operation.
9. Run full safety suite.
10. Document backup, restore and recovery procedures.

Acceptance:
- Portable deployment works.
- Database and quarantine remain accessible.
- No network calls occur.
- All safety tests pass.
- Application starts and scans without administrator rights.

---

# 15. Testing strategy

## 15.1 Test environments

Use:
- Unit tests with temporary test directories.
- Integration tests using synthetic application fixtures.
- A disposable Windows 11 VM for registry/AppX and permission tests.
- A test account with representative development tools.
- Never use the primary user profile as the first destructive test target.

## 15.2 Required safety tests

| Test | Expected result |
|---|---|
| Installed app folder | Protected |
| App data with weak name match | Unknown/protected |
| Historical uninstalled app | Potential orphan candidate |
| No historical record | Unconfirmed candidate |
| Shared folder | Protected |
| Browser profile | Protected |
| Git repository | Protected |
| Docker volume | Protected |
| Database file | Protected |
| Reparse point | Not followed |
| Junction to outside root | Not followed |
| File locked by process | Blocked/skipped |
| Stale plan | Rejected |
| Path changed after preview | Rejected |
| Quarantine destination exists | No overwrite |
| Cross-volume interruption | Recovery required |
| Restore destination exists | Conflict shown |
| Database unavailable | Cleanup blocked |
| App crash during quarantine | Journal recovery |
| Permanent delete of original path | Rejected |

## 15.3 Acceptance rule

No cleanup functionality is production-ready until the safety tests pass. A successful build is not proof that cleanup is safe.

---

# 16. Performance and reliability design

- Use bounded worker concurrency, configurable by the user.
- Use streaming filesystem enumeration.
- Batch database writes.
- Keep UI reads separate from scan writes.
- Use SQLite WAL mode where appropriate.
- Avoid hashing all files during inventory.
- Hash only selected quarantine items when needed.
- Cache directory metadata with scan timestamps.
- Provide full rescan and incremental rescan.
- Treat size calculations as estimates when access is incomplete.
- Report inaccessible paths and skipped files.
- Never report incomplete scan totals as complete.

Distinguish:
- Logical file size.
- Allocated disk size.
- Candidate bytes.
- Validated eligible bytes.
- Quarantined bytes.
- Actual freed bytes.

Moving a file to quarantine on the same volume does not free disk space. Permanent deletion is what reclaims the space. The dashboard must communicate this clearly.

---

# 17. Portable deployment and local data

Suggested runtime layout:

```text
WindowsCleanupManager/
├── WindowsCleanupManager.exe
├── Rules/
│   └── application-rules.json
├── Data/
│   └── cleanup.db
├── Logs/
└── Quarantine/
```

The data directory must be configurable. Do not assume the executable directory is writable. Prefer a user-selected writable data root, with a portable mode option that stores data beside the executable when permissions allow.

Publishing command:

```powershell
dotnet publish .\src\WCM.Desktop\WCM.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

Test WPF and native SQLite runtime behavior with the exact published artifact. Keep the database and quarantine outside the single-file executable.

---

# 18. Milestones and delivery order

| Milestone | Deliverable | Exit condition |
|---|---|---|
| M0 | Solution foundation | Build and tests pass |
| M1 | WPF shell and DI | Offline app launches |
| M2 | SQLite persistence | Migrations pass |
| M3 | Application discovery | Inventory is correct |
| M4 | Storage scanner | Scan report is correct |
| M5 | Mapping engine | Evidence is explainable |
| M6 | Orphan detection | Candidates are conservative |
| M7 | Cache rules | Unsupported data stays protected |
| M8 | WPF screens | Review workflow works |
| M9 | Cleanup planner | Dry-run passes safety tests |
| M10 | Quarantine | Reversible operations work |
| M11 | Restore/deletion | Recovery and deletion tests pass |
| M12 | Packaging/QA | Portable release passes all tests |

---

# 19. Definition of Done — v1.0

- [ ] WPF application launches offline.
- [ ] Installed application discovery works.
- [ ] Packaged applications are represented.
- [ ] Configured storage roots are scanned.
- [ ] Scan progress and cancellation work.
- [ ] Application-to-storage mappings are persisted.
- [ ] Shared ownership is detected.
- [ ] Confidence scores show evidence.
- [ ] Historical inventory is retained.
- [ ] Potential orphan detection is conservative.
- [ ] Cache rules are local and versioned.
- [ ] Unknown data is protected.
- [ ] Installed app data is protected.
- [ ] Dry-run cleanup preview works.
- [ ] Quarantine and verification work.
- [ ] Restore and conflict handling work.
- [ ] Permanent deletion is restricted to quarantine.
- [ ] Audit history is retained.
- [ ] Recovery after interrupted operations works.
- [ ] Portable self-contained deployment works.
- [ ] All required safety tests pass.

---

# 20. AI coding agent master implementation prompt

Use the following prompt with Cursor, Claude Code or another repository-aware coding agent after saving this document as `docs/WCM_Master_Plan.md`.

## Prompt 1 — Read and plan only

```text
You are the senior .NET architect and implementation lead for
Windows Cleanup Manager (WCM).

The authoritative project specification is:
@docs/WCM_Master_Plan.md

Your task is to inspect the existing repository and prepare a
detailed implementation plan. Do not implement code yet.

MANDATORY RULES:

1. Read the complete WCM_Master_Plan.md.
2. Inspect the entire existing solution and project structure.
3. Identify existing code, dependencies, conventions and build state.
4. Do not assume a class, method, database table or feature exists
   without verifying it in the repository.
5. Map every requirement to:
   - Project
   - Class
   - Interface
   - Method
   - Database migration
   - WPF View
   - ViewModel
   - Unit test
   - Integration/safety test
6. Identify dependencies between implementation tasks.
7. Identify architectural risks and unresolved design decisions.
8. Create a phase-by-phase implementation checklist.
9. Include exact file paths and intended method signatures.
10. Identify any specification conflicts before implementation.

SAFETY REQUIREMENTS:

- No original file deletion or movement during planning.
- Installed application data must remain protected.
- Unknown and shared ownership must remain protected.
- No cleanup code may be introduced during the planning task.
- Do not use network services or online application identification.
- Do not modify user data or real application settings.

DELIVERABLES:

Create or update:
- docs/Architecture.md
- docs/SRS.md
- docs/Database_Design.md
- docs/UI_Specification.md
- docs/Implementation_Plan.md
- docs/Test_Plan.md
- docs/Requirement_Traceability.md

Every document must agree with WCM_Master_Plan.md.

Before finishing:
- Validate requirement coverage.
- Identify missing requirements.
- Identify conflicts.
- Report current build status.
- Report the exact first implementation task.

Do not begin implementation.
```

## Prompt 2 — Implement one phase at a time

```text
We are implementing Windows Cleanup Manager according to:

@docs/WCM_Master_Plan.md
@docs/Implementation_Plan.md
@docs/Requirement_Traceability.md

IMPLEMENTATION RULES:

1. Read the complete master specification before working.
2. Inspect the actual repository and current implementation state.
3. Work on exactly ONE implementation phase at a time.
4. Implement only the selected phase and its dependencies.
5. Follow the documented architecture and dependency direction.
6. Create or update tests for every meaningful change.
7. Build the complete solution.
8. Run relevant unit and integration tests.
9. Perform a regression check.
10. Update implementation documentation and phase checklist.
11. Report all changed files and methods.
12. Report actual test and build results.

NEVER:

- Implement multiple phases without approval.
- Delete or move original user files during discovery development.
- Enable cleanup rules by default.
- Treat unknown folders as orphaned.
- Clean installed application data.
- Follow reparse points during scans.
- Bypass protection checks.
- Claim tests passed without executing them.
- Use cloud APIs or internet services.
- Replace existing architecture without documented justification.

SAFETY-FIRST DEVELOPMENT:

All destructive operations must be tested using synthetic fixtures
inside temporary test directories or a disposable Windows VM.

The first implementation phase must not contain filesystem
deletion, movement or cleanup functionality.

At the end, provide:
- Implementation summary
- Files created/modified
- Build result
- Test results
- Requirement IDs completed
- Known issues
- Remaining tasks
- Recommended next phase

Stop after completing the selected phase.
```

## Prompt 3 — Independent completeness and safety audit

```text
Perform a strict independent implementation audit of
Windows Cleanup Manager.

Read:
@docs/WCM_Master_Plan.md
@docs/SRS.md
@docs/Implementation_Plan.md
@docs/Requirement_Traceability.md
@docs/Test_Plan.md

Inspect the actual source code, database migrations,
WPF Views/ViewModels, cleanup engine and tests.

Do not trust previous completion claims.

For every functional and non-functional requirement:
1. Locate the actual implementation.
2. Locate the relevant tests.
3. Execute available tests.
4. Verify the behavior.
5. Mark PASS, PARTIAL, FAIL or NOT IMPLEMENTED.
6. Record exact source file and method.
7. Record missing implementation.
8. Identify unsafe behavior.

Perform special security and safety checks:
- Installed application data protection.
- Unknown ownership protection.
- Shared folder protection.
- Path traversal and reparse-point handling.
- Time-of-check/time-of-use validation.
- Running process detection.
- Quarantine integrity.
- Interrupted-operation recovery.
- Restore conflict handling.
- Permanent deletion boundaries.
- Audit logging.
- Offline operation.

Never modify original user files during the audit.

Produce:
- docs/Implementation_Audit.md
- docs/Safety_Audit.md
- Updated docs/Requirement_Traceability.md

Do not mark a requirement complete without source-level
and test-level evidence.
```

---

# 21. First task to execute

**Start with Phase 0: Repository and implementation controls.**

The first deliverable should be a compiling solution containing the six project boundaries, test project, dependency configuration, documentation structure and local rule catalogue with every cleanup operation disabled.

Only after that should the coding agent implement application discovery.

**Project success criterion:** The software must know what data belongs to an application, why it believes that, and why a particular file is safe to move before it is ever allowed to clean anything.
