# Prints switch and lag results of several output folders side by side.
import json, sys, glob, os
R='/private/tmp/t7-3e22/x2/runs/'
tags=sys.argv[1:]
def load(tag, part, case):
    p=f'{R}out-{tag}/results/{part}/{case}.json'
    return json.load(open(p)) if os.path.exists(p) else None
for case in ('cabin','bay-sun-off','lamp-hall-16'):
    for tag in tags:
        r=load(tag,'switch',case)
        if r: print(f"switch {case:14s} {tag:14s} slowest off/on {r['offDraws']}/{r['onDraws']}  whole off/on {r['offDrawsWhole']}/{r['onDrawsWhole']}  change {r['change']:+.3f}")
for case in ('bay-sun','lamp-hall-16-moving','lamp-hall-64-spot-moving'):
    for tag in tags:
        r=load(tag,'lag',case)
        if r: print(f"lag {case:26s} {tag:14s} " + " ".join(f"{k}={v:.4f}" if isinstance(v,float) else f"{k}={v}" for k,v in r.items() if not isinstance(v,(list,dict))))
