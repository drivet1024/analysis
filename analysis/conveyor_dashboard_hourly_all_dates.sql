WITH valid_dates AS (
    SELECT DATE_FORMAT(sort_date, '%Y-%m-%d') AS SORT_DATE
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
        DATE_FORMAT(date_insert, '%Y-%m-%d %H:00:00') AS EVENT_HOUR,
        CASE
            WHEN line_id = 3 THEN 'Sol'
            WHEN line_id IN (0, 1) THEN 'Haut'
        END AS CONVEYOR_GROUP,
        parcel_id,
        l,
        h,
        w,
        weight
    FROM parcel_scan_history
    WHERE depot_id = 1
      AND line_id IN (0, 1, 3)
      AND date_insert >= '2026-06-01 17:00:00'
      AND date_insert < '2026-07-16 08:00:00'
      AND (TIME(date_insert) >= '17:00:00' OR TIME(date_insert) < '08:00:00')
)
SELECT
    b.SORT_DATE,
    b.EVENT_HOUR,
    b.CONVEYOR_GROUP,
    COUNT(*) AS RAW_PASSAGES,
    COUNT(DISTINCT NULLIF(b.parcel_id, 0)) AS UNIQUE_READABLE_PARCELS,
    SUM(b.parcel_id IS NULL OR b.parcel_id = 0) AS UNREADABLE_SCAN_ROWS,
    SUM(
        b.l IS NULL OR b.l <= 0
        OR b.h IS NULL OR b.h <= 0
        OR b.w IS NULL OR b.w <= 0
    ) AS MISSING_DIMENSION_SCAN_ROWS,
    SUM(b.weight IS NULL OR b.weight <= 0) AS MISSING_WEIGHT_SCAN_ROWS
FROM scan_base AS b
JOIN valid_dates AS d
  ON d.SORT_DATE = b.SORT_DATE
GROUP BY b.SORT_DATE, b.EVENT_HOUR, b.CONVEYOR_GROUP
ORDER BY b.SORT_DATE DESC, b.EVENT_HOUR, FIELD(b.CONVEYOR_GROUP, 'Haut', 'Sol');
