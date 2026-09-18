from pathlib import Path
import re,sqlite3
source=Path('ConveyorDashboard/EdiDepotChuteService.cs').read_text(encoding='utf-8')
queries=re.findall(r'new MySqlCommand\("""(.*?)"""',source,re.S)
c=sqlite3.connect(':memory:')
c.executescript('CREATE TABLE parcel_scan_history(chute,parcel_id,depot_id,line_id,date_insert);')
for chute,pid,depot,line in [(1,10,1,0),(1,10,1,0),(2,10,1,1),(2,11,1,1),(2,0,1,1),(None,None,1,0),(1,12,1,3),(1,13,2,0)]:
 c.execute('INSERT INTO parcel_scan_history VALUES(?,?,?,?,?)',(chute,pid,depot,line,'2026-09-17 18:00:00'))
params=dict(depot=1,conveyor=1,otherConveyor=0,start='2026-09-17 04:00:00',end='2026-09-18 04:00:00')
rows=c.execute(queries[2],params).fetchall()
assert sum(r[2] for r in rows)==6
assert all(r[4]==2 and r[5]==6 for r in rows)
assert sum(r[1] for r in rows)==3 # same parcel can visit more than one chute
assert sum(r[3] for r in rows)==2
assert c.execute(queries[2],dict(params,conveyor=2)).fetchone()[1:3]==(1,1)
assert not c.execute(queries[2],dict(params,start='2026-09-18 04:00:00',end='2026-09-19 04:00:00')).fetchall()
c.create_function('CONCAT',-1,lambda *a: ''.join(map(str,a)))
c.executescript('CREATE TABLE conveyor_shift(id,conveyor_id,name); CREATE TABLE conveyor_list(CONVEYOR_ID,CONVEYOR_NAME,DEPOT_ID); CREATE TABLE conveyor(DEPOT_ID,ENABLED,SHIFT_ID); CREATE TABLE conveyor_shift_route(route_id,chute_no,shift_id,conveyor_id); CREATE TABLE route(ROUTE_ID,END_DEPOT_ID); CREATE TABLE depot(DEPOTNUMBER,DEPOTNAME); CREATE TABLE location(ROUTE_ID,DEPOTNUMBER,ENABLED,LOC_POSTAL_CODE); INSERT INTO conveyor_list VALUES(1,"Haut",1),(3,"QC",2); INSERT INTO conveyor_shift VALUES(3,1,"Soir"),(20,3,"Jour"); INSERT INTO conveyor VALUES(1,1,3); INSERT INTO conveyor_shift_route VALUES(100,4,3,1),(101,5,3,1),(102,6,20,3); INSERT INTO route VALUES(100,1),(101,2),(102,2); INSERT INTO depot VALUES(1,"STH"),(2,"QC"); INSERT INTO location VALUES(100,1,1,"J3V1A1"),(100,1,1,"J3V1A2"),(100,1,1,"J3T1A1"),(100,1,0,"H1A1A1"),(100,2,1,"H2A1A1");')
configs=c.execute(queries[0],dict(depot=1)).fetchall()
assert len(configs)==1 and configs[0][0]==3 and configs[0][3]==1
mapping=c.execute(queries[1].replace('LEFT(l.LOC_POSTAL_CODE,3)','substr(l.LOC_POSTAL_CODE,1,3)'),dict(depot=1,configuration=3)).fetchall()
assert {r[5] for r in mapping if r[0]==100}=={'J3V','J3T'}
assert next(r for r in mapping if r[0]==101)[4]=='QC'
assert not c.execute(queries[1].replace('LEFT(l.LOC_POSTAL_CODE,3)','substr(l.LOC_POSTAL_CODE,1,3)'),dict(depot=1,configuration=20)).fetchall()
print('Real scan counts, cross-chute deduplication, line/depot scope, configuration isolation and active local FSA mapping passed.')

all_rows=c.execute(queries[2],dict(params,conveyor=None)).fetchall()
assert sum(r[2] for r in all_rows)==7
assert {r[6] for r in all_rows}=={1,2}
