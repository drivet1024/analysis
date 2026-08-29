SELECT
    c.CUSTOMER_ID AS customer_id,
    COALESCE(c.NAME, CONCAT('Client ', c.CUSTOMER_ID)) AS client,
    COALESCE(c.ACTIVE, 0) AS customer_active,
    r.ROUTE_ID AS route_id,
    r.ROUTE_NAME AS route_name,
    si.SECTOR_ID AS sector_id,
    si.SECTOR_NAME AS sector_name,
    si.SECTOR_TYPE AS sector_type,
    CASE si.SECTOR_TYPE
        WHEN 1 THEN 'Voiturier'
        WHEN 2 THEN 'Agent'
    END AS broker_type,
    si.ACTIVE AS sector_active,
    CONCAT_WS(', ',
        IF(c.PUSUNDAY = 1, 'Dimanche', NULL),
        IF(c.PUMONDAY = 1, 'Lundi', NULL),
        IF(c.PUTUESDAY = 1, 'Mardi', NULL),
        IF(c.PUWEDNESDAY = 1, 'Mercredi', NULL),
        IF(c.PUTHURSDAY = 1, 'Jeudi', NULL),
        IF(c.PUFRIDAY = 1, 'Vendredi', NULL),
        IF(c.PUSATURDAY = 1, 'Samedi', NULL)
    ) AS pickup_days,
    CONCAT_WS(' · ',
        IF(c.PUSUNDAY = 1, CONCAT('Dim ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMESUNDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUMONDAY = 1, CONCAT('Lun ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMEMONDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUTUESDAY = 1, CONCAT('Mar ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMETUESDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUWEDNESDAY = 1, CONCAT('Mer ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMEWEDNESDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUTHURSDAY = 1, CONCAT('Jeu ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMETHURSDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUFRIDAY = 1, CONCAT('Ven ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMEFRIDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUSATURDAY = 1, CONCAT('Sam ', COALESCE(NULLIF(NULLIF(TRIM(c.PUTIMESATURDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.PUTIME), ''), ':'), 'N/D')), NULL)
    ) AS pickup_schedule,
    CONCAT_WS(' · ',
        IF(c.PUSUNDAY = 1, CONCAT('Dim ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMESUNDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUMONDAY = 1, CONCAT('Lun ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMEMONDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUTUESDAY = 1, CONCAT('Mar ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMETUESDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUWEDNESDAY = 1, CONCAT('Mer ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMEWEDNESDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUTHURSDAY = 1, CONCAT('Jeu ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMETHURSDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUFRIDAY = 1, CONCAT('Ven ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMEFRIDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL),
        IF(c.PUSATURDAY = 1, CONCAT('Sam ', COALESCE(NULLIF(NULLIF(TRIM(c.CLOSINGTIMESATURDAY), ''), ':'), NULLIF(NULLIF(TRIM(c.CLOSINGTIME), ''), ':'), 'N/D')), NULL)
    ) AS closing_schedule,
    c.NOTE_PU_FR AS note
FROM customer c
JOIN route r
  ON r.ROUTE_ID = c.PU_ROUTE_ID
JOIN sector_info si
  ON si.SECTOR_ID = r.SECTOR_ID
WHERE si.SECTOR_TYPE IN (1, 2)
  AND (c.PUSUNDAY = 1 OR c.PUMONDAY = 1 OR c.PUTUESDAY = 1
       OR c.PUWEDNESDAY = 1 OR c.PUTHURSDAY = 1 OR c.PUFRIDAY = 1
       OR c.PUSATURDAY = 1)
ORDER BY COALESCE(c.ACTIVE, 0) DESC, si.SECTOR_NAME, r.ROUTE_ID, client;
