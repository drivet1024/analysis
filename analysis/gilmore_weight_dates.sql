SELECT
  CASE
    WHEN TIME(ph.DATE_LIV) < '09:00:00' THEN DATE(ph.DATE_LIV - INTERVAL 1 DAY)
    ELSE DATE(ph.DATE_LIV)
  END AS operational_date,
  DATE(ph.DATE_LIV) AS calendar_date,
  COUNT(*) AS weighted_passages,
  COUNT(DISTINCT ph.PARCEL_ID) AS weighted_parcels,
  MIN(ph.DATE_LIV) AS first_weight_time,
  MAX(ph.DATE_LIV) AS last_weight_time,
  MIN(ph.WEIGHT) AS min_weight,
  MAX(ph.WEIGHT) AS max_weight
FROM parcel_history ph
WHERE ph.EXCEPTION = 903
  AND ph.SOURCE_TYPE = 200
  AND ph.DEPOT_ID = 28
  AND ph.SOURCE_ID IS NULL
  AND ph.PARCEL_ID IS NOT NULL
  AND ph.PARCEL_ID <> 0
  AND COALESCE(ph.VOID, 0) = 0
  AND ph.DATE_INSERT >= '2026-07-01'
  AND ph.DATE_INSERT < '2026-08-01'
  AND ph.DATE_LIV >= '2026-07-01'
  AND ph.DATE_LIV < '2026-08-01'
  AND ph.WEIGHT IS NOT NULL
  AND ph.WEIGHT > 0
GROUP BY operational_date, calendar_date
ORDER BY operational_date DESC, calendar_date DESC
LIMIT 100;
