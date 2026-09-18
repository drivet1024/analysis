from pathlib import Path
import sqlite3
s=Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8')
cte=s.split('private const string ConveyorRecirculationCte = """',1)[1].split('"""',1)[0]
tail=s.split('public async Task<ConveyorChute16Response>',1)[1].split('const string sql = ConveyorRecirculationCte + """',1)[1].split('"""',1)[0]
c=sqlite3.connect(':memory:'); c.create_function('CONCAT',-1,lambda *a: ''.join(map(str,a)))
c.executescript('CREATE TABLE parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id,weight,l,h,w); CREATE TABLE parcel(PARCEL_ID,CUSTOMER_ID); CREATE TABLE customer(CUSTOMER_ID,NAME); INSERT INTO parcel VALUES(1,10),(1,10); INSERT INTO customer VALUES(10,"Client A");')
c.executescript("ALTER TABLE parcel ADD COLUMN SHIPMENT_INTERNAL_ID; ALTER TABLE parcel ADD COLUMN SHIPPING_ID; ALTER TABLE parcel ADD COLUMN EXP_DATE; CREATE TABLE shipment(ID,SHIPPING_ID,EXP_DATE,DEST_POSTAL_CODE); CREATE TABLE location(LOC_POSTAL_CODE PRIMARY KEY,ENABLED); INSERT INTO shipment VALUES(100,200,'2026-09-17',' h1s 0a1 '); INSERT INTO location VALUES('H1S0A1',1); UPDATE parcel SET SHIPMENT_INTERNAL_ID=100 WHERE PARCEL_ID=1;")
for pid,line,chute,camera,depot,time in [(1,0,16,'read',1,'16:00'),(1,0,16,'read',1,'16:01'),(0,0,16,'?unread',1,'16:02'),(None,0,16,'?unread',1,'16:03'),(None,0,16,None,1,'16:04'),(0,0,16,'read',1,'16:05'),(2,0,16,'?read',1,'16:06'),(3,2,16,'read',1,'16:07'),(4,0,98,'read',1,'16:08'),(5,0,16,'read',2,'16:09')]:
 c.execute('INSERT INTO parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id) VALUES(?,?,?,?,?,?)',(pid,line,chute,camera,'2026-09-17 '+time,depot))
params=dict(shiftStart='2026-09-17 15:00',shiftEnd='2026-09-18 03:00',depotId=1,hasFloor=1)
rows=c.execute(cte+tail,params).fetchall()
assert len(rows)==5,rows
assert sum(r[0]==1 for r in rows)==2
assert all(r[2]==16 for r in rows)
assert rows[0][5]=='Client A'
assert sum(r[0] is None or r[0]==0 for r in rows)==2
count=c.execute(cte+" SELECT SUM(chute=16 AND NOT ((parcel_id IS NULL OR parcel_id=0) AND COALESCE(camera_data,'') LIKE '?%')) FROM scope",params).fetchone()[0]
assert len(rows)==count
assert not c.execute(cte+tail,dict(params,shiftStart='2026-09-18 15:00',shiftEnd='2026-09-19 03:00')).fetchall()
print('Chute 16 SQL fixture checks passed: exact KPI count, non-read exclusion, unidentified passages, depot and shift. SQLite verification only.')

assert all(r[6:]==('H1S0A1','active') for r in rows if r[0]==1)
assert all(r[7]=='unknown' for r in rows if r[0]!=1)
for pid,postal,enabled,expected in [(10,'H2A0A1',0,'inactive'),(11,'Z9Z9Z9',None,'not_found'),(12,' ',None,'missing'),(13,'H3A0A1',None,'unknown')]:
 c.execute('INSERT INTO parcel VALUES(?,?,?,?,?)',(pid,10,pid,None,None))
 c.execute('INSERT INTO shipment VALUES(?,?,?,?)',(pid,pid,'2026-09-17',postal))
 if expected in ('inactive','unknown'): c.execute('INSERT INTO location VALUES(?,?)',(postal,enabled))
 c.execute("INSERT INTO parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id) VALUES(?,0,16,'read','2026-09-17 17:00',1)",(pid,))
 result=c.execute(cte+tail,params).fetchall()
 assert next(r for r in result if r[0]==pid)[7]==expected
c.execute("INSERT INTO shipment VALUES(100,201,'2026-09-17','H2A0A1')")
assert all(r[7]=='ambiguous' for r in c.execute(cte+tail,params).fetchall() if r[0]==1)
print('Postal classification checks passed: normalized codes, inactive, nonexistent, missing, unknown and conflicting references.')

# Legacy parcels resolve by shipping ID and expedition date; unrelated dates must not match.
for pid,internal in [(20,None),(21,0)]:
 c.execute('INSERT INTO parcel VALUES(?,?,?,?,?)',(pid,10,internal,500,'2026-09-17'))
 c.execute("INSERT INTO parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id) VALUES(?,0,16,'read','2026-09-17 17:10',1)",(pid,))
c.execute("INSERT INTO shipment VALUES(500,500,'2026-09-17','H2A0A1')")
c.execute("INSERT INTO shipment VALUES(501,500,'2026-09-16','H1S0A1')")
legacy=[r for r in c.execute(cte+tail,params).fetchall() if r[0] in (20,21)]
assert len(legacy)==2 and all(r[6:]==('H2A0A1','inactive') for r in legacy)
print('Split indexed shipment lookup verified for internal IDs and legacy shipping/date references.')
