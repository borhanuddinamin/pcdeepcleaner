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
    Path TEXT NOT NULL UNIQUE COLLATE NOCASE,
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
    Path TEXT NOT NULL COLLATE NOCASE,
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
    PathPattern TEXT NOT NULL UNIQUE COLLATE NOCASE,
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
CREATE INDEX IX_Snapshots_ApplicationId ON ApplicationSnapshots(ApplicationId);
CREATE INDEX IX_Mappings_ApplicationId
    ON ApplicationStorageMappings(ApplicationId);
CREATE INDEX IX_Mappings_StorageLocationId
    ON ApplicationStorageMappings(StorageLocationId);
CREATE INDEX IX_Evidence_MappingId ON OwnershipEvidence(MappingId);
CREATE INDEX IX_ScanItems_SessionId ON ScanItems(ScanSessionId);
CREATE INDEX IX_ScanItems_Path ON ScanItems(Path);
CREATE INDEX IX_PlanItems_PlanId ON CleanupPlanItems(CleanupPlanId);
CREATE INDEX IX_QuarantineItems_OperationId
    ON QuarantineItems(OperationId);
CREATE INDEX IX_DeletionOperations_ItemId ON DeletionOperations(QuarantineItemId);
