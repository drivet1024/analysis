SELECT
    DATE(cs.sort_date) AS SORT_DATE,
    cs.line_id,
    cs.last_update,
    cs.nb_parcel,
    cs.nb_noread,
    cs.nb_rejected,
    cs.nb_sort_by_wb,
    cs.nb_sort_by_postal_code,
    cs.nb_dimensioner_error,
    cs.nb_scale_error
FROM conveyor_status AS cs
WHERE cs.depot_id = 1
  AND cs.sort_date >= '2026-06-14'
  AND cs.sort_date <= '2026-07-12'
  AND DAYOFWEEK(cs.sort_date) = 1
ORDER BY cs.sort_date, cs.line_id;
