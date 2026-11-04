# Screen tracer room under a 12.0 ms GPU target

A read-only researcher (GPT 6.1 SOL) wrote this report on 2026-11-04 for `ter-gayt`, as `/private/tmp/trace-budget-12ms.md`. The design update `ter-jxjx` kept its content as the source of section 11 of [temporal-renderer.md](../../plans/temporal-renderer.md). It changed only the punctuation and a few words to the project's writing rules. The source keys name files outside the repository, such as `/private/tmp/9nrc/runs/`, which can be gone. Their line numbers refer to the files as they were on 2026-11-04.

Read-only research, November 4, 2026. No repository or worktree file changed. No build, test, or GPU work ran.

## Result

The two trace dispatches get an estimated 2.94 to 3.76 ms under the requested 12.0 ms frame target. Each dispatch executes a GPU shader for reflection or diffuse rays. An allowance reserves time for work that is not measured. This estimate keeps the remaining allowances from section 11 of the design. At the unchanged controller limit of 15.0 ms, they get an estimated 5.94 to 6.76 ms. The frame before either trace dispatch costs an estimated 8.24 to 9.06 ms. Sources: the calculations in sections 3 and 4 below, using R, G, P, L, and S.

There is also a narrower calculation. Replacing the old passes and adding only FSR, output-size work, the plan, the resolve, and the finest depth level leaves 3.87 to 4.16 ms. That calculation does not pay for the new indirect accumulations, corrected environment reads, or the opaque-image copy. It is a replacement-only ceiling, not the complete design's allowance. Sources: section 3, rows A through H.

At 10% own rays, V6 with a diffuse cap of 62 steps costs 3.900 ms on the deck. It does not fit the complete design's nominal range. An estimated 74% to 97% of that workload fits. It is borderline against the replacement-only ceiling. Source: Q, row `deck | 10 % | v6-d62 | sum`, and section 5's calculation.

These are planning ranges, not measured bounds. In particular, the raw 9nrc files do not support the brief's statement that their clocks isolate each timed span. Their summary scripts use the active-time mean over the whole process. Section 2 explains this source discrepancy. The conversion's empirical error is about 15% either way, before this extra clock uncertainty. Sources: U:1 and G:232.

The 12.0 ms target does not change the controller's runtime limit. The requested limits and image sizes come from the task brief. The design's existing controller target is 15.0 ms. Source: P:478 and P:1134.

## 1. Terms and source keys

A frame span measures elapsed GPU time for a frame. A pass is one stage of GPU work. A dispatch runs one compute shader. All times below use milliseconds, written as ms. A nanosecond, written as ns, is one millionth of a millisecond. MHz means millions of clock cycles per second.

The render size is the image size before upscaling. The output size is the final image size. This report uses 1280 × 720 rendering and 1920 × 1080 output, as requested. FSR 3.1 performs the upscaling. A block ray serves one 2 × 2 pixel block. An own ray serves one additional pixel. A walk advances a ray through screen depth. A step cap limits the number of advances. Sources: task brief, P:1136, P:1161, and L:263.

An allowance reserves time for work that is not measured. A nominal range combines observed run ranges and stated allowances. It does not include the conversion's empirical error. A margin is the dispatch allowance minus the dispatch time. A positive margin means time remains.

| Key | File | Relevant lines or table rows |
|---|---|---|
| F | `/private/tmp/trace-budget-facts.md` | Read in full. Original subtotal and reserves: 75–89. Remaining allowances and limits: 49–73 and 185–208. |
| R | `/private/tmp/9nrc/runs/` | Raw `gpu-timings.json` and matching `.log` for each view, size, round, and build. Detailed use follows below. |
| N | `/private/tmp/9nrc/fix/neighbours.txt` | Deck branch: 3 and 7. Hall branch: 11 and 15. Base comparison: 1, 5, 9, and 13. |
| NM | `/private/tmp/9nrc/fix/neighbours.py` | Group definition: 1–4 and 19–25. |
| U | `/private/tmp/9nrc/tool/summary.py` | Clock definition: 1–4. Clock extraction: 9–12. Per-run conversion: 23–30. |
| W | `/private/tmp/9nrc/windowed.txt` | Diagnostic single-pass rows only. Not used to price a removed or retained pass alone. |
| G | `/Users/alexispurslane/Development/tertius/docs/research/temporal-renderer/gpu-calibration.md` | Conversion: 206–232. FSR: 276–289. Remaining design allowances: 381–390. Limits: 439–450. |
| P | `/Users/alexispurslane/Development/tertius/docs/plans/temporal-renderer.md` | Pass changes: 232–260. Section 11: 1132–1163. Measurement method: 1167–1171. |
| M | `/Users/alexispurslane/Development/tertius/docs/research/temporal-renderer/pass-map.md` | Timestamp warning: 17. Removed passes: 30–39. Output passes: 43–49. |
| L | `/Users/alexispurslane/Development/tertius/docs/research/temporal-renderer/lean-trace-spike.md` | Variant definitions: 89–98. Plan and resolve: 146–155. Ray counts: 261–267. |
| S | `/Users/alexispurslane/.t3/worktrees/tertius/ter-sftw-profiling-spike/docs/research/temporal-renderer/profiling-spike.md` | Top-clock sums: 48–85. Cap quality: 195–235. Split-pass costs: 339–369. Limits: 586–599. Recommendation: 645–681. |
| Q | `/Users/alexispurslane/.t3/worktrees/tertius/ter-sftw-profiling-spike/captures/sftw/top/review-table.md` | Measured paired V6-at-62 sums: rows 14, 26, 38, and 50. |
| T9 | `/Users/alexispurslane/Development/tertius/.tickets/ter-9nrc.md` | Removal: 69. Measurement method: 73. Revised times and hall explanation: 74–78. |
| TG | `/Users/alexispurslane/Development/tertius/.tickets/ter-gayt.md` | Earlier budget note: 22. It predates this recalculation. |
| TB | `/Users/alexispurslane/Development/tertius/.tickets/ter-bqun.md` | Runtime light cap and its acceptance: 15. |
| TM | `/Users/alexispurslane/Development/tertius/.tickets/ter-ohqr.md` | Already-built kept positions, motion target, and surface test: 28–34. |

