---
id: ter-9wwx
status: in_progress
deps: []
links: []
created: 2026-11-24T23:56:27Z
type: feature
priority: 2
assignee: Alexis Purslane
---
# Fit Blue Danube with a kilometre-scale inhabited wheel and licensed scene models

## Design

Keep the station orbit and docking port on axis. Size the inhabited floor at 2,000 m radius and derive the angular velocity from standard gravity. Use the same floor and rotation for the visual ring, frame, character route and docking. Preserve the smaller spin-station test scene. Dress the station, shuttle, passenger, and inhabited rooms with identified freely licensed model parts at human scale through the demo rendering path; retain physics hulls where they represent the finished art. Prefer a coherent industrial passenger terminal and readable silhouettes. Keep sources, licenses and adaptation notes with the assets.

## Acceptance Criteria

The normal Blue Danube sequence launches, docks and reaches the usable rim. Real renderer captures show the ring, fitted shuttle, character and furnished spaces. Verify actual rim gravity/rate, clearances, credits and normal entry from a fresh checkout.

The models reach the scenes through a reusable slice of the asset pipeline's model design (docs/plans/asset-pipeline.md, "Models: nodes and pieces"), not through Blue Danube code (user, 2026-11-24). The engine has a `ModelId`, `ModelDesc` of mesh and material parts with local offsets, `IRenderResources.CreateModel` and `RenderData.Model`. One entity draws one rigid model piece with all its parts through the normal scene paths: snapshot, culling, shadows, motion and impostors. A library resolves a package model reference and reads its GLB, apart from placement. It caches the uploaded handles per render-resource owner, so a repeated placement shares them. It knows no Blue Danube, Pioneer or curated file names. Procedural mesh and material callers keep working. A separate moving visual, such as the walking passenger's body, uses the existing attachments. At least two different assets (a ship and furniture) go through the same API, with repeated placement, several materials, local part transforms under a moving and turning parent, and normal culling and lifetime. The Runpod acceptance run uses this path. ARCHITECTURE.md and this ticket state which subset of the asset-pipeline design exists and which does not.


## Notes

**2026-11-25T00:19:36Z**

The art direction is a working orbital port. The selected exterior models are the Pioneer Lunar Shuttle and the orbital station by the Pioneer developers (CC BY-SA 3.0). The new habitat layer adds four-deck cabin galleries, lit concourses and windowless service bays to both axial faces of the ring. Its Blender check exports and reloads the GLB and verifies outward window faces, embedded panel normals and lighting levels. Removing outward faces, window emission, panel normals or raised arcades each fails the check. These are art-build checks. The integrated film and the normal launch still need verification.

**2026-11-25T00:21:56Z**

Acceptance adds a full run on a Linux GPU in the cloud (user, 2026-11-24). Start it only after the combined Release build and the art and physics fit are ready, never while assets are still missing. On a Runpod worker, through the tools/runpod workbench and its spend caps: stage the verified asset inputs, do a fresh setup and build, then run the standard Blue Danube script without a break, from the launch through the docking and the rim arrival to the coda, with real art and the normal film cadence. Settings as the earlier survey: a 1920x1080 window with FSR 3.1 at a fixed 2/3 scale (1280x720 render). Record the hardware, the build, the actual scale, the clocks and the frame count. Report the GPU render time (median and p95 in ms, and as FPS), excluding image readback, capture and encoding; say how they are excluded, and do not subtract separate p95s. Report the wall FPS (rendered frames divided by elapsed render time) separately from provisioning, build time and the film's playback FPS. Give per-section figures where the telemetry allows. Check that the coda ran. Bring back stills or film from the worker and the timing logs. No separate full run on the Mac is needed.

**2026-11-25T02:45:12Z**

Engine model slice, branch ter-9wwx-engine-models (first API commit f8a43397e). ModelId, ModelPart, ModelDesc, CreateModel, RenderData.Model and RenderInstance.Model exist. The batcher's ModelExpansion turns each model instance into one instance per part, so culling, shadows, motion, impostors and environment faces see ordinary instances. Rule: a model draws only at a uniform entity scale (spawn refuses another). Tests: full Checked 11,427 passed, full Release 11,429 passed. ModelRenderTests: the model's image is byte for byte the image of the same parts as plain entities (model-parts.png).

Mutants. Stryker, cloud, a0a549e60, valid: 17 killed, 1 survived, 5 no-mutant, 1 removed. tools/mutate.py: 51 listed, 47 killed, 2 survived as equivalent, 2 build-failed. Rows that were not killed:

