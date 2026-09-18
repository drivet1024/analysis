from pathlib import Path
import re,sqlite3
source=Path('ConveyorDashboard/EdiDepotChuteService.cs').read_text(encoding='utf-8-sig')
query=re.search(r'new MySqlCommand\("""(.*?)"""',source,re.S)[1]
query=query.replace("LEFT(REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ',''),3)","substr(REPLACE(UPPER(TRIM(s.DEST_POSTAL_CODE)),' ',''),1,3)")
c=sqlite3.connect(':memory:')
c.executescript('''
CREATE TABLE parcel_scan_history(chute,parcel_id,depot_id,line_id,date_insert);
CREATE TABLE parcel(PARCEL_ID,SHIPMENT_INTERNAL_ID,SHIPPING_ID,EXP_DATE);
CREATE TABLE shipment(ID,SHIPPING_ID,EXP_DATE,DEST_POSTAL_CODE,DEST_ROUTE_ID,DEST_SECTOR_ID);
CREATE TABLE location(LOC_POSTAL_CODE,DEPOTNUMBER);
CREATE TABLE route(ROUTE_ID,END_DEPOT_ID);
CREATE TABLE sector_info(SECTOR_ID,DEPOTNUMBER);
INSERT INTO parcel VALUES (10,100,'a','2026-09-17'),(10,100,'a','2026-09-17'),(11,101,'b','2026-09-17'),(12,102,'c','2026-09-17'),(13,NULL,'legacy','2026-09-17'),(14,104,'d','2026-09-17'),(15,105,'e','2026-09-17'),(15,106,'f','2026-09-17');
INSERT INTO shipment VALUES (100,'a','2026-09-17','J3V 1A1',1,123),(101,'b','2026-09-17','G1A1A1',2,234),(102,'c','2026-09-17',NULL,NULL,NULL),(103,'legacy','2026-09-17',NULL,1,345),(104,'d','2026-09-17',NULL,NULL,123),(105,'e','2026-09-17',NULL,1,123),(106,'f','2026-09-17',NULL,2,234);
INSERT INTO location VALUES ('J3V1A1',1),('G1A1A1',2);
INSERT INTO route VALUES (1,1),(2,2);
INSERT INTO sector_info VALUES (123,1),(234,2),(345,1);
''')
for chute,pid,depot,line in [(1,10,1,0),(1,10,1,0),(2,10,1,1),(1,11,1,1),(1,12,1,0),(1,13,1,1),(1,14,1,0),(1,15,1,0),(1,999,1,0),(1,0,1,0),(1,None,1,0),(1,10,1,3),(1,10,2,0)]:
 c.execute('INSERT INTO parcel_scan_history VALUES(?,?,?,?,?)',(chute,pid,depot,line,'2026-09-17 18:00:00'))
params=dict(depot=1,start='2026-09-17 04:00:00',end='2026-09-18 04:00:00')
rows=c.execute(query,params).fetchall()
assert sum(r[3] for r in rows)==5,rows
assert all(r[5:]==(3,5) for r in rows),rows
assert {r[1] for r in rows}=={123,345},rows
assert sum(r[2] for r in rows)==4 # unique total is deduplicated across chutes
assert 'J3V' in {r[4] for r in rows}
qc=c.execute(query,dict(params,depot=2)).fetchall()
assert len(qc)==1 and qc[0][1:4]==(234,1,1),qc
assert not c.execute(query,dict(params,start='2026-09-18 04:00:00',end='2026-09-19 04:00:00')).fetchall()
print('High conveyor destination filter, actual sectors/FSA, legacy shipments, real parcels, repeated scans, ambiguous destinations and totals passed.')
