# stills.py <case> <variant...>: the survey's specks per 10,000 visible pixels and its blotch, of each variant's still.
import json, sys
case = sys.argv[1]
for v in sys.argv[2:]:
    r = json.load(open(f's/{v}/results/still/{case}.json'))
    print(f"{v:14s} {case:12s} specks {r['specks']:6.2f}  blotch {r['blotch']:.4f}")