The raw run name is `<view>-<size>-r<round>-<build>`. Each JSON file sits inside a directory with that name. Its matching clock log is beside that directory. The branch rows describe the renderer after sampling and direct accumulation are removed. The base rows use the former sampling path. Sources: R, `/private/tmp/9nrc/runs.txt`, and T9:69–78.

## 2. Method and the clock discrepancy

G:211 gives the conversion exactly:

```text
windowed time = measured time × the run's clock / 1296 MHz
```

Here, windowed time is the estimated duration at the requested GPU clock. Measured time is the source duration. The run's clock is the frequency associated with that duration. The denominator is the GPU-limited window clock from G:150–204.

I convert every run separately, then average or take the range. I do not multiply the mean duration by the mean clock. The latter loses the relation between a run's clock and its duration. Source: NM:19–25 and U:23–30.

The updated runs replace the earlier assumed headless clock of 800 MHz. However, their recorded clock is not a clean timed-span clock:

- U:1–4 explicitly calls it the GPU-active-time mean over the whole run.
- U:9–12 and NM:8–11 read the final `run seconds=... active_ms=... mhz=...` line.
- The inspected R logs contain no `timed-start` or `timed-end` markers. G:214 requires those markers to isolate a timed span.

For example, `R/deck-1280x720-r0-branch.log:67` reports 785.1 MHz over the process's active GPU work. Lines 16–32 include an initial high-clock period and a later low-clock period. The timing JSON has no clock field. Therefore I reproduce the published per-run conversions, but label them estimates. I do not relabel them as measurements at 1296 MHz.

MoltenVK translates Vulkan work to Metal on this machine. A timestamp can latch late and include work from the next pass. Therefore I subtract groups of adjacent passes, not isolated direct-light, composite, or occlusion-sector times. Sources: M:17, NM:1–4, and `R/deck-1280x720-r0-branch.log:63`.

This grouping reduces attribution errors. It does not prove the exact savings after removal. Work can move across a group's boundary, and deleting passes can change overlap. A measured frame with the old groups disabled is still needed.

## 3. Deck frame before the two trace dispatches

### What the measured frame contains

The 9nrc branch runs have no FSR pass. Their width and height are equal to their render and output sizes. They still contain the old occlusion passes, old reflection passes, and SMAA, the old anti-aliasing stage. Source: `R/deck-1280x720-r0-branch/gpu-timings.json:3`, pass rows at 76–106, 125–141, and 167–213. The other branch rounds have the same pass names.

The conversion starts from the measured frame span. It keeps shadows, environment work, deformation, the motion-enabled G-buffer and the surface test. It also keeps the existing depth pyramid, exact direct light, composite, scattering, haze, transparent rendering, and exposure. It also keeps whatever frame gaps the span contains. I do not rebuild this retained work from isolated pass costs. Sources: the complete R pass lists, P:232–260, and T9:69–78.

The new motion target, kept deformed positions, and surface test already exist in this baseline. Their old section-11 allowances, added again, count them twice. Sources: TM:28–34 and T9:73–78.

### Input ledger

Every value in the converted column is an estimate unless it is explicitly called an allowance. Ranges cover the branch rounds, not the conversion error. Each raw R source below uses rounds r0 through r4.

