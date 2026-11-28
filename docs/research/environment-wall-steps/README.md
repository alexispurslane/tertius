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

## Reproduce

Generate the round-trip script and render it from the repository root. The round trip preserves every frame of the original forward walk. It then walks back along the hallway and cuts to an outside camera and back to the same point in the hallway.

```powershell
New-Item -ItemType Directory -Force /tmp/environment-wall-steps
nix develop --command python3 -I docs/research/environment-wall-steps/walk.py prepare /tmp/environment-wall-steps/walk.json
nix develop --command dotnet run -c Release --project src/Tertius.Demo -- --script /tmp/environment-wall-steps/walk.json --headless --gray-box --out /tmp/environment-wall-steps --video /tmp/environment-wall-steps/walk.mkv --fps 60 --exposure 3.79
nix develop --command python3 -I docs/research/environment-wall-steps/walk.py measure /tmp/environment-wall-steps/walk.mkv /tmp/environment-wall-steps/measure
```

Use `prepare --short` for the 90-frame capture controls. Run that script with `--screen-trace off --bloom off`.
