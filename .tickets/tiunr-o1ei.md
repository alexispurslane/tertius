---
id: tiunr-o1ei
status: in_progress
deps: []
links: []
created: 2026-11-06T19:48:30Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Retain validated hit-distance support with reflected-light history

User selected temporal distance-history guidance after the latest-ray footprint experiment. Keep raw ray hit/miss data and all old controls immutable. Track distance moment and valid-hit support through exactly the accepted light-history reprojection/blending and existing short-history repair, with camera-cut/surface rejection. Derive representative hit distance and support for the unchanged footprint, without changing rays, sources, material, existing light RGB histories or brightness capping. Focused behavior reds/greens, then two matched smooth/rough sharing captures and retained-reference accuracy/noise views. No reference integration, control recapture, commits, children or workflows.


## Notes

**2026-11-06T19:49:03Z**

User selected history-based guide. Preserve raw distances separately. Store first distance moment (meters) and accepted finite-hit weight (dimensionless) with the same reprojection/temporal weights and existing short-history repair as light. Misses add zero support, not a zero-metre hit. Representative distance=moment/support; exchange support=min(1,hitWeight*current light history length), explicitly evidence weight not a probability/certified confidence. Existing light RGB, material, source, rays, latest fast-history alpha, footprint/caps and maximum exchange remain unchanged. Optional guide/scratch owned by TemporalSignal only for specular sharing, reused/reset/resized there. Baseline transport exposes latest-ray behavior for assertion reds. Planned bounded work: two Rendering Release builds, one final tool build, two test cycles, conditional two 64+32 sharing cases, one saved-data report; no old-control recapture or light integration. Guards>=5GiB, cumulative evidence cap524288KiB. Unexpected substantive blocker stops; no children/workflows/commit.

**2026-11-06T19:54:57Z**

Initial red phase stopped before any compile/test at preservation check, exit91, commands RELEASED14:53:07. Known cause: historical10-control manifest includes the working accumulate_temporal.comp, now intentionally changed for approved guide transport. Incoming archive contains exact originalc520a0ef... SHA matching that unchanged manifest. Preserve failed logs/scripts. New verification checks those original bytes in incoming-source.tar.gz, all other legacy entries normally, and all frozen evidence/binaries unchanged. This is not a behavioral red or a changed control. Resume the same bounded baseline under red-recovery namespace; zero compiler attempts consumed so far.

**2026-11-06T19:57:04Z**

Scaffold Rendering compilation succeeded with0warnings/errors. Affected test compilation then failed TS0002 on new four-argument Vector4 constructors, no behavior tests ran. All commands ended/released14:55:37. Narrow tests-only named-argument correction recorded separately. Reuse the exact successful scaffold DLL as runtime and compile reference for one tests-only recovery, no new Rendering/tool compilation or baseline repetition. Update actual planned tests compilation count to3 including this failed cycle; final Rendering/tool budget unchanged.

**2026-11-06T19:59:25Z**

Second tests-only compile exposed CS1734/CS1574: insertion placed the existing SamplePass XML description on the new nested test class. No tests ran; source-only correction moves that unchanged description back to SamplePass. Keep failed compile/log/source. One tests-only docfix check reuses the same3cce6a... successful scaffold DLL, no renderer/tool rebuild. Actual test compilation count will be4 including two failed setups and final, not the initial estimate of2. No feature behavior implemented before assertion reds.

**2026-11-06T20:02:35Z**

15actual GPU baseline cases completed:11intended history/support assertion reds,1disabled-path synchronization-validation failure,3passes. The latter exposes newly added writable guide descriptors aliasing Sample when disabled, not evidence against the history model. Final wiring will alias inert guide roles to corresponding existing Slow/Scratch roles instead. Baseline SHA3cce6a41658e2b81f2ee064077265f717f35fe1301fad03cd16946235d28971c retained. Add two directly required new behavior reds only (short-history guide repair; weak support exchange) against this same binary, without repeating the15cases or recompiling Rendering. Test-compilation accounting now5cycles including two setup failures, two red batches and final checks; two Rendering/one tool build budget unchanged.

**2026-11-06T20:08:42Z**

Continuation verified the same frozen baseline without rerun:15 cases=11 intended history assertion reds,1 inactive-descriptor synchronization failure,3 passes. Added batch2=1 meaningful short-repair red and1 sharing-support case blocked by that same synchronization failure; do not count the latter as a support assertion red. Final implementation follows accepted reprojection and temporal/repair light weights for guide RG only, retains raw hit alpha, reduces symmetric exchange by minimum endpoint support, and maps disabled descriptor aliases to existing Slow/Scratch access roles. Statically checked named arguments/expression-tree constraints. Next: exactly the final Rendering Release build, one tool build, one affected-test compilation and focused checks; no new baseline.

**2026-11-06T20:14:28Z**