| Row | Part | Source duration or allowance, ms | Clock evidence | Cost used at 1296 MHz, ms | Source and treatment |
|---|---|---:|---|---:|---|
| A | Measured whole frame at 1280 × 720 after 9nrc | Mean 11.3325, range 10.6972–11.8197 | Recorded whole-run active-time clock: mean 813.78 MHz, range 770.5–887.0 | Mean 7.1039, range 7.0270–7.3213 | N:3, `/private/tmp/9nrc/summary.md:7`, and R's `frame.meanMs`. Converted per run. |
| B | Remove the four old occlusion passes | Group mean 2.2959, range 2.1400–2.4307 | Same run clock as A | Subtract mean 1.4386, range 1.4230–1.4647 | P:243 and M:30–33. R pass rows `occlusion depth`, `occlusion sectors`, `occlusion denoise`, `occlusion upsample`. Example: deck-720 r0 JSON:76–99. Converted adjacent-group sum. |
| C | Remove the old reflection group | Group mean 0.6970, range 0.6785–0.7128 | Same run clock as A | Subtract mean 0.4373, range 0.4238–0.4644 | P:246–247 and M:37–39. R rows `radiance pyramid`, `reflection trace`, `reflections`. Example: deck-720 r0 JSON:125–141. Converted adjacent-group sum. |
| D | Remove all old work from bloom through capture at 720p | Group mean 0.4382, range 0.4183–0.4592 | Same run clock as A | Subtract mean 0.2748, range 0.2706–0.2863 | R rows `bloom` through `capture`, including all SMAA passes. Example: deck-720 r0 JSON:167–213. The retained output-size passes are added back in F. |
| E | Add FSR 3.1 from 720p to 1080p | Batched measured median 1.991, run medians 1.975–2.085 | Recorded timed-span clock: mean 1257 MHz, range 1241–1272 | Add 1.9311–1.9910 | G:283–287 and G:387. Lower endpoint is 1.991 × 1257 / 1296. Upper endpoint retains the measured near-top-clock median. This practical bracket is not the full run range. |
| F | Add bloom, tone map, outlines, and capture at 1920 × 1080 | Group mean 0.4789, range 0.4749–0.4832 | Recorded clock of each deck-1080 branch run: mean 1087.78 MHz, range 1076.1–1102.9 | Add mean 0.4019, range 0.3980–0.4066 | R/deck-1920x1080-r0..r4-branch, named post-pass rows. P:251–255 sets their output size. Sum the full terminal group, then subtract the adjacent three-pass SMAA group. Converted estimate. |
| G | Add the ray plan and the resolve | Allowance 0.300 for each | No measured final clock or final implementation | Add 0.6000 | L:146–151 and F:89. These remain planning allowances. I do not convert an allowance like a measurement. |
| H | Add V6's finest depth level | Measured 0.024–0.025 at the first spike's unrecorded clock | Unknown original clock | Reserve 0.0240–0.0250 | S:675 and S:377. This is an allowance using the unconverted source range, not a measured windowed cost. I do not reuse F's 0.015 ms estimate based on 800 MHz. |

The raw frame mean and range in A are calculated from the five JSON files. N and the summary round them to 11.33, 10.70, and 11.82 ms. All later arithmetic uses the JSON values.

The output-size work in F is taken from the new 9nrc measurements, not from the older assumed 1050 MHz population in M. FSR is not in those measurements, so E comes from its separate calibration run. This avoids charging both the 720p and 1080p post-processing work. Sources: R pass lists, G:283, and P:251–255.

SMAA is deleted, not moved. At 720p, its adjacent three-pass sum is an estimated 0.0690–0.0756 ms after conversion. At 1080p, it is 0.1331–0.1384 ms. D removes the former. F does not add the latter. Sources: R, deck branch, named SMAA rows, and P:253.

### Preserve each run's subtraction

The next table subtracts B, C, and D from A within each 720p run. Its final column is the retained frame before FSR and tracer additions. Every number is calculated from the matching R JSON and recorded clock.

| Deck round | Recorded clock, MHz | Converted frame, ms | Converted occlusion group, ms | Converted reflection group, ms | Converted terminal group, ms | Retained frame, ms | Clock source |
|---|---:|---:|---:|---:|---:|---:|---|
| r0 | 785.1 | 7.0457 | 1.4350 | 0.4303 | 0.2706 | 4.9098 | `R/deck-1280x720-r0-branch.log:67` |
| r1 | 770.5 | 7.0270 | 1.4451 | 0.4238 | 0.2730 | 4.8851 | `R/deck-1280x720-r1-branch.log:63` |
| r2 | 820.3 | 7.0609 | 1.4253 | 0.4344 | 0.2718 | 4.9295 | `R/deck-1280x720-r2-branch.log:63` |
| r3 | 806.0 | 7.0646 | 1.4230 | 0.4335 | 0.2721 | 4.9359 | `R/deck-1280x720-r3-branch.log:63` |
| r4 | 887.0 | 7.3213 | 1.4647 | 0.4644 | 0.2863 | 5.1059 | `R/deck-1280x720-r4-branch.log:62` |

