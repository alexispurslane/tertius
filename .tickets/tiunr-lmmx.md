---
id: tiunr-lmmx
status: in_progress
deps: []
links: []
created: 2026-11-06T21:33:40Z
type: epic
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Compare 16 same-data reflection reconstruction hypotheses

User-authorized parallel medium-effort isolated-worktree campaign. Compare actual renderer noise, visible-plate reference accuracy and reflection width using frozen current inputs and existing center references. First wave 16 distinct literature-backed hypotheses. Conditional second wave up to 4 evidence-derived hypotheses, not a parameter sweep. No source/history/ray/material change, reference integration, production acceptance, commits or merges.

## Design

docs/plans/reflection-reconstruction-campaign.md


## Notes

H02 is the first completed measured hypothesis:130 focused greens, two original
candidate captures,192 renderer draws (64 measured), one saved-data report.
Parent verified fixture/output seals and inspected actual draw95, mean and
reference. Rough noise RMS25.6528 vs circular29.2528 W/(m² sr), persistent mean
error RMS32.6562 vs34.0402, central5–95 width25.59691 vs25.54744px; reference
width24.47115px. Smooth plate frames are byte-identical to circular. Speckles
remain. Lower noise does not demonstrate less cross-lobe widening.

Fifteen other SAME-worktree hypotheses resumed under RECOVERY-BRIEF-v4.md,
v3 reference overlay and common fixturev4. Start receipts are external
recovery-launches-v4.json/tsv; no new workers or worktrees, no silent reset of
earlier compile counts. Candidate tool rebuild now measures each changed
shared reconstruction. All earlier failed seals remain immutable. Heavy phases
remain serialized by normal gpu-lock.sh; no production integration/acceptance.

H04 completed127/127 focused greens, two validated captures and one report.
Parent verified artifact and post-release seals and inspected actual/mean/noise
images. Both retained control metrics exactly match H02 for both materials.
Rough noise RMS29.23561 vs circular29.25279 W/(m² sr), mean-error RMS34.03338 vs
34.04023, width25.547845 vs25.547437px. These are near-null changes in this
fixture, not a demonstrated resolution of speckles or wrong-neighbor exchange.
Smooth plate frames are byte-identical to circular. The equal-width isotropic
GGX slope-overlap coefficient is demonstrated by literal real-shader tests; it
is not exact spherical visible-normal/BRDF overlap or donor ground truth.
Evidence remains sealed in H04-recovery-v4; parent receipt is external
parent-inspections/H04/inspection.json. Two of16 hypotheses now have results;
fourteen result deliveries remain pending, with no polling or new capture.

**2026-11-06T21:38:25Z**

First wave frozen: 16 medium-effort gpt-6.1-sol agents, one T3 worktree per hypothesis. Common protocol docs/plans/reflection-reconstruction-campaign.md and /private/tmp/tertius-reflection-hypothesis-campaign-20261106/AGENT-BRIEF.md. Exact dirty overlay and existing Release assets archived. No baseline/control/reference capture or integration repeated. Normal GPU queue serializes heavy phases. Each candidate gets at most two original-fixture 64+32 captures, actual images and common noise/reference metrics. Up to four later causal follow-ups, not a tuning sweep. Parent source/old evidence immutable.

**2026-11-06T21:41:07Z**

All 16 first-wave delegate launches accepted with isolated T3 worktrees: gpt-6.1-sol, reasoningEffort=medium, async. Exact task IDs and thread links are retained in /private/tmp/tertius-reflection-hypothesis-campaign-20261106/launches.tsv. Launch receipts reported working, not completed. No result or render success claimed. Completion notifications will wake the parent; no polling or wait loop. Parent will compare returned seals/images/common metrics and derive at most four causal follow-ups. Source/model applicability gaps remain explicit.

**2026-11-06T21:50:58Z**

Shared bootstrap defect tracked in tiunr-56l3. H02 stopped after3compileattempts,0tests/rendererframes. This is setup failure, not anisotropic-filter evidence. Original H02 seal verified; all existing artifacts immutable. One bounded central tests-only recovery runs before heavy sibling work resumes. Recovery task ID retained in setup ticket. No silent budget reset. Parent supplies new reference metadata from existing frozen bytes without dependency compilation.

**2026-11-06T23:16:39Z**

Parent inspected four measured candidates H02/H03/H04/H10. New H03/H10 seals verify321/309entries, finalXML127/129green,44exactinputfiles,1152unchangedsurroundingpairs,1638400raw=plate+emitterscalars. H10noise19.60782 versus circular29.25279 and meanerror31.12719 versus34.04023 W/(m²sr), but width25.93801 versus25.54744px(reference24.47115). No winner/production acceptance. Parent receipt C/parent-inspections/four-results/inspection.json; docs/plans/reflection-reconstruction-campaign.md updated. H08 conditional tests-only history-storage diagnosis and H09 no-donor identity correction resumed in SAME threads; literal old budgets preserved. H11 genuine bright-step failure remains stopped. Nine other deliveries pending; no polling.

**2026-11-06T23:30:50Z**

All16first-wave deliveries received:15native-measured candidates, H11stopped. Parent verified nine additional seals: H01 360,H05 321,H06 350,H07 332,H12 319,H13 486,H14 326,H15 323,H16 279 entries. Each22inputfilesexact,576surroundingpairsunchanged,819200rawaccountingscalarsexact,smoothplate32framesidentical; no finalXMLfailures. H08/H09new recovered measurements delivered and still need parent end-to-end inspection. H13noise21.143515,meanerror30.199067 W/(m²sr),width25.243299px beats circular in these observables but remains0.772152px wider thanreference; specklesremain. Two matched controls H17(tiunr-nqhn)/H18(tiunr-lj7t) launched to isolate normalizedmean versus guide-affine term and raw versus smoothedguide. No tuning or productionpromotion. Waiting by completionnotifications only.

**2026-11-06T23:38:56Z**

Parent verified all14 original H17 seal entries (37c35b11e2643e789cfb68c244a61ac840c5942317289ef3615007e174154e61) and inspected failed queue invocation. Exit127 occurred before builds/tests: intended log path was passed as executable. Authorized SAME-thread continuation reflection-campaign-H17-queue-recovery-v2-20261106 into NEW H17-queue-recovery-v2 evidence; old H17 immutable. Correct helper interface <log> <command> [args], no helper modification. Existing0Rendering/0tool/0test builds and1failedqueue launch retained; original2/2/3 maxima unchanged. Parent capacity18:37EST71543752KiB free,59%memoryfree,load2.19/2.49/3.38. New launch accepted starting; no algorithm outcome claimed, no extra worker, capture, import or production acceptance.

**2026-11-07T00:01:47Z**

Parent inspection complete for15nativefirstwave+H17/H18;H11stopped. Durable saved-results-v2receipt now covers9earlieradditionalstages,H08/H09/H18;controls-v3receipt verifiesH17. Acrossnewparentreceipts286inputfilecomparisons,7488savedsnapshothashpairs,10649600rawaccountingscalars;212savedreportinputhashesverified. BothH08rawproofscheckedbytewise/all6144scalarrecords. ExternalH18seal repairs onlydirectorylinkinventory, originalsuntouched. Matchedcontrols distinguish normalization/noise benefit from fittedguide width/error benefit. Updated campaigndoc;combinedactual/mean+noise/error+fullnamedmetricsunderparent-inspections/controls-v3. No parentbuild/test/render/metricsrerun, child, import, commit, acceptance or closure.
