from pathlib import Path
import sqlite3
s=Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8')
cte=s.split('private const string ConveyorRecirculationCte = """',1)[1].split('"""',1)[0]
tail=s.split('public async Task<ConveyorRecirculationResponse>',1)[1].split('const string sql = ConveyorRecirculationCte + """',1)[1].split('"""',1)[0]
c=sqlite3.connect(':memory:'); c.create_function('CONCAT',-1,lambda *a: ''.join(map(str,a)))
c.executescript('CREATE TABLE parcel_scan_history(parcel_id,line_id,chute,camera_data,date_insert,depot_id); CREATE TABLE parcel(PARCEL_ID,CUSTOMER_ID); CREATE TABLE customer(CUSTOMER_ID,NAME); INSERT INTO parcel VALUES(1,10),(1,10); INSERT INTO customer VALUES(10,"Client A");')
for pid,line,chute,times,depot in [(1,0,39,3,1),(1,1,38,2,1),(2,0,39,1,1),(2,0,38,1,1),(3,0,98,2,1),(0,0,39,2,1),(4,2,39,2,1),(5,0,16,2,1),(6,0,39,2,2)]:
 for i in range(times): c.execute('INSERT INTO parcel_scan_history VALUES(?,?,?,?,?,?)',(pid,line,chute,'','2026-09-17 16:0'+str(i),depot))
params=dict(shiftStart='2026-09-17 15:00',shiftEnd='2026-09-18 03:00',depotId=1,hasFloor=1)
rows=c.execute((cte+tail).replace('<=>','IS'),params).fetchall()
assert len(rows)==7,rows
assert set(r[0] for r in rows)=={1,5}
assert len([r for r in rows if r[0]==1 and r[1]==0])==3
assert next(r for r in rows if r[0]==1)[5]=='Client A'
assert next(r for r in rows if r[0]==5)[5]=='Client non identifié'
count=c.execute(cte+' SELECT COUNT(DISTINCT parcel_id) FROM same_chute_repeat',params).fetchone()[0]
assert count==len(set(r[0] for r in rows))==2
assert not c.execute((cte+tail).replace('<=>','IS'),dict(params,shiftStart='2026-09-18 15:00',shiftEnd='2026-09-19 03:00')).fetchall()
print('Recirculation SQL fixture checks passed: KPI reconciliation, repeated chute/line, exclusions, customer joins and shift scope. SQLite verification only.')
