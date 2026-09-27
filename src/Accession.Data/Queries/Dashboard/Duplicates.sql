-- Duplicates by SHA-1 across the selected media (hashed files only).
-- Unique bytes count each hash once; duplicate bytes = HashedBytes - UniqueBytes.
WITH f AS (
    SELECT f.Sha1, f.SizeBytes
    FROM File f JOIN Media m ON m.MediaKey = f.MediaKey AND m.IsDeleted = 0
    WHERE f.Sha1 IS NOT NULL
      AND (@MediaKeysJson IS NULL OR m.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))
),
g AS (SELECT Sha1, MAX(SizeBytes) AS SizeBytes FROM f GROUP BY Sha1)
SELECT
    (SELECT COUNT(*) FROM f)                      AS HashedFiles,
    (SELECT COUNT(*) FROM g)                      AS UniqueFiles,
    (SELECT COALESCE(SUM(SizeBytes), 0) FROM f)   AS HashedBytes,
    (SELECT COALESCE(SUM(SizeBytes), 0) FROM g)   AS UniqueBytes,
    (SELECT COUNT(*) FROM File f2 JOIN Media m2 ON m2.MediaKey = f2.MediaKey AND m2.IsDeleted = 0
      WHERE f2.HashStatus = 0
        AND (@MediaKeysJson IS NULL OR m2.MediaKey IN (SELECT value FROM json_each(@MediaKeysJson)))) AS NotHashedFiles;
