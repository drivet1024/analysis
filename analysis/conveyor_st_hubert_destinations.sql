WITH ranked_events AS (
    SELECT
        ph.PARCEL_ID,
        ph.Destination_Depot_ID,
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
    COALESCE(d.DEPOTNAME, CONCAT('DEPOT ', re.Destination_Depot_ID), 'NON RENSEIGNÉ') AS DESTINATION,
    re.Destination_Depot_ID,
    COUNT(*) AS UNIQUE_PARCELS
FROM ranked_events AS re
LEFT JOIN depot AS d
    ON d.DEPOTNUMBER = re.Destination_Depot_ID
WHERE re.row_rank = 1
GROUP BY DESTINATION, re.Destination_Depot_ID
ORDER BY UNIQUE_PARCELS DESC;
