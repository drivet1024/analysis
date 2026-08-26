SELECT
    depot_id,
    line_id,
    sort_date,
    last_update,
    nb_parcel,
    nb_noread,
    nb_rejected,
    nb_sort_by_wb,
    nb_sort_by_postal_code,
    nb_dimensioner_error,
    nb_scale_error,
    nb_code_42,
    nb_code_68,
    nb_code_98,
    percentage_code_42,
    percentage_code_68,
    percentage_code_98
FROM conveyor_status
WHERE depot_id = 1
  AND sort_date = '2026-07-12'
ORDER BY line_id, last_update;
