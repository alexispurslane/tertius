# Orange light from the pilot's helmet in the cockpit (#20)

Issue #20 asks why the 1:41 cockpit shot of the demo film lost the orange light that the pilot's helmet reflects onto
the sloped panel above it. The legacy renderer at 860685c71 showed that light. The renderer with the merged directional
light volume (main at 90a6be90d) shows less of it. This folder holds the evidence and the diagnostic tools of the
investigation. No production change for #20 came out of it.

## Result

- The helmet's light on the panel is small in both renderers. Over the panel region (`panelOrange`, a 120 × 130 px crop
  at +770+65 of the 1280 × 720 frame), the mean helmet light of 120 frames is (+1.95, +0.91, +0.31) in 8-bit sRGB levels
  in legacy and (+0.74, +0.32, +0.09) with the merged grid. Helmet light is the image with the normal helmet minus the
  image with a black helmet, at the fixed exposure 6.0043726.
- An upper bound holds the grid's possible gain to about +1.7 levels of red. The bound is the unoccluded form factor
  from the helmet to the real panel receiver sample: perfect transport of the volume's first bounce adds at most that.
- In legacy, the light comes from the panel's own reflection rays and screen trace. It does not depend on the
  specular light at the surfaces that the reflection rays hit (see "Attribution check" below).
- The grid draws the helmet's light as one narrow streak along the strut, a ray effect of its 32 discrete transport
  directions. Legacy spreads it over the lower-left half of the panel, with grain.

## Evidence

| File | What it shows |
|---|---|
| `still-frame60.jpg` | Frame 60 of the shot, legacy and merged grid. |
| `panel-means.jpg` | Top: the 120-frame mean of the canopy crop (560 × 315 px at +688+4). Bottom: helmet light × 16, where mid-gray is zero. |
| `helmet-light-by-directions.jpg` | Helmet light × 16 for legacy and for the grid with 32 current, 128 Fibonacci and 512 Fibonacci directions. |
| `hit-specular-attribution.jpg` | Helmet light × 16 for legacy and for legacy without hit-side specular light, and their difference × 64. |
| `panel-moving.mp4` | 4 s of the shot in motion: legacy, legacy with a black helmet, merged grid, merged grid with a black helmet. |

### Pixel decomposition

The composite debug modes (`tools/composite-debug-modes.patch`) split the merged grid's panel pixel at `panelOrange`.
Values are pre-exposed linear red:

| Part | Total | From the helmet |
|---|---|---|
| Direct light | 0.0359 | 0 |
| Volume diffuse light | 0.0186 | 0.00104 |
| Volume broad specular light | 0.0027 | 0.00029 |

The helmet gives about 2.3% of the pixel. The panel's roughness is 0.50, so it gets only the broad specular lobe.

### Direction count

The stored helmet light is the red diffuse lobe that the volume holds at the real panel receiver sample (position
(−0.32, +0.19, −1.11) m from the camera, normal (−0.39, −0.84, +0.38)). The share of the bound compares it with the
unoccluded form factor of 0.000065. The pass times are Mac times under load, so compare them only with each other.

| Direction set | Stored helmet light | Share of bound | Panel pixels (8-bit red) | Volume pass |
|---|---|---|---|---|
| Current 32 | 0.000030 | 46% | +0.8 | 2.15 ms |
| Fibonacci 32 | 0.000077 | 118% | +1.6 | 2.17 ms |
| Fibonacci 128 | 0.000047 | 72% | +0.8 | 3.60 ms |
| Fibonacci 512 | 0.000043 | 66% | +0.8 | 10.69 ms |

The 512-direction result converges near 66% of the bound. Fibonacci 32 overshoots the bound because one of its
directions lines up with the helmet. More directions change the stored radiance, but the panel's pixels change by less
than one level.

### Attribution check

In legacy, `leavingLightOf` in `screen_trace.glsl` gives a reflection ray the diffuse light at the hit point plus the
hit point's specular factor times its direct specular and specular history. The volume's source holds only diffuse
light and emission. The check removes the specular part (`tools/legacy-no-hit-specular.patch`, which sets
`vec3 reflection = light;`) and renders the same shot with and without the black helmet.

| Legacy variant | Helmet light at `panelOrange` (8-bit RGB) | Helmet light in the canopy-crop map at the same region, ÷ 16 |
|---|---|---|
| Unmodified | (+1.95, +0.91, +0.31) | (+1.70, +0.85, +0.33) |
| Without hit-side specular | (+1.95, +0.92, +0.31) | (+1.70, +0.86, +0.33) |

The two maps in `hit-specular-attribution.jpg` match in shape. Their difference is grain, with a mean under 0.01 levels.
So the difference between legacy and the grid does not come from the hit-side specular light. The two columns
measure the region on different images (the full frames, and the 120-frame mean crop), so they differ from each other.

Remaining uncertainty: the check removes the specular light at the hit points only. It does not test legacy's
temporal accumulation or its screen-space ray footprint, which also shape where the light lands.

## Related work

- #22 records a separate defect of the volume: the coarse grids shade the finer surfaces that they inherit at one
  point per tile and restrain their color after mixing. A tested prototype is on this branch as its own commit. It
  raises the helmet's stored light in the 0.5 m grid from 0.0043 to 0.0225 and in the 1 m grid from 0 to 0.0056. It does not
  visibly change the panel's pixels, so it does not fix #20.
- #21 is the hallway range study. It is independent of this work.

## Tools

The tools are diagnostic only. Paths inside them point at the scratch checkouts of the investigation.

| File | Use |
|---|---|
| `helmet-control2.py` | Patches `GlbReader.cs` to recolor one texture, chosen by its encoded size, through `TERTIUS_DEBUG_RECOLOR_BYTES=1637884` (the helmet) and `TERTIUS_DEBUG_RECOLOR_MODE` = `black`, `gray` or `blue`. The black helmet keeps its geometry and shadow. |
| `legacy-no-hit-specular.patch` | The attribution check on 860685c71, with the recolor control. |
| `composite-debug-modes.patch` | `TERTIUS_DEBUG_COMPOSITE` = 1 position, 3 geometric normal, 4 volume diffuse, 5 broad specular, 6 roughness and factors, 7 direct light. |
| `render2.sh` | Renders a script headless at the fixed exposure under the shared GPU lock. |
| `cockpit.json`, `cockpit61.json` | Scripts that reach the 1:41 shot. |
| `measure.sh`, `meanpanel.sh` | Region means and the 120-frame mean crop. |
| `helmet_at_panel.py`, `analytic.py` | The receiver sample and the form-factor bound. |
| `fib-dirs.py` | Patches the volume to use N Fibonacci directions with equal weights. |

The blue helmet is not a fair control. Under the warm cabin lamp, a mixed blue bucket reaches saturation 0.61, so the
volume's color restraint keeps about 6% of it. The black helmet has no such asymmetry.
