SELECT
    p.CUSTOMER_ID AS customer_id,
    COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client,
    c.ACTIVE AS active,
    p.ROUTE_ID AS route_id,
    CASE
        WHEN c.CUSTOMER_ID IS NULL THEN 'client absent de customer'
        WHEN COALESCE(c.ACTIVE, 0) <> 1 THEN 'client inactif'
        WHEN p.ROUTE_ID > 50100 THEN 'route > 50100'
        WHEN LOWER(c.NAME) LIKE '%nationex%' THEN 'nom contient Nationex'
        WHEN NOT (p.SUNDAY = 1 OR p.MONDAY = 1 OR p.TUESDAY = 1 OR p.WEDNESDAY = 1 OR p.THURSDAY = 1 OR p.FRIDAY = 1 OR p.SATURDAY = 1) THEN 'aucun jour actif'
        ELSE 'retenu dans analyse'
    END AS classification
FROM customer_schedule_pickup p
LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE c.CUSTOMER_ID IS NULL
   OR c.ACTIVE <> 1
   OR p.ROUTE_ID > 50100
   OR LOWER(c.NAME) LIKE '%nationex%'
   OR NOT (p.SUNDAY = 1 OR p.MONDAY = 1 OR p.TUESDAY = 1 OR p.WEDNESDAY = 1 OR p.THURSDAY = 1 OR p.FRIDAY = 1 OR p.SATURDAY = 1)
ORDER BY classification, client;
