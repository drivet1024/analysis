WITH
/* Maximum réel dans une fenêtre continue de 60 minutes — convoyeur du haut. */
shift_anchor AS (
    SELECT CASE
        WHEN CURTIME() < '04:00:00'
            THEN CURDATE() - INTERVAL 1 DAY + INTERVAL 16 HOUR
        ELSE CURDATE() + INTERVAL 16 HOUR
    END AS current_shift_start
),
first_scan_by_parcel AS (
    SELECT
        DATE(ph.DATE_LIV - INTERVAL 4 HOUR) AS shift_date,
        ph.PARCEL_ID,
        MIN(ph.DATE_LIV) AS first_scan
    FROM parcel_history PARTITION (p2026) ph
    CROSS JOIN shift_anchor sa
    WHERE ph.EXCEPTION = 903
      AND ph.DEPOT_ID = 1
      AND ph.SOURCE_TYPE = 200
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID = 1)
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND ph.DATE_INSERT >= sa.current_shift_start - INTERVAL 14 DAY - INTERVAL 1 HOUR
      AND ph.DATE_INSERT <  sa.current_shift_start
      AND ph.DATE_LIV >= sa.current_shift_start - INTERVAL 14 DAY
      AND ph.DATE_LIV <  sa.current_shift_start
      AND (HOUR(ph.DATE_LIV) >= 16 OR HOUR(ph.DATE_LIV) < 4)
    GROUP BY DATE(ph.DATE_LIV - INTERVAL 4 HOUR), ph.PARCEL_ID
),
minute_counts AS (
    SELECT
        shift_date,
        CAST(DATE_FORMAT(first_scan, '%Y-%m-%d %H:%i:00') AS DATETIME) AS minute_start,
        COUNT(*) AS parcels
    FROM first_scan_by_parcel
    GROUP BY
        shift_date,
        CAST(DATE_FORMAT(first_scan, '%Y-%m-%d %H:%i:00') AS DATETIME)
),
rolling_60_minutes AS (
    SELECT
        m1.shift_date,
        m1.minute_start AS window_start,
        m1.minute_start + INTERVAL 60 MINUTE AS window_end,
        (
            SELECT SUM(m2.parcels)
            FROM minute_counts m2
            WHERE m2.shift_date = m1.shift_date
              AND m2.minute_start >= m1.minute_start
              AND m2.minute_start <  m1.minute_start + INTERVAL 60 MINUTE
        ) AS parcels_in_60_minutes
    FROM minute_counts m1
    -- Garde seulement les fenêtres complètes à l'intérieur du quart 16 h–4 h.
    WHERE m1.minute_start <= TIMESTAMP(m1.shift_date) + INTERVAL 27 HOUR
)
SELECT
    shift_date,
    window_start,
    window_end,
    parcels_in_60_minutes
FROM rolling_60_minutes
ORDER BY parcels_in_60_minutes DESC, window_start DESC
LIMIT 1;

