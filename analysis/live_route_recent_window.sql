SELECT
    CURRENT_DATE AS database_today,
    MAX(ROUTE_DATE) AS latest_route_date_not_future,
    COUNT(*) AS rows_last_30_days,
    COUNT(DISTINCT CUSTOMER_ID) AS customers_last_30_days,
    COUNT(DISTINCT PICKUP_ID) AS pickup_ids_last_30_days,
    SUM(PICKUP_ID IS NOT NULL AND CUSTOMER_ID IS NOT NULL) AS rows_with_customer_pickup,
    SUM(FORECASTED_ARRIVAL_TIME IS NOT NULL) AS rows_with_forecast_time
FROM live_route
WHERE ROUTE_DATE >= CURRENT_DATE - INTERVAL 30 DAY
  AND ROUTE_DATE < CURRENT_DATE + INTERVAL 1 DAY;
