# strips.py <today's jitter result> <no-jitter result>: the strips check's question 1 from a variant's cabin switch
# runs (the reference's, whose history of 32 draws smooths the cycle, and the cap of 4 draws'). It reads the lamps-on control (series "on", the lamps on in every frame, a still state) over film frames 172 to
# 459, 16 full cycles of the 18 jitter offsets. Under today's jitter it prints, for the whole image and r11, the mean
# scene luminance of each jitter phase (phase = run draw mod 18, run draw = 33 + film frame; the phase's numbering is
# arbitrary, its order is the draws' order), and the mean over the cycle. Under no jitter it prints the mean over the
# same frames. The series hold absolute scene luminance (renderer-survey.py, region_means), the same unit in both runs.
import json, sys

FIRST, LAST, CYCLE, STILL_DRAWS = 172, 459, 18, 33
today = json.load(open(sys.argv[1]))["series"]["on"]
none = json.load(open(sys.argv[2]))["series"]["on"]
for name, k in (("whole", 0), ("r11", 5)):
    phases = [[] for _ in range(CYCLE)]
    for t in range(FIRST, LAST + 1):
        phases[(STILL_DRAWS + t) % CYCLE].append(today[t][k])
    means = [sum(p) / len(p) for p in phases]
    cycle = sum(means) / CYCLE
    still = sum(none[t][k] for t in range(FIRST, LAST + 1)) / (LAST - FIRST + 1)
    order = sorted(means)
    print(f"{name}: today's jitter, per phase: " + " ".join(f"{m:.4f}" for m in means))
    print(f"{name}: cycle mean {cycle:.4f}, darkest phase {order[0]:.4f}, median phase {order[CYCLE // 2]:.4f},"
          f" brightest phase {order[-1]:.4f} ({100 * (order[-1] / cycle - 1):+.1f} % over the mean);"
          f" no jitter {still:.4f} ({100 * (still / cycle - 1):+.1f} % against the cycle mean,"
          f" {100 * (still / order[CYCLE // 2] - 1):+.1f} % against the median phase)")
