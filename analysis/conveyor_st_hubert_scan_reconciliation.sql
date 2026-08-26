WITH raw_scans AS (
    SELECT
        psh.line_id,
        psh.parcel_id
    FROM parcel_scan_history AS psh
    WHERE psh.depot_id = 1
      AND psh.line_id IN (0, 1, 3)
      AND psh.date_insert >= '2026-07-12 17:00:00'
      AND psh.date_insert <  '2026-07-13 08:00:00'
),
line_rollup AS (
    SELECT
        CAST(line_id AS CHAR) AS LINE_ID,
        COUNT(*) AS RAW_SCAN_ROWS,
        SUM(parcel_id IS NOT NULL AND parcel_id <> 0) AS READABLE_SCAN_ROWS,
        COUNT(DISTINCT NULLIF(parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
        SUM(parcel_id IS NULL OR parcel_id = 0) AS UNREADABLE_SCAN_ROWS
    FROM raw_scans
    GROUP BY line_id
),
overall_rollup AS (
    SELECT
        'TOTAL' AS LINE_ID,
        COUNT(*) AS RAW_SCAN_ROWS,
        SUM(parcel_id IS NOT NULL AND parcel_id <> 0) AS READABLE_SCAN_ROWS,
        COUNT(DISTINCT NULLIF(parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
        SUM(parcel_id IS NULL OR parcel_id = 0) AS UNREADABLE_SCAN_ROWS
    FROM raw_scans
)
SELECT
    LINE_ID,
    RAW_SCAN_ROWS,
    READABLE_SCAN_ROWS,
    UNIQUE_READABLE_PARCELS,
    UNREADABLE_SCAN_ROWS,
    READABLE_SCAN_ROWS - UNIQUE_READABLE_PARCELS AS EXTRA_READABLE_SCANS
FROM line_rollup
UNION ALL
SELECT
    LINE_ID,
    RAW_SCAN_ROWS,
    READABLE_SCAN_ROWS,
    UNIQUE_READABLE_PARCELS,
    UNREADABLE_SCAN_ROWS,
    READABLE_SCAN_ROWS - UNIQUE_READABLE_PARCELS AS EXTRA_READABLE_SCANS
FROM overall_rollup
ORDER BY CASE WHEN LINE_ID = 'TOTAL' THEN 1 ELSE 0 END, LINE_ID;
