from pathlib import Path
import sqlite3
source = Path('ConveyorDashboard/Program.cs').read_text(encoding='utf-8')
cte = source.split('private const string UnderTwoPoundsCte = """', 1)[1].split('"""', 1)[0]
def query(method):
    section = source.split(' '+method+'(', 1)[1]
    tail = section.split('const string sql = UnderTwoPoundsCte + """', 1)[1].split('"""', 1)[0]
    return (cte+'\n'+tail).replace(' PARTITION (p2026)', '').replace('@shiftStart-INTERVAL 1 HOUR', '@shiftStart')
c = sqlite3.connect(':memory:')
c.create_function('CONCAT', -1, lambda *args: ''.join(map(str,args)))
c.executescript('CREATE TABLE parcel_history(PARCEL_ID,WEIGHT,LENGTH,HEIGHT,WIDTH,DATE_INSERT,CUSTOMER_ID,DATE_LIV,DEPOT_ID,EXCEPTION,SOURCE_TYPE,SOURCE_ID,VOID,CHUTE_NO); CREATE TABLE parcel(PARCEL_ID,CUSTOMER_ID); CREATE TABLE customer(CUSTOMER_ID,NAME); INSERT INTO customer VALUES(1,"Client A"),(2,"Client B"); INSERT INTO parcel VALUES(3,2);')
for pid,client,weight,l,h,w,time in [(1,1,1,10,2,4,'16:00'),(1,1,1.5,20,4,8,'17:00'),(1,1,2.5,0,0,0,'18:00'),(2,1,1,None,None,None,'17:00'),(3,None,1,10,2,4,'17:00'),(4,1,3,100,100,100,'17:00'),(5,None,1,2,3,4,'17:00')]:
    t='2026-09-17 '+time
    c.execute('INSERT INTO parcel_history VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)',(pid,weight,l,h,w,t,client,t,1,903,200,None,0, (39 if time == '16:00' else 38) if pid == 1 else None))
params=dict(shiftStart='2026-09-17 15:00',shiftEnd='2026-09-18 03:00',depotId=1,hasFloor=1,customerId=1)
summary=c.execute(query('GetConveyorUnderTwoPoundsClientsAsync'),params).fetchall()
detail=c.execute(query('GetConveyorUnderTwoPoundsParcelsAsync'),params).fetchall()
assert len(detail)==next(row[2] for row in summary if row[0]==1)==2
assert detail[0][0:6]==(1,1,20,4,8,3),detail
assert detail[1][2:5]==(None,None,None)
assert set(detail[0][8].split(','))=={'38','39'}
assert detail[1][8] is None
for client,expected in [(2,3),(0,5)]:
    rows=c.execute(query('GetConveyorUnderTwoPoundsParcelsAsync'),dict(params,customerId=client)).fetchall()
    assert len(rows)==1 and rows[0][0]==expected
assert not c.execute(query('GetConveyorUnderTwoPoundsParcelsAsync'),dict(params,depotId=2)).fetchall()
print('Shared SQL logic verified with SQLite fixtures: client isolation, fallback, unique parcels, dimensions and passage counts. MySQL connection not exercised.')
