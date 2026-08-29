SELECT
    r.ROUTE_ID AS route_id,
    r.ROUTE_NAME AS route_name,
    r.SECTOR_ID AS sector_id,
    si.SECTOR_NAME AS sector_name,
    si.SECTOR_TYPE AS sector_type,
    CASE si.SECTOR_TYPE
        WHEN 0 THEN 'Nationex'
        WHEN 1 THEN 'Voiturier'
        WHEN 2 THEN 'Agent'
        ELSE 'Non défini'
    END AS sector_type_name,
    COUNT(*) AS scheduled_rows,
    COUNT(DISTINCT csp.CUSTOMER_ID) AS scheduled_clients
FROM customer_schedule_pickup csp
JOIN route r
  ON r.ROUTE_ID = csp.ROUTE_ID
LEFT JOIN sector_info si
  ON si.SECTOR_ID = r.SECTOR_ID
GROUP BY r.ROUTE_ID, r.ROUTE_NAME, r.SECTOR_ID,
         si.SECTOR_NAME, si.SECTOR_TYPE
ORDER BY si.SECTOR_TYPE, si.SECTOR_NAME, r.ROUTE_ID;
