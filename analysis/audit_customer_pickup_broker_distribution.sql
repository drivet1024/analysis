SELECT
    COALESCE(si.SECTOR_TYPE, -1) AS sector_type,
    CASE si.SECTOR_TYPE
        WHEN 0 THEN 'Nationex'
        WHEN 1 THEN 'Voiturier'
        WHEN 2 THEN 'Agent'
        ELSE 'Sans secteur'
    END AS sector_type_name,
    si.ADD_PICKUP_TPSLROUTE AS add_pickup_tpslroute,
    COUNT(*) AS scheduled_customers,
    COUNT(DISTINCT c.PU_ROUTE_ID) AS pickup_routes,
    SUM(COALESCE(c.PUMONDAY, 0) + COALESCE(c.PUTUESDAY, 0)
      + COALESCE(c.PUWEDNESDAY, 0) + COALESCE(c.PUTHURSDAY, 0)
      + COALESCE(c.PUFRIDAY, 0) + COALESCE(c.PUSATURDAY, 0)
      + COALESCE(c.PUSUNDAY, 0)) AS scheduled_weekdays
FROM customer c
LEFT JOIN route r ON r.ROUTE_ID = c.PU_ROUTE_ID
LEFT JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
WHERE c.ACTIVE = 1
  AND (c.PUMONDAY = 1 OR c.PUTUESDAY = 1 OR c.PUWEDNESDAY = 1
       OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1 OR c.PUSATURDAY = 1 OR c.PUSUNDAY = 1)
GROUP BY COALESCE(si.SECTOR_TYPE, -1), si.ADD_PICKUP_TPSLROUTE
ORDER BY sector_type, add_pickup_tpslroute;