The retained frame has a mean of 4.9533 ms and a range of 4.8851–5.1059 ms. This is a subtraction estimate. It is not a measurement with those passes absent.

Adding E, F, G, and H gives the replacement-only frame of 7.8382–8.1285 ms. The lower endpoint adds the lower retained-frame, FSR, output-group, and finest-level values. The upper endpoint adds their upper values. These inputs are separate experiments, so this is an envelope, not one observed frame. Source: the preceding ledger and per-run table.

### Complete the remaining section-11 work

The earlier F report reserves more than the tracer replacement itself. The removal of direct-light sampling does not remove those costs. I retain them for the complete-design result.

| Row | Remaining work | Additional windowed cost, ms | Status and source |
|---|---|---:|---|
| I | Indirect diffuse and specular accumulation | 0.2000–0.4000 | Unmeasured allowance, not the deleted direct accumulation. P:1149, P:258, and G:381. It combines new indirect samples with earlier frames. |
| J | Corrected environment reads | 0.1620–0.4861 | Estimate: P:1150's 0.2–0.6 ms × 1050/1296. G:382 gives 0.16–0.49 ms. The spike includes plain environment reads, not this corrected search. L:146 and L:155. The clock assumption is historical, not newly measured. |
| K | Copy of the opaque-only image before transparent rendering | 0.04195 | Estimate, not an isolated measured copy. P:1153 reserves 0.61 ms for transparent rendering plus the copy. M:41 gives 1.256 ms for transparent rendering alone at 1080p. Their implied incremental allowance is (0.61 − 1.256 / 2.25) × 1050/1296. P:249 requires the copy. |

The factor 2.25 is the ratio of the two pixel counts. Its use in K follows P:1136 and P:1163. K isolates only a rough incremental copy allowance from the old rounded budget. It does not replace the measured transparent cost that is already retained in A. A measurement can replace this estimate.

I through K add an estimated 0.4040–0.9281 ms. Thus the complete-design frame before the two dispatches is 8.2422–9.0566 ms. Source: rows I through K added to the replacement-only range.

This does not prove that all unbuilt code fits those reserves. In particular, I is still a guess. Extra hit-history reads in the final tracer also lack a measured incremental cost. Source: P:1149, G:381, and L:155.

## 4. Room under the two limits

Subtract the frame before the dispatches from the selected frame limit. The largest frame cost gives the smallest allowance. All values below are calculated from section 3.

| Scope | Frame before either dispatch, ms | Dispatch room under 12.0 ms, ms | Dispatch room under 15.0 ms, ms |
|---|---:|---:|---:|
| Replacement-only calculation, rows A through H | 7.838–8.129 | 3.871–4.162 | 6.871–7.162 |
| Complete design, also rows I through K | 8.242–9.057 | 2.943–3.758 | 5.943–6.758 |

The old 1.1 ms dispatch figure is not an input to either result. L:151 derives it from a guessed 1.7 ms whole-tracer allowance minus the plan and resolve. This report derives the remaining time from the updated frame instead.

## 5. Walk configurations against the room

### What the timed configurations mean

V1 uses one nearest depth per cell. V6 uses near and far depth to skip empty depth regions. Both retain the hit-facing test. V1 at 64 and V6 at 64 have a reflection cap of 256 steps and a diffuse cap of 64 steps. V6 at 62 and V6 at 54 change only the diffuse cap. Sources: L:89–94, S:17–18, S:72, and S:197.

V5c is a different, biased configuration. It includes lower caps of 96 reflection steps and 48 diffuse steps, coarser terminal depth levels, and the vote exit. When few lanes still walk, the vote exit stops some remaining rough rays. Its time belongs to that entire configuration, not to the vote alone. Sources: L:94–98.

S:48 times each ray kind repeated 20 times inside its pass, then divides the pass time by 20. The table uses the resulting time of one reflection dispatch plus one diffuse dispatch. It does not add the split-mode setup or shading costs. Those costs do not add to the whole trace, as S:369 explains.

The top-clock measurements already run near the requested clock. I apply no 800 MHz factor and no further 1.3× correction. S:48–63 records span clocks near the top state and 1295–1296 MHz after the initial climb. S:76 records the same near-top state for V5c. S:222 records it for V6 at 62.

### Fit rule

The main comparison uses the complete-design allowance of 2.9434–3.7578 ms. The replacement-only column uses 3.8715–4.1618 ms. Both allowances come from the deck. Applying them to floor trace times compares walk costs, not a measured complete floor frame. No complete post-9nrc floor baseline is present in R.

