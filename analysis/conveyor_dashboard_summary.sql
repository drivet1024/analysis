WITH scan_base AS (
    SELECT
        CASE
            WHEN line_id = 3 THEN 'Sol'
            WHEN line_id IN (0, 1) THEN 'Haut'
        END AS CONVEYOR_GROUP,
        id,
        parcel_id,
        l,
        h,
        w,
        weight,
        date_insert
    FROM parcel_scan_history
    WHERE depot_id = 1
      AND line_id IN (0, 1, 3)
      AND date_insert >= '2026-07-12 17:00:00'
      AND date_insert < '2026-07-13 08:00:00'
),
readable_ranked AS (
    SELECT
        CONVEYOR_GROUP,
        parcel_id,
        l,
        h,
        w,
        weight,
        ROW_NUMBER() OVER (
            PARTITION BY CONVEYOR_GROUP, parcel_id
            ORDER BY date_insert DESC, id DESC
        ) AS latest_rank,
        COUNT(*) OVER (
            PARTITION BY CONVEYOR_GROUP, parcel_id
        ) AS scan_count
    FROM scan_base
    WHERE parcel_id IS NOT NULL
      AND parcel_id <> 0
),
scan_metrics AS (
    SELECT
        CONVEYOR_GROUP,
        SUM(latest_rank = 1) AS UNIQUE_READABLE_PARCELS,
        SUM(latest_rank = 1 AND scan_count > 1) AS RECIRCULATED_PARCELS,
        SUM(
            latest_rank = 1
            AND (
                l IS NULL OR l <= 0
                OR h IS NULL OR h <= 0
                OR w IS NULL OR w <= 0
            )
        ) AS NO_DIMENSIONS,
        SUM(
            latest_rank = 1
            AND (weight IS NULL OR weight <= 0)
        ) AS NO_WEIGHT
    FROM readable_ranked
    GROUP BY CONVEYOR_GROUP
),
raw_metrics AS (
    SELECT
        CONVEYOR_GROUP,
        COUNT(*) AS RAW_SCAN_ROWS,
        SUM(parcel_id IS NULL OR parcel_id = 0) AS UNREADABLE_SCAN_ROWS
    FROM scan_base
    GROUP BY CONVEYOR_GROUP
),
status_metrics AS (
    SELECT
        CASE
            WHEN line_id = 3 THEN 'Sol'
            WHEN line_id IN (0, 1) THEN 'Haut'
        END AS CONVEYOR_GROUP,
        SUM(nb_parcel) AS OFFICIAL_PASSAGES,
        MAX(last_update) AS LAST_UPDATE
    FROM conveyor_status
    WHERE depot_id = 1
      AND sort_date = '2026-07-12'
      AND line_id IN (0, 1, 3)
    GROUP BY CONVEYOR_GROUP
)
SELECT
    s.CONVEYOR_GROUP,
    s.OFFICIAL_PASSAGES,
    r.RAW_SCAN_ROWS,
    s.OFFICIAL_PASSAGES - r.RAW_SCAN_ROWS AS OFFICIAL_RAW_GAP,
    r.UNREADABLE_SCAN_ROWS,
    m.UNIQUE_READABLE_PARCELS,
    m.RECIRCULATED_PARCELS,
    ROUND(m.RECIRCULATED_PARCELS / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS RECIRCULATION_RATE,
    m.NO_DIMENSIONS,
    ROUND(m.NO_DIMENSIONS / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS NO_DIMENSIONS_RATE,
    m.NO_WEIGHT,
    ROUND(m.NO_WEIGHT / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS NO_WEIGHT_RATE,
    s.LAST_UPDATE
FROM status_metrics AS s
JOIN raw_metrics AS r
  ON r.CONVEYOR_GROUP = s.CONVEYOR_GROUP
JOIN scan_metrics AS m
  ON m.CONVEYOR_GROUP = s.CONVEYOR_GROUP
ORDER BY FIELD(s.CONVEYOR_GROUP, 'Haut', 'Sol');
