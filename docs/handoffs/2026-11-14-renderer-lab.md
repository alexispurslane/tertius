# Handoff: the renderer lab, 2026-11-05 to 2026-11-14

This document closes the long orchestrator session "ter-icn3: Unity-guided diffuse-light noise research". It has two jobs:

1. It gives a fresh session the brief for the next large piece of work, the back-face depth layer (part 1).
2. It records where the lab stands, which decisions Alexis made, and how the lab operates (parts 2 to 5).

If you start the back-face work, read part 1 first. Before you start any other renderer work, read parts 2 to 5.

All times are New York time unless a line says UTC.

## Part 1. The back-face depth layer

### The problem

The screen walk decides whether a ray hit a surface that is on screen. The screen holds one depth layer, so the walk does not know how thick a surface is. Today it assumes that every surface is solid for a fixed share of its view depth behind it.

Ticket `ter-ukw6` records what this costs. In `rect-lamps` with the sun on, the ceiling reflects the sunlit sill of the end window. The sill is thinner than a pixel and seen almost edge-on. The probe of 2026-11-14 (main at `557d825b1`) measured the bright reflection light of the ceiling's glow area:

| Where the ray crosses the sill's plane | Share of the bright light |
|---|---:|
| Past the sill's edge, where the camera sees sky through the window: a false hit | 74 % |
| On the sill itself: a real hit | 26 % |

The bright samples carry 86 % of that area's reflection light, so about 64 % of all its reflection light is false. The sunlit sill is about 1,000 times brighter than the light that a missing ray reads. So even a hit near the back of the slab reads a very large value. The false light also sits in the clamps-off reference, because the reference uses the same walk.

### The code today

- `src/Tertius.Rendering/Shaders/screen_walk.glsl`, `walkScreen`: at the finest level a pixel's slab reaches from its surface depth `w` back to `w / (1 + ThicknessShare)`. A ray inside the slab hits where `meetsSlab && hitsPixel` holds, with an entry share from 0 at the slab's front to 1 at its back.
- `ThicknessShare = 0.1` lives in `src/Tertius.Rendering/DepthPyramid.cs`. The pyramid holds the center depth of each pixel (R32F) and the bounds per level (RG32F).
- `src/Tertius.Rendering/Shaders/screen_trace.glsl`, `traceFrom`, near lines 766 and 767:

  ```glsl
  float fade = edgeFade(hit) * (1.0 - entryShare);
  result.light = mix(missLight, hitLightOf(hitPixel, kind), fade);
  ```

- The C# copy of the walk is `src/Tertius.Rendering/Shading/ScreenRay.cs`. It changes together with the shader.

### What Alexis decided, and why

On 2026-11-14 Alexis chose to design the real mechanism first: a second depth layer of back faces, so that each pixel knows where its object really ends. Two cheaper designs were rejected:

| Rejected design | Why |
|---|---|
| A: a ray enters a surface only where the surface is seen, for every surface | Rays slip past a thick box's edge into its hidden side. That is a new leak, and floors near objects lose their contact darkening. |
| B: the same test only for surfaces seen within about 6° of edge-on | A tuned exception beside the 10 % rule. Sills seen at moderate angles still leak. The back-face layer replaces it. |

Alexis's rule applies: prefer the real mechanism. When a redesign replaces a quick fix, design first.

### What the fresh session does first

1. Find the sources of the technique. One candidate is McGuire and Mara, "Efficient GPU Screen-Space Ray Tracing" (JCGT, 2014). Also find how current engines get a surface's thickness. For each source, state its situation: scene scale, hardware, quality target and the systems that it relies on, such as a denoiser. Then say whether it fits Tertius.
2. Measure the frame cost before a design is final. Build a throwaway depth-only pass of back faces. Time it on the deck and in the cabin, on the Mac. Then time it in one workbench job on the RTX 2000 Ada, against main in the same job.
3. Write two designs, as Alexis's rule asks: a compact table, a usage example of each, what both share, and one AskUserQuestion per point where they differ.
4. Create the epic's design doc in `docs/plans/` and its tickets. The epic ticket is `ter-kh4g`.

