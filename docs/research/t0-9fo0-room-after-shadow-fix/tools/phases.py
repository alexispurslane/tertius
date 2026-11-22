# phases.py <today's jitter result> [<no-jitter result>] [first,last]: strips.py of t0-ion0 for all 9 regions. It reads the
# cabin switch's lamps-on control (series "on", the lamps on in every frame, a still state) over film frames first to last,
# by default 36 to 215: 10 full cycles of the 18 jitter offsets, all before world tick 300 (frame 239), where the sun's
# light in the room changes (take 5's climb). Per region, the mean scene luminance of each jitter phase (phase = run draw
# mod 18, run draw = 33 + film frame, as in strips.py), the cycle mean, the darkest and the brightest phase against the
# cycle mean, and, from the no-jitter result's same control and frames, the mean without jitter against the cycle mean.
# The no-jitter result comes from TERTIUS_SURVEY_SWITCH_RUNS=on, which keeps the "on" series in series/switch/cabin-on.json.
import json, sys

CYCLE, STILL_DRAWS = 18, 33
today = json.load(open(sys.argv[1]))["series"]["on"]
none = None
first, last = 36, 215
for arg in sys.argv[2:]:
    if "," in arg:
        first, last = (int(x) for x in arg.split(","))
    else:
        data = json.load(open(arg))
        none = data["series"]["on"] if "series" in data else data
print(f"frames {first}..{last}")
print(f"{'region':>6} {'cycle mean':>10} {'darkest':>8} {'brightest':>9} {'no jitter':>9}")
for k in range(len(today[0])):
    phases = [[] for _ in range(CYCLE)]
    for t in range(first, last + 1):
        phases[(STILL_DRAWS + t) % CYCLE].append(today[t][k])
    means = [sum(p) / len(p) for p in phases]
    cycle = sum(means) / CYCLE
    name = "whole" if k == 0 else f"r{(k - 1) // 3}{(k - 1) % 3}"
    line = f"{name:>6} {cycle:10.4f} {100 * (min(means) / cycle - 1):+7.2f}% {100 * (max(means) / cycle - 1):+8.2f}%"
    if none is not None:
        still = sum(none[t][k] for t in range(first, last + 1)) / (last - first + 1)
        line += f" {100 * (still / cycle - 1):+8.2f}%"
    print(line)
