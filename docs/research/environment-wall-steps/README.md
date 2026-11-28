# Brightness steps on the nose wall

The base is `d3123fe715dea1fd141649271c315096712d72a2`. The Mac rendered [the hallway walk](../../../scripts/hallway-walk.json) in the orbit scene at 1280 × 720, with gray geometry and fixed exposure of 3.79 stops. The camera moved 5 cm per frame. The scene used the default environment map, screen tracer, bloom and FSR 3.1 at a render scale of 2/3. [Issue #26](https://github.com/alexispurslane/tertius/issues/26)

The wall patch is fixed in the ship's reference frame, on the wall beside the nose window. The measurement uses the same projection and crop as [#21 and #25](../outer-grid-guard/README.md). It also measures the crop with a six-pixel margin on all sides. Display luma is the Rec. 709 weighted mean of the stored red, green and blue codes on a 0–255 scale. The measurement requires at least eight rows in both frames. A wall sample is outside the local light grids when its distance along the hallway exceeds 7.5 m.

At frame 38 the patch was 58.33 m from the camera and brightened by 9.092 levels. The patch with the margin brightened by 9.120 levels. The largest later increase in the patch was 2.06 levels at 17.83 m. Many smaller steps recurred after 18 frames, or 0.90 m of camera travel.

## Capture controls

Each short walk keeps the same first 90 frames. These controls turn off screen tracing and bloom and keep exposure at 3.79 stops. The environment-off control uses −3 stops to keep the wall away from display saturation. Changes are signed at frame 38. The last column gives the largest absolute change in the patch during the short walk.

| Control | Patch | Patch with margin | Largest patch change |
|---|---:|---:|---:|
| Original environment map | +9.484 | +9.517 | 9.484 |
| Environment map off | +0.012 | −0.001 | 0.034 |
| Diffuse environment term removed | −0.280 | −0.263 | 0.318 |
| Specular environment term removed | +9.650 | +9.662 | 9.650 |
| Box projection off | +10.797 | +10.800 | 10.797 |
| Diffuse lookup fixed at a single texel center | +9.811 | +9.833 | 9.811 |
| Capture held from the first frame | −0.211 | −0.195 | 0.249 |
| All six irradiance faces updated on each capture update | +9.438 | +9.475 | 9.438 |
| All capture faces refreshed on every frame | +0.066 | +0.049 | 3.901 |

The jump comes through the diffuse term. The fixed lookup still jumps while it reads the same irradiance texel. Recomputing all diffuse directions on each capture update still gives the jump. When all captures run every frame, the 9-level jump spreads across frames 34 to 37. The largest increment is 3.901 levels at frame 34. The capture sees a rapid change in incoming light, and the ordinary capture schedule publishes the change in one frame.

For the term controls, set `indirectDiffuse` or `indirectSpecular` to `vec3(0.0)` in `composite_pass.glsl`. For the parallax control, set `projectsEnvironmentBoxes` to false in `environment.glsl`. For the fixed lookup, replace the coordinate of the irradiance texture read with `vec4(faceRay(4u, vec2(8.5 / 16.0)), float(map))`, which reads the center of one texel of the positive-Z face. For the held capture, clear the scheduled masks in `EnvironmentMap.FacesFor` before the existing refresh rule fills them for the first draw. For all irradiance faces, give the irradiance dispatch in `EnvironmentPrefilterPass.RecordMap` the face range from 0 through 5 every time it runs. For fresh captures, pass `everyFace: true` to `EnvironmentMap.FacesFor`.

## Matched forward and reverse walks

The new renderer blends the diffuse contribution of each captured face. All affected wall orientations start responding on the capture's draw. Each orientation keeps its own solid-angle and cosine weights. The hallway has three maps and captures one face per draw, so a new contribution reaches its target across 18 draws. A refresh starts with the destination light on its first draw.

The full match contains 2,352 frames per renderer. That includes both 1,100-frame walks, the holds at their ends, and the cuts out of and back into the hallway. This table uses the pairs for which the wall stays outside the light grids. The 95th percentile and largest change use the absolute change between consecutive frames, in display-luma levels. [Measurement data](measurements.json)

| Walk and crop | Measured pairs | Largest, main | Largest, new | 95th percentile, main | 95th percentile, new | Above 0.6, main | Above 0.6, new |
|---|---:|---:|---:|---:|---:|---:|---:|
| Toward nose, patch | 1,023 | 9.092 | 1.800 | 0.446 | 0.350 | 33 | 11 |
| Toward nose, patch with margin | 1,023 | 9.120 | 1.595 | 0.456 | 0.342 | 33 | 10 |
| Back along the hallway, patch | 1,021 | 3.498 | 0.724 | 0.449 | 0.338 | 33 | 5 |
| Back along the hallway, patch with margin | 1,021 | 3.042 | 0.720 | 0.446 | 0.337 | 34 | 4 |

At frame 38, the patch changes by −0.258 levels after the fix. The patch with the margin changes by −0.201 levels. The largest remaining forward change is +1.800 levels at frame 48, 57.83 m from the wall. That change is +1.705 with the screen tracer disabled, +1.294 with bloom disabled, and +0.631 at native resolution with FSR disabled. The native control changes the input sampling as well as turning off FSR. The matched tests use the default FSR and bloom settings.

The camera returns to the same point after the cut. The first frame back reads 60.698 levels in the patch on both renderers. The decoded RGB hashes match for all 32 frames, starting with the cut to the far camera and including all 30 frames after the cut back. The regression also cuts behind an opaque panel while the initial diffuse contribution is still blending. Both measured directions receive zero radiance from the blocked emitter on the first draw after the cut.

The specular filters still read the original capture schedule. The reflection tests verify that two receivers at different positions see their own box intersections, and that the capture's open-sky share masks the reflected sky. The settled diffuse cubes match a fresh capture of the same scene. The convolution and combine shaders pass SPIR-V validation for Vulkan 1.3.

The contribution buffer requests 442,368 bytes per map and 1,008 bytes for a shared header. That is 1,328,112 bytes for the three hallway maps and 18,580,464 bytes at the 42-map limit. These are buffer sizes before any memory alignment required by the driver. Each captured face still uses 1,572,864 source and receiver pairs at the default 128-texel capture size. Combining the six weighted contributions adds one pass over each served map's 1,536 diffuse directions. This ticket uses Mac runs for rendering correctness.

### Vulkan resources

The prefilter adds one compute storage-buffer binding to its existing two descriptor sets. The irradiance pipeline still uses the existing RGBA16F storage image and the same cube-array features. The largest added or reused resource fits the limits that Vulkan guarantees. [Vulkan required limits](https://docs.vulkan.org/spec/latest/chapters/limits.html#limits-minmax)

| Resource | Maximum used by the irradiance update | Guaranteed limit |
|---|---:|---:|
| Contribution buffer descriptor range | 18,580,464 bytes | 134,217,728 bytes |
| Storage-buffer bindings per compute stage | 1 | 4 |
| Storage-image bindings per compute stage | 4 | 4 |
| Irradiance cube array layers | 252 | 256 |
| Workgroups on a single dispatch axis | 252 | 65,535 |
| Invocations per workgroup | 64 | 128 |
| Shared memory per workgroup | 3,328 bytes | 16,384 bytes |
| Push constants used by the convolution | 16 bytes | 128 bytes |

The buffer indices cover 252 capture slots at the map limit. Each slot has 1,536 records. The draw workgroup selects one set bit in the captured-face mask. No workgroup can address a capture slot beyond the served maps. Vulkan validation on the Mac and SPIR-V validation check the bindings and the shaders. The limits above come from the Vulkan specification.

### Frames at the original step

| Frame | Wall patch |
|---|---|
| [Main, frame 37](frames/main-37.png) | The area just right of the far window reads 60.42 levels. |
| [Main, frame 38](frames/main-38.png) | After 5 cm of camera motion the same patch brightens to 69.52 levels. |
| [New, frame 37](frames/fix-37.png) | The matched wall patch reads 60.42 levels before the capture update. |
| [New, frame 38](frames/fix-38.png) | The matched patch reads 60.16 levels on the update draw. |

## Reproduce

Generate the round-trip script and render it from the repository root. The round trip preserves every frame of the original forward walk. It then walks back along the hallway and cuts to an outside camera and back to the same point in the hallway.

```powershell
New-Item -ItemType Directory -Force /tmp/environment-wall-steps
nix develop --command python3 -I docs/research/environment-wall-steps/walk.py prepare /tmp/environment-wall-steps/walk.json
nix develop --command dotnet run -c Release --project src/Tertius.Demo -- --script /tmp/environment-wall-steps/walk.json --headless --gray-box --out /tmp/environment-wall-steps --video /tmp/environment-wall-steps/walk.mkv --fps 60 --exposure 3.79
nix develop --command python3 -I docs/research/environment-wall-steps/walk.py measure /tmp/environment-wall-steps/walk.mkv /tmp/environment-wall-steps/measure
```

Use `prepare --short` for the 90-frame capture controls. Run that script with `--screen-trace off --bloom off`.
