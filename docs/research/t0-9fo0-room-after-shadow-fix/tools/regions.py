# regions.py <result> <reference result>: per series index (0 the whole image, 1 + 3 * row + column the 3 x 3 regions,
# so 5 is r11), the draws after each switch until the run stays within 5 % of the reference's control, by switch.py's
# rule ("none" if it is still outside at the window's last frame), the level 60 draws after each switch against the
# reference's control, and the region's share of the whole image's luminance in the reference's "on" control.
import json, sys

OFF, ON, END, TOL = 160, 360, 460, 0.05  # as in switch.py, which runs on import


def settle(series, control, first, last):  # switch.py's rule
    bad = [k for k in range(first, last) if abs(series[k] - control[k]) > TOL * abs(control[k])]
    return 0 if not bad else (None if bad[-1] == last - 1 else bad[-1] - first + 1)


run = json.load(open(sys.argv[1]))["series"]
ref = json.load(open(sys.argv[2]))["series"]
sw = run["switch"]
print(f"{'index':>5} {'settle off':>10} {'settle on':>9} {'lvl60 off':>9} {'lvl60 on':>8} {'share on':>8}")
for i in range(len(sw[0])):
    s = [f[i] for f in sw]
    off = settle(s, [f[i] for f in ref["off"]], OFF, ON)
    on = settle(s, [f[i] for f in ref["on"]], ON, END)
    l_off = s[OFF + 60] / ref["off"][OFF + 60][i]
    l_on = s[ON + 60] / ref["on"][ON + 60][i]
    share = sum(f[i] for f in ref["on"][36:160]) / sum(f[0] for f in ref["on"][36:160])
    name = "whole" if i == 0 else f"r{(i - 1) // 3}{(i - 1) % 3}"
    print(f"{name:>5} {str(off):>10} {str(on):>9} {100 * l_off:8.1f}% {100 * l_on:7.1f}% {share:8.3f}")
