from pathlib import Path
import sqlite3
s=Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8')
cte=s.split('private const string ConveyorRecirculationCte = """',1)[1].split('"""',1)[0]
tail=s.split('public async Task<ConveyorChute16Response>',1)[1].split('const string sql = ConveyorRecirculationCte + """',1)[1].split('"""',1)[0]
c=sqlite3.connect(':memory:'); c.create_function('CONCAT',-1,lambda *a: ''.join(map(str,a)))
c.executescript('CREATE TABLE parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id); CREATE TABLE parcel(PARCEL_ID,CUSTOMER_ID); CREATE TABLE customer(CUSTOMER_ID,NAME); INSERT INTO parcel VALUES(1,10),(1,10); INSERT INTO customer VALUES(10,"Client A");')
for pid,line,chute,camera,depot,time in [(1,0,16,'read',1,'16:00'),(1,0,16,'read',1,'16:01'),(0,0,16,'?unread',1,'16:02'),(None,0,16,'?unread',1,'16:03'),(None,0,16,None,1,'16:04'),(0,0,16,'read',1,'16:05'),(2,0,16,'?read',1,'16:06'),(3,2,16,'read',1,'16:07'),(4,0,98,'read',1,'16:08'),(5,0,16,'read',2,'16:09')]:
 c.execute('INSERT INTO parcel_scan_history VALUES(?,?,?,?,?,?)',(pid,line,chute,camera,'2026-09-17 '+time,depot))
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
