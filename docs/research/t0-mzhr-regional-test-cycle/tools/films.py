# films.py <motion result>...: level and blotch of the film rect-bay at its middle and at its last frame (the survey's
# motion samples "middle" and "end"; level = the frame's light over a still of the same state, minus 1).
import json, sys
print(f"{'variant':30s} {'level mid/last':>16s} {'blotch mid/last':>16s}")
for p in sys.argv[1:]:
    s = json.load(open(p))["samples"]
    name = p.split("/results/")[0].split("/")[-1]
    print(f"{name:30s} {s['middle']['level']:+7.2f} / {s['end']['level']:+5.2f}  {s['middle']['blotch']:6.3f} / {s['end']['blotch']:5.3f}")
