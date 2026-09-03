WITH
/*
  Maximum observé utilisé par le tableau de bord — convoyeur du haut, Saint-Hubert.
  IMPORTANT : equivalent_colis_heure annualise la meilleure minute.
  Exemple : 71 colis dans une minute = 71 * 60 = 4 260 colis/heure.
  Ce résultat ne représente pas une heure complète à ce débit.
*/
shift_anchor AS (
    SELECT CASE
        WHEN CURTIME() < '04:00:00'
            THEN CURDATE() - INTERVAL 1 DAY + INTERVAL 16 HOUR
        ELSE CURDATE() + INTERVAL 16 HOUR
    END AS current_shift_start
),
first_scan_by_parcel AS (
    SELECT
        DATE(ph.DATE_LIV - INTERVAL 4 HOUR) AS shift_date,
        ph.PARCEL_ID,
        MIN(ph.DATE_LIV) AS first_scan
    FROM parcel_history PARTITION (p2026) ph
    CROSS JOIN shift_anchor sa
    WHERE ph.EXCEPTION = 903
      AND ph.DEPOT_ID = 1
      AND ph.SOURCE_TYPE = 200
      AND (ph.SOURCE_ID IS NULL OR ph.SOURCE_ID = 1)
      AND COALESCE(ph.VOID, 0) = 0
      AND ph.PARCEL_ID IS NOT NULL
      AND ph.PARCEL_ID <> 0
      -- Les 14 derniers jours, en excluant le quart courant non terminé.
      AND ph.DATE_INSERT >= sa.current_shift_start - INTERVAL 14 DAY - INTERVAL 1 HOUR
      AND ph.DATE_INSERT <  sa.current_shift_start
      AND ph.DATE_LIV >= sa.current_shift_start - INTERVAL 14 DAY
      AND ph.DATE_LIV <  sa.current_shift_start
      AND (HOUR(ph.DATE_LIV) >= 16 OR HOUR(ph.DATE_LIV) < 4)
    GROUP BY
        DATE(ph.DATE_LIV - INTERVAL 4 HOUR),
        ph.PARCEL_ID
),
minute_counts AS (
    SELECT
        shift_date,
        CAST(DATE_FORMAT(first_scan, '%Y-%m-%d %H:%i:00') AS DATETIME) AS minute_start,
        COUNT(*) AS colis_dans_la_minute
    FROM first_scan_by_parcel
    GROUP BY
        shift_date,
        CAST(DATE_FORMAT(first_scan, '%Y-%m-%d %H:%i:00') AS DATETIME)
)
SELECT
    shift_date,
    minute_start,
    colis_dans_la_minute,
    colis_dans_la_minute * 60 AS equivalent_colis_heure
FROM minute_counts
ORDER BY colis_dans_la_minute DESC, minute_start DESC
LIMIT 1;
