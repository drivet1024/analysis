SELECT
    'conveyor_hourlystats' AS SOURCE_TABLE,
    MIN(DATE_HEURE) AS MIN_TS,
    MAX(DATE_HEURE) AS MAX_TS,
    COUNT(*) AS ROW_COUNT,
    COUNT(DISTINCT DEPOT_ID) AS DEPOTS
FROM conveyor_hourlystats
UNION ALL
SELECT
    'conveyor_stats_history',
    MIN(last_update),
    MAX(last_update),
    COUNT(*),
    COUNT(DISTINCT depot_id)
FROM conveyor_stats_history
UNION ALL
SELECT
    'parcel_scan_history',
    MIN(date_insert),
    MAX(date_insert),
    COUNT(*),
    COUNT(DISTINCT depot_id)
FROM parcel_scan_history;
