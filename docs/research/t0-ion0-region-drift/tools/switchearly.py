# switchearly.py <reference result> <variant result>...: the cabin's level 5, 10 and 20 draws after each switch, in the
# terms of t0-xlcu's switch.py: switch[SWITCH + d] / reference control[SWITCH + d], in %, for the whole image and r11
# (the center region), where the reference's control holds the lamps in the state after the switch.
import json, sys

OFF, ON, DS = 160, 360, (5, 10, 20)
ref = json.load(open(sys.argv[1]))["series"]
head = "  ".join(f"{state} d {d:>2d} w/r11" for state in ("off", "on") for d in DS)
print(f"{'variant':16s} {head}")
for path in sys.argv[2:]:
    sw = json.load(open(path))["series"]["switch"]
    cells = []
    for state, first in (("off", OFF), ("on", ON)):
        for d in DS:
            w, c = (100 * sw[first + d][k] / ref[state][first + d][k] for k in (0, 5))
            cells.append(f"{w:6.1f}/{c:<6.1f}")
    name = path.split("/results/")[0].split("/")[-1]
    print(f"{name:16s} " + "  ".join(f"{x:>15s}" for x in cells))
print("levels in % of the reference's control at the same frame")
