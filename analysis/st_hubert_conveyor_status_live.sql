SELECT
    NOW() AS database_now,
    depot_id,
    line_id,
    sort_date,
    last_update,
    nb_parcel,
    nb_noread,
    nb_rejected
FROM conveyor_status
WHERE depot_id = 1
ORDER BY last_update DESC
LIMIT 30;
