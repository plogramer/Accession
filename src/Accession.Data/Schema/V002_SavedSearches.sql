-- Schema v2: saved searches (fixed lists of files the user collects on the Files screen).
-- Files are remembered by media, folder path and file name rather than FileId, so they stay in the list after a
-- rescan (which replaces the File rows).

CREATE TABLE SavedSearch (
    SavedSearchId        INTEGER PRIMARY KEY,
    Name                 TEXT    NOT NULL COLLATE NOCASE,
    Description          TEXT,
    CreatedAtUtc         TEXT    NOT NULL,
    CreatedBy            TEXT    NOT NULL,
    ModifiedAtUtc        TEXT    NOT NULL,
    ModifiedBy           TEXT    NOT NULL
);
CREATE UNIQUE INDEX UX_SavedSearch_Name ON SavedSearch (Name);

CREATE TABLE SavedSearchFile (
    SavedSearchId        INTEGER NOT NULL REFERENCES SavedSearch (SavedSearchId) ON DELETE CASCADE,
    MediaKey             INTEGER NOT NULL,
    FolderPath           TEXT    NOT NULL COLLATE NOCASE,   -- Folder.RelativePath
    Name                 TEXT    NOT NULL COLLATE NOCASE,   -- File.Name
    AddedAtUtc           TEXT    NOT NULL,
    AddedBy              TEXT    NOT NULL,
    PRIMARY KEY (SavedSearchId, MediaKey, FolderPath, Name)
) WITHOUT ROWID;
CREATE INDEX IX_SavedSearchFile_Media ON SavedSearchFile (MediaKey);
