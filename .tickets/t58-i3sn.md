---
id: t58-i3sn
status: open
deps: []
links: []
created: 2026-11-22T17:47:37Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# At a render scale near 1 the cabin shows bright specks on the ceiling, a bright line and dots at the openings' edges, and grain on the floor's sides

Found by the combined check t0-gbgj (record docs/research/t0-gbgj-combined-check, take 2b). Both sides carry the back-face layer: M2 is main 284025851, X2 is the merged head a7e373197 (Design X on the layer).

The take: the switch "cabin" of the renderer survey at 4.08 stops, default tone curve and bloom, at a constant render scale of 0.956 and at 2/3. The constant scale comes from the record's scratch-render-scale.patch (TERTIUS_X_RENDER_SCALE). The acceptance lead's replay path of the dynamic scale (scratch-dynamic-scale.patch of a01ec40d0) gives the same pictures on M2 within 4 of 255 steps, so the faults do not depend on how the scale is set. Every take of the survey runs at the headless 2/3. A machine with headroom renders near 1.

What stays at 0.956 and is gone or much weaker at 2/3:
- single bright specks on the ceiling, with the lamps on (frame 150) and after they go off;
- a bright thin vertical line along the inner edge of the near openings, with the lamps on and off;
- a row of bright dots along the openings' lower sill, with the lamps on and off;
- faint fine grain on the floor's sides after the lamps go off.

Pictures in the record's pics/:
- jamb-m2-fixed-0956-vs-23.png: M2's whole frames, 0.956 left and 2/3 right, at film frames 150 (10 draws before off) and 355 (195 after off), half scale.
- opening-0956-vs-23.png: the near right opening (output pixels 1700 to 1920 by 440 to 640) at 2x nearest neighbor, frames 150, 220 and 355, columns M2 0.956, X2 0.956, M2 2/3, X2 2/3.

Counts of specks per box (tools/edgefaults.py: groups of pixels at least 1.5 times and 0.01 above their 7 x 7 median in display-linear light), ceiling / opening / floor sides:

| Frame | M2 0.956 | X2 0.956 | M2 2/3 | X2 2/3 |
|---|---|---|---|---|
| 150, 10 before off | 5 / 39 / 0 | 5 / 39 / 0 | 0 / 5 / 0 | 0 / 5 / 0 |
| 220, 60 after off | 10 / 36 / 41 | 10 / 45 / 33 | 0 / 2 / 0 | 0 / 0 / 0 |
| 355, 195 after off | 10 / 37 / 30 | 11 / 37 / 30 | 0 / 32 / 0 | 0 / 33 / 0 |

At 2/3, frame 355 shows a faint dotted line at the opening's inner edge (32 and 33 specks), much dimmer than at 0.956.

M2 against X2: the same faults on both, at the same places and in about the same counts. The one difference is at frame 220, where X2's jamb face below the line carries a brighter blotch than M2's.

Candidates, not tested:
- screen rays at the openings' edges: the class of ter-ukw6;
- rare bright hits on the lit panels: the class of ter-fe63.

The lamp glow is not a cause: with the probe's fair switch (glow off with the light, 68239c0af), the openings, the jambs and the floor are bit-identical to the glow-stays run in every kept frame.

No fix in t0-gbgj.


## Notes

**2026-11-22T18:51:39Z**

