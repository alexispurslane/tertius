# What the region evidence buys and costs: Design X with and without it (a take for t0-qvm1)

The line orchestrator's question of 2026-11-22: what does region evidence buy, and what does it cost, so that the follow-up `t0-qvm1` can end with a repaired rule or with no rule. The region's drift test has three measured faults: false fires in a still scene (`t0-gumm`'s history map), a count that restarts on a moving render size (`t4f-71qp`), and a late start after a stop (`t0-3vwx`'s final walk). This take is no gate of the merge.

## The two codes

Both are the final code, the cap's commit `05a27262d` (Design X, light evidence restarting at `FullRateFrames` 8), built with `t0-3vwx`'s fitted probe patch and its region, dynamic-scale and history logs, and `t0-gumm`'s own-ray log and history dump (`t0-gumm-catch-up-spread/tools/builds.sh`):

- `xfd`: Design X as it is, with region evidence.
- `xfn`: the same with `t0-gumm-catch-up-spread/scratch-no-region-evidence.patch`: the region's drift test still runs and its log still lists the fires, but region evidence caps no history. For the images this is the rule turned off. Its cost is not measured here.

## The settled history lengths of the still cabin

![the history maps](pics/history-map-rule-on-off.png)

`pics/history-map-rule-on-off.png`: the cabin of the switch film, lamps on, settled, at the constant scale 2/3, draw 190 (before the lamps go off at 193). Left the still at draw 190, right the slow diffuse history's length per render pixel (`t0-gumm-catch-up-spread/tools/histmap.py`).

| Code | Mean length | Under 8 draws | 8 to 31.5 draws | 31.5 or more (the cap of 32) |
|---|---|---|---|---|
| With region evidence | 28.35 draws | 1.9 % | 20.8 % | 77.4 % |
| Without region evidence | 31.40 draws | 1.9 % | 0.0 % | 98.1 % |

- With the rule, region-sized rectangles on the ceiling, the walls and the floor hold shorter histories: the region test's false fires cap a region at 8 draws, and its histories regrow.
- Without the rule, every pixel except the window openings (which show space and hold no history) reaches the cap.
- The rule-on numbers equal X5's map of `t0-gumm` to the digit: the cap of `05a27262d` does not act in a still.

## The walk's stop

Video: `pics/walk-rule-on-off.mp4`, with region evidence left and without it right, 260 draws (the walk's last 60 and the 200 after the stop). Both codes play back the card's walk log of Design X `05a27262d` from `t0-3vwx`'s final take (scale 0.949 on every draw of the video), so the numbers stand beside that film's. `tools/queue.sh`, its output in `queue.txt`.

| The center region's light after the stop, against the last frame | 0 draws | 20 draws | 40 draws |
|---|---|---|---|
| Design X with region evidence | +5.87 % | +4.61 % | +3.58 % |
| Design X without region evidence | +5.52 % | +4.27 % | +3.26 % |
| Main `454574bf8` (`t0-3vwx`'s final film) | +5.34 % | +3.73 % | +2.49 % |

- The rule-on run gives the final film's numbers to the digit.
- Without region evidence the trail after the stop is a little shorter, not longer: 0.32 to 0.35 points less light at each point. The region test starts only about 36 draws after the stop, so it cannot shorten this trail; the false fires before the walk leave shorter histories, which is the only difference the rule makes here. (Not measured: the history lengths at the stop.)
- The survey's settle after the stop, slowest region: 47 draws with region evidence (the final film: 47) and 53 without it (main: 25). Without it the whole image settles in 3 draws rather than 0, and the slow regions are the same ones (`r01` 50, `r10` 53, `r11` 39, `r20` 39, against 47, 47, 35, 36). So region evidence buys about 6 draws of the walk's settle and costs 0.3 points of its trail.

## The lamp switch

The survey's take of the fair lamp switch (`switch cabin`) at the constant scale 2/3, `sw-QD` with region evidence and `sw-QN` without it (`tools/queue.sh`, the survey's `regions.py` in `queue.txt`). "Settle" is the draws until the light stays within 5 % of its settled level, in the 200 draws after off and the 100 after on. "Level at 60" is the light 60 draws after the event against the settled level.

| Code | Off: whole, slowest region | On: whole, slowest region | Level at 60 after off | Level at 60 after on |
|---|---|---|---|---|
| With region evidence | 71, 82 | 34, 54 | 107.0 % | 98.2 % |
| Without region evidence | 189, none in 200 | 68, none in 100 | 122.5 % | 94.4 % |

- Without region evidence, the light trails the switch. 60 draws after off, the image is still 22.5 % too bright (the upper corners 139.6 % and 138.0 %), and only the lamp's own region and the middle column settle within 200 draws (`r01` 146, `r11` 171, `r21` 177). After on, only the bottom row settles within 100 draws (46 to 64).
- With region evidence, every region settles after off within 82 draws and after on within 54.
- So region evidence buys the switch's settle: about 120 draws of the whole image's settle after off, and every region's settle after both events. This is the case that the rule exists for. A light change that the block's own rays do not see (the corners, which the lamp lights only through bounce light) reaches those histories only through the region.
