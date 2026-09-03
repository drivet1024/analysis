WITH scheduled_today AS (
    SELECT p.CUSTOMER_ID customer_id,
           GROUP_CONCAT(DISTINCT p.ROUTE_ID ORDER BY p.ROUTE_ID SEPARATOR ' / ') routes,
           COALESCE(GROUP_CONCAT(DISTINCT TIME_FORMAT(p.END_TIME,'%H:%i') ORDER BY p.END_TIME SEPARATOR ' / '),'N/D') pickup_time
    FROM customer_schedule_pickup p
    WHERE p.ROUTE_ID BETWEEN 50000 AND 50099
      AND CASE DAYOFWEEK(CURDATE())
            WHEN 1 THEN p.SUNDAY WHEN 2 THEN p.MONDAY WHEN 3 THEN p.TUESDAY
            WHEN 4 THEN p.WEDNESDAY WHEN 5 THEN p.THURSDAY WHEN 6 THEN p.FRIDAY
            WHEN 7 THEN p.SATURDAY END = 1
    GROUP BY p.CUSTOMER_ID
),
created_parcels AS (
    SELECT p.PARCEL_ID parcel_id,
           MAX(p.CUSTOMER_ID) customer_id,
           MIN(p.INSERT_DATE) created_at
    FROM parcel p
    JOIN scheduled_today st ON st.customer_id=p.CUSTOMER_ID
    WHERE p.INSERT_DATE >= CURDATE() - INTERVAL 2 DAY
      AND p.INSERT_DATE < CURDATE() + INTERVAL 1 DAY
      AND p.PARCEL_ID IS NOT NULL
      AND p.PARCEL_ID <> 0
    GROUP BY p.PARCEL_ID
),
passed_parcels AS (
    SELECT DISTINCT ph.PARCEL_ID parcel_id
    FROM parcel_history ph
    JOIN created_parcels cp ON cp.parcel_id=ph.PARCEL_ID
    WHERE ph.EXCEPTION=903
      AND ph.DEPOT_ID=1
      AND COALESCE(ph.VOID,0)=0
      AND ((ph.SOURCE_TYPE=200 AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID IN (1,3))) OR ph.SOURCE_TYPE=201)
)
SELECT cp.customer_id,
       COALESCE(c.NAME,CONCAT('Client ',cp.customer_id)) customer_name,
       st.routes,
       st.pickup_time,
       COUNT(*) unprocessed_parcels,
       SUM(DATE(cp.created_at)=CURDATE()) created_today,
       SUM(DATE(cp.created_at)=CURDATE()-INTERVAL 1 DAY) created_yesterday,
       SUM(DATE(cp.created_at)=CURDATE()-INTERVAL 2 DAY) created_two_days_ago,
       MIN(cp.created_at) oldest_created,
       MAX(cp.created_at) newest_created
FROM created_parcels cp
JOIN scheduled_today st ON st.customer_id=cp.customer_id
LEFT JOIN passed_parcels pp ON pp.parcel_id=cp.parcel_id
LEFT JOIN customer c ON c.CUSTOMER_ID=cp.customer_id
WHERE pp.parcel_id IS NULL
GROUP BY cp.customer_id,c.NAME,st.routes,st.pickup_time
ORDER BY unprocessed_parcels DESC,st.pickup_time,customer_name
LIMIT 30;
