---
id: tz-na6d
status: open
deps: []
links: []
created: 2026-11-14T14:14:55Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, moltenvk, portability]
---
# A depth clear in the middle of a rendering loses the surfaces drawn before it on the Mac (vkCmdClearAttachments under MoltenVK)

Found by the frame-time sweep on 2026-11-13, in take 18 of the environment face ticket (`t2-zcr4`).

## What the sweep tried

A geometry pass draws its far, middle and near depth ranges in three renderings. Each rendering clears the depth with its load operation. Two scratch builds drew the three ranges in one rendering instead. A range after the first cleared the depth to its own boundary with `vkCmdClearAttachments`: the depth aspect alone, over the whole render area.

- `edits/faceone.py` changes the surfaces of an environment face (`EnvironmentFacePass`).
- `edits/geoone.py` changes the main view's G-buffer pass (`GeometryPass`).

## What happened on the Mac

On the M1 Max under MoltenVK 1.4.2, the color attachments lost what the draws before the clear wrote.

- The face build clears only before a range that holds surfaces. Its map lost the surfaces of the ranges behind the last drawn range, such as Earth in `mirror-balls`. Of 24 stills, 19 differ from the base, and the mean falls by up to 100 %.
- The G-buffer build clears before every range. Its main view lost every opaque surface where the near range drew nothing after its clear. Of 24 stills, 18 differ.
- Two runs of one build on this Mac give the same bytes, so each difference is a changed image.

The stills are in `docs/research/frame-time-sweep-2026-11-11/env-face-t2-zcr4/`: `one-balls.png`, `one-apron.png` and `one-station-end.png`. Each shows the base on the left, the face build in the middle and the G-buffer build on the right.

The timings of both builds in take 18 are void, because both builds drew less.

## Why it matters

The call is valid Vulkan. The specification lets `vkCmdClearAttachments` clear one aspect of one attachment inside a render pass instance, and it leaves every other attachment as it is. So a correct driver keeps the colors.

The engine must not depend on a clear inside a rendering until the cause is known, on Linux too. No code on main uses the call today.

## What is not known

- The cause. One reading: MoltenVK's clear writes the color attachments, or it restarts the Metal render pass with the load operations of the rendering. The other reading: the Apple GPU's hidden surface removal drops an opaque fragment under a later opaque fragment that writes depth alone.
- The behavior on the cloud's RTX 2000 Ada and on lavapipe. Nobody ran the two builds there.

## How to find the cause

1. Run both edit scripts on the cloud card, and compare the stills with the base inside one job.
2. On the Mac, replace the call with a drawn triangle: depth compare always, depth write on, a color write mask of 0. Equal stills then point at MoltenVK's clear. Lost surfaces point at the GPU.
3. Read `MVKCmdClearAttachments` in MoltenVK's source for the path that a whole-area clear takes.

## Why the idea was tried

A rendering has a fixed cost on this Mac, and an environment face runs three surface renderings, one per depth range. The face ticket kept the three renderings. Section A14 of `docs/research/frame-time-sweep-2026-11-11/addendum.md` has the measurements.

