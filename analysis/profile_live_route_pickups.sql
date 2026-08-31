WITH bounds AS (
    SELECT MAX(ROUTE_DATE) AS max_route_date
    FROM live_route
), recent AS (
    SELECT lr.*
    FROM live_route AS lr
    CROSS JOIN bounds AS b
    WHERE lr.ROUTE_DATE >= b.max_route_date - INTERVAL 29 DAY
      AND lr.ROUTE_DATE <= b.max_route_date
)
SELECT
    MIN(ROUTE_DATE) AS first_route_date,
    MAX(ROUTE_DATE) AS last_route_date,
    COUNT(*) AS rows_in_window,
    COUNT(DISTINCT CUSTOMER_ID) AS customers,
    COUNT(DISTINCT PICKUP_ID) AS pickup_ids,
    SUM(PICKUP_ID IS NOT NULL AND CUSTOMER_ID IS NOT NULL) AS rows_with_customer_pickup,
    SUM(FORECASTED_ARRIVAL_TIME IS NOT NULL) AS rows_with_forecast_time
FROM recent;

WITH bounds AS (
    SELECT MAX(ROUTE_DATE) AS max_route_date
    FROM live_route
), recent AS (
    SELECT lr.*
    FROM live_route AS lr
    CROSS JOIN bounds AS b
    WHERE lr.ROUTE_DATE >= b.max_route_date - INTERVAL 29 DAY
      AND lr.ROUTE_DATE <= b.max_route_date
)
SELECT
    LIVE_ROUTE_TYPE,
    LIVE_ROUTE_STATUS,
    COUNT(*) AS rows,
    COUNT(DISTINCT CUSTOMER_ID) AS customers,
    COUNT(DISTINCT PICKUP_ID) AS pickup_ids,
    SUM(PICKUP_ID IS NOT NULL AND CUSTOMER_ID IS NOT NULL) AS customer_pickup_rows,
    SUM(FORECASTED_ARRIVAL_TIME IS NOT NULL) AS rows_with_forecast_time
FROM recent
GROUP BY LIVE_ROUTE_TYPE, LIVE_ROUTE_STATUS
ORDER BY customer_pickup_rows DESC, rows DESC;

WITH bounds AS (
    SELECT MAX(ROUTE_DATE) AS max_route_date
    FROM live_route
), recent AS (
    SELECT lr.*
    FROM live_route AS lr
    CROSS JOIN bounds AS b
    WHERE lr.ROUTE_DATE >= b.max_route_date - INTERVAL 29 DAY
      AND lr.ROUTE_DATE <= b.max_route_date
)
SELECT
    ROUTE_DATE,
    ROUTE_ID,
    CUSTOMER_ID,
    PICKUP_ID,
    COUNT(*) AS rows_per_pickup,
    COUNT(DISTINCT FORECASTED_ARRIVAL_TIME) AS distinct_forecast_times,
    MIN(FORECASTED_ARRIVAL_TIME) AS first_forecast_time,
    MAX(FORECASTED_ARRIVAL_TIME) AS last_forecast_time,
    MAX(UPDATE_DATE) AS latest_update
FROM recent
WHERE CUSTOMER_ID = 158563
  AND PICKUP_ID IS NOT NULL
GROUP BY ROUTE_DATE, ROUTE_ID, CUSTOMER_ID, PICKUP_ID
ORDER BY ROUTE_DATE DESC, PICKUP_ID
LIMIT 100;
