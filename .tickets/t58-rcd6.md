---
id: t58-rcd6
status: in_progress
deps: []
links: [ter-6fka, t10-ogon, t10-ck8w]
created: 2026-11-22T22:51:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, history]
---
# After 100 draws on the bright exterior, a cut into the cabin stays saturated white for over 100 draws

Found by t0-gumm on 2026-11-22 (docs/research/t0-gumm-catch-up-spread, section "The card, job 1"), not hunted.

The film: docs/research/t0-gumm-catch-up-spread/cut-exterior.json. The orbit scene on the chase camera (the planet, far too bright at this exposure): 60 ticks, a still, film a of 100 frames (draws 33 to 132), then the camera changes to the person camera in the cabin and film b runs 200 frames (draws 133 to 332).

The run: the build xt of that record (X5, the tree e232080f of the line's merge 710834cc0, with the survey's probe patch and the record's scratch logs), headless at 1920 x 1080, the constant render scale 2/3 played back through TERTIUS_X_SIZE_REPLAY. The exposure is fixed: TERTIUS_PROBE_EXPOSURE=4.08, and exposure.json gives 4.08 stops for every frame. So the automatic exposure of t2-supl is not involved.

What the frames show (the whole image's mean of the probe's series, frames.series; the cabin's settled level is about 302):
- a-000099, the last chase frame: 29,614.
- b-000000 to b-000050: 65,536 or close to it, a white frame.
- b-000100: 40,421. b-000150: 5,166. b-000180: 571. b-000199: 329, still 9 % above the settled cabin.

The history log shows that the cut restarts the histories: at draw 133 the diffuse history's mean length falls from 10.5 to 0.98 draws. So some state that a cut does not empty carries the exterior's light into the cabin and decays over about 150 draws.

A cut inside the cabin (one frame on the chase camera between two person films, the record's cut.json) shows no such light: the first frame after the cut is at 293 against 302.

Picture: docs/research/t0-gumm-catch-up-spread/pics/cut-exterior-white.png: a-000099, b-000000 and b-000010 side by side.

To see it: run cut-exterior.json headless with TERTIUS_PROBE_EXPOSURE=4.08 and TERTIUS_PROBE_SERIES=<file>, and read the series of film b.


## Notes

**2026-11-23T14:17:05Z**

2026-11-23, line orchestrator of the back-face line: the fault repeats in the surface share's takes (t1-wl7c), on main 373da00eb and on the share's head e11b3548b alike, so the share neither causes nor changes it. Two takes of the orbit scene at one fixed exposure, in plain Release copies. 1. Person camera, 30 draws of the chase camera on the sunlit exterior, person camera again: the lamp hall stays saturated white for all 60 draws after the cut (videos/cut-via-exterior.mp4 and cut-via-exterior-c-000048-sbs.png, draw 48 after the cut). 2. The same with one draw of the chase camera: no saturation, and 3 capture specks in the first draw after the cut, then 0, on both builds (videos/cut.mp4). So the fault needs more than 1 draw on the exterior and has all it needs after 30. That bounds what holds the light: something that gathers over 2 to 30 draws, not the cut's first draw. Folder: /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/surface-share/videos/.

**2026-11-23T14:19:50Z**

2026-11-23, orchestrator: this is a visible fault by the user's bar (a white room for one to two seconds after a cut from outside), so it gets a diagnosis now, and I link it to the structural step ter-6fka. My hypothesis, to test and not to assume: the state that the cut does not empty is the eye's environment map. It refreshes one face per draw, which fits the back-face line's bound (nothing after 1 exterior draw, everything after 30). After the cut every history is empty, so leavingLightOf and the young histories read the closed form, and the closed form reads the map, which still holds the sunlit exterior. That light enters the diffuse histories. The bounce loop then feeds on itself (a ray's hit reads the hit pixel's history), and in a pale room its gain per round is high, which fits a decay over about 150 draws where the map itself is fresh after 6. The test, a log and no fix: per draw from 5 draws before the cut to 200 after it, the mean of the eye's map's irradiance, of the closed form's light, of the diffuse history and of the hit light, in the cut-via-exterior take. Then one scratch variant that refreshes all six faces of the eye's map in the reset draw, before any pass reads it: does the white frame go? If the hypothesis holds, the class is 'light that depends on where the eye was', the same class as t2b-xeo7, and the far-field store per semantic frame removes it by construction, because a frame's capture points do not follow the eye. A refresh of all six faces at a reset is then the direct fix that the batch of the face pass (t1-6tn2's probe) makes cheap.

**2026-11-23T14:21:49Z**

2026-11-23, line orchestrator of the back-face line: the diagnosis is launched, by the orchestrator's order of this day. It goes to the lead of the face cost split (t1-6tn2), because that lead holds the environment map's probe and because a log needs no quiet machine: it fills the time in which that lead's timing rows wait for a load below 8. Thread thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1, worktree t3-1f553e45-00934a8d076a004a, clientRequestId back-face-6tn2-rcd6-diagnosis. The work: the orchestrator's log (per draw from 5 before the cut to 200 after it: each map's frame, refreshed face and mean irradiance, the means of the closed form's light, the diffuse history and the hit light, and the whole image's mean) on this ticket's own script cut-exterior.json, and one scratch variant that refreshes all six faces of every map that the eye's view reads in the cut's reset draw. One check that I added, which can speak against the hypothesis: if the map takes one exterior face per draw, then the one chase draw of cut.json puts the exterior into one face of six, and the map's irradiance must show it, yet that take shows no light (293 against 302). The lead logs that take too: which face of which map the chase draw refreshes, and what the irradiance does. If the variant does not remove the white room, the lead reports which logged mean stays high and builds nothing more. No fix is designed here. The record goes to docs/research/cut-white-room/README.md on the lead's branch.

**2026-11-23T14:55:06Z**

2026-11-23, line orchestrator of the back-face line: the diagnosis is done (the face cost split's lead; record docs/research/cut-white-room/README.md, commit ab5c2715b on t3/fix-projection-aspect-wobble, not merged yet; a plain copy of main e7d4970fe with a probe, no engine change). The orchestrator's hypothesis holds in its main part. The cause: every environment map is captured at the eye, and EnvironmentChain.Keeps keeps a map when the chain's frames are the same and looks at no eye position. Both cameras serve the same three frames, so the cut keeps every map. Map 2, the map of the camera's frame, still holds the faces that the chase eye drew outside the hull: its mean irradiance is 29.14 against a true 0.00603 in the cabin, about 4,800 times too much. Map 1 leaves out the ship, so it reads 28.07 from either eye. The scratch variant refreshes all six faces of every map in the reset draw, and the white room is gone from the first draw. Means as map 2's irradiance / diffuse history / whole image (8-bit mean x 257), main against the variant: draw 0 after the cut 29.14 / 309 / 65,535 against 0.00603 / 0.103 / 23,587; draw 10 8.10 / 218 / 65,535 against 0.00603 / 0.123 / 25,228; draw 50 0.00603 / 75.1 / 65,481 against 0.00603 / 0.131 / 26,042; draw 199 0.00603 / 0.405 / 43,597 against 0.00603 / 0.138 / 26,701. Why it lasts about 200 draws, in two parts. 1. The map is fresh only at draw 27: its faces refresh in turn, one face per draw over the 18 faces of the three maps, and it takes two rounds, because the prefilter writes the irradiance of only the drawn face from the six faces as they are stored then (EnvironmentPrefilterPass's own comment), so the first round convolves each face while later faces still hold the exterior. 2. After that the light stays in the loop between the diffuse history and the hit light (a ray reads the hit pixel's hit light, which reads that pixel's history). The loop loses about 3.4 percent per draw: the history's ratio is 0.963 to 0.968 per draw from draw 30 to 199. The one-draw take agrees: its one chase draw refreshed face -X of map 1 and no face of map 2, and map 2 stayed at 0.00603. One part of the hypothesis does not hold: the closed form reads 0 in the cabin at every draw after the cut, so it is not the path. The record does not name the term that reads the stale map at draw 0. The candidate is the diffuse ray that leaves the screen (environmentLobe in screen_trace.glsl, line 457, which reads the radiance cube). Also open: map 0's irradiance reads 0 at every draw. No fix is designed here. The fix that the variant shows: a reset draw refreshes all faces of every map before any pass reads them, as a run's first draw already does.

**2026-11-23T14:59:22Z**

Orchestrator's decision, 2026-11-23: fix now, with today's face pass. The white room is the worst visible fault on the list, and the fix uses a code path that exists (a run's first draw refreshes all faces). It does not wait for the batch.

The model. A map is captured at the eye, so its content depends on the frame and on the eye's place. A cut moves the eye's place in one step. The histories already hear of that step as the reset. The maps must hear of it from the same source. So: no second detector of a cut in the environment chain. The one reset that empties the histories also makes the chain keep no map, and the chain then takes its existing path for a run's first draw (all faces of every map before any pass reads them, then the prefilter over six fresh faces). The class of the bug is 'a store that depends on the eye's place does not hear the eye jump'. The lead lists every such store and says for each one who tells it of the reset.

Conditions.
1. The extra cost stays in the reset's one draw (about 12 ms on the Mac for 18 faces). The lead measures it. A cut hides one long frame.
2. The render scale's cost model of the catch-up kind does not learn from that draw. The lead checks the catch-up request at the next cut against a run before the fix (within 0.02), on the card or the Mac. If it moves, the reset's first draw becomes no measurement, the same rule as the run's first draw in t0-ygp1. The controller's rates and band stay untouched (ter-5kz6).
3. Acceptance: in cut-exterior.json the whole image's mean at draws 0, 10 and 50 after the cut is within 15 % of its value at draw 199, no frame is white, and a test shows that after a reset draw every face of every map is of that draw. The user gets the film as a side-by-side video, main against the fix.
4. Not in this ticket: the carrier (which read brings the map's light into the diffuse history) and the loop's 3.4 % per draw. The carrier gets one logging step in this ticket only if it is cheap, because ter-6fka needs to know it.

Order. The surface share's merge is not delayed. The face cost lead holds the scratch variant, so my lean is that it builds this after its range-skip timing rows and before the batch probe. The line decides between that and a new lead in the slot that the surface share's merge frees, by which comes first.

**2026-11-23T15:02:25Z**

2026-11-23, line orchestrator of the back-face line: the build's detail, after the orchestrator's decision above. The lead of the face cost split builds it in its worktree t3-1f553e45-00934a8d076a004a, branch t3/fix-projection-aspect-wobble, after its range-skip rows and before the rest of the spacing of 4 and the batch probe. My reason for that lead: a new worktree with a full build needs 30 GiB free and the disk has 24, and that lead's range-skip rows come before the surface share's merge frees a slot.

### The model

Terms, one name each:
- **Cut**: a draw whose snapshot's count of camera cuts differs from the last drawn frame's. The simulation's RenderSnapshotBuilder counts a cut when it builds a snapshot with a camera that it did not interpolate. A cut moves the eye's place in one step.
- **Reset**: a draw that empties every history. FrameHistory.Resets gives its four causes: the first drawn frame, a cut, a change of the output size, the first draw of a still.
- **Eye-dependent store**: a store whose content depends on the eye's place. The histories are such stores, and so is every environment map, because a map is captured at the eye.

What causes what:
- FrameHistory.Begin is the one choke point of the resets, and it already derives both values that matter here: `Reset` and `RefreshesEveryFace`. Today `RefreshesEveryFace` is true only for the first draw of a still. The renderer passes it to EnvironmentMap.FacesFor, which then draws all six faces of every served map before any pass reads a map, and the prefilter runs over six fresh faces. That is the existing path of decision 10 in docs/plans/environment-maps-per-frame.md.
- The fix: Begin derives `RefreshesEveryFace` as true for a cut too. The cut is the comparison that `Resets` already makes, so both read one named predicate. No code outside FrameHistory detects a cut, and EnvironmentChain.Keeps does not change: it answers "is this the map of the same frames", which stays true at a cut.
- Nothing new is stored. The maps' owner (EnvironmentMap.FacesFor) and the prefilter stay as they are.

### One settlement of mine, for the orchestrator to reverse

The orchestrator's note says "the one reset that empties the histories also makes the chain keep no map". I narrow that to the reset's causes that move the eye: a cut and the first draw of a still. A change of the output size resets the histories because their images have the wrong size, and it does not move the eye, so the maps stay right. If every reset refreshed every face, a window drag would draw 18 faces in every draw of the drag, about 12 ms more per draw on the Mac. The first drawn frame needs no rule: no map is kept there. The source stays one: Begin derives both values from the same comparison.

### Signatures

FrameHistory.cs, the predicate that `Resets` and the derivation share (the lead chooses the name):

```csharp
    /// <summary>Whether <paramref name="snapshot"/> cuts the camera since the last drawn frame: its count of camera cuts differs.</summary>
    private bool CutsFrom(RenderSnapshot snapshot) => drawn is { } last && last.CameraCuts != snapshot.CameraCuts;
```

In `Begin`:

```csharp
            RefreshesEveryFace: (still && draw == 0) || CutsFrom(snapshot),
```

where
- `still` = whether the draw belongs to a headless still (a clock step of 0 or of at least MeteringStepSeconds).
- `draw` = the draw's number within the snapshot, 0 for the first.
- `drawn` = the record of the last drawn frame, null before the first.

`PendingFrame.RefreshesEveryFace` keeps its name, and its comment names both causes and the reason: a map is captured at the eye. The parameter `longStep` of EnvironmentMap.FacesFor and EnvironmentFaces.RefreshesAll no longer names what it carries. The lead renames it (for example `everyFace`) with its comments, and updates decision 10's wording in docs/plans/environment-maps-per-frame.md and docs/plans/environment-map.md's table "Which faces a frame draws".

### The class of the bug

"A store that depends on the eye's place does not hear the eye jump." The lead lists every store of the renderer that outlives a draw, and says for each one whether it depends on the eye's place and who tells it of a cut. The list goes into the ticket and into ARCHITECTURE.md beside the reset's text. A store on the list that does not hear the cut is a bug ticket of its own, not a part of this build, unless its fix is the same one line.

### Acceptance

1. Tests first, red on main for the right reason:
   - A draw after a cut refreshes every face of every served map. A change of the output size alone does not. A continued draw does not. The first draw of a still does, as before.
   - One test at the level of the renderer: after a cut from a bright place outside a closed dark room into that room, in one frame chain, the room's map reads the room in the cut's draw. Before the fix it reads the bright place. The lead chooses the smallest scene that shows it.
   - The orchestrator's condition: after a reset draw by a cut, every face of every map is of that draw.
2. The film cut-exterior.json at the fixed exposure of 4.08 stops: the whole image's mean at draws 0, 10 and 50 after the cut is within 15 % of its value at draw 199, and no frame is white. A side-by-side video, main left, the fix right, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. The lead looks at frames of it at 1:1 and describes them.
3. The cost stays in the cut's one draw. The lead measures the frame's GPU time of the cut's draw and of the 10 draws after it, main against the fix, on the Mac at a load under 8, with the load in each row. The draws after the cut are equal within the noise.
4. The render scale. The cut's draw has the cost kind catch-up (its draw index is 0), and ScaleController.Measure reads its timings. The lead compares the catch-up request at the next cut, main against the fix, in a film with two cuts under the dynamic render scale. If it moves by more than 0.02, the lead stops that part and reports the two numbers to me: the remedy (the cut's draw is no measurement, the rule of t0-ygp1) is in the Design X line's hands, and the orchestrator coordinates it. The controller's rates and band stay untouched (ter-5kz6).
5. The carrier, only if it is one switch and one run in the probe copy: in main's cut-exterior take, the diffuse ray that leaves the screen reads zero in place of `environmentLobe` (screen_trace.glsl, line 457). Does the diffuse history's jump at draw 0 (84.6 to 309) go? The answer goes into this ticket for ter-6fka. No more than that one step.
6. The usual gates: one fresh reviewer for the fix's diff, every finding resolved; main merged into the branch; every test project in Checked, then in Release; coverage of the changed lines; the mutants of the changed Rendering C# through tools/mutate.py with one result each; ARCHITECTURE.md and the two plan docs updated.

### Out of scope

- The loop's loss of 3.4 % per draw between the diffuse history and the hit light.
- The prefilter that needs two rounds after a change in turn (its own ticket, owed at the merge of the split's record).
- A map's capture place that does not follow the eye (the far-field store, ter-6fka).
- A change of the render scale's controller.

Cloud money: up to 0.10 dollars for this ticket, only through tools/runpod/gpu_job.py, and only if the render scale's check needs the card.

**2026-11-23T17:54:01Z**

2026-11-23, lead (t1-6tn2's lead): the build, in progress on t3/fix-projection-aspect-wobble (review base 8ef2812e8, a merge of main bdc49b929).

The model, as settled by the line on 2026-11-23 (fork C): the count of camera cuts holds two causes, an eye jump and a hand-off of the camera's frame. FrameHistory.Begin derives RefreshesEveryFace from the still's first draw and from JumpsTheEye = CutsFrom (the reset's predicate) && KeepsTheFrame (the frame comparison of the continued record). A cut that changes the camera's frame goes through EnvironmentChain.Keeps, as decision 9 says, so a doorway costs what it cost on main. The first commit (93d2c5847) refreshed every map at every cut, a hand-off included; the reviewer found that, and C replaces it.

Red first, on the code before each change:
- Against main (8ef2812e8): ADrawAfterACameraCutRefreshesEveryFaceOfTheMaps (now ACutThatKeepsTheCamerasFrameRefreshesEveryFaceOfTheMaps) found False; ACameraCutRefreshesEveryFaceOfEveryMapInItsOwnDraw found map 0's faces 0, 1, 3, 4, 5 of the frames before; AfterACutFromABrightPlaceIntoADarkRoomTheMapHoldsTheRoomInTheCutsDraw found <1, 1, 1> on faces 1, 3, 4, 5.
- Against 93d2c5847 (every cut refreshes): AHandOffOfTheCamerasFrameResetsTheHistoriesButLeavesTheMapsToTheChain found True; WhenTheCameraEntersAChildTheMapsOutsideKeepTheirFaces, with the cut count that the simulation sends at a frame change, found the root's face +X <0, 0, 1> where decision 9 keeps <1, 0, 0>.

The stores that depend on the eye's place, and how each hears of a cut: ARCHITECTURE.md, "The frame history", the table after the pending state. The two that do not hear it are their own tickets: t10-ogon (impostor captures) and t10-ck8w (the exposure); neither has this fix's one-line cause. The residual of the maps (a cut to a camera in another frame keeps the ancestor maps whose frames stay; their error is the eye's jump inside the subtree that they leave out, the kind of error of a face in turn) is stated there too.

Film (acceptance 2, cut-exterior.json, exposure 4.08, whole-image mean as 8-bit mean x 257) at draws 0 / 10 / 50 / 199 after the cut: main 65,535 / 65,535 / 65,481 / 43,597; fix 23,587 / 25,228 / 26,042 / 26,701 (0.883, 0.945, 0.975 of draw 199). No frame of the fix is white: at most 0.45 % of its pixels saturate, the lamps. The film's cut keeps the camera's frame, so A and C give it alike. Video: t3-1f553e45-00934a8d076a004a-evidence/rcd6/video/cut-exterior-main-vs-fix.mp4.
