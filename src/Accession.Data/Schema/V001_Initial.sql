-- Accession inventory schema, version 1 (requirements section 7.3).
-- Timestamps: UTC ISO-8601 text yyyy-MM-ddTHH:mm:ss.fffffffZ. Sizes: INTEGER bytes.

-- One row. Inventory identity, matter info and root location.
CREATE TABLE InventoryConfig (
    ConfigId             INTEGER PRIMARY KEY CHECK (ConfigId = 1),
    InventoryGuid        TEXT    NOT NULL,
    SchemaVersion        INTEGER NOT NULL,
    RootPath             TEXT    NOT NULL,
    ClientName           TEXT    NOT NULL,
    ClientCode           TEXT    NOT NULL,          -- "Client ID"
    MatterName           TEXT    NOT NULL,
    MatterCode           TEXT    NOT NULL,          -- "Matter ID"
    Description          TEXT,
    MatterUrl            TEXT,
    CreatedAtUtc         TEXT    NOT NULL,
    CreatedBy            TEXT    NOT NULL,
    CreatedOnMachine     TEXT    NOT NULL,
    CreatedAppVersion    TEXT    NOT NULL,
    LastOpenedAtUtc      TEXT,
    LastOpenedBy         TEXT,
    LastDbPath           TEXT
);

-- Applied schema migrations.
CREATE TABLE SchemaMigration (
    Version              INTEGER PRIMARY KEY,
    AppliedAtUtc         TEXT    NOT NULL,
    AppliedBy            TEXT    NOT NULL,
    AppVersion           TEXT    NOT NULL
);

-- One row. Single-user lock.
CREATE TABLE InventoryLock (
    LockId               INTEGER PRIMARY KEY CHECK (LockId = 1),
    IsLocked             INTEGER NOT NULL DEFAULT 0,
    LockedBy             TEXT,
    MachineName          TEXT,
    ProcessId            INTEGER,
    SessionGuid          TEXT,
    LockedAtUtc          TEXT,
    HeartbeatAtUtc       TEXT
);

CREATE TABLE Media (
    MediaKey             INTEGER PRIMARY KEY,
    MediaId              TEXT    NOT NULL COLLATE NOCASE,   -- folder name
    RelativePath         TEXT    NOT NULL COLLATE NOCASE,   -- \123-123_001\
    Status               TEXT    NOT NULL,
    StatusBeforeMissing  TEXT,                              -- restored when a Missing folder reappears (DSC-03)
    AddedAtUtc           TEXT    NOT NULL,
    AddedBy              TEXT    NOT NULL,
    ScanCount            INTEGER NOT NULL DEFAULT 0,
    LastScanStartedUtc   TEXT,
    LastScanCompletedUtc TEXT,
    FolderCount          INTEGER NOT NULL DEFAULT 0,
    FileCount            INTEGER NOT NULL DEFAULT 0,
    TotalBytes           INTEGER NOT NULL DEFAULT 0,
    HashedCount          INTEGER NOT NULL DEFAULT 0,
    ErrorCount           INTEGER NOT NULL DEFAULT 0,
    IsDeleted            INTEGER NOT NULL DEFAULT 0,
    DeletedAtUtc         TEXT,
    DeletedBy            TEXT
);
CREATE UNIQUE INDEX UX_Media_MediaId_Active ON Media (MediaId) WHERE IsDeleted = 0;