### Points that the design covers

- Open surfaces without back faces: single cards, hair cards, glass and other transparent surfaces.
- Moving and skinned objects, and particles.
- FSR's jitter, and the depth pyramid's bounds per level: what the walk reads at the coarse levels.
- The walk's extra read per step, and its cost.
- Memory at 1280 × 720.
- Linux: write to the Vulkan spec, and use only device features that NVIDIA, RADV, ANV and lavapipe offer.
- What happens to the `rect-lamps` sill and to contact darkening near objects.
- The related ticket `ter-y3k4`, a one-image depth pyramid for the walk.

### Acceptance

- The probe's false sill hits fall to about 0.
- The reflection and bounce stills of `rect-lamps`, `exit-cabin` and the deck show no new leaks or holes against main. Judge them by eye first, beside the reference.
- Contact darkening near objects does not change visibly.
- The frame cost stays inside a budget that Alexis accepts. Today the deck takes about 19.7 ms on the Mac (M1 Max) and about 8.4 ms on the RTX 2000 Ada. The goal is under 12 ms, and 16.7 ms is accepted for ship interiors.
- The card's suites pass.

### After it merges

1. Build the median reference of `rect-lamps` again. It ignores single bright samples. The method: one clamps-off run of about 16,000 draws, split into batches, with each pixel's median of the batch means. That median measured at most 0.30 % low.
2. Measure the ceiling's glow level, its daub and its specks again (ticket `t0-adau`).
3. Then decide how to bring the rare bright reflection samples home. Alexis asked "why not both?". Both designs guide the rough reflection rays toward bright openings. One design adds the clipped-excess safety net now. The other adds it only after a measured loss remains.

## Part 2. Where the lab stands

### Main

Main holds 58 merges from this session. By theme:

| Theme | What merged |
|---|---|
| Lights | The rectangle light with LTC shine and its cheaper form. The tube light. One light loop per light shape. The local shadow filter by driver. Sixteen shadow taps under FSR. Key and fill: each pixel computes its 4 strongest lamps in full and the rest as diffuse fill. |
| Noise and history | The reflected-light history rule. The light-change age and its record. The diffuse spatial filter and its thin kernel. Guided diffuse rays. Shared reflection rays by lobe fit. Blue noise for the reflection rays. FSR's light-change mask for moving lamps. The hit light image. The light-age area rule (`t0-6gej`): no false change evidence in a still. |
| Frame time | The deck's cuts. The reflection step cap, from 256 to 96 steps. The environment face that lights each texel once. Key and fill. FSR's clear step. A patched MoltenVK that ends fewer Metal encoders. |
| Linux | The NVIDIA bring-up on Runpod. The integer specular G-buffer. The Linux-suite fixes. On 2026-11-14 the Host and Rendering suites pass on the RTX 2000 Ada with no known failure. |
| Tools | The renderer survey and the renderer-finish design doc. Survey films that write numbers instead of PNG files. The GPU lock with a shared mode, which test runs hold. The Runpod job queue: `tools/runpod/gpu_job.py`, the A/B tools, CPU Stryker runs, caps by Runpod's bill, the watchdog's stop marker and `tools/runpod/shards.py`. GitHub as the primary remote. |
| Environment | The semantic-frame box projection. |

### Design X, the main open work against mottle

Mottle is Alexis's top quality concern. Design X gives each 8 × 8 block change evidence from the light age, and it marks moved objects from motion vectors.

- Branch: `t7-3e22-retained-deviation`, in the worktree `/Users/alexispurslane/.t3/worktrees/tertius/t3-09c31e02`. The head was `5e7d169ae` at the time of writing. The mottle agent's final report gives the last head.
- Built:
  - the sample age's layout, block evidence from the light age and the moved-object mark
  - the removal of the old block test, of the own-sample mean and of the history cut before the blend
  - the evidence count per cause, and lock-in tests with real rays
