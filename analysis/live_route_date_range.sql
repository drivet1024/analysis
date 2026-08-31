SELECT
    MAX(ROUTE_DATE) AS max_route_date,
    MIN(ROUTE_DATE) AS min_route_date,
    COUNT(*) AS row_count
FROM live_route;
