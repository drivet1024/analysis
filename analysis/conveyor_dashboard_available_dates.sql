SELECT
    sort_date AS SORT_DATE,
    COUNT(DISTINCT line_id) AS LINE_COUNT,
    SUM(nb_parcel) AS OFFICIAL_PASSAGES
FROM conveyor_status
WHERE depot_id = 1
  AND line_id IN (0, 1, 3)
  AND sort_date >= '2026-06-01'
  AND sort_date <= '2026-07-15'
GROUP BY sort_date
HAVING COUNT(DISTINCT line_id) = 3
ORDER BY sort_date DESC;
