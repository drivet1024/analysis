WITH raw AS (
    -- Validation de référence : St-Hubert, journée opérationnelle du 15 juillet 2026.
    -- Cette requête est en lecture seule et ne dépend jamais de conveyor_status.
    SELECT
        CASE WHEN ph.SOURCE_ID = 3 THEN 'sth-floor' ELSE 'sth-top' END AS conveyor_key,
        ph.PARCEL_ID,
        ph.DATE_LIV,
        ph.CHUTE_NO,
        ph.WEIGHT,
        ph.LENGTH,
        ph.WIDTH,
        ph.HEIGHT
    FROM parcel_history ph
    WHERE ph.EXCEPTION = 903
      AND ph.SOURCE_TYPE = 200
      AND ph.DEPOT_ID = 1
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1, 3))
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.DATE_INSERT >= '2026-07-14 15:00:00'
      AND ph.DATE_INSERT <  '2026-07-17 03:00:00'
      AND ph.DATE_LIV >= '2026-07-15 15:00:00'
      AND ph.DATE_LIV <  '2026-07-16 03:00:00'
),
repeated_chute AS (
    SELECT DISTINCT conveyor_key, PARCEL_ID
    FROM (
        SELECT conveyor_key, PARCEL_ID, CHUTE_NO
        FROM raw
        WHERE CHUTE_NO IS NOT NULL AND CHUTE_NO <> 98
        GROUP BY conveyor_key, PARCEL_ID, CHUTE_NO
        HAVING COUNT(*) >= 2
    ) x
),
parcel_rollup AS (
    SELECT
        r.conveyor_key,
        r.PARCEL_ID,
        COUNT(*) AS passages,
        MAX(r.CHUTE_NO = 98) AS chute_98,
        MAX(rc.PARCEL_ID IS NOT NULL) AS same_chute_repeated,
        MAX(r.WEIGHT IS NULL OR r.WEIGHT <= 0) AS no_weight,
        MAX(r.LENGTH IS NULL OR r.LENGTH <= 0 OR r.WIDTH IS NULL OR r.WIDTH <= 0 OR r.HEIGHT IS NULL OR r.HEIGHT <= 0) AS no_dimensions
    FROM raw r
    LEFT JOIN repeated_chute rc
      ON rc.conveyor_key = r.conveyor_key
     AND rc.PARCEL_ID = r.PARCEL_ID
    GROUP BY r.conveyor_key, r.PARCEL_ID
)
SELECT
    conveyor_key,
    COUNT(*) AS unique_parcels,
    SUM(passages) AS passages,
    SUM(passages > 1) AS recirculated,
    ROUND(SUM(passages > 1) / COUNT(*) * 100, 2) AS recirculation_pct,
    SUM(chute_98) AS chute_98,
    SUM(same_chute_repeated) AS same_chute_repeated,
    SUM(no_weight) AS no_weight,
    ROUND(SUM(no_weight) / COUNT(*) * 100, 2) AS no_weight_pct,
    SUM(no_dimensions) AS no_dimensions,
    ROUND(SUM(no_dimensions) / COUNT(*) * 100, 2) AS no_dimensions_pct
FROM parcel_rollup
GROUP BY conveyor_key
ORDER BY conveyor_key;
