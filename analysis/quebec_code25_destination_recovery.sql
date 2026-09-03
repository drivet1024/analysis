WITH code25_parcels AS (
    SELECT ph.PARCEL_ID,MIN(ph.DATE_LIV) first_code25,MAX(ph.DATE_LIV) last_code25
    FROM parcel_history PARTITION (p2026) ph
    WHERE ph.DEPOT_ID=2 AND ph.EXCEPTION=25 AND ph.SOURCE_TYPE=900
      AND COALESCE(ph.VOID,0)=0 AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
      AND ph.DATE_INSERT>=CURDATE()-INTERVAL 1 DAY
      AND ph.DATE_INSERT<CURDATE()+INTERVAL 1 DAY
      AND ph.DATE_LIV>=CURDATE() AND ph.DATE_LIV<CURDATE()+INTERVAL 1 DAY
    GROUP BY ph.PARCEL_ID
),
destinations AS (
    SELECT cp.PARCEL_ID,
           COUNT(DISTINCT ph.Destination_Depot_ID) destination_count,
           MAX(ph.Destination_Depot_ID) destination_depot_id,
           GROUP_CONCAT(DISTINCT ph.Destination_Depot_ID ORDER BY ph.Destination_Depot_ID) destination_values
    FROM code25_parcels cp
    LEFT JOIN parcel_history PARTITION (p2026) ph
      ON ph.PARCEL_ID=cp.PARCEL_ID
     AND ph.Destination_Depot_ID IS NOT NULL
     AND COALESCE(ph.VOID,0)=0
     AND ph.DATE_INSERT>=CURDATE()-INTERVAL 30 DAY
    GROUP BY cp.PARCEL_ID
)
SELECT destination_count,destination_depot_id,destination_values,COUNT(*) parcels
FROM destinations
GROUP BY destination_count,destination_depot_id,destination_values
ORDER BY parcels DESC;