The size ramp on M3 = main a819b10b2 (the layer and the walk's stored inverse depth), from the combined check t0-gbgj. The ramp is the acceptance lead's sizeramp.sh/sizeramp.py of b53c037f1: every render size that RenderExtent gives for 1920 x 1080 at scales 0.50 to 1.0 (181 sizes), each held for 40 draws through the replay of scratch-dynamic-scale.patch, in the cabin's view with the lamps on at 4.08 stops. A size leaks when either jamb crop's display-linear mean is above 0.08. Grey jambs measure 0.035 to 0.055.

32 of 181 sizes leak with the layer: 1000 x 560, 1008 x 560, 1072 x 600, 1080 x 600, 1200 x 672, 1272 x 712, 1344 x 752, 1376 x 776, 1400 x 784, 1416 x 792, 1432 x 808, 1472 x 824, 1488 x 832, 1544 x 864, 1560 x 872, 1600 x 896, 1616 x 904, 1616 x 912, 1632 x 912, 1672 x 936, 1688 x 944, 1744 x 976, 1744 x 984, 1760 x 984, 1800 x 1008, 1800 x 1016, 1816 x 1016, 1816 x 1024, 1832 x 1024, 1872 x 1048, 1872 x 1056, 1888 x 1056. The brightest: 1800 x 1008 (right 0.2298, left 0.2420), 1872 x 1048 (0.2073, 0.2390), 1600 x 896 (0.1789, 0.2099). The leak is not monotone in the size: 1832 x 1032 (scale 0.956, the size of the earlier takes) gives 0.0491, and the next size down, 1832 x 1024, gives 0.0805 and 0.0888. Settle check: 1344 x 752 moves 0.0836 to 0.0876 over the last 10 draws; 1920 x 1080 stays at 0.044.

Crops: docs/research/t0-gbgj-combined-check/pics/ramp-M3-openings.png, the near right opening at 2x of 1800 x 1008, 1872 x 1048, 1600 x 896, 1344 x 752 and 1920 x 1080 (no leak). At the leaking sizes the jamb face is silver and speckled, the inner edge carries a broad bright line with a glow, the sill and the ceiling carry bright dots. The table: .../ramp-M3.txt, the chart .../pics/ramp-M3.png.

So the layer weakens the leak but does not cure it: on main without the layer, the acceptance lead's bright jambs measured 0.27 to 0.46. The candidate stays as written: rays or shadow tests at the thin edge of the wall's opening, aliased against the render grid (the class of ter-ukw6). Not tested.

The new main a819b10b2 against M2 = 284025851 at the constant 0.956 (1832 x 1032): the bright line at the openings' inner edge, the sill's dots, the ceiling's specks and the floor's grain are the same. Specks per box (ceiling / opening / floor sides) at frames 150, 220, 355: M2 5/39/0, 10/36/41, 10/37/30; M3 5/39/0, 10/36/42, 10/37/31. pics/opening-0956-m2-vs-m3.png. The jamb's soft blotch at frame 220 may be a little dimmer on M3.

**2026-11-22T19:20:54Z**

The ramps on X3 (the line's merge bcca31e95) and on W (the wobble fix's branch 839bce1b5), and the window test, from t0-gbgj.
- X3: 35 sizes leak, 54 above 0.06, largest 0.2339 / 0.2996 at 1800 x 1008. It differs from M3 only at five sizes within 0.012 of the threshold. Design X leaks where main with the layer leaks.
- W: 0 sizes leak, 0 above 0.06, largest 0.0468. The fix draws every render size at 16:9.
- Window test (render scale held at 1, the render size equals the window; crops scaled by x' = W/2 + (x - 960) H/1080, y' = y H/1080): M3 1800 x 1008 (aspect error +0.45 %) right 0.0977, left 0.0582; W the same to four decimals; 1920 x 1080 0.0436 / 0.0435 on both. On M3: 1784 x 1008 0.0437 / 0.0437, 1792 x 1008 0.0438 / 0.0433, 1808 x 1008 0.0435 / 0.0431, 1816 x 1008 0.0510 / 0.0591. Crop: docs/research/t0-gbgj-combined-check/pics/window-1800x1008-M3-W.png.
- So W hides the leak at a 16:9 output and does not cure it: a window whose own shape is off 16:9 can sit on a tooth all the time. Candidate cause (from the back-face line, read from the code, not measured): the texel grid of the sun's shadow cascades, whose texel size depends on the field of view and the aspect (ShadowCascades.cs).
