CREATE TABLE OrphanCandidates (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    AppKey TEXT,
    ApplicationName TEXT,
    Path TEXT NOT NULL UNIQUE COLLATE NOCASE,
    Classification TEXT NOT NULL,
    Decision TEXT NOT NULL DEFAULT 'Pending',
    SizeBytes INTEGER NOT NULL DEFAULT 0,
    LastDetectedAt TEXT,
    Evidence TEXT NOT NULL
);

ALTER TABLE CleanupPlanItems ADD COLUMN Path TEXT;
ALTER TABLE CleanupPlanItems ADD COLUMN SourceKind TEXT;
ALTER TABLE CleanupPlanItems ADD COLUMN AppKey TEXT;
ALTER TABLE CleanupPlanItems ADD COLUMN RuleId TEXT;
ALTER TABLE CleanupPlanItems ADD COLUMN SizeBytes INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CleanupPlanItems ADD COLUMN ItemCount INTEGER NOT NULL DEFAULT 0;
ALTER TABLE CleanupPlanItems ADD COLUMN Fingerprint TEXT;
ALTER TABLE CleanupPlanItems ADD COLUMN BlockReason TEXT;

ALTER TABLE QuarantineItems ADD COLUMN AppKey TEXT;
ALTER TABLE QuarantineItems ADD COLUMN ItemCount INTEGER NOT NULL DEFAULT 0;
ALTER TABLE QuarantineItems ADD COLUMN CreatedAt TEXT;
ALTER TABLE QuarantineItems ADD COLUMN Message TEXT;
ALTER TABLE QuarantineItems ADD COLUMN RestoredPath TEXT;

ALTER TABLE RestoreOperations ADD COLUMN QuarantineItemId INTEGER REFERENCES QuarantineItems(Id);
ALTER TABLE RestoreOperations ADD COLUMN RestoredPath TEXT;
ALTER TABLE DeletionOperations ADD COLUMN Bytes INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IX_QuarantineItems_Status ON QuarantineItems(Status);
CREATE INDEX IX_RestoreOperations_ItemId ON RestoreOperations(QuarantineItemId);