For each margin range, the lower endpoint subtracts the slowest kept trace from the smallest allowance. The upper endpoint subtracts the fastest kept trace from the largest allowance. If both endpoints are nonnegative, a configuration fits. A conditional fit crosses zero. A no-fit range stays negative. These labels describe nominal arithmetic only, not a guaranteed measured result.

For ray reduction, I divide the allowance by the measured full dispatch time, then cap the result at 100%. Both kinds lose the same share of their rays. This uses the spike's average cost per ray and assumes time scales with ray count. It is an estimate, not a measured reduced-ray run. Fixed dispatch work and changes in lane use can break that assumption.

### Deck

All times and margins are ms. The source column identifies each measured paired sum. The remaining columns are calculated from that sum and section 4.

| Walk | Own rays | Top-clock dispatch sum, median [kept range] | Complete-design margin at 12 ms | Complete-design fit | Ray workload that fits, estimate | Replacement-only margin at 12 ms | Replacement-only fit | Source |
|---|---:|---:|---:|---|---:|---:|---|---|
| V1 at 64 | 10% | 4.571 [4.563–4.577] | −1.634 to −0.805 | No | 64.3–82.4% | −0.706 to −0.401 | No | S:52 |
| V6 at 64 | 10% | 3.925 [3.920–3.928] | −0.985 to −0.162 | No | 74.9–95.9% | −0.057 to +0.242 | Conditional | S:53 |
| V6 at 62 | 10% | 3.900 [3.891–3.969] | −1.026 to −0.133 | No | 74.2–96.6% | −0.098 to +0.271 | Conditional | Q:14 |
| V6 at 54 | 10% | 3.779 [3.775–3.786] | −0.843 to −0.017 | No, near the optimistic end | 77.7–99.5% | +0.085 to +0.387 | Yes | S:54 |
| V5c | 10% | 2.069 [2.066–2.099] | +0.844 to +1.692 | Yes | 100% | +1.772 to +2.096 | Yes | S:80 |
| V1 at 64 | 25% | 6.381 [6.369–6.392] | −3.449 to −2.611 | No | 46.0–59.0% | −2.521 to −2.207 | No | S:55 |
| V6 at 64 | 25% | 5.384 [5.372–5.409] | −2.466 to −1.614 | No | 54.4–70.0% | −1.538 to −1.210 | No | S:56 |
| V6 at 62 | 25% | 5.342 [5.331–5.359] | −2.416 to −1.573 | No | 54.9–70.5% | −1.488 to −1.169 | No | Q:26 |
| V6 at 54 | 25% | 5.173 [5.169–5.180] | −2.237 to −1.411 | No | 56.8–72.7% | −1.309 to −1.007 | No | S:57 |
| V5c | 25% | 2.874 [2.871–2.884] | +0.059 to +0.887 | Yes, little worst-end margin | 100% | +0.987 to +1.291 | Yes | S:81 |

### Floor, using the deck's frame allowance

| Walk | Own rays | Top-clock dispatch sum, median [kept range] | Complete-design margin at 12 ms | Complete-design fit | Ray workload that fits, estimate | Replacement-only margin at 12 ms | Replacement-only fit | Source |
|---|---:|---:|---:|---|---:|---:|---|---|
| V1 at 64 | 10% | 4.415 [4.409–4.484] | −1.541 to −0.651 | No | 65.6–85.2% | −0.613 to −0.247 | No | S:58 |
| V6 at 64 | 10% | 3.498 [3.487–3.519] | −0.576 to +0.271 | Conditional | 83.6–100% | +0.352 to +0.675 | Yes | S:59 |
| V6 at 62 | 10% | 3.469 [3.458–3.483] | −0.540 to +0.300 | Conditional | 84.5–100% | +0.388 to +0.704 | Yes | Q:38 |
| V6 at 54 | 10% | 3.346 [3.335–3.361] | −0.418 to +0.423 | Conditional | 87.6–100% | +0.510 to +0.827 | Yes | S:60 |
| V5c | 10% | 2.238 [2.234–2.242] | +0.701 to +1.524 | Yes | 100% | +1.629 to +1.928 | Yes | S:82 |
| V1 at 64 | 25% | 6.098 [6.084–6.103] | −3.160 to −2.326 | No | 48.2–61.8% | −2.232 to −1.922 | No | S:61 |
| V6 at 64 | 25% | 4.767 [4.759–4.830] | −1.887 to −1.001 | No | 60.9–79.0% | −0.959 to −0.597 | No | S:62 |
| V6 at 62 | 25% | 4.714 [4.705–4.738] | −1.795 to −0.947 | No | 62.1–79.9% | −0.867 to −0.543 | No | Q:50 |
| V6 at 54 | 25% | 4.538 [4.529–4.542] | −1.599 to −0.771 | No | 64.8–83.0% | −0.671 to −0.367 | No | S:63 |
| V5c | 25% | 3.103 [3.093–3.119] | −0.176 to +0.665 | Conditional | 94.4–100% | +0.752 to +1.069 | Yes | S:83 |