CREATE TABLE Folder (
    FolderId             INTEGER PRIMARY KEY,
    MediaKey             INTEGER NOT NULL REFERENCES Media (MediaKey),
    ParentFolderId       INTEGER REFERENCES Folder (FolderId),
    Name                 TEXT    NOT NULL COLLATE NOCASE,
    RelativePath         TEXT    NOT NULL COLLATE NOCASE,
    CreatedUtc           TEXT,
    ModifiedUtc          TEXT,
    AccessedUtc          TEXT,
    IsReparsePoint       INTEGER NOT NULL DEFAULT 0,
    IsEnumerated         INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX UX_Folder_Media_Path ON Folder (MediaKey, RelativePath);
CREATE INDEX IX_Folder_Parent ON Folder (ParentFolderId);

CREATE TABLE File (
    FileId               INTEGER PRIMARY KEY,
    MediaKey             INTEGER NOT NULL REFERENCES Media (MediaKey),
    FolderId             INTEGER NOT NULL REFERENCES Folder (FolderId),
    Name                 TEXT    NOT NULL COLLATE NOCASE,
    Extension            TEXT    NOT NULL COLLATE NOCASE,
    SizeBytes            INTEGER NOT NULL,
    CreatedUtc           TEXT,
    ModifiedUtc          TEXT,
    AccessedUtc          TEXT,
    Sha1                 TEXT,
    HashStatus           INTEGER NOT NULL DEFAULT 0,        -- 0 Pending, 1 Hashed, 2 Error, 3 Skipped (reparse point)
    HashedAtUtc          TEXT
);
CREATE INDEX IX_File_Folder        ON File (FolderId);
CREATE INDEX IX_File_Media_Ext     ON File (MediaKey, Extension);
CREATE INDEX IX_File_Sha1          ON File (Sha1) WHERE Sha1 IS NOT NULL;
CREATE INDEX IX_File_Media_Pending ON File (MediaKey) WHERE HashStatus = 0;
CREATE INDEX IX_File_Size          ON File (SizeBytes);

CREATE TABLE ScanLog (
    ScanId               INTEGER PRIMARY KEY,
    MediaKey             INTEGER NOT NULL REFERENCES Media (MediaKey),
    MediaId              TEXT    NOT NULL,
    ScanType             TEXT    NOT NULL,                  -- Full | Resume | RetryFailed
    StartedAtUtc         TEXT    NOT NULL,
    EndedAtUtc           TEXT,
    Outcome              TEXT,
    UserName             TEXT    NOT NULL,
    MachineName          TEXT    NOT NULL,
    AppVersion           TEXT    NOT NULL,
    EnumThreads          INTEGER NOT NULL,
    HashThreads          INTEGER NOT NULL,
    FolderCount          INTEGER,
    FileCount            INTEGER,
    TotalBytes           INTEGER,
    HashedCount          INTEGER,
    ErrorCount           INTEGER,
    Notes                TEXT
);
CREATE INDEX IX_ScanLog_Media ON ScanLog (MediaKey);

CREATE TABLE ScanError (
    ErrorId              INTEGER PRIMARY KEY,
    MediaKey             INTEGER NOT NULL REFERENCES Media (MediaKey),
    ScanId               INTEGER REFERENCES ScanLog (ScanId),
    RelativePath         TEXT    NOT NULL COLLATE NOCASE,
    ItemType             TEXT    NOT NULL,                  -- Folder | File
    ErrorType            TEXT    NOT NULL,
    Severity             TEXT    NOT NULL DEFAULT 'Error',  -- Error | Warning | Info
    ErrorCode            INTEGER,
    Message              TEXT,
    OccurredAtUtc        TEXT    NOT NULL
);
CREATE INDEX IX_ScanError_Media ON ScanError (MediaKey);

-- Application-defined categories (seeded, not editable in the UI).
CREATE TABLE FileCategory (
    CategoryId           INTEGER PRIMARY KEY,
    Name                 TEXT    NOT NULL UNIQUE,
    Description          TEXT,
    SortOrder            INTEGER NOT NULL
);

CREATE TABLE ExtensionCategory (
    Extension            TEXT    PRIMARY KEY COLLATE NOCASE,
    CategoryId           INTEGER NOT NULL REFERENCES FileCategory (CategoryId)
);

-- Filled at the end of each scan; drives the dashboard.
CREATE TABLE MediaExtensionSummary (
    MediaKey             INTEGER NOT NULL REFERENCES Media (MediaKey),
    Extension            TEXT    NOT NULL COLLATE NOCASE,
    FileCount            INTEGER NOT NULL,
    TotalBytes           INTEGER NOT NULL,
    PRIMARY KEY (MediaKey, Extension)
);

CREATE TABLE AuditLog (
    AuditId              INTEGER PRIMARY KEY,
    OccurredAtUtc        TEXT    NOT NULL,
    UserName             TEXT    NOT NULL,
    MachineName          TEXT    NOT NULL,
    Action               TEXT    NOT NULL,
    MediaId              TEXT,
    Details              TEXT
);
CREATE INDEX IX_AuditLog_Time ON AuditLog (OccurredAtUtc);
