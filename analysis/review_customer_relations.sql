SELECT 'schedule_orphans' AS check_name,
       COUNT(*) AS rows_checked,
       SUM(NOT EXISTS (SELECT 1 FROM customer c WHERE c.CUSTOMER_ID = p.CUSTOMER_ID)) AS orphan_rows,
       COUNT(DISTINCT p.CUSTOMER_ID) AS distinct_customers
FROM customer_schedule_pickup p
UNION ALL
SELECT 'shipment_customer_orphans',
       COUNT(*),
       SUM(NOT EXISTS (SELECT 1 FROM customer c WHERE c.CUSTOMER_ID = s.CUSTOMER_ID)),
       COUNT(DISTINCT s.CUSTOMER_ID)
FROM shipment s
WHERE s.INSERT_DATE >= CURRENT_DATE - INTERVAL 90 DAY
UNION ALL
SELECT 'history_customer_orphans_90d',
       COUNT(*),
       SUM(ph.CUSTOMER_ID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM customer c WHERE c.CUSTOMER_ID = ph.CUSTOMER_ID)),
       COUNT(DISTINCT ph.CUSTOMER_ID)
FROM parcel_history ph
WHERE ph.DATE_INSERT >= CURRENT_DATE - INTERVAL 90 DAY
UNION ALL
SELECT 'history_shipment_key_gaps_90d',
       COUNT(*),
       SUM(ph.SHIPPING_ID IS NOT NULL AND NOT EXISTS (
           SELECT 1 FROM shipment s
           WHERE s.SHIPPING_ID = ph.SHIPPING_ID AND s.EXP_DATE = ph.EXP_DATE
       )),
       COUNT(DISTINCT ph.SHIPPING_ID)
FROM parcel_history ph
WHERE ph.DATE_INSERT >= CURRENT_DATE - INTERVAL 90 DAY
UNION ALL
SELECT 'history_quality_90d',
       COUNT(*),
       SUM(ph.PARCEL_ID IS NULL OR ph.PARCEL_ID = 0),
       SUM(ph.VOID = 1)
FROM parcel_history ph
WHERE ph.DATE_INSERT >= CURRENT_DATE - INTERVAL 90 DAY;
