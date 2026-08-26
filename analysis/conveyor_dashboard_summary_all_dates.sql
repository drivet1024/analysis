WITH valid_dates AS (
    SELECT sort_date
    FROM conveyor_status
    WHERE depot_id = 1
      AND line_id IN (0, 1, 3)
      AND sort_date >= '2026-06-01'
      AND sort_date <= '2026-07-15'
    GROUP BY sort_date
    HAVING COUNT(DISTINCT line_id) = 3
       AND SUM(nb_parcel) > 0
),
scan_base AS (
    SELECT
        DATE_FORMAT(
            CASE
                WHEN TIME(date_insert) >= '17:00:00' THEN DATE(date_insert)
                ELSE DATE(date_insert - INTERVAL 1 DAY)
            END,
            '%Y-%m-%d'
        ) AS SORT_DATE,
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
      AND date_insert >= '2026-06-01 17:00:00'
      AND date_insert < '2026-07-16 08:00:00'
      AND (TIME(date_insert) >= '17:00:00' OR TIME(date_insert) < '08:00:00')
),
readable_ranked AS (
    SELECT
        SORT_DATE,
        CONVEYOR_GROUP,
        parcel_id,
        l,
        h,
        w,
        weight,
        ROW_NUMBER() OVER (
            PARTITION BY SORT_DATE, CONVEYOR_GROUP, parcel_id
            ORDER BY date_insert DESC, id DESC
        ) AS latest_rank,
        COUNT(*) OVER (
            PARTITION BY SORT_DATE, CONVEYOR_GROUP, parcel_id
        ) AS scan_count
    FROM scan_base
    WHERE parcel_id IS NOT NULL
      AND parcel_id <> 0
),
scan_metrics AS (
    SELECT
        SORT_DATE,
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
        SUM(latest_rank = 1 AND (weight IS NULL OR weight <= 0)) AS NO_WEIGHT
    FROM readable_ranked
    GROUP BY SORT_DATE, CONVEYOR_GROUP
),
raw_metrics AS (
    SELECT
        SORT_DATE,
        CONVEYOR_GROUP,
        COUNT(*) AS RAW_SCAN_ROWS,
        SUM(parcel_id IS NULL OR parcel_id = 0) AS UNREADABLE_SCAN_ROWS
    FROM scan_base
    GROUP BY SORT_DATE, CONVEYOR_GROUP
),
status_metrics AS (
    SELECT
        DATE_FORMAT(s.sort_date, '%Y-%m-%d') AS SORT_DATE,
        CASE
            WHEN s.line_id = 3 THEN 'Sol'
            WHEN s.line_id IN (0, 1) THEN 'Haut'
        END AS CONVEYOR_GROUP,
        SUM(s.nb_parcel) AS OFFICIAL_PASSAGES,
        MAX(s.last_update) AS LAST_UPDATE
    FROM conveyor_status AS s
    JOIN valid_dates AS d
      ON d.sort_date = s.sort_date
    WHERE s.depot_id = 1
      AND s.line_id IN (0, 1, 3)
    GROUP BY s.sort_date, CONVEYOR_GROUP
)
SELECT
    s.SORT_DATE,
    s.CONVEYOR_GROUP,
    s.OFFICIAL_PASSAGES,
    COALESCE(r.RAW_SCAN_ROWS, 0) AS RAW_SCAN_ROWS,
    s.OFFICIAL_PASSAGES - COALESCE(r.RAW_SCAN_ROWS, 0) AS OFFICIAL_RAW_GAP,
    COALESCE(r.UNREADABLE_SCAN_ROWS, 0) AS UNREADABLE_SCAN_ROWS,
    COALESCE(m.UNIQUE_READABLE_PARCELS, 0) AS UNIQUE_READABLE_PARCELS,
    COALESCE(m.RECIRCULATED_PARCELS, 0) AS RECIRCULATED_PARCELS,
    ROUND(COALESCE(m.RECIRCULATED_PARCELS, 0) / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS RECIRCULATION_RATE,
    COALESCE(m.NO_DIMENSIONS, 0) AS NO_DIMENSIONS,
    ROUND(COALESCE(m.NO_DIMENSIONS, 0) / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS NO_DIMENSIONS_RATE,
    COALESCE(m.NO_WEIGHT, 0) AS NO_WEIGHT,
    ROUND(COALESCE(m.NO_WEIGHT, 0) / NULLIF(m.UNIQUE_READABLE_PARCELS, 0), 6) AS NO_WEIGHT_RATE,
    s.LAST_UPDATE
FROM status_metrics AS s
LEFT JOIN raw_metrics AS r
  ON r.SORT_DATE = s.SORT_DATE
 AND r.CONVEYOR_GROUP = s.CONVEYOR_GROUP
LEFT JOIN scan_metrics AS m
  ON m.SORT_DATE = s.SORT_DATE
 AND m.CONVEYOR_GROUP = s.CONVEYOR_GROUP
ORDER BY s.SORT_DATE DESC, FIELD(s.CONVEYOR_GROUP, 'Haut', 'Sol');
