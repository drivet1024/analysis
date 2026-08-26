EXPLAIN
WITH ranked_events AS (
    SELECT
        ph.PARCEL_ID,
        ph.SHIPPING_ID,
        ph.EXP_DATE,
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
SELECT COUNT(*)
FROM ranked_events AS re
LEFT JOIN shipment PARTITION (p2026) AS s
    ON s.SHIPPING_ID = re.SHIPPING_ID
   AND s.EXP_DATE = re.EXP_DATE
LEFT JOIN location AS l
    ON l.LOC_POSTAL_CODE = REPLACE(UPPER(s.DEST_POSTAL_CODE), ' ', '')
WHERE re.row_rank = 1;
