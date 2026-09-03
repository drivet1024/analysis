SELECT
    'parcel_scan_history' AS source_name,
    NOW() AS database_now,
    depot_id,
    line_id,
    COUNT(*) AS rows_today,
    MIN(date_insert) AS first_seen,
    MAX(date_insert) AS last_seen
FROM parcel_scan_history
WHERE date_insert >= CURDATE()
GROUP BY depot_id, line_id
ORDER BY last_seen DESC, rows_today DESC
LIMIT 50;
