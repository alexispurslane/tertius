# lags.py <run folder>...: the median lag in draws and the median lag error (share of the light) of the three lag films.
import json, sys, os
CASES = ("bay-sun", "lamp-hall-16-moving", "lamp-hall-64-spot-moving")
print(f"{'variant':24s} " + " ".join(f"{c:>26s}" for c in CASES))
for d in sys.argv[1:]:
    cells = []
    for c in CASES:
        p = f"{d}/results/lag/{c}.json"
        if os.path.exists(p):
            r = json.load(open(p)); cells.append(f"{r['lagDraws']:12.2f} / {r['errorMedian']:.4f}")
        else:
            cells.append(f"{'not taken':>26s}")
    print(f"{os.path.basename(d.rstrip('/')):24s} " + " ".join(f"{x:>26s}" for x in cells))
