SELECT
    COALESCE(si.SECTOR_TYPE, -1) AS sector_type,
    CASE si.SECTOR_TYPE
        WHEN 0 THEN 'Nationex'
        WHEN 1 THEN 'Voiturier'
        WHEN 2 THEN 'Agent'
        ELSE 'Sans secteur'
    END AS sector_type_name,
    COUNT(*) AS schedule_rows,
    COUNT(DISTINCT p.CUSTOMER_ID) AS schedule_customers,
    COUNT(DISTINCT c.PU_ROUTE_ID) AS customer_pickup_routes,
    SUM(p.SUNDAY + p.MONDAY + p.TUESDAY + p.WEDNESDAY
      + p.THURSDAY + p.FRIDAY + p.SATURDAY) AS scheduled_weekdays
FROM customer_schedule_pickup p
LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
LEFT JOIN route r ON r.ROUTE_ID = c.PU_ROUTE_ID
LEFT JOIN sector_info si ON si.SECTOR_ID = r.SECTOR_ID
GROUP BY COALESCE(si.SECTOR_TYPE, -1)
ORDER BY sector_type;
