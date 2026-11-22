# cycle.py <series name> <result> <first,last> <index>...: the 18-draw jitter cycle of a region in a film's per-frame region
# means. <series name> is "stand" (the stand part's result["film"], the single bounce) or "switch:<run>" (the switch
# part's result["series"][<run>], all light). Each frame over <first,last> is divided by the centred 18-frame moving mean,
# which removes a slow trend, and the ratios are averaged per frame mod 18. The cycle is the highest minus the lowest of
# those 18 phase means, in % of the level. "spread" is the standard deviation of the single ratios, in %, and "steps" the
# mean over 18 frames after the last frame against the 18 frames before the first, in %.
import json, sys

name, path, span = sys.argv[1], sys.argv[2], sys.argv[3]
first, last = (int(x) for x in span.split(","))
r = json.load(open(path))
rows = r["film"] if name == "stand" else r["series"][name.split(":")[1]]
print(f"{name} {path} frames {first}..{last}")
for i in (int(x) for x in sys.argv[4:]):
    s = [row[i] for row in rows]
    ratio = {t: s[t] / (sum(s[t - 9:t + 9]) / 18) for t in range(first, last + 1)}
    phase = [[v for t, v in ratio.items() if t % 18 == p] for p in range(18)]
    means = [sum(p) / len(p) for p in phase]
    mean = sum(ratio.values()) / len(ratio)
    sd = (sum((v - mean) ** 2 for v in ratio.values()) / len(ratio)) ** 0.5
    label = "whole" if i == 0 else f"r{(i - 1) // 3}{(i - 1) % 3}"
    print(f"  {label}: cycle {100 * (max(means) - min(means)):.2f} %  spread {100 * sd:.2f} %  phase means "
          + " ".join(f"{100 * (m - 1):+.2f}" for m in means))
