"""Run the production region aggregates against boundary/profile fixtures in SQLite."""
from pathlib import Path
import math
import re
import sqlite3

source = Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8-sig')
query = source.split('const string regionsSql = """', 1)[1].split('""";', 1)[0]
# JSON_TABLE is MySQL-specific; supply its materialized output as a fixture table.
query = 'WITH dimension_global AS (' + query.split('), dimension_global AS (', 1)[1]
query = query.replace("GROUP_CONCAT(DISTINCT DEPOTNAME ORDER BY DEPOTNAME SEPARATOR ', ')", 'GROUP_CONCAT(DISTINCT DEPOTNAME)')
query = re.sub(r'DATE_SUB\((@\w+), INTERVAL (\d+) DAY\)', r"datetime(\1, '-\2 day')", query)
c = sqlite3.connect(':memory:')
c.row_factory = sqlite3.Row
c.create_function('CEILING', 1, math.ceil)
c.executescript('''
CREATE TABLE dimension_profiles(day_offset, CUSTOMER_ID, history_count, valid_count, volume_sum);
CREATE TABLE depot(DEPOTNUMBER, DEPOTNAME);
CREATE TABLE location(LOC_POSTAL_CODE, DEPOTNUMBER);
CREATE TABLE shipment(INSERT_DATE, CUSTOMER_ID, PARCEL_NB, DEST_POSTAL_CODE, SHIPMENT_STATUS);
INSERT INTO depot VALUES(2,'Québec');
INSERT INTO location VALUES('TEST',2);
INSERT INTO dimension_profiles VALUES(0,1,20,20,2000),(1,1,20,20,4000),(7,1,20,20,6000);
''')
def shipment(date, count, customer=1, status=0):
    c.execute('INSERT INTO shipment VALUES(?,?,?,?,?)', (date,customer,count,'TEST',status))

shipment('2026-09-22 04:00:00', 10)
shipment('2026-09-22 12:00:00', 100) # current cutoff excluded
shipment('2026-09-21 04:00:00', 20)
shipment('2026-09-21 12:00:00', 30) # same-time cutoff, included only in final
shipment('2026-09-22 03:59:59', 5)  # operational yesterday
shipment('2026-09-15 04:00:00', 40)
shipment('2026-09-15 12:00:00', 50)
shipment('2026-09-16 03:59:59', 7)
shipment('2026-09-16 04:00:00', 1000) # final cutoff excluded
shipment('2026-09-15 03:59:59', 1000) # start excluded
shipment('2026-09-21 09:00:00', 1000, status=500)
shipment('2026-09-15 09:00:00', 1000, status=501)
params=dict(analysisDate='2026-09-22 04:00:00', analysisEnd='2026-09-22 12:00:00', regionsStart='2026-09-15 04:00:00')
def result():
    return c.execute(query,params).fetchone()
r=result()
assert (r['parcels_today'],r['estimated_parcel_volume']) == (10,1000)
for prefix, parcels, mean in [('yesterday_same_time',20,200),('yesterday_final',55,200),('last_week_same_time',40,300),('last_week_final',97,300)]:
    assert (r[prefix+'_parcels'],r[prefix+'_volume'],r[prefix+'_client']) == (parcels,parcels*mean,parcels)
shipment('2026-09-21 09:00:00', 3, customer=2)
r=result()
assert r['yesterday_same_time_fallback']==3 and r['yesterday_same_time_volume']==4600
# Fewer than 20 valid measurements falls back to the weighted global profile.
c.execute('INSERT INTO dimension_profiles VALUES(1,2,19,19,7600)')
r=result()
assert r['yesterday_same_time_fallback']==3 and r['yesterday_same_time_volume'] > 4600
c.execute('DELETE FROM dimension_profiles WHERE day_offset=7')
r=result()
assert r['last_week_same_time_missing']==40 and r['last_week_final_missing']==97
assert r['yesterday_final_missing']==0
# A past selected day uses a full-day cutoff for both historical comparisons.
params['analysisEnd']='2026-09-23 04:00:00'
r=result()
assert r['yesterday_same_time_parcels']==r['yesterday_final_parcels']
assert r['last_week_same_time_parcels']==r['last_week_final_parcels']
print('Transport SQL: same-time/final boundaries, 4am day, exclusions, date-specific profiles, fallback and missing coverage passed (SQLite fixtures).')
