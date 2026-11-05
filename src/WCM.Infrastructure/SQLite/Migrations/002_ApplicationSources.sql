ALTER TABLE Applications ADD COLUMN ReviewReason TEXT;

CREATE TABLE ApplicationSourceRecords (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ApplicationId INTEGER NOT NULL,
    SourceKey TEXT NOT NULL,
    DetectionSource TEXT NOT NULL,
    Name TEXT NOT NULL,
    Publisher TEXT,
    Version TEXT,
    InstallLocation TEXT,
    ExecutablePath TEXT,
    Architecture TEXT,
    Status TEXT NOT NULL,
    ObservedAt TEXT NOT NULL,
    FOREIGN KEY(ApplicationId) REFERENCES Applications(Id)
);

CREATE INDEX IX_SourceRecords_ApplicationId ON ApplicationSourceRecords(ApplicationId);
