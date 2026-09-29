#!/usr/bin/env python3
"""All-stage fixed-power lane comparison; not an economy or player win-rate model."""
import json
from pathlib import Path
from battle_model import load_snapshot, simulate
root=Path(__file__).resolve().parents[2]
s=load_snapshot(root)
records=[]
for stage, data in sorted(s['stages'].items()):
    level=stage.bit_length()
    lanes=[]
    for col in range(data['columns']):
        cats=[dict(slot=row*data['columns']+col,column=col,row=row,level=level) for row in range(3)]
        result=simulate(s,stage,cats)
        lanes.append(dict(column=col,**result))
    records.append(dict(stage=stage,level=level,winning_lanes=sum(x['won'] for x in lanes),columns=data['columns'],lanes=lanes))
summary=dict(stages=len(records),battles=sum(r['columns'] for r in records),no_winning_lane=sum(r['winning_lanes']==0 for r in records),exactly_one_winning_lane=sum(r['winning_lanes']==1 for r in records),all_lanes_win=sum(r['winning_lanes']==r['columns'] for r in records),timeouts=sum(x['phase']=='Timeout' for r in records for x in r['lanes']))
out=root/'Library/BalanceValidation/catalog-report.json'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(dict(summary=summary,source_hashes=s['source_hashes'],records=records),ensure_ascii=False,indent=2))
print(json.dumps(summary))
