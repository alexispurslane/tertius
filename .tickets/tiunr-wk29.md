---
id: tiunr-wk29
status: closed
deps: []
links: [tiunr-4b2g, ter-18hc, tiunr-nxtr]
created: 2026-11-08T00:59:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Under the pixel history rule, the anti-lag cuts more rough-plate history during a camera pan


## Notes

**2026-11-08T00:59:42Z**

Measured 2026-11-07 (evidence /private/tmp/tertius-jitter-pixel-20261107/report.md). With FSR and TERTIUS_HISTORY_JITTER=pixel, during the rough-plate pan (measured frames 60 to 119 of --camera walk) the anti-lag cuts 40% of the crop's plate pixels per frame, against 22% with today's jitter-corrected place. At frame 119 the crop's median accepted length is 319 frames against 999. The pre-FSR grain measure also rises in every motion phase. The cause is unknown. Still scenes improve under the rule: rough median 12 to 88 frames, smooth 264 to 1,152, final FSR noise 2.91 to 2.48 (rough) and 1.70 to 1.11 (smooth), smooth reflection edge 4.0 to 1.7 px.

**2026-11-08T01:04:44Z**

2026-11-07: the user chose to diagnose the pan cuts before building on the pixel rule or turning to the rough speckle. A delegated run adds a pixel-rule trace with fallback use, captures the walk with FSR (default and pixel rule, plus a no-jitter pair and, if possible, the pixel rule without fallback), and tests five causes: mixed rules at the fallback (P1), surface-test failures on jitter-free taps (P2), dark-pixel anti-lag sensitivity on longer histories (P3), pixel-center against sample-point motion (P4) and the sharper history resampled by motion (P5). No fix until the user picks one.

**2026-11-08T02:04:41Z**

Diagnosed 2026-11-08 (evidence /private/tmp/tertius-pan-cuts-20261107/report.md, instrumentation only, uncommitted). The pixel rule does not cause the pan's extra cuts: under motion it equals no jitter in every total (rough pan crop cut share 40.3% pixel, 40.2% no jitter, 22.0% control; whole plate 7.5%, 7.5%, 1.4%). The control cuts less because its jitter makes the reflected place a filter of rare bright hits in still frames: in the frame after a hit the reflected place wins at the hit pixel in 95% of cases (control) against 0% (pixel rule, no jitter), because a still pixel under the pixel rule reads only its own texel, whose one tap fails the hit-distance test. So the pixel rule keeps that light in its long slow histories (425 far-plate pixels at frame 0, control 0). The pan spreads it bilinearly into neighbors with empty fast boxes, and the anti-lag cuts relative to the tiny box mean: a transient, far-plate cuts peak at 4.8% at frame 78 and end by 108; 1,107 cut pixel-frames per hit against 51. TERTIUS_DEBUG_REFLECTED_PLACE=off (new) confirms it: the control then keeps the hits (44,929 pixels) and cuts 20% of the plate per frame in every phase. Ruled out: P1 fallback (0.013% of crop pixel-frames, all at silhouettes), P2 place weights (0.083% lost at both places), P4 center motion (at most 0.0012 px), constant pan fraction (pixel rule blurs less, 0.305 against 0.389). P3 amplifies; smooth pan loss is only longer histories at equal cut share (median length at cut 408 against 55). P5 holds in a narrower form: the kept speckle is the hit light. Open: whether that kept light is the light the control loses under jitter; the fix design. Added: trace layer 8 and flags 10/11, measured-trace-flags.u8.gz, the switch; 25 new tests; focused 302 run 0 failed 5 skipped in Checked and Release; default output byte-identical to the sealed captures.

**2026-11-08T02:10:17Z**

2026-11-08: verified the no-jitter identity (279 of 279 render files equal between the two rules) and read the heat maps. The cause is the box clamp's bias against rare bright light, the mechanism of ter-18hc, which the control's jitter hid in still frames. Linked to ter-18hc. Next: the user chooses between accepting the pixel rule and a fix of the box.

**2026-11-08T02:19:47Z**

2026-11-08: the user chose design T for the box (tiunr-nxtr): keep the rare light and widen the box by a standard error from a long-run second moment. The firefly clamp was rejected.

**2026-11-08T20:05:06Z**

2026-11-08: the diagnosis is done, and it cleared the pixel history rule (tiunr-4b2g), which merges into main now.
