SELECT
    NOW() AS database_now,
    CASE WHEN line_id = 3 THEN 'Sol' ELSE 'Haut' END AS conveyor_line,
    COUNT(*) AS raw_passages,
    COUNT(DISTINCT NULLIF(parcel_id, 0)) AS unique_readable_parcels,
    SUM(parcel_id IS NULL OR parcel_id = 0) AS unreadable_passages,
    MIN(date_insert) AS first_scan,
    MAX(date_insert) AS last_scan
FROM parcel_scan_history
WHERE depot_id = 1
  AND line_id IN (0, 1, 3)
  AND date_insert >= CURDATE() + INTERVAL 16 HOUR
  AND date_insert < CURDATE() + INTERVAL 1 DAY
GROUP BY conveyor_line
ORDER BY conveyor_line;
