WITH code25_parcels AS (
    SELECT ph.PARCEL_ID,MAX(ph.SHIPPING_ID) shipping_id,MAX(ph.EXP_DATE) exp_date,
           MIN(ph.DATE_LIV) first_scan,MAX(ph.DATE_LIV) last_scan
    FROM parcel_history PARTITION (p2026) ph
    WHERE ph.DEPOT_ID=2
      AND ph.EXCEPTION=25
      AND COALESCE(ph.VOID,0)=0
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID<>0
      AND ph.DATE_INSERT>=CURDATE()-INTERVAL 1 DAY
      AND ph.DATE_INSERT<CURDATE()+INTERVAL 1 DAY
      AND ph.DATE_LIV>=CURDATE()
      AND ph.DATE_LIV<CURDATE()+INTERVAL 1 DAY
    GROUP BY ph.PARCEL_ID
),
parcel_destination AS (
    SELECT cp.PARCEL_ID,cp.first_scan,cp.last_scan,
           MAX(s.DEST_ROUTE_ID) destination_route_id,
           MAX(COALESCE(NULLIF(r.END_DEPOT_ID,0),si.DEPOTNUMBER)) destination_depot_id,
           COUNT(DISTINCT s.DEST_ROUTE_ID) destination_matches
    FROM code25_parcels cp
    LEFT JOIN shipment s ON s.SHIPPING_ID=cp.shipping_id AND s.EXP_DATE=cp.exp_date
    LEFT JOIN route r ON r.ROUTE_ID=s.DEST_ROUTE_ID
    LEFT JOIN sector_info si ON si.SECTOR_ID=s.DEST_SECTOR_ID
    GROUP BY cp.PARCEL_ID,cp.first_scan,cp.last_scan
)
SELECT pd.destination_depot_id,
       COALESCE(NULLIF(TRIM(d.DEPOTNAME),''),d.DEPOT_SHORT_LABEL,d.DEPOTNAMESHORT,
                CONCAT('Dépôt ',pd.destination_depot_id),'Non déterminé') destination_name,
       COUNT(DISTINCT pd.destination_route_id) route_count,
       COUNT(*) parcels,MIN(pd.first_scan) first_scan,MAX(pd.last_scan) last_scan,
       SUM(pd.destination_matches>1) ambiguous_parcels
FROM parcel_destination pd
LEFT JOIN depot d ON d.DEPOTNUMBER=pd.destination_depot_id
GROUP BY pd.destination_depot_id,d.DEPOT_SHORT_LABEL,d.DEPOTNAMESHORT,d.DEPOTNAME
ORDER BY parcels DESC,pd.destination_depot_id;
