# Prints, for a switch result, the ratio switch/control - 1 of the whole image and of chosen regions at chosen draws.
import json, sys
names = ["whole"] + [f"r{r}{c}" for r in range(3) for c in range(3)]
def show(path, regs=(0, 5, 2, 8)):
    r = json.load(open(path)); se = r["series"]
    sw, off, on = se["switch"], se["off"], se["on"]
    print(path.split("/")[-4], "regions changed:", r["regions"], "change", round(r["change"], 4))
    print("  draw-after-off " + " ".join(f"{names[k]:>8s}" for k in regs))
    for d in (1, 4, 6, 8, 10, 12, 15, 20, 30, 40, 60, 80, 100, 150, 199):
        t = 160 + d
        print(f"  off+{d:3d}        " + " ".join(f"{sw[t][k] / off[t][k] - 1:+8.3f}" for k in regs))
    for d in (1, 4, 6, 8, 10, 12, 15, 20, 30, 40, 50, 60, 70, 80, 90, 99):
        t = 360 + d
        print(f"  on+{d:3d}         " + " ".join(f"{sw[t][k] / on[t][k] - 1:+8.3f}" for k in regs))
    print("  control on/off level at end:", " ".join(f"{on[459][k] / off[459][k]:8.3f}" for k in regs))
    print("  before the switch (draw 159) switch/on:", " ".join(f"{sw[159][k] / on[159][k] - 1:+8.3f}" for k in regs))
for p in sys.argv[1:]:
    show(p)
