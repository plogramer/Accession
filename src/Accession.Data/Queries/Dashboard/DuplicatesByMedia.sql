-- Duplicates within each media (by SHA-1, hashed files only).
SELECT f.MediaKey, COUNT(*) AS HashedFiles, COUNT(DISTINCT f.Sha1) AS UniqueFiles
FROM File f JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
WHERE f.Sha1 IS NOT NULL
  AND (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
GROUP BY f.MediaKey;
