# switch.py <reference result> <variant result>...: the two measures of a cabin switch take (ticket t0-mzhr).
# The series of a switch result (renderer-survey.py, switch_result) hold, per film frame t from 0 to 459, the mean
# scene luminance of the whole image (index 0) and of the 3 x 3 regions (index 1 + 3 * row + column, so 5 is r11, the
# center). Run "switch" turns the lamps off at frame 160 and on at frame 360, run "off" and run "on" hold them off or on.
# "d draws after a switch" is frame SWITCH + d, as in ratio.py of design-x-acceptance-2026-11-14.
#   settle: the survey's own counts (slowest changed region, whole image) and r11's by the same rule (settle_draws)
#   level at 60: switch[SWITCH + 60] / reference control[SWITCH + 60], the control of the reference run that holds the
#                lamps in the state after the switch ("off" after 160, "on" after 360)
#   dip:   the lowest of switch[SWITCH + d] / reference control[SWITCH + d] for d = 1 to 60, with its d
#   cycle: the mean of switch[SWITCH + d] / reference control[SWITCH + d] for d = 52 to 69, one full jitter cycle of 18
#          draws around 60 (added by t0-mzhr: a single frame of a short history swings with the cycle)
#   own15: switch[SWITCH + 15] / own control[SWITCH + 15] - 1 for r11, the number of the first takes' notes
import json, sys

OFF, ON, END, TOL = 160, 360, 460, 0.05

def settle(series, control, first, last):
    bad = [k for k in range(first, last) if abs(series[k] - control[k]) > TOL * abs(control[k])]
    return 0 if not bad else (None if bad[-1] == last - 1 else bad[-1] - first + 1)

def show(v):
    return "none" if v is None else str(v)

ref = json.load(open(sys.argv[1]))["series"]
print(f"{'variant':34s} {'slowest off/on':>14s} {'whole off/on':>12s} {'r11 off/on':>11s}"
      f" {'lvl60 off w/r11':>16s} {'lvl60 on w/r11':>15s} {'cyc off w/r11':>14s} {'cyc on w/r11':>13s} {'dip off r11':>12s} {'dip on r11':>12s} {'own15 on r11':>12s}")
for path in sys.argv[2:]:
    r = json.load(open(path)); se = r["series"]; sw = se["switch"]
    r11 = [row[5] for row in sw]
    s_off = settle(r11, [row[5] for row in se["off"]], OFF, ON)
    s_on = settle(r11, [row[5] for row in se["on"]], ON, END)
    def level(state, first, k, d=60):
        return sw[first + d][k] / ref[state][first + d][k]
    def dip(state, first):
        return min((level(state, first, 5, d), d) for d in range(1, 61))
    do, dn = dip("off", OFF), dip("on", ON)
    def cycle(state, first, k):
        return sum(level(state, first, k, d) for d in range(52, 70)) / 18
    own15 = sw[ON + 15][5] / se["on"][ON + 15][5] - 1
    name = path.split("/results/")[0].split("/")[-1]
    print(f"{name:34s} {show(r['offDraws']):>6s}/{show(r['onDraws']):<7s} {show(r['offDrawsWhole']):>5s}/{show(r['onDrawsWhole']):<6s}"
          f" {show(s_off):>4s}/{show(s_on):<6s} {100*level('off',OFF,0):7.1f}/{100*level('off',OFF,5):<7.1f}"
          f" {100*level('on',ON,0):7.1f}/{100*level('on',ON,5):<7.1f}"
          f" {100*cycle('off',OFF,0):6.1f}/{100*cycle('off',OFF,5):<7.1f} {100*cycle('on',ON,0):5.1f}/{100*cycle('on',ON,5):<7.1f} {100*do[0]:6.1f}@{do[1]:<4d} {100*dn[0]:6.1f}@{dn[1]:<4d}"
          f" {100*own15:+11.1f}")
print("levels and dips in % of the reference's control at the same frame; own15 in % against the run's own control")
