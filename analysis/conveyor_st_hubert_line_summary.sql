SELECT
    cs.line_id,
    cs.nb_parcel,
    cs.nb_noread,
    ROUND(100.0 * cs.nb_noread / NULLIF(cs.nb_parcel, 0), 2) AS NOREAD_RATE_PCT,
    cs.nb_rejected,
    ROUND(100.0 * cs.nb_rejected / NULLIF(cs.nb_parcel, 0), 2) AS REJECT_RATE_PCT,
    cs.nb_dimensioner_error,
    ROUND(100.0 * cs.nb_dimensioner_error / NULLIF(cs.nb_parcel, 0), 2) AS DIMENSION_ERROR_RATE_PCT,
    cs.nb_scale_error,
    ROUND(100.0 * cs.nb_scale_error / NULLIF(cs.nb_parcel, 0), 2) AS SCALE_ERROR_RATE_PCT,
    cs.last_update
FROM conveyor_status AS cs
WHERE cs.depot_id = 1
  AND cs.sort_date = '2026-07-12'
ORDER BY cs.line_id;