- Takes T12 to T14 (`t0-f8oc`, with main as the control):
  - The lag films pass. The error rises by at most 0.10 percentage points, inside Alexis's allowance of 0.5.
  - `bay-sun-off` and `lamp-hall-16` settle within 60 draws.
  - The cabin switch fails. The bounce light settles too dark after a light change, and main does better there.
  - A moved object only narrows the clamp box. Cutting its history doubled the noisy blocks with no visible gain.
- Next, in order:
  1. Merge main into the branch. This brings `t0-6gej` and the probe fix `ter-1wo4`.
  2. Run the cabin switch again with the bounce light alone, for the branch and main.
  3. Find the cause of the dark settle. The first suspect is the clamp box that light evidence narrows: it can cut the rare bright samples, as in `ter-18hc`.
  4. Take `t0-5tyj`: stills, climb, motion films, frame time, a Linux check and screenshots.
  5. Close `t0-iido`: harden, update ARCHITECTURE.md and merge.
- After Design X: the long Halton jitter sequence (`t0-f238`) and the 128-draw history without change evidence (`t0-70v1`). They merge together, after `t0-70v1`'s check of the half-float history.

### The `rect-lamps` ceiling

This is the scene that worries Alexis most: a long room with rectangular lamps along the ceiling and small windows along the walls. The lamps sit in the ceiling, so the ceiling gets no direct light. Its clouds and specks come from three causes:

| Cause | Fix | State |
|---|---|---|
| False change evidence from thin bright edges | `t0-6gej` | Merged. The count fell from 1.95 % of block-draws to 0. |
| Too few averaged draws | The 128-draw history, `t0-70v1` | After Design X |
| False sill hits in the reflections | The back-face depth layer, `ter-ukw6` and `ter-kh4g` | Part 1 |

The preview of 2026-11-14 at 264 draws, as ceiling daub against the median reference: main 0.0567, Design X 0.0545, and 0.0454 for the stand-in of the strip fix plus the 128-draw history. The eye starts to see blotches at about 0.010. The pictures are in `/tmp/t7-3e22/preview/` and `/tmp/t7-3e22/robust/`. That folder does not survive a restart of the Mac.

### Open tickets found along the way

| Ticket | Subject | State |
|---|---|---|
| `t0-adau` | Specks near the far lamps of `rect-lamps` | Most of the cause is `ter-ukw6`. Measure again after it. |
| `ter-bdoh` | `rect-lamps`' glossy floor sparkles in shown stills but not in the reference | No owner |
| `t0-izfl` | Clamps-off runs keep averaging past the 32-draw cap | No owner |
| `t0-8wag` | A block's ray pixel starts its rays from only half of the block's width | No owner |
| `t8-o16a` | The shader's tube light sits 2 to 3 % above the C# tube near a capsule end | The cause is not known. Two tests have a 3 % band until it closes. |
| `t2-kibe` | A cheap specular fill for key and fill. The hall is 2.47 % dark without it. | Do not build before Alexis asks. |
| `t2-x53j` | Nothing guards the key-and-fill shader's register count under Apple's step at 116 | Open |
| `tz-na6d` | A depth clear inside a rendering loses earlier surfaces on the Mac | Do not depend on that call. |
| `tz-frma` | The list of lamps that reach an environment face | Its first step is a count. |
| `ter-b4ux` | The frame span. Its note holds the sweep's ranked list of next frame-time cuts. | For the next frame-time work |
| `ter-bwn3` | Cloud test shards build the test project once each | For the Runpod agent |
| `ter-byim` | Derive descriptor pool counts from the set layouts | Two merges drifted a hand-kept count. |
| `ter-jza0` | Rough metal at the hand-over roughness keeps a coarse grain | Probably helped by `t0-70v1` |

## Part 3. Decisions that Alexis made

| Date | Decision |
|---|---|
| 2026-11-12 | Design X: light age per block, plus a moved-object signal in the same merge. The lag error can rise by about 0.5 % of the light. |
| 2026-11-12 | Reflection step cap 96. |
| 2026-11-12 | Judge mottle by crops beside the true reference first, then by numbers. |
| 2026-11-12 | Agents can change any parameter by a justified binary search. The condition: the image does not change visibly to the eye or to FLIP, and mottle does not get worse. |
| 2026-11-12 | Use the cloud GPU to speed work up, inside the cap. |
| 2026-11-14 | Sub-pixel strips, design R: a bounce hit reads this draw's sample, and the history grows to 128 draws without change evidence. |
| 2026-11-14 | `ter-ukw6`: design the back-face depth layer first. |

