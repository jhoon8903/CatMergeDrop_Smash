"""Read-only barrier experiment. Exact cumulative model calibrated against 58 ECS Drops."""
import json, math
from pathlib import Path
from battle_model import load_snapshot,simulate
root=Path(__file__).resolve().parents[2]
s=load_snapshot(root); records=[]; changes=[]
for n,d in sorted(s['stages'].items()):
 e=n.bit_length(); stats=s['cats'][e-1]; target=(17*stats['damage']*(stats['bounces']+1)+19)//20
 level=next((i+1 for i,b in enumerate(s['bricks']) if b['health']>=target),len(s['bricks']))
 cells=[dict(c) for c in d['cells']]; lanes=[]; selected=[]
 for lane in range(d['columns']):
  targets=[2,3,5,7,9] if lane%2 else [2,4,6,8,9]
  selected.append(sorted(set(min(d['rows']-1,r) for r in targets)))
 protected={row*d['columns']+lane for lane,rows in enumerate(selected) for row in rows}
 for lane,rows in enumerate(selected):
  for row in rows:
   idx=row*d['columns']+lane
   if cells[idx]['kind']!='normal':
    donors=[i for i,c in enumerate(cells) if i not in protected and c['present'] and c['kind']=='normal' and i//d['columns']>=2]
    if not donors: donors=[i for i,c in enumerate(cells) if i not in protected and c['present'] and c['kind']=='normal']
    donor=min(donors,key=lambda i:(i%d['columns']==lane,abs(i//d['columns']-row),i))
    cells[donor]['kind'],cells[idx]['kind']=cells[idx]['kind'],'normal'
   cells[idx]['level']=max(cells[idx]['level'],level)
 for idx,(old,new) in enumerate(zip(d['cells'],cells)):
  if old!=new:changes.append(dict(stage=n,index=idx,old=old,new=new))
 before=[]
 for lane in range(d['columns']):
  cats=[dict(slot=i*d['columns']+lane,column=lane,row=i,level=e) for i in range(3)]
  before.append(simulate(s,n,cats)['won'])
 old=d['cells'];d['cells']=cells
 for lane in range(d['columns']):
  tests=[]
  for lvl in [e,e+1]:
   hp=None;cats=[dict(slot=i*d['columns']+lane,column=lane,row=i,level=lvl) for i in range(3)]
   for drop in range(1,5):
    out=simulate(s,n,cats,initial_health=hp);hp=out['brick_health']
    if out['won']:break
   tests.append(drop if out['won'] else 0)
  lanes.append(dict(lane=lane,before_one_drop=before[lane],baseline_drops=tests[0],grown_drops=tests[1],rows=selected[lane]))
 records.append(dict(stage=n,baseline_level=e,barrier_level=level,lanes=lanes))
 d['cells']=old
summary=dict(stages=len(records),lanes=sum(len(r['lanes']) for r in records),changed_cells=len(changes),before_one_drop=sum(l['before_one_drop'] for r in records for l in r['lanes']),after_one_drop=sum(l['baseline_drops']==1 for r in records for l in r['lanes']),no_grown_clear_stages=[r['stage'] for r in records if not any(l['grown_drops'] for l in r['lanes'])])
p=root/'Library/BalanceValidation/lane-barrier-plan.json';p.parent.mkdir(exist_ok=True,parents=True);p.write_text(json.dumps(dict(summary=summary,records=records,changes=changes,source_hashes=s['source_hashes']),indent=2));print(json.dumps(summary))
print('remaining one-shot stages',[(r['stage'],[l['lane'] for l in r['lanes'] if l['baseline_drops']==1]) for r in records if any(l['baseline_drops']==1 for l in r['lanes'])][:30])
