---
id: ter-qt72
status: in_progress
deps: []
links: [ter-89xq]
created: 2026-11-22T16:23:08Z
type: bug
priority: 0
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, dynamic-resolution]
---
# The whole picture wobbles horizontally when the dynamic render scale changes the render size: the projection takes its aspect from the rounded render size

Found by the user on 2026-11-22 in a side-by-side video of the lamp hall under the dynamic render scale: the whole picture wobbles in and out horizontally whenever the render size changes, on main and on Design X alike. The user's order: "we should fix the wobble immediately". The orchestrator settled the design below on 2026-11-22. This ticket goes ahead of every ticket of the back-face line that has not started.

Video: /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144/docs/research/design-x-acceptance-2026-11-22/pics/switch-cabin-dynamic-16ms.mp4 (each side shows its render size per draw).

## The cause, as read from main's code. Nobody measured it yet: the red tests do.

The render size rounds each axis on its own (src/Tertius.Rendering/UpscalingSetting.cs):

```csharp
    internal Extent2D RenderExtent(Extent2D output, float scale) =>
        this switch
        {
            NoUpscaling => output,
            Fsr3Upscaling => new(Scaled(output.Width, scale), Scaled(output.Height, scale)),
        };

    private static uint Scaled(uint pixels, float scale) => Math.Max(8, (uint)(pixels * scale) / 8 * 8);
```

where

- `output` = the window's size in output pixels, which the upscaler fills.
- `scale` = the render scale of the draw, the render size over the output size on each axis, which the dynamic render scale changes from draw to draw.
- `Scaled(pixels, scale)` = that axis of the render size: `pixels * scale`, rounded down to a multiple of 8, and at least 8.

So the render width over the render height is not the window's width over its height. For a 16:9 window it moves between about 1.775 and 1.786 (1904 x 1072, 1912 x 1072, 1872 x 1048), and it is exact at the scale 2/3 (1280 x 720).

The renderer passes that render size to the batcher (src/Tertius.Rendering/VulkanRenderer.cs, about line 771):

```csharp
        Batcher.Prepare(
            snapshot: snapshot,
            alpha: alpha,
            ...
            width: (int)pending.RenderSize.Width,
            height: (int)pending.RenderSize.Height,
```

and the batcher takes the camera's aspect from it (src/Tertius.Rendering/Scene/SceneBatcher.cs, `InterpolateCamera`):

```csharp
        float aspect = (float)Math.Max(width, 1) / Math.Max(height, 1);
        float tanY = MathF.Tan(fov * 0.5f);
```

where

- `width`, `height` = the render size of the draw, in render pixels.
- `aspect` = the projection's width over its height. With `tanY` it gives the horizontal field of view.
- `fov` = the camera's vertical field of view, in radians.

So the camera's horizontal field of view follows the rounded render size, and the upscaler stretches the picture back to the window's fixed shape. Prediction: horizontal distances on screen change by up to 0.57 % between those sizes, and nothing moves vertically or at the scale 2/3.

## The class of the bug

A quantity of the picture taken from the resolution that samples it. The picture's shape is a fact of the window. The render size says only how many samples the picture gets. Any code that reads a shape, a field of view or a pixel's footprint from the render size, or assumes square render pixels, is a member of the class.

## The model (settled)

The camera's shape is the window's shape. The picture's aspect ratio is one fact with one authority, the output extent. The render size is a resolution only: how many samples the picture gets, never what the camera sees. So the projection takes its aspect from the output extent, and every count of pixels keeps coming from the render size.

- What changes: the one place that turns a size into the projection's aspect. The lead chooses the signature. One fitting form: `Batcher.Prepare` takes the picture's aspect (output width over output height) beside the render size, so that no caller can build a projection from the render size again.
- What follows: render pixels are no longer exactly square, by up to about 0.5 %. The lead audits every place in C# and GLSL that derives an aspect, a horizontal field of view or a pixel's footprint from the render size, or assumes square render pixels: the jitter's offsets, the motion vectors, FSR 3.1's inputs (its vertical field of view and whatever it derives from the render size), the screen tracer and its plan, the depth and light pyramids, the cascade fitting, the environment maps, and every upscaling setting beside `Fsr3Upscaling`. Each place gets a fix or a one-sentence reason why it is right as it is. The audit's table goes into this ticket's note.

## Rejected, with the reason

Render sizes that keep the window's exact shape. A 16:9 window with steps of 8 has such a size only every 128 x 72 pixels, and a window of another shape can have none below its own size. The user also decided on 2026-11-22 that nothing restricts the render scale controller for another feature's sake.

## Tests, red first

1. The projection's horizontal field of view is the same for every render size at a fixed output extent: a unit test of the batcher, with 1904 x 1072, 1912 x 1072, 1872 x 1048 and 1280 x 720 for one 16:9 output.
2. A world point lands at the same place of the output image, within a small share of an output pixel, when the same view is drawn at those render sizes: a renderer test through the real upscaling path. It fails today by up to about 0.5 % of the distance from the centre.
3. Lock-in: a size whose ratio is not the window's, such as 1872 x 1048, so that a later change cannot pass by luck at 1280 x 720.

Each red test's numbers go to the line orchestrator at once, because they are the first measurement of the cause.

## Acceptance

- No reference image changes, because every reference take runs at an exact ratio. A changed reference image is a finding: report it, and do not approve it silently.
- One side-by-side video of the lamp hall under a played-back moving render scale: main before the fix on the left, the fix on the right, real frames at 60 a second, one fixed exposure, H.264 at CRF 12 or lower. With it, the distance between two tracked vertical edges per frame: after the fix it stays constant within about 0.2 output pixels. The Design X line's acceptance lead has the method (a logged scale played back, tracked edges). Ask the line orchestrator to connect you if you need its scratch patch.
- The game runs under the dynamic render scale. Judge under it, and never hold the render size to help this fix.
- The full Checked suite, then the full Release suite, one configuration at a time. The Linux run of the changed test classes on lavapipe.
- ARCHITECTURE.md says where the projection's aspect comes from.
- One reviewer. Coverage and mutants of the changed lines as CLAUDE.md says: Rendering code goes to tools/mutate.py.

## Out of scope

A change of the render scale controller or of the rounding to multiples of 8. Other tickets of the dynamic resolution's epic ter-89xq: keep the diff small, because other sessions work in those files.


## Notes

**2026-11-22T16:25:02Z**

2026-11-22, the line orchestrator. The ticket lead is launched. taskId and child thread: node/thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1. clientRequestId: slabs-4-ter-qt72-lead-1. Target: claudeAgent, claude-opus-5-5, effort high, role implementation, isolation worktree. Its reviewer: the same target, role review. Base and review base: main 4298ae7ec. Cloud money: $0.30. The research take t1-r9vj starts no build and no render until this fix is in its review. Before the launch I removed the build output (bin and obj folders, 3.2 GiB) of three worktrees of the line whose work is merged or mine: t3-58f87f59-3970e2671e4b1d5d, t3-7e999be0 and t3-1b834d57.
