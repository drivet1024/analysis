WITH conveyor_parcels AS (
    SELECT DISTINCT ph.PARCEL_ID
    FROM parcel_history ph
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.CUSTOMER_ID = 303544
      AND ph.DATE_INSERT >= '2026-07-14 15:00:00'
      AND ph.DATE_INSERT <  '2026-07-17 03:00:00'
      AND ph.DATE_LIV >= '2026-07-15 15:00:00'
      AND ph.DATE_LIV <  '2026-07-16 03:00:00'
      AND COALESCE(ph.VOID, 0) = 0
)
SELECT
    p.EXP_DATE,
    COUNT(DISTINCT cp.PARCEL_ID) AS conveyor_parcels
FROM conveyor_parcels cp
LEFT JOIN parcel p ON p.PARCEL_ID = cp.PARCEL_ID
GROUP BY p.EXP_DATE
ORDER BY p.EXP_DATE;