Final Release Rendering/tool/test compiles all succeeded with0warnings/errors;124focused checks passed. Capture admission then STOPPED at shader provenance, exit134, before mkdir live/any renderer frame:6changed SPIR-V modules, not the expected4. Two additional variants are accumulate_diffuse_single_temporal/spatial.comp from the shared accumulator include. No assertion of their semantic equivalence, no widened whitelist, no retry/rebuild/capture/report. Allcommands ended and RELEASED2026-11-06T15:11:12-05:00, owned processes0. Frozen Rendering38af284901416556af1837c5555da7d5a6ef8cc806a0c15a54e871452f994d23/tool275fd58cc7ca2cf36c0e32585fe51cc2df426c82c3587d71d2c64fd5256eaf20. Existing controls/seals verify after restoration. Two Rendering builds,1toolbuild,5testcompiles(2setupfailures,2redbatches,1green),0newframes/0comparisoncases. Next correction must isolate distance-only declarations from unused diffuse variants or prove their binary changes; do not silently broaden provenance.

**2026-11-06T20:16:26Z**

Updated ARCHITECTURE.md, tool README and research plan to describe temporal guide semantics and distinguish prior latest-ray images from this unmeasured stage. Stage README records all attempts,124greens,6vs4shader gate and0/2captures. No further build/test/render/numerical command after release. Ticket remains open; broad user reference/noise goal is not complete.

**2026-11-06T20:18:00Z**

Lightweight seal complete:126-file manifest77930d6301945dab5faaaed6f71412619c8bac61a5c24b5882b7f8a0f3a56e4b, all126verify. All old seals/frozen source/runtime checks pass. Final guard33731000KiBfree,cumulative250824KiB<524288KiB. No owned dotnet build/test/render process remains; unrelated daemons were left untouched. ExplicitRELEASE at15:11:12EST remains; zero new renderer frames and no future reservation. Temporal-history prototype is implemented/test-green but its visual/noise comparison is BLOCKED by unresolved diffuse shader provenance. Do not close ticket or mark broader goal complete.

**2026-11-06T20:21:24Z**

User Continue resumes the stopped shader-provenance correction in new immutable namespace reflection-distance-history-recovery-20261106-1520. Existing6vs4shader admission failure is the real unchanged-diffuse regression red; do not rerun old history reds. Source/binary baseline preserved and all prior seals verify. Narrow correction moves guide-only declarations/helper out of shared accumulate.glsl into a two-caller accumulation guide include. This keeps the4module whitelist unchanged. Bounded work:1Rendering Release compile,0toolcompile(reuse frozen275fd58...API-identical tool),1affectedtestcompile/focused checks; conditional exactly2new64+32sharingcases and1saved-data metrics report. No reference integration/control recapture/dependencybuild/children/workflow/commit. Normalpaths checkpointed;>=5GiB/cumulative512MiBguards. Unexpected blocker stops; explicitrelease afterownedprocesses.

**2026-11-06T20:25:33Z**

Recovery build/test passed0warnings/124greens, but admission process at15:23:38loaded PREVIOUS Engine38af...:CurrentAssembly explicitly points to old1445/frozen/bin, while rebuilt/frozen Engine is347de2fc91aa276dc48a137324e0af911fa6fec7df34dd989ae4bf20cca29fef. New obj diffuse SPIRV hashes exactly match originalb6e8.../d3e5.... Cause is runtime file placement: symlinked executable resolves application base to oldstage. Allcommands stopped/released; failed-admission.sha256 preserves this exact attempt. Narrow setup correction only:materialize the SAME275fd58...toolentryDLL in frozen-local-entry/bin, link unchanged deps and corrected347de...Engine. No additional source edit/build/test or whitelist change. One guarded read-only shader-provenance command will confirm actual loaded path before any capture; no previous captures existed. Subsequent logs/newreceipt namespace preserved separately.

**2026-11-06T20:41:34Z**

Temporal distance-history comparison complete:124focused greens; corrected runtimeEngine347de2fc.../unchangedtool275fd58...; exactly56/60identical shaders with four intended changes. Both smooth/rough original-fixture cases completed once,64warm+32measured each; all32unfiltered/decoded/classification controls exact,576surroundingpairs unchanged,raw=plate+emitter every scalar. Roughnoise29.2528 vs33.7151uncapped/32.6706latest(-13.2353%/-10.4613%);meanerror34.0402 vs36.1510/35.5770;outermissing6.9188% vs8.4888/8.3166. Crop-total~105.409%refunchanged;roughwidth+0.0795px to25.5474(ref24.4711),so notbetterallmetrics. Roughguideavailability17.0606%->84.8953%;effectivehitweightmean0.4074first/0.4151last. Smoothall32platebytesexactuncapped/latest. Inspected actualdraw95andmeans:specklesremain; modest improvement not solution. AllownedheavycommandsRELEASED15:29:59EST;no laterreservation. Docs/README updated; sealing follows,no hardening/productionacceptance.

**2026-11-06T20:44:37Z**

Final lightweight seal complete: 323 entries; manifest SHA256 ec4225f12794a174b1c50359809bd68e5f93cd6c211b02b4d71d6cdd181a2ad9. All entry checks passed; directory symlink targets separately retained. Prior failed admission/old stage seals and corrected runtime/source/test identities reverified. Final docs/ticket snapshot archived before this receipt. Heavy RELEASE remains 2026-11-06T15:29:59-05:00, zero owned heavy process, no later reservation. No build/test/render/report repeated during sealing. Ticket remains in progress; broader goal incomplete.
