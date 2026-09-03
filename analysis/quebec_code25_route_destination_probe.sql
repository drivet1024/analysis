WITH code25 AS (
    SELECT ph.PARCEL_ID,ph.SHIPPING_ID,MIN(ph.DATE_LIV) first_code25
    FROM parcel_history PARTITION (p2026) ph
    WHERE ph.DEPOT_ID=2 AND ph.EXCEPTION=25 AND ph.SOURCE_TYPE=900
      AND COALESCE(ph.VOID,0)=0 AND ph.PARCEL_ID IS NOT NULL AND ph.PARCEL_ID<>0
      AND ph.DATE_INSERT>=CURDATE()-INTERVAL 1 DAY
      AND ph.DATE_INSERT<CURDATE()+INTERVAL 1 DAY
      AND ph.DATE_LIV>=CURDATE() AND ph.DATE_LIV<CURDATE()+INTERVAL 1 DAY
    GROUP BY ph.PARCEL_ID,ph.SHIPPING_ID
),
parcel_link AS (
    SELECT c.PARCEL_ID,c.first_code25,
           MAX(p.SHIPMENT_INTERNAL_ID) shipment_internal_id,
           COUNT(DISTINCT p.SHIPMENT_INTERNAL_ID) shipment_matches
    FROM code25 c
    LEFT JOIN parcel p ON p.PARCEL_ID=c.PARCEL_ID
    GROUP BY c.PARCEL_ID,c.first_code25
)
SELECT pl.PARCEL_ID,pl.first_code25,pl.shipment_matches,
       s.DEST_SECTOR_ID,si.SECTOR_NAME,si.DEPOTNUMBER sector_depot_id,
       s.DEST_ROUTE_ID,r.START_DEPOT_ID,r.END_DEPOT_ID,r.ROUTE_NAME,
       nr.DEPOT_ID nat_route_depot
FROM parcel_link pl
LEFT JOIN shipment s ON s.ID=pl.shipment_internal_id
LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
LEFT JOIN nat_route nr ON nr.ROUTE_ID=s.DEST_ROUTE_ID
ORDER BY pl.PARCEL_ID
LIMIT 100;
