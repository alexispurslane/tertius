# climbstep.py <climb result>: the step of each region's single-bounce plateau between the climb's two states (world
# ticks 300 and 306), from the survey's climb result. A plateau is the survey's: the mean of a state's unfiltered stills
# from still 5 on (renderer-survey.py, climb_result). "sd" is the standard deviation of those stills about their mean, in %
# of the plateau. Before the shadow fix, r11's plateau fell 43 % from tick 300 to tick 306 (t0-ion0).
import json, statistics, sys

r = json.load(open(sys.argv[1]))
FIRST = 5
print(f"{'region':>6} {'t300':>9} {'t306':>9} {'step':>8} {'sd t300':>8} {'sd t306':>8}")
for k in range(len(r["plateau"]["t300"])):
    a, b = r["plateau"]["t300"][k], r["plateau"]["t306"][k]
    sds = [100 * statistics.pstdev([row[k] for row in r["unfiltered"][s][FIRST:]]) / r["plateau"][s][k]
           for s in ("t300", "t306")]
    name = "whole" if k == 0 else f"r{(k - 1) // 3}{(k - 1) % 3}"
    print(f"{name:>6} {a:9.5f} {b:9.5f} {100 * (b / a - 1):+7.3f}% {sds[0]:7.3f}% {sds[1]:7.3f}%")
