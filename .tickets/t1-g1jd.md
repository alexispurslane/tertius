---
id: t1-g1jd
status: closed
deps: []
links: [ter-18hc, ter-pdw8, t1-yi11, t1-nyjl, t0-qvm1]
created: 2026-11-23T05:42:37Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal, diffuse]
---
# The anti-lag after the clamp shortens the diffuse history at 8 % of a still's pixels, so they never reach the full length

**2026-11-25 · Retired.** Preserve the temporal lighting measurements. Choose any change to sampling, weighting or history only after isolating the source of the pattern on the new deck (ter-xzj7).

Found in the ray end experiment (ter-pdw8, take group 1 and interim 4, base 3549d3283, before Design X). In a still of the lamp hall at 1072 x 603, draw 630, 51,304 of 634,438 surface pixels (8.1 %) hold a diffuse history of 8 to 23 draws (median 18) against the frame's median of 32, the cap. Only 0.14 % of them fail the surface test. They are scattered at random over every open surface, densest on the near floor (16 % of the middle floor), and not on silhouettes.

## The writer, by a direct test
Step 4 of accumulate_spatial.comp clamps the slow history into the box of the 3 x 3 fast histories, and then the anti-lag shortens the history by the size of the clamp's move:

    historyLength = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));

where lag is the luminance of the clamp's move of the slow history over the mean of the 3 x 3 fast histories, parameters.antiLagShare is the move at which the length falls to the fast cap, and fastCap is the fast history's largest length (4). In a build copy that skips this line for the diffuse signal only (parameters.light != DiffuseLight), the pixels with a short history fell from 51,304 (8.1 %) to 174 (0.027 %), and on the middle floor from 16 % to 0. A median of 18 against 32 fits a cut to the fast cap followed by growth of 1 per draw. The light-change age is compiled out of the diffuse accumulator on this base and changed the map by nothing.

## Why it is one root with ter-18hc
A rare bright sample moves the fast histories' box or pulls one fast history up. The clamp then moves the slow history. ter-18hc records the light that this move loses. This ticket records the second effect of the same event: the anti-lag reads the move as a change of the lighting and cuts the history's length, in a still where nothing changed. The anti-lag cannot tell a noise sample from a real change. The ray census (ter-8pco) names the samples: far hits and misses carry 76 to 100 % of the variance.

## What it costs, as far as measured
- A pixel with a short history blends each new sample with a larger weight, so it is noisier until it regrows. The cost in grain and boil is NOT measured: the test build was read for the share map only.
- Any rule that reads the history's length as a witness of 'new surface' reads these pixels wrong. S2 of ter-pdw8 lost 1.1 to 1.3 % of the level that way, and S2' reads the surface test alone for that reason.

## Not measured
- The size of lag at the cut, and whether a far hit or a guided sample moved the box. The accumulation trace exists only for the specular signal: TemporalSignal.cs:106-107 gives every other signal HistoryInstrumentation.None, and the trace variant compiles with TERTIUS_PRECISE_SPECULAR_HISTORY, which the diffuse accumulator does not use. The measure needs the trace on the diffuse signal.
- The same count on main after Design X. Design X changes the clamp's variance and adds a region drift test, whose false fires also shorten histories in about a fifth of a still's picture (the orchestrator, 2026-11-23). Two rules that shorten histories on noise is the pattern to see whole.

## To do
No fix is designed, and none is built without the orchestrator's word. First steps, in order: 1. count the short histories of a still on main after Design X, by writer (the anti-lag, the region drift test). 2. Measure what the anti-lag's cuts cost in grain and boil of a still (the build copy without the line, against main), and what the anti-lag buys after a lamp switch (draws to settle). 3. Then design: a witness of a real change that a single bright sample does not trigger.

Evidence: the share maps sh1072-share2-map.png (8.1 %) and sh1072-share2-map-no-antilag-s2prime.png in /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a-evidence/rayend/pics/; the record of ter-pdw8 under docs/research/ takes them over.


## Step 1 done: the count by writer on main after Design X (2026-11-23)
Record: docs/research/t1-g1jd-short-histories-by-writer/README.md. Main ae4364022 with the probe, four builds (main, region evidence off, the diffuse anti-lag off, both off), frozen stills with the history kept through 28 captures, two runs each. Shares of the surface pixels (L above 0):

| View, dump draw | Main: 8 to 23 | Main: under 31.5 | Region evidence alone | Anti-lag alone | Overlap | Neither |
|---|---|---|---|---|---|---|
| Lamp hall 1072 x 603, 630 | 9.56 % | 10.95 % | 10.93 % | 0 | 0 | 0.03 % |
| Still cabin 2/3, 190 | 16.56 % | 21.13 % | 21.10 % | 0 | 0 | 0.04 % |
| Bay 2/3, 630 | 19.42 % | 29.33 % | 29.33 % | 0 | 0 | 0 |

- On main, the short histories come from region evidence: whole region blocks whose drift test fires in a frozen world. The anti-lag shortens none at the dump draw: its map equals main's bit for bit in the lamp hall and the bay, and differs at 2 pixels by 0.03 draws at most in the cabin. Its captures still differ from main's, so it moves the light at other draws.
- The title of this ticket held on base 3549d3283. It does not hold on main.
- Each build's two runs are equal bit for bit in every view (spread 0).
- Grain (blotch score) with region evidence off: lamp hall 0.0053 to 0.0049, bay 0.0217 to 0.0190, cabin 0.0065 to 0.0112 (not explained). With the anti-lag off, the bay's specks rose from 0.0248 to 0.0297.
- Not measured: the reflections' history (needs a new dump patch).