The V6-at-62 rows use Q's measured sum from the same runs. I do not add its diffuse median to a reflection median from another batch. S:675 rounds those same sums to 3.90, 5.34, 3.47, and 4.71 ms.

### Relation to the cost per ray

At 10% own rays, S:677 reports V6-at-64 costs of 8.9 ns per reflection ray and 4.0 ns per diffuse ray on the deck. It reports 6.7 ns and 4.2 ns on the floor. L:263 gives 216,798 block rays per kind on the deck and 230,400 on the floor. Its random own-ray rule gives roughly 1.4 times those counts at 10% own rays and 2.0 times them at 25%. This count estimate is not a measured retained-ray count.

For the deck at 10%, the complete-design allowance divided by the measured V6-at-64 sum gives 74.9–95.9%. Thus an estimated quarter of that workload must go at the small-allowance end. This differs from the old report's roughly 35% that fit 1.1 ms, because neither that allowance nor its 800 MHz conversion is retained. Sources: S:53, S:677, F:149–167, and the deck table above.

### Image cost is a separate decision

Tile bias is the average signed brightness error over image tiles. More samples do not remove a systematic bias.

| Walk | Deck diffuse tile bias | Floor diffuse tile bias | Meaning | Source |
|---|---:|---:|---|---|
| V1 at 64 | −9.5% | +0.6% | Reference quality for this comparison, not ground truth | S:201 |
| V6 at 64 | −7.1% | +0.7% | Better deck average than V1 | S:202 |
| V6 at 62 | −7.5% | +0.8% | Within the floor's measured noise relative to V1 | S:203 and S:219–220 |
| V6 at 54 | −9.5% | +1.4% | Matches V1 on the deck only | S:207 and S:219 |
| V5c | −11.6% | +4.2% | The earlier spike's converged diffuse bias of the whole configuration | S:251 and S:260 |

V5c's reflection tile bias is −20.4% on the deck and +7.8% on the floor. Sources: S:251 and S:260. A time fit does not imply the same image quality. S:599 also limits the cap comparison to these two views and this one tile measure.

At the unchanged 15.0 ms limit, the complete-design allowance is 5.943–6.758 ms. All measured V6 and V5c rows fit that nominal allowance. V1 at 25% is conditional for both views, since its kept range exceeds the lower allowance but stays below the upper allowance. Sources: section 4 and the two timing tables.

## 6. The 64-lamp hall

### Measured frame after 9nrc

The hall is a stress view because each pixel now shades all 64 shadowed lamps. Their reach covers the hall, so a spatial light list cannot remove them there. Sources: T9:78 and TB:15.

The next table describes native-size frames before tracer replacement and without FSR. These are not the requested mixed-resolution final frame.

| Hall image size | Branch raw frame span, mean [range], ms | Recorded clock, mean [range], MHz | Converted branch frame, mean [range], ms | Converted base frame, mean [range], ms | Mean change, ms | Source |
|---|---:|---:|---:|---:|---:|---|
| 1280 × 720 | 9.916 [9.405–10.273] | 903.1 [865.1–965.4] | 6.902 [6.844–7.006] | 5.92 [5.90–5.95] | About +0.98 | R hall-720 branch `frame.meanMs` and final clock lines, N:9 and N:11, summary:21 and summary:23 |
| 1920 × 1080 | 14.409 [13.217–15.347] | 1117.8 [1026.4–1233.9] | 12.380 [12.154–12.584] | 10.43 [10.25–10.59] | About +1.95 | R hall-1080 branch `frame.meanMs` and final clock lines, N:13 and N:15, summary:29 and summary:31 |

Those mean increases reproduce the ticket's approximately 1.0 and 2.0 ms increase. They are whole-frame comparisons after conversion, not prices for the exact light loop alone. Source: T9:78.

### Hall at the requested mixed resolution

I apply the same replacement calculation to the hall's own raw runs. Its output-size post group comes from the hall's native-1080p runs, not from the deck. I reuse the same FSR bracket and unmeasured allowances. Their hall-specific values are not measured.

