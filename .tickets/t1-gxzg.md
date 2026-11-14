---
id: t1-gxzg
status: in_progress
deps: [t1-n8x9]
links: []
created: 2026-11-14T23:06:20Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, experiment]
---
# Stage 1b pictures: stills, crops, false sill hits and hair under the 10 % rule and under the layer as the slab

This ticket is an experiment, with no code for the branch. It uses the probe of t1-n8x9: read `docs/research/back-face-depth-layer/stage-1b/probe.md` and that ticket's terms first. Alexis chose the layer as the slab, with a sheet for a surface with no back face. The job of this ticket is to find a misbehavior of that model that has a name: a new leak, a new hole, or contact darkening that looks wrong and not only different. It also measures the false sill hits.

## Design of the experiment

Every comparison holds two columns: the 10 % rule (main's tree) on the left and the layer as the slab with sheets on the right. A third column is allowed where the ticket names it. Both columns of one comparison come from one machine, one script, one output size of 1920 × 1080 and one fixed exposure. For the exposure, run main once, read the stops in the run's `exposure.json`, and pass them to every column with `--exposure <stops>`. Run with `--bloom off`. Use a settled frame: a film of the same length before the capture in every column.

A crop is a part of a still at full size, one image pixel per pixel, with no scaling. A comparison image holds the crops of its columns side by side with a thin gap, in the order above.

### Part 1: the false sill hits

The saved run of the sill probe is `/tmp/t7-3e22/robust/hits9` (`result.json` holds the exact commands, `hits-true.txt` the data). Its tools are copies in `/tmp/t7-3e22/robust`: `hitclass-final.patch`, `hittrue-final.patch`, `robust.py`, `hits_report.py`, `truepix.py`. They apply after `tools/renderer-survey/probe.patch`. That folder belongs to the other line: read and copy, never change it. The probe classes each hit of a reflection ray from the ceiling's glow area by where the ray really crosses: class 0 an opening (a false hit), class 1 the hit surface itself (a real hit), class 2 a nearer surface, class 3 undetermined.

Run the same probe under two rules in one cloud job: the 10 % rule and the layer as the slab. The trimmed slab is optional. Report per rule:

- The light and the sample count of each class, and the total.
- The false share: the light of class 0 over the total light of the glow area's bright reflection samples.
- The false light as a share of the 10 % rule's false light. The total falls with the false hits, so a share of the own total alone misleads.

Then explain the false hits that stay under the layer as the slab. Stage 1 estimated them at about 8 % of the 10 % rule's bright light: rays within the real 0.50 m of the sill and within one pixel of it. Use the per-pixel and per-fade-step records. Say where their hit pixels are: the sill's row, the side faces of the window openings, or elsewhere. Say how deep they enter and what makes them false. One candidate is a back depth that is known at the pixel's center only. Others are a strip thinner than a pixel, the jitter, and a limit of the probe's own classes. End with one of two findings: the chosen design removes them, or they need a named other thing.

### Part 2: contact darkening and thick walls

There are three views. The first is `scripts/rect-lamps.json`, with the scene `orbit` and the camera `person`. The second is the cabin of `demo-exit`: `scripts/ter-kh4g-cabin-timing.json` shows its camera. The third is the deck: `scripts/temporal-upscale-timing.json` shows the path to its camera. Write stills scripts of your own with a shorter film where a job needs it.

Do not search by eye alone. For each view, compute the per-pixel difference of the two columns. Compute the same difference between two settled frames of main some draws apart, as the noise floor. List the regions where the difference stands above the noise floor. Look at each region at full size, and name it: the place in the scene, the direction of the change, and one of these classes:

- A leak: light arrives where a wall or an object must block it.
- A hole: an occluder or a reflection that the 10 % rule shows is gone.
- Contact darkening weaker, or stronger: where an object meets a floor or a wall.
- A removed false hit: the 10 % rule's light or shade was wrong, such as the ceiling's bright clouds.
- Another class, which you describe.

Crop every thick wall and pillar where a ray can pass behind the silhouette, also where the difference is small. There the slab is now deeper than 10 %. For these crops add a third column: the layer as the slab with the fade over at most the 10 % slab (the probe's switch). It shows what the fade by the entry share does on thicker slabs.

### Part 3: surfaces with no back face

Use at least one hair view with cards near the camera. `scripts/skin-and-hair.json` has the cameras `hair-camera` and `hair-cutout-camera`, and `scripts/hair-bent-cards.json` has two more. Also use `scripts/hair-rugs.json` with `low-camera`, where cards lie at a grazing angle. Add one view of dense geometry, such as `scripts/station.json` or `scripts/demo-meshes.json`.

- Report for each of these views the share of surface pixels with no back face behind them, from a dump.
- Show stills and crops of the hair views: the 10 % rule beside the sheet. Add the sheet as deep as its own plane (`NoBackSheetPlane`) as a third column.
- Check with a number that a ray can hit a slab of zero thickness. Count the walk's hits whose hit pixel is a sheet, under the 10 % rule, under `NoBackSheet` and under `NoBackSheetPlane`, in the same frames. The method is yours. The stair case in t1-n8x9 says where a loss is expected: a sheet at a grazing angle, with rays that travel along it. Say how large the loss is. If the loss is visible, show it in a crop.

## Settled decisions

- The stills can come from the cloud or from the Mac. One comparison never mixes the two.
- The cloud budget of this ticket is $1.50 by the job log's estimates, through `tools/runpod/gpu_job.py` only. A stills job needs no warm-up run and a timeout that fits its film (bug ticket t1-lw1f).
- On the Mac, a render takes `tools/gpu-lock.sh` in shared mode. The cost ticket t1-stwp runs timing takes in exclusive mode at the same time, so a render can wait for up to about 20 minutes.

## Deliverables

- `docs/research/back-face-depth-layer/stage-1b/pictures.md`: the tables of part 1 and the list of named places of part 2 per view. It also holds the shares and the count of part 3, and the commands of every run. Keep measured facts apart from your reading. Plain English, as the lint hook asks.
- The comparison images as PNG files under `docs/research/back-face-depth-layer/stage-1b/`, at most 16 files and at most about 6 MB together. Open each one with the Read tool before you describe it. Mark in pictures.md the three to six that show the most.
- The full stills and the raw data under `/private/tmp/ter-kh4g/1b/pictures/`, named in pictures.md.

## Acceptance

- Each part has its numbers and its images, and pictures.md says what was not checked.
- Every image that pictures.md describes was opened, and each description names the columns.
- The cloud dollars of this ticket are stated from the job log.

## Out of scope

Any change of the probe's rules beyond what the parts name, the cost, the design document and the real mechanism.


## Notes

**2026-11-14T23:56:25Z**

Launched 2026-11-14 from 24a320541 as a ticket lead on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId: ter-kh4g-1b-t1-gxzg-lead-1. taskId: node:delegated-task:command%3Amcp%3A3579fc31-496e-48d6-a7df-6042784d08cb%3Adelegate-task%3Ater-kh4g-1b-t1-gxzg-lead-1. childThreadId: thread:delegated-task:command%3Amcp%3A3579fc31-496e-48d6-a7df-6042784d08cb%3Adelegate-task%3Ater-kh4g-1b-t1-gxzg-lead-1.

**2026-11-14T23:56:25Z**

Answer of the line orchestrator in the lead's brief: the hair view of scripts/lamp-hall-16-hair.json holds 80,465 sheets among 270,728 surface pixels, more than the hair cards alone. Part 3 finds out which surfaces they are and treats large sheets at a grazing angle as its main subject beside the hair cards.