The orchestrator settled these inside Alexis's rules:

- Key and fill uses 4 key lights by default. The fourth costs no more than the run-to-run spread on both machines.
- Key and fill merged with its known bias: the fill has no specular light. The hall is 2.47 % dark, the deck 0.31 % and the cabin 0.08 %. ARCHITECTURE.md records it.

## Part 4. How the lab operates

### The cloud

- The cloud is Runpod serverless, on the RTX 2000 Ada. Agents send jobs only through `tools/runpod/gpu_job.py`. `tools/runpod/WORKBENCH.md` is the agents' guide.
- Limits: $3 per UTC day and $30 in total. On 2026-11-14 Runpod's bill showed $11.57, and the job log's estimates sum to $13.79. Plan with the higher number. When the $30 is spent, no more cloud money comes until about 2026-12-07.
- The key is in `~/.config/tertius/runpod-key`, mode 600, and `~/.config/tertius/runpod-key-file` points to it. Agents never hold, read or copy it. Never print it.
- The audit log is `~/.local/state/tertius/gpu-jobs/jobs.jsonl`.
- Compare a change only against main inside the same job, because workers differ by about 0.4 % between jobs.
- Use the cloud for targeted GPU tests, the Release Rendering suite (six shards from `tools/runpod/shards.py`, about $0.50), image A/Bs, stills and films. Keep on the Mac: timing takes, the pre-commit hook's Checked run and the full Checked suite before a merge.

### Background tasks of the orchestrator

These scripts live in `/private/tmp/runpod-setup/`. A restart of the Mac deletes that folder.

- Watchdog: `python3 -I /private/tmp/runpod-setup/watchdog.py /Users/alexispurslane/.config/tertius/runpod-key`. At $28 in total it stops every endpoint and writes `~/.local/state/tertius/gpu-jobs/stopped-by-watchdog`. When you restart the endpoints, delete that file.
- Janitor: `bash /private/tmp/runpod-setup/ktrace-janitor.sh`. It deletes leftover trace files.
- Heartbeat: `/bin/bash /private/tmp/runpod-setup/heartbeat.sh`.

### The Claude limit

On 2026-11-12 at 23:25 the account's weekly Claude limit stopped every agent for 33 hours. The limit resets on Saturdays at 09:00. Four building agents at once used a week's limit in about 5.6 days. So run one builder and one second agent at high effort, not xhigh. Agents report only results, decisions, questions and problems, not "still waiting".

### The Mac

- Run at most four building agents at once. Run Checked and Release one after the other, never at once.
- GPU work takes `tools/gpu-lock.sh`. Test runs and renders take the shared mode. Timing takes take the exclusive mode, about 20 minutes at most per take.

## Part 5. Lessons that cost time

- Build a worktree under `/tmp` from its `/private/tmp` path. Otherwise `.editorconfig` does not apply, and the build fails with CS1573.
- In the shared main checkout, stage a new ticket by its exact path. `git add .tickets/` stages other sessions' notes.
- Git merges two equal edits of a hand-kept count without a conflict, and the sum can be wrong (`ter-byim`).
- On Apple's GPU, one more register can cost a whole gain: the key-and-fill shader lost 0.35 ms at register 116 (`t2-x53j`). On the RTX card, a change of texture access kind can move timing in the opposite direction from the Mac. Time such a change on both machines.
- Vulkan leaves the rounding of a 32-bit to 16-bit float conversion to the driver unless the shader declares RoundingModeRTE. NVIDIA rounds toward zero, and the Mac rounds to nearest.
- A test that compares a rendered level to a CPU value needs room for one rounding step on each driver.
- A long reference with the clamps off is full of fireflies. Use the median of batch means before you score against it.
- A hand-written list of test classes drops nested classes. Use `tools/runpod/shards.py`.
