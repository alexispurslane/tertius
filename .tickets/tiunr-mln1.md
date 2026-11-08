---
id: tiunr-mln1
status: in_progress
deps: []
links: []
created: 2026-11-06T18:33:09Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Set reflection-sharing footprint from roughness and ray-hit distance

User selected hypothesis H1: determine sharing footprint from roughness and hit distance, with roughness taking priority. Preserve all frozen 3x3/5x5 controls, original reference, source/material/history behavior and measurement workflow. Initial source inspection: screen_trace.glsl retains ray-origin-to-hit distance in meters; screen_trace_resolve.comp stores it in specularSample alpha; accumulate_temporal.comp copies it to current fast-history alpha. Slow-history alpha is history length. CompositePass currently binds only direct and slow-history images. Proposed minimal transport is a read-only binding of existing Specular.Sample(0), with no new retained distance image or trace. Shared 2x2 rays supply donor-ray distance, not an exact per-receiver distance; rough-signal handover and zero/miss semantics need explicit treatment. No new heavy command has started. Settle roughness-first bounded adjustment versus projected-cone footprint before implementation; retain actual output/noise/reference metrics for later validation.


## Notes

**2026-11-06T19:08:21Z**

User selected Design B. Bounded experiment: at most two Rendering Release builds (descriptor/API scaffold for behavior reds, then final), one final tool build and two focused test build/run cycles, all BPR=false/no-restore. Conditional two original smooth/rough sharing captures, 64 warm-up plus32 measured draws each, no control recaptures or light integration. Preserve all seals, source/control/history policies and old binaries. Cumulative evidence cap393216KiB under user storage authorization; >=5242880KiB disk guards. Proposed footprint is a planar projected GGX-cone proxy with near-mirror cap, both endpoint radii and retained 5x5 search bound. Unknown/saturated distance means no new sharing. No outcome-based retuning.

**2026-11-06T19:11:39Z**

Descriptor/API baseline completed normally: 35 focused cases, 10 intended assertion failures and25passes, zero errors. Baseline Rendering SHA2f1165f0ff116948c4cab7f00bf8b9b16895b020043cfd163bfaa7f74ae36844. Final footprint chosen before captures: planar normal-incidence cone slope2alpha/(1-alpha^2), alpha=flooredRoughness^2, finite-distance factor h/(h+viewDepth), native-pixel projection; radius capped by3*min(alpha/0.1^2,1) native pixel pitches. World-space separation must fit BOTH endpoint radii. The0.1 mirror-guard scale and3pixel pitch upper cap are reconstruction policy, not changed material roughness or a certified GGX support. The5x5 search/tap budget and previous angular weights stay fixed. All32 measured distance guides will be retained. No result-driven coefficient tuning.

**2026-11-06T19:13:14Z**

Reported final-build failure before any final tests/captures: glslc found the no-radius return guard in the bool cell-compatibility function instead of vec3 reflectionSharedAt. Failed source archive/logs retained under green/. Narrow source correction moves that guard only. Under the current user-directed experiment (allocation requests discontinued), amend the self-imposed build estimate to THREE Rendering attempts total: one successful scaffold, this failed final compile, one corrected final compile. Still one tool compilation and two test build/run cycles; no baseline rerun, policy retune or extra capture round. This is not a behavioral green.

**2026-11-06T19:14:45Z**

Final corrected build and focused checks are green:107/107, zero errors/skips. Rendering SHA8a53c2723380c9dfb50fb4699d9652cc643cff57c412c0c2eabe2cf2fd1d9d27; tool SHA9d91d076065aa42cc2ddf735bed1f0a001ca35953eb84ca32e376dea79cc53aa. Actual build attempts:3 Rendering (scaffold success, failed misplaced guard, corrected success),1 tool,2 tests-only. Normal original build-path binaries restored; all old seals/production/control hashes verify. Proceeding with exactly two native smooth/rough sharing captures,64+32draws each, no reference integrations or control recaptures.

**2026-11-06 final DesignB receipt**

Two of two original smooth/rough sharing cases completed validation:192original renderer draws,64measured draws. Same native1280x720,zerojitter,80x160crop,one reset,64warm-up+32measured draws per case. All unfiltered frames and decoded/classification/history/distance inputs match frozen controls. All576surrounding-copy pairs are unchanged, raw=plate+emitter for every scalar. No reference integration or control recapture. Old26analytic failures stay failed historical diagnostics.

One saved-data report preserves all prior OFF/ON/uncapped metric/temporal objects and reference-sensitivity objects exactly. Seven lossless gzip tables verify against original byte/hash receipts. Actual draw95and separate32-frame mean crops use identical exposure-6.8/Reinhard/sRGB mapping. The six material/reference/fixed5x5/DesignB crops were inspected. HTML comparison includes profiles, noise bars and fixed-basis accuracy observables.

Rough DesignB noise RMS32.6706 versus33.7151uncapped/20.0502fixed5x5 W/(m²sr):3.0981%less than uncapped but62.9441%more than fixed5x5. Rough persistent mean-error RMS35.5770 versus36.1510/30.7705:1.5877%less/15.6204%more. Rough missing5.5314%,extra10.9403%of reference crop light,outer missing8.3166%of fixed-band reference light. The new crop still shows strong speckling. Smooth plate arrays are byte-identical to uncapped: noise13.3464,mean-error52.0798,missing1.3155%,extra5.7877%. Smooth noise rises19.9826%against fixed5x5. Separate emitter/raw arrays differ and are not covered by plate-byte identity.

Latest guide distance is known for68166/399552rough and67107/399552smooth plate/frame samples (17.0606%/16.7956%); all remaining values are zero. The conservative both-endpoint gate therefore disables most exchange, despite nonzero accumulated light. Smooth near-mirror radius falls below one pixel pitch, so it adds no plate blur or smoothing. This exposes latest-stochastic-guide versus accumulated-signal mismatch, not a solved denoiser or a rejection of roughness/distance guidance in general. No post-result coefficient retune.

ARCHITECTURE.md, the tool README and research plan record current transport/formula/limitations and historical controls. The stage README records exact builds, failure, counts, quantities and hashes. All owned heavy processes ended and RELEASED2026-11-06T14:18:09-05:00; no future reservation. Later lightweight inspection verified1583prior-seal entries,26new frozen runtime identities and6source identities. Read-only seal checks preserve an overbroad raw-comparison difference and a mistaken gzip-path EOF before correct retained plate-byte identity. No new build/test/capture followed. No quality, physical-energy, performance or default acceptance. Ticket remains in_progress, no commit or integration claim. Evidence: /private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/reflection-footprint-b-20261106-1405/README.md.
