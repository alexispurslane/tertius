---
id: ter-b4ux
status: open
deps: []
links: []
created: 2026-11-09T22:18:27Z
type: bug
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
---
# The deck's GPU frame span rose from 13.4 ms to 30.1 ms since the build of 2026-11-05

Survey ter-6k3k, 2026-11-09, /private/tmp/tertius-survey-20261109/ab.log: scripts/temporal-upscale-timing.json at 1920x1080 output with FSR, Release, GPU clock 1296 MHz, alternating rounds in one GPU lock take. The build of 2026-11-05 (old-bin) gave a median span of 13.40 and 13.38 ms. Main gave 30.05 and 30.15 ms. The station-cabin trace on main (trace/station-cabin/passes.md) gives these times alone: diffuse filter 4.47 ms, reflection rays 3.82 ms, FSR 1.65 ms, diffuse rays 1.45 ms, specular accumulation 1.42 ms, direct light 0.77 ms. An Android emulator ran during some runs, but gpu-users.py shows that it takes no GPU time. Merges since 2026-11-05: 1560c7305 (reflected-light history rule, light-change age, tube light), 570d79288 (diffuse spatial filter), b1aee9b73 and b204dcc2e (rectangle light), d32fd30b4 (16 shadow taps), and smaller ones.

## Acceptance Criteria

Each pass's change in time between the old build and main is measured with GPU traces and attributed to the merge or commit that caused it. Each regression that comes from a plain mistake (wrong resolution, repeated work, a debug path left on, an unneeded full-screen pass) is fixed and merged. The rest are ranked with a plan to cut each one. The deck's span is measured again after the fixes.

## Notes

**2026-11-14, the next frame-time candidates**

The frame-time sweep stops for this week after the face that lights each texel once (t2-zcr4). This note ranks what it leaves, by gain for the risk. The source is docs/research/frame-time-sweep-2026-11-11/addendum.md, sections A12 to A14. All gains are for the deck on the M1 Max.

The deck's median frame span with the pass time stamps is 19.93 to 19.94 ms, in three traces of take 19. The budget is a mean of about 16.1 ms.

| # | Candidate | Gain (ms) | How sure | Effect on the image | Ticket and first step |
|---:|---|---:|---|---|---|
| 1 | One byte per pixel holds the same-surface answers of the 3 x 3 neighbors | 0.45 | A bound of 0.69, less an estimated 0.2 for the write | None | ter-yaqk. It waits for Design A of the mottle agent |
| 2 | A list of the lamps that reach an environment face | below 0.2 | Not sized. The loop of a real face costs about 0.2 ms | None with a conservative list | tz-frma. First count the lamps per face on the deck, with no build |
| 3 | The local shadow tiles of a frame in one render pass | up to 0.25 | Not measured: 13 render encoder breaks of about 21 µs | None | ter-jr0f, in the tile agent's files. Spike first |
| 4 | Fewer time stamps in a window | at most 0.05 | The measured price of a removed encoder | None | ter-wp7u |
| 5 | Compute passes next to each other in the frame's order | at most 0.05 | The measured price of a removed encoder | None | ter-jj53 |
| 6 | A face renders only the depth ranges that hold surfaces | 0.02 | Measured in take 19 | None: 32 of 32 stills are equal | No ticket. The edit script is edits/faceskip.py. Alone it does not pay for one more rule |

Row 3 has one caution. A spike that clears depth in the middle of a rendering meets the fault of ticket tz-na6d on the Mac.

Three larger cuts change the image, so Alexis decides them. They are rows 7 to 9 of the ranked table of section A12.

- The reflected place only below a roughness of 0.2 to 0.25: up to 0.7 ms, with a possible smear of rough reflections under camera motion.
- Bounce light accumulated and filtered once per 2 x 2 block: about 1.0 to 1.3 ms. Design ticket ter-ewua waits for Design A.
- A render scale of 0.59 in place of 0.667: about 3.5 ms, and a softer image. Declined for now.

Five ideas are measured and are no candidates.

- The main view's G-buffer pass renders only the depth ranges that hold something: no gain in take 19.
- One rendering for the depth ranges of a face or of the main view: wrong images on the Mac (tz-na6d).
- Buffer updates out of the middle of the frame: 0.00 to 0.06 ms (ter-n8bk, closed).
- The bloom as compute dispatches: no gain (ter-lm61, closed).
- The pyramids in fewer dispatches: no gain with the patched MoltenVK (te-r7nt).

No sum of the exact rows reaches the budget. The largest part lies outside this table: the direct light and the transparent pass paid 5.9 ms in section A12, and the tile agent owns them (t2-mlvy, t2-vsqh). The budget is a 95th percentile, so ticket ter-4iic also counts: young histories after a camera cut raise the deck's 95th percentile.