| Part | Cost at 1296 MHz, ms | Status and source |
|---|---:|---|
| Whole 720p frame | 6.8436–7.0057 | Converted from R hall-1280x720 r0..r4 branch, N:11 |
| Remove occlusion group | Subtract 1.0832–1.1251 | Converted adjacent-group sum from the same R runs, rows named in B |
| Remove reflection group | Subtract 0.3945–0.4258 | Converted adjacent-group sum from the same R runs, rows named in C |
| Remove old terminal group | Subtract 0.2677–0.2834 | Converted adjacent-group sum from the same R runs, rows named in D |
| Retained 720p frame after per-run subtraction | 5.0839–5.1715, mean 5.1204 | Calculated within each matching hall run, as for the deck |
| Add 1080p bloom, tone map, outlines, and capture | 0.3116–0.3395, mean 0.3240 | Converted group from R hall-1920x1080 r0..r4 branch, same terminal-minus-SMAA method as F |
| Add FSR | 1.9311–1.9910 | Reused estimate from E, not a hall FSR measurement |
| Add plan, resolve, and finest level | 0.6240–0.6250 | Same allowances as G and H, not hall measurements |
| Replacement-only frame before the dispatches | 7.9506–8.1270 | Calculated from the preceding rows |
| Add remaining design work | 0.4040–0.9281 | Same allowances as I through K, not hall measurements |
| Complete-design frame before the dispatches | 8.3546–9.0551 | Calculated from the preceding rows |

| Hall scope | Room under 12.0 ms, ms | Room under 15.0 ms, ms | Source |
|---|---:|---:|---|
| Replacement-only | 3.8730–4.0494 | 6.8730–7.0494 | Subtract the hall replacement-only frame from each target |
| Complete design | 2.9449–3.6454 | 5.9449–6.6454 | Subtract the hall complete-design frame from each target |

The hall's direct-light neighbourhood is more expensive, but its other work is cheaper than the deck's. Therefore its complete frame is not the deck frame plus the hall's old-to-new increase. The requested final-frame rooms overlap. Sources: N:3 and N:11, the deck per-run table, and the hall calculation above.

No top-clock hall trace time is supplied. Therefore the remaining room is known only as this estimate. Whether any walk configuration fits the hall requires a hall trace measurement.

### Can the light cap recover the deck's room?

TB:15 defines a cap on the lights kept by the main view. It affects every main-view consumer, while environment views keep separate light membership. It is not a cap on only the direct shader.

The supplied data does not identify a measured cap. It has one hall light count and no same-scene sweep of kept light counts. That point cannot separate the cost added by each light from the cost independent of light count.

An illustrative estimate is possible, but it is not a default recommendation. The mean retained frame plus output post group is 5.3552 ms on the deck and 5.4444 ms in the hall. Their difference is about 0.0892 ms. Sources: the deck retained mean 4.9533 plus output mean 0.4019, and hall means 5.1204 plus 0.3240 above.

The hall's converted adjacent neighbourhood is 4.7683 ms on average. Subtracting its old occlusion group of 1.1021 ms leaves 3.6663 ms for the retained depth/direct/composite neighbourhood. Sources: R hall-720 r0..r4 branch, the NM-defined neighbourhood, N:11, and the hall occlusion group above.

If all 3.6663 ms scaled linearly with the 64 lamps, the estimate is 64 × (1 − 0.0892 / 3.6663) = 62.4 lamps. Rounding down gives an illustrative cap of 62 lights for the mean complete-frame room. This is an estimate under a deliberately optimistic model. The neighbourhood includes fixed depth and composite work, so its actual per-light slope is smaller than this calculation assumes. Shadow changes and forward lighting can also change the slope. Sources: the preceding two paragraphs and TB:15.

Thus the data supports neither 62 nor another value as the runtime default. It also does not support equating a hall light-neighbourhood budget with a complete-frame budget. A quiet, same-view sweep of kept light counts is needed. Measure whole-frame spans and adjacent depth-through-composite sums at the target clock. Repeat with the final tracer, FSR, output size, and light fading enabled. That sweep can choose the cap for TB's actual acceptance condition.

## 7. Error, assumptions, and missing measurements

### Conversion and extrapolation errors

