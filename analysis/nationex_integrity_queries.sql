-- Nationex integrity checks
-- Read-only, bounded to 2026-07-09. Run EXPLAIN before execution.

-- 1. Current parcel parent coverage. The valid shipment business key is
--    (SHIPPING_ID, EXP_DATE); SHIPMENT_INTERNAL_ID is zero in this window.
SELECT COUNT(*) AS total_colis,
       SUM(NOT EXISTS (
         SELECT 1
         FROM shipment AS s
         WHERE s.SHIPPING_ID = p.SHIPPING_ID
           AND s.EXP_DATE = p.EXP_DATE
       )) AS sans_shipment_metier,
       SUM(NOT EXISTS (
         SELECT 1
         FROM customer AS c
         WHERE c.CUSTOMER_ID = p.CUSTOMER_ID
       )) AS sans_customer,
       SUM(NOT EXISTS (
         SELECT 1
         FROM shipping AS sh
         WHERE sh.shipping_id = p.SHIPPING_ID
       )) AS sans_shipping_legacy
FROM parcel AS p
WHERE p.INSERT_DATE >= '2026-07-09'
  AND p.INSERT_DATE < '2026-07-10';

-- 2. Source-type reference coverage.
SELECT h.SOURCE_TYPE,
       r.SOURCE_TYPE_FR,
       COUNT(*) AS evenements
FROM parcel_history AS h
LEFT JOIN parcel_history_source_type AS r
  ON r.SOURCE_TYPE = h.SOURCE_TYPE
WHERE h.DATE_INSERT >= '2026-07-09'
  AND h.DATE_INSERT < '2026-07-10'
GROUP BY h.SOURCE_TYPE, r.SOURCE_TYPE_FR
ORDER BY evenements DESC, h.SOURCE_TYPE;

-- 3. NAT_CLIK API dual-model coverage.
SELECT COUNT(*) AS evenements_natclik,
       SUM(EXISTS (
         SELECT 1
         FROM parcel AS p
         WHERE p.PARCEL_ID = h.PARCEL_ID
           AND p.SHIPPING_ID = h.SHIPPING_ID
       )) AS dans_parcel_courant,
       SUM(EXISTS (
         SELECT 1
         FROM shipment AS s
         WHERE s.SHIPPING_ID = h.SHIPPING_ID
       )) AS dans_shipment_courant,
       SUM(EXISTS (
         SELECT 1
         FROM livraison AS l
         WHERE l.BILLNUMEXP = h.SHIPPING_ID
       )) AS dans_livraison_legacy
FROM parcel_history AS h
WHERE h.DATE_INSERT >= '2026-07-09'
  AND h.DATE_INSERT < '2026-07-10'
  AND h.SOURCE_TYPE = 600;
