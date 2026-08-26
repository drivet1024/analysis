WITH ranked_events AS (
    SELECT
        ph.PARCEL_ID,
        ph.SOURCE_ID,
        ph.Destination_Depot_ID,
        ph.CHUTE_NO,
        ph.WEIGHT,
        ph.HEIGHT,
        ph.WIDTH,
        ph.LENGTH,
        ph.DATE_INSERT,
        ROW_NUMBER() OVER (
            PARTITION BY ph.PARCEL_ID
            ORDER BY ph.DATE_INSERT DESC, ph.PARCEL_HISTORY_ID DESC
        ) AS row_rank
    FROM parcel_history PARTITION (p2026) AS ph
    WHERE ph.DATE_INSERT >= '2026-07-12 17:00:00'
      AND ph.DATE_INSERT <  '2026-07-13 08:00:00'
      AND ph.DEPOT_ID = 1
      AND ph.SOURCE_TYPE = 200
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.PARCEL_ID IS NOT NULL
)
SELECT
    COUNT(*) AS UNIQUE_ROUTED_PARCELS,
    SUM(Destination_Depot_ID IS NULL OR Destination_Depot_ID = 0) AS MISSING_DESTINATION_DEPOT,
    SUM(CHUTE_NO IS NULL) AS MISSING_CHUTE,
    SUM(WEIGHT IS NULL OR WEIGHT <= 0) AS MISSING_WEIGHT,
    SUM(HEIGHT IS NULL OR HEIGHT <= 0 OR WIDTH IS NULL OR WIDTH <= 0 OR LENGTH IS NULL OR LENGTH <= 0)
        AS MISSING_DIMENSIONS
FROM ranked_events
WHERE row_rank = 1;