| id | place | result | answer |
|---|---|---|---|
| Simulation-4888 | World.Entities.cs:452 | survived | Judgment call: the message wording is no behavior. Stryker ignore comment. |
| Simulation L210, L448, L449, L454, L456 | World.Entities.cs | no-mutant | Listed as S01 to S06: all killed but S01 (build-failed), whose throw Simulation-4716 kills. |
| Simulation R538 | RenderSnapshotBuilder.cs | removed | The old visibility line; Draws replaces it, and Simulation-3745 to 3750 kill its mutants. |
| E01 | ModelExpansion.cs:47 | build-failed | Equivalent: always copying gives the same spans as the pass-through. |
| E08 | ModelExpansion.cs:90 | survived | Equivalent: HasVolume excludes a zero previous scale, and a zero current scale draws in no view. |
| U07 | UploadQueue.cs:265 | survived | Equivalent: None's value is -1, which fails the unsigned bound. |
| B02 (first form) | SceneBatcher.cs:439 | build-failed | Rewritten as poses: [], killed. |

Survivors E04, E07, E16, E22, U14 and B01 got tests that kill them (commit 108b082df). Core-4083 to 4089 survived the first run, which was NOT VALID (a Dubious test), and ModelIdTests kills them in the valid run.

**2026-11-25T06:06:30Z**

The fit to the art: mutation testing of the changed Demo lines with tools/mutate.py (20 listed mutants, Host tests in Checked, 2026-11-25). The Demo has no Stryker run. The first run killed 14. Four survivors got tests that kill them, red on the mutant first, and the mutant that did not compile was rewritten and killed.

| id | place | change | result | killed by |
|---|---|---|---|---|
| C01 | DemoScenes.Shuttle.cs:97 | art scale 1.5 | killed | DockingEndStateTests (3 tests) |
| C02 | DemoScenes.Shuttle.cs:112 | art not 1 m aft | killed | DockingEndStateTests, ShuttleTests |
| C03 | DemoScenes.Shuttle.cs:164 | doorway flush with the hatch | killed | LoftedHullTests.TheHullsMeshFacesOutOfItsSolid |
| C04 | DemoScenes.Shuttle.cs:186 | lip 0.3 m thick | survived, then killed | ShuttleTests.ThePorchsLipReachesOverTheLandingWithAirUnderIt (new) |
| C05 | DemoScenes.Shuttle.cs:427 | bubble without its 2 m margin | survived, then killed | ShuttleTests.TheBubbleHoldsTheHullsBoxGrownByTwoMeters (new) |
| C06 | Lift.cs:135 | lift predicts one tick less | killed | BlueDanubeExitTests.TheLiftWaitsAtItsTopUntilThePersonRidesIt |
| C07 | Lift.cs:156 | no drift prediction | build-failed (unused local), rewritten as drift * (0 * ahead), killed | BlueDanubeExitTests.TheLiftWaitsAtItsTopUntilThePersonRidesIt |
| C08 | Lift.cs:108 | no hold at the top | killed | BlueDanubeExitTests (3 tests) |
| C09 | Lift.cs:111 | no hold at the foot | survived | Judgment: at the foot the lift lies in the station's frame at rest, where the last descending sample left it, and a kinematic body with no new sample holds still there, so the hold writes the pose that the lift already has. It matters only for a foot inside another frame's bubble, which no scene has. |
| C10 | Navigation.cs:26 | sweep with the wrong sign | killed | RendezvousAutopilotTests, DemoLaunchTests |
| C11 | DemoScenes.DemoSurface.cs:443 | suit half height | killed | BlueDanubeArtSceneTests, BlueDanubeArtFitTests |
| C12 | DemoScenes.DemoSurface.cs:454 | suit footprint not centered in X | survived, then killed | BlueDanubeArtSceneTests.TheSuitStandsOnTheCapsulesBottomWithItsFootprintUnderThePerson (new) |
| C13 | DemoScenes.StationProps.cs:70 | seating reach too short | killed | BlueDanubeArtFitTests.EachPropsModelLiesWithinItsKindsReach |
| C14 | DemoScenes.StationProps.cs:128 | pod onto the ramp's lane | killed | StationPropsTests.TheBayPropsKeepClearOfThePersonsWayTheParkedHullAndTheLift |
| C15 | DemoScenes.StationProps.cs:171 | floor up flipped | killed | StationPropsTests.EveryPropStandsUpright |
| C16 | DemoScenes.StationProps.cs:198 | wall anchor on the front | survived, then killed | StationPropsTests.PoseOfStandsAFloorPropOnItsBottomAndHangsAWallPropByItsBack (new) |
| C17 | StationLayout.cs:142 | lift 1 m off the stair's lane | killed | DockingEndStateTests, StationLayoutTests |
| C18 | StationLayout.cs:136 | hold point on the bubble | killed | StationLayoutTests (3 tests) |
| C19 | DemoScenes.DemoStation.cs:627 | one id for both walls | killed | BlueDanubeArtSceneTests |
| C20 | BlueDanube.cs:95 | sill section ends at the tram's side | killed | BlueDanubeScriptTests |

Not listed, because no test can see them: the camera poses (sill, docked, work, ride-side, wheel-terminal, gallery) and DemoStation.GalleryFace. The stills checked them by eye.
