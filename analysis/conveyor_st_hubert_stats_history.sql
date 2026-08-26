SELECT
    depot_id,
    line_id,
    last_update,
    nb_parcel,
    nb_noread,
    nb_rejected,
    nb_sort_by_wb,
    nb_sort_by_postal_code,
    nb_dimensioner_error,
    nb_scale_error,
    nb_code_42_cnv,
    nb_code_98_cnv,
    nb_code_98_software,
    nb_total_parcel_cnv,
    nb_total_recirculated_cnv,
    nb_sort_by_postal_code_without_wb
FROM conveyor_stats_history
WHERE depot_id = 1
  AND last_update >= '2026-07-12 12:00:00'
  AND last_update <  '2026-07-13 12:00:00'
ORDER BY line_id, last_update;
