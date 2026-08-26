SELECT
    DATE(cs.sort_date) AS SORT_DATE,
    SUM(cs.nb_parcel) AS PARCELS,
    SUM(cs.nb_noread) AS NOREAD,
    ROUND(100.0 * SUM(cs.nb_noread) / NULLIF(SUM(cs.nb_parcel), 0), 2) AS NOREAD_RATE_PCT,
    SUM(cs.nb_rejected) AS REJECTED,
    ROUND(100.0 * SUM(cs.nb_rejected) / NULLIF(SUM(cs.nb_parcel), 0), 2) AS REJECT_RATE_PCT,
    SUM(cs.nb_dimensioner_error) AS DIMENSION_ERRORS,
    ROUND(100.0 * SUM(cs.nb_dimensioner_error) / NULLIF(SUM(cs.nb_parcel), 0), 2) AS DIMENSION_ERROR_RATE_PCT,
    SUM(cs.nb_scale_error) AS SCALE_ERRORS,
    ROUND(100.0 * SUM(cs.nb_scale_error) / NULLIF(SUM(cs.nb_parcel), 0), 2) AS SCALE_ERROR_RATE_PCT
FROM conveyor_status AS cs
WHERE cs.depot_id = 1
  AND cs.sort_date >= '2026-06-14'
  AND cs.sort_date <= '2026-07-12'
  AND DAYOFWEEK(cs.sort_date) = 1
GROUP BY DATE(cs.sort_date)
ORDER BY SORT_DATE;