| Quantity | Error or limitation | What the nominal range includes | Source |
|---|---|---|---|
| Converted 9nrc frame and group costs | About ±15% empirical clock-rule error. Additional uncertainty because logs use whole-run active-time clocks rather than marked timed-span clocks. No source quantifies that additional error. | Observed per-run spread after each run's conversion only | G:227–232, U:1–4, R logs |
| FSR conversion | About ±15% empirical clock-rule error. Memory-bound work can scale less than the rule assumes. | The practical 1.931–1.991 ms bracket, not all run medians or all frame percentiles | G:225–232 and G:283–287 |
| Output post group | Same approximate ±15% conversion error. Boundaries around deleted SMAA can move timestamp attribution. | Spread across new 9nrc native-1080p runs | G:232, M:17, R branch post rows |
| Corrected-read reserve | Approximate ±15% conversion error plus an assumed historical clock of 1050 MHz, with comparable runs spanning 1022–1076 MHz. Its final search workload is unmeasured. | The old design workload allowance of 0.2–0.6 ms, converted at the central assumed clock | G:221, G:232, G:382, P:1150 |
| Opaque-image copy estimate | Same historical clock assumption and empirical error. Subtraction of rounded old budget values adds unquantified error. The historical clock range alone gives about 0.0408–0.0430 ms. | The central incremental allowance 0.04195 ms | G:221, P:1153, M:41, calculation in K |
| Plan, resolve, and indirect accumulations | Unmeasured final passes. There is no measured error bound on their allowances. | The stated reserves only | L:148–151, P:1149 |
| Finest depth level | Its source clock was not recorded. Carrying the unconverted 0.024–0.025 ms as a reserve is not a proved top-clock upper bound. | That source duration range, used as an allowance | S:377 and S:675 |
| Spike dispatch times | Kept-run spread is measured near the top clock. Repeating the same trace can retain more inputs in fast local memory than one real dispatch. The size of that effect is not measured. | The report's kept-run ranges, without rescaling | S:48–85, S:222–231, and S:596–597 |
| Reduced-ray shares | Linear time-per-ray model only. Fixed work and changes in how GPU lanes stay busy can change the result. | Full-cost range divided into the budget range | S:677 and section 5 |
| Floor comparison | No matching post-9nrc complete floor baseline | Deck frame allowance applied to measured floor trace times | R has only deck and hall runs, S has floor trace times |
| Hall cap | No same-scene light-count slope. The illustrative 62-light result assumes fixed work scales with lights. | No measured cap bound | R, N:11, and TB:15 |

As a sensitivity calculation, applying ±15% to all converted-cost terms gives a complete-design frame of approximately 7.13–10.26 ms. This leaves approximately 1.74–4.87 ms under 12.0 ms. The calculation leaves the unconverted planning allowances unchanged. It is not a confidence interval or a new bound. It still excludes the unquantified clock-span, pass-removal, final-shader, and repeated-trace cache errors. Sources: G:232 and section 3's component values.

The main 2.94–3.76 ms result is therefore more useful for allocating work than for accepting a final frame. A small positive margin, such as V5c's 0.059 ms worst-end deck margin at 25%, is much smaller than these errors. Source: deck fit table and G:232.

### Measurements that would close the gaps

| Missing number | Measurement needed | Why existing numbers do not give it |
|---|---|---|
| Exact retained frame after deleting old passes | Disable the four occlusion passes and three reflection passes. Measure the complete span at the requested clock, view, and sizes. | Timestamp group subtraction cannot reproduce changed overlap or moved boundaries. |
| Correct 9nrc timed-span clocks | Add `timed-start` and `timed-end` around the retained frame sequence, then use the marked active-time clock. | The current scripts use the final whole-process summary. |
| Integrated FSR and output post-processing cost | Measure one frame with 720p rendering, 1080p FSR output, and output-size bloom, tone map, outlines, and capture. | Current FSR and scene post-processing measurements come from separate experiments. |
| Final plan, resolve, and indirect accumulation costs | Time their neighbouring sums and the complete frame with the real history-based ray plan. | The plan is a stand-in. Resolve and final indirect work retain allowances. |
| Final hit and corrected-miss read costs | Measure the completed trace with emission, light mark, histories, and corrected environment reads, then compare whole-frame spans. | Plain-map spike shading does not perform all final reads. L:155 identifies this difference. |
| Top-clock finest-level cost | Measure it with adjacent retained work, or compare complete frames with and without it at a recorded top clock. | Its quoted isolated duration comes from the first spike's unrecorded clock. |
| Single-dispatch trace time without repeated-cache benefit | Keep the GPU at the top clock with representative frame work and run each real trace only once. | S:596 explicitly leaves the repetition effect unmeasured. |
| Floor and hall fit | Measure their complete final frames and hall trace dispatches at the same resolution pair. | Deck non-trace costs and two other trace views cannot establish hall or floor frame times. |
| Runtime light cap | Sweep the cap on the same hall scene, then repeat its stress acceptance with excess in-reach lights. | One 64-lamp result cannot determine fixed cost and marginal cost per retained light. |

All proposed measurements are follow-up work. None ran for this report.

## 8. Conclusion

Removing sampled direct light frees substantial deck time, but the old clock assumption also overstated the trace's speed. The current evidence gives a complete-design planning allowance of 2.94–3.76 ms for the two dispatches at the requested 12.0 ms target. Sources: sections 3–5.

At the design's full ray workload, V1 does not fit that nominal deck allowance. V6 at 64 or 62 also does not fit it on the deck. V6 at 54 nearly reaches its optimistic end but changes the floor's image quality. V5c fits on the deck, including at 25% own rays, with the documented bias and little conservative margin in the latter case. Sources: the deck fit table and image-cost table.

If only the immediate replacement is in scope, the larger 3.87–4.16 ms ceiling makes V6 at 64 or 62 borderline at 10% own rays. That result cannot fund the remaining design work a second time. Keeping those two scopes separate is necessary before the budget decision.
