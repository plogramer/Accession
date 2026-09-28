-- Schema v3: saved searches record the computer they were created on and the computer of the last change.
ALTER TABLE SavedSearch ADD COLUMN CreatedOnMachine TEXT;
ALTER TABLE SavedSearch ADD COLUMN ModifiedOnMachine TEXT;
