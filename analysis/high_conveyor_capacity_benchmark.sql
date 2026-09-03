WITH
shift_anchor AS (
    SELECT CASE
        WHEN CURTIME()<'04:00:00' THEN CURDATE()-INTERVAL 1 DAY+INTERVAL 16 HOUR
        ELSE CURDATE()+INTERVAL 16 HOUR
    END current_shift_start
),
first_by_shift AS (
    SELECT DATE(ph.DATE_LIV-INTERVAL 4 HOUR) shift_date,
           ph.PARCEL_ID parcel_id,
           MIN(ph.DATE_LIV) first_scan
    FROM parcel_history PARTITION (p2026) ph
    CROSS JOIN shift_anchor sa
    WHERE ph.EXCEPTION=903
      AND ph.DEPOT_ID=1
      AND ph.SOURCE_TYPE=200
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID=1)
      AND COALESCE(ph.VOID,0)=0
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID<>0
      AND ph.DATE_INSERT>=sa.current_shift_start-INTERVAL 14 DAY-INTERVAL 1 HOUR
      AND ph.DATE_INSERT<sa.current_shift_start
      AND ph.DATE_LIV>=sa.current_shift_start-INTERVAL 14 DAY
      AND ph.DATE_LIV<sa.current_shift_start
      AND (HOUR(ph.DATE_LIV)>=16 OR HOUR(ph.DATE_LIV)<4)
    GROUP BY DATE(ph.DATE_LIV-INTERVAL 4 HOUR),ph.PARCEL_ID
),
minute_counts AS (
    SELECT shift_date,DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00') minute_start,COUNT(*) parcels
    FROM first_by_shift
    GROUP BY shift_date,DATE_FORMAT(first_scan,'%Y-%m-%d %H:%i:00')
),
ranked_minutes AS (
    SELECT shift_date,parcels,
           ROW_NUMBER() OVER (PARTITION BY shift_date ORDER BY parcels) row_number_asc,
           COUNT(*) OVER (PARTITION BY shift_date) active_minutes
    FROM minute_counts
),
shift_stats AS (
    SELECT shift_date,
           MAX(parcels) peak_per_minute,
           MAX(CASE WHEN row_number_asc=CEIL(active_minutes*0.95) THEN parcels END) p95_active_minute,
           ROUND(AVG(parcels),1) average_active_minute,
           MAX(active_minutes) active_minutes
    FROM ranked_minutes
    GROUP BY shift_date
)
SELECT shift_date,peak_per_minute,p95_active_minute,average_active_minute,active_minutes
FROM shift_stats
ORDER BY shift_date DESC;
