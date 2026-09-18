from pathlib import Path
import sqlite3
s=Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8')
cte=s.split('private const string ConveyorRecirculationCte = """',1)[1].split('"""',1)[0]
tail=s.split('public async Task<ConveyorChute98Response>',1)[1].split('const string sql = ConveyorRecirculationCte + """',1)[1].split('"""',1)[0]
c=sqlite3.connect(':memory:'); c.create_function('CONCAT',-1,lambda *a: ''.join(map(str,a)))
c.executescript('CREATE TABLE parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id,weight,l,h,w); CREATE TABLE parcel(PARCEL_ID,CUSTOMER_ID); CREATE TABLE customer(CUSTOMER_ID,NAME); INSERT INTO parcel VALUES(1,10),(1,10); INSERT INTO customer VALUES(10,"Client A");')
for pid,line,chute,times,depot in [(1,0,98,3,1),(1,1,98,2,1),(2,0,16,1,1),(0,0,98,2,1),(None,0,98,1,1),(4,2,98,2,1),(6,0,98,2,2)]:
 for i in range(times): c.execute('INSERT INTO parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id) VALUES(?,?,?,?,?,?)',(pid,line,chute,'?unread','2026-09-17 16:0'+str(i),depot))
params=dict(shiftStart='2026-09-17 15:00',shiftEnd='2026-09-18 03:00',depotId=1,hasFloor=1)
rows=c.execute(cte+tail,params).fetchall()
assert len(rows)==8,rows
assert all(r[2]==98 for r in rows)
assert sum(r[0]==1 for r in rows)==5
assert sum(r[0] is None or r[0]==0 for r in rows)==3
assert rows[0][5] in ('Client A','Client non identifié')
count=c.execute(cte+' SELECT SUM(chute=98) FROM scope',params).fetchone()[0]
assert len(rows)==count
assert not c.execute(cte+tail,dict(params,shiftStart='2026-09-18 15:00',shiftEnd='2026-09-19 03:00')).fetchall()
print('Chute 98 SQL checks passed: KPI count, repeated and unidentified passages, depot and shift scope.')
