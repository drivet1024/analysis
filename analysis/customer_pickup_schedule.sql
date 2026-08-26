SELECT
    'Dimanche' AS jour,
    0 AS jour_numero,
    p.ROUTE_ID AS route_id,
    p.CUSTOMER_ID AS customer_id,
    COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)) AS client,
    p.START_TIME AS heure_debut,
    p.END_TIME AS heure_fin,
    p.NOTE_FR AS note
FROM customer_schedule_pickup p
LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.SUNDAY = 1
UNION ALL
SELECT 'Lundi', 1, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.MONDAY = 1
UNION ALL
SELECT 'Mardi', 2, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.TUESDAY = 1
UNION ALL
SELECT 'Mercredi', 3, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.WEDNESDAY = 1
UNION ALL
SELECT 'Jeudi', 4, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.THURSDAY = 1
UNION ALL
SELECT 'Vendredi', 5, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.FRIDAY = 1
UNION ALL
SELECT 'Samedi', 6, p.ROUTE_ID, p.CUSTOMER_ID, COALESCE(c.NAME, CONCAT('Client ', p.CUSTOMER_ID)), p.START_TIME, p.END_TIME, p.NOTE_FR
FROM customer_schedule_pickup p LEFT JOIN customer c ON c.CUSTOMER_ID = p.CUSTOMER_ID
WHERE p.SATURDAY = 1
ORDER BY jour_numero, heure_debut, client;
