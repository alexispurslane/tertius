# Point and spot light shadows

Ticket: `ter-s9cv`. The user approved this design. The part "Lights instead of one sun" has its own ticket, `ter-bpvl`, which waits for automatic exposure (`ter-zzyr`).

## Goal

Let point lights and spot lights cast shadows, within a fixed budget, so that interiors such as a ship's corridors look right. Today only the sun casts shadows.

## Design

- `RenderLight` is a union of `PointRenderLight` and `SpotRenderLight`, so both case records get a `CastsShadow` flag, and the union stores it once for both. `PointLightData` and `SpotLightData` get the same flag. This is a change to Core.
- All local-light shadows share one tiled shadow atlas, a large depth texture divided into tiles. A spot light uses one perspective tile. A point light uses six tiles, one for each cube face.
- Every frame, the renderer ranks the shadow-casting lights by importance: projected size on screen, irradiance and distance. Only the top lights get tiles, within a tile budget.
- The renderer caches a tile while its light and its casters do not move. A cached tile costs nothing to render.
- The renderer culls casters on the CPU against the light's reach sphere or cone (see "Reach" below).
- Sampling uses PCF (percentage-closer filtering) with a depth bias scaled to the tile's texel size.

## Lights instead of one sun

On 2026-10-11, the user moved a second goal into this epic: the sun stops being a special case. On 2026-10-21, the user chose this design from two designs. The choices were SI units, one light list for the renderer, and a reach derived from the power.

### How the sun works today

- A scene sets `World.SunFrame` (one frame), `World.SunColor` and `World.SunIntensity`.
- Each frame, the snapshot takes the sun's direction from the camera toward that frame's origin. It takes the sun's angular radius from the render radius of the frame's owner.
- The brightness is a fixed number from the scene. It does not fall off with the distance to the star.
- Only the sun gets cascaded shadow maps, far shadows (the sphere tests in the lighting shader), and baking into impostor captures. Every other light is a point or spot light with a range, no disc and no shadows.

### The model

A light is a property of an entity. Every light is the same kind of thing: a sphere that emits a power. A star is an entity with a point light, as a lamp is.

The renderer gets one list of lights. It shades each light per pixel, in one loop, with the direction, the distance and the irradiance at that pixel. Whether a light is far follows from geometry. Far chooses only how the light casts shadows.

### Units

Every light quantity is in SI units of radiant energy. The renderer treats visible light as one band and does no photometric conversion. A color is linear RGB, and it scales a quantity per channel.

| Quantity | Unit | Where it is used |
|---|---|---|
| Power | W | `PointLightData.Power`, `SpotLightData.Power` |
| Intensity | W/sr | Derived from the power, see "Light data" |
| Irradiance | W/m² | The light that reaches a surface |
| Radiance | W/(m²·sr) | Emissive materials, the emission of puffs, the sky and ground colors of the ambient light, and the HDR image |

Two examples set the scale. The Sun has a power of 3.8 × 10^26 W, which gives 1,361 W/m² at 1 AU. A lamp with a power of 25 W gives about 0.5 W/m² at 2 m.

The hemisphere ambient light keeps its formula. Its sky and ground colors become radiances. A surface under a uniform sky of radiance L gets the irradiance πL, and a matte surface sends back the radiance albedo × L. That is the formula in `ambientLight` today.

### Light data

```csharp
public readonly record struct PointLightData(Pose Offset, Vector3 Color, float Power, float Radius, bool CastsShadow);

public readonly record struct SpotLightData(
    Pose Offset, Vector3 Color, float Power, float Radius, bool CastsShadow, float InnerConeAngle, float OuterConeAngle);
```

- `Power` is the radiant power that leaves the light, in watts. A light whose power is zero or less, or not finite, adds no light.
- `Radius` is the radius of the emitter, in meters, zero or more. Today the sun's angular radius comes from the bounding radius of its owner's render row. That is wrong for a lamp, because its bounding radius is the fixture, and its light has an offset from the entity.
- `Intensity` and `Range` go away.
- A point light sends its power equally in every direction. Its intensity is `I = Power / (4π)`.
- A spot light sends its power through its outer cone. Its intensity is `I = Power / (2π (1 − cos OuterConeAngle))`. A narrower cone with the same power is brighter, as a real spot light is. The fade between the inner and the outer cone is not counted in that solid angle. We accept that small error.
- The irradiance at distance d is `Color · I / d²`, times the cone factor of a spot light.

The render cases `PointRenderLight` and `SpotRenderLight` change in the same way. They also get the values of the previous snapshot (see "Interpolation").

### Reach

The reach of a light is the distance at which its irradiance falls to the cutoff irradiance `E_cut`:

```
reach = sqrt(I / E_cut)
```

- `E_cut` is one constant of the renderer. A white matte surface at the brightest allowed exposure turns it into 1/256 of output white. So a light below `E_cut` changes the output by less than one 8-bit step at any exposure. The lower clamp of the automatic exposure (`ter-zzyr`) sets its value.
- The renderer computes the reach of each light in each rendered frame. It culls lights by the reach, and the smooth window of Karis 2013 takes the reach as its range. No second value can disagree with the power.
- A light so far away that `d²` overflows a float, beyond about 1.8 × 10^19 m, gets an infinite `d²`. That is beyond any reach, so the renderer culls the light with no special rule.

A lamp without a shadow tile lights the far side of a wall within its reach. Its reach is longer than the explicit ranges of today. The user accepted this. If a scene shows the leak, the fix is a shadow tile for that lamp, not a range.

### Far and near

If its distance from the camera is at least `FarDistance`, a light is far:

```
FarDistance = 10,000 × ShadowCascades.Distance = 2 × 10^7 m (20,000 km)
```

- `IsFar(position)` is a pure predicate of the renderer, and unit tests pin it.
- At that distance, across the 2 km shadow-map region, the direction of the light changes by at most 10^-4 rad. Its irradiance changes by at most 0.02%.
- A caster h meters above a receiver at distance x from the camera moves its shadow by h·x / d. A cascade texel at distance x grows with x too. For a caster 100 m high, the move is about a hundredth of a texel. So a light that crosses the line changes its shadow technique with no visible pop.
- In normal play no light crosses the line. Lamps reach tens of meters, and stars are millions of kilometers away.

### Shadows of each light

The shadow slot of a light says how it casts shadows. It is a closed set, so it is a custom `[Union]` in the shape of `Tertius.Target`. Its default holds no case and means no shadows.

| Case | Fields | Meaning |
|---|---|---|
| `CascadeSet` | `Index`, `Occluders` | A far light with cascaded shadow maps, plus far shadows beyond the maps' reach |
| `FarShadowsOnly` | `Occluders` | A far light with far shadows everywhere, and no maps |
| `AtlasTiles` | `First`, `Count` | A near light with tiles in the atlas: one for a spot light, six for a point light |

`Occluders` is an `OccluderRange(Start, Count)` into the occluder buffer, in the shape of `InboxRange`.

The batcher chooses the slots with pure predicates, in this order:

1. A light without `CastsShadow` gets no slot.
2. The far lights are ranked by the luminance of their irradiance at the camera. Ties keep the snapshot order, which is the dense order of the entity table, so every run ranks the same.
3. The first `RendererOptions.CascadedLights` far lights get a `CascadeSet`. The default is 1. Each set is four maps of `ShadowMapSize`², 64 MB at the default size.
4. Each other far light compares its irradiance at the camera with that of the brightest far light. If the ratio is at least `FarShadowShare`, the light gets `FarShadowsOnly`. `FarShadowShare` is a named constant that we measure. A dimmer far light gets no slot, because its shadows cannot show beside the brightest.
5. The near lights get `AtlasTiles` by the ranking of the section "Design", within the tile budget.

Each far light has its own occluder list. The pruning rule of today uses the light's own angular radius, `asin(Radius / d)`. The split between casters in the maps and casters beyond them exists only for a light with a `CascadeSet`. For a light with `FarShadowsOnly`, every caster is a sphere everywhere.

A sphere shadow of a long or flat caster near the camera looks wrong. If a scene shows it, the fix is to give that light a cascade set, not a special rule.

The far shadows cost the far lights with shadows times their casters, per pixel. Each list is pruned against its own light, so the cost grows only with shadows that can show.

### Interpolation

Today a light sits at the position of the last tick, while instances interpolate between ticks. A lamp on a moving ship lags its fixture by up to one tick. The analogue of a light is an instance, so a light interpolates in the same way.

- `PointRenderLight` and `SpotRenderLight` get `PreviousPosition`. A spot light also gets `PreviousDirection`.
- The snapshot builder fills them by the rule for instances, including the rule for a camera that changed frame.
- The batcher interpolates them with the alpha of the instances, before it computes the reach and `IsFar`.

### Precision

Light positions reach the renderer as floats relative to the camera. The snapshot builder computes them in double precision, as today.

- A star at 1 AU has a position error of about 10 km, which turns its direction by less than 10^-7 rad.
- In the shader, `lightPosition − position` loses the pixel's position for such a light. The result is the direction to the star, with the same error.

### Exposure and the HDR image

The HDR target and the emissive target of the G-buffer are half floats, with a largest value of 65,504. In SI units, a sunlit white surface sends about 430 W/(m²·sr), and the Sun's disc about 2 × 10^7 W/(m²·sr).

- Every pass that writes the HDR image or the emissive target writes pre-exposed radiance: the radiance times the exposure of the previous rendered frame, clamped to 65,504.
- Tone mapping then multiplies by no exposure.
- The automatic exposure (`ter-zzyr`) owns the exposure value, and it gives the value to those passes.
- Bloom (`ter-63wr`) works on the pre-exposed values.

### The shaders

- `sunLight` and `localLights` in `lighting.glsl` become one loop over the light list.
- For each light, the loop computes the direction, the distance, the irradiance and the reach window per pixel. It then applies the light's shadow slot.
- `GpuLight` gets the intensity, the radius, the reach, the kind and the shadow slot. The frame constants lose `sunDirection` and `sunRadiance`. The cascade matrices move to a buffer with one entry per cascade set.
- `scatteredLight` (the lit puffs) runs the same loop. A light with a cascade set samples it with the light direction as the normal, as the sun does today.
- The impostor capture runs the same loop with no shadow maps, as today.

### The impostor signature

Today the signature of a capture keeps the sun apart from one sum over the local lights. With one list, it keeps the irradiance and the direction in object space of each light that reaches the object. The capture error takes, for each light, the change of its direction times its share of the total light. It also takes the relative change of the total light. In a scene lit by one star, this is the rule of today.

### What goes away

- `World.SunFrame`, `World.SunColor` and `World.SunIntensity`, and their fields in `RenderView`.
- `RenderSun`, `RenderSnapshot.Sun`, `PreparedScene.Sun` and `SceneBatcher.HasSun`.
- `sunDirection` and `sunRadiance` in the frame constants of the shaders.
- `LightBasis.FromSunDirection` becomes `LightBasis.FromDirection`.
- `Eclipse.SunVisibility` becomes `Eclipse.Visibility`, which takes the light's direction and angular radius.

### Every reader and writer

| Value | Written by | When | Read by |
|---|---|---|---|
| `Power`, `Radius`, `CastsShadow` of `LightData` | Scenes, content files and components, through `RenderData.Light` | During a tick, as today | `RenderSnapshotBuilder` |
| `PointRenderLight`, `SpotRenderLight`, with their previous values | `RenderSnapshotBuilder` | At each sync point, for the finished tick, as the instances | `SceneBatcher`, `ImpostorManager` |
| Interpolated position and direction | `SceneBatcher` | Each rendered frame | Reach, `IsFar`, `GpuLight` |
| Reach | `SceneBatcher` | Each rendered frame | Light culling, caster culling of the atlas, `GpuLight`, the reach window in the shaders |
| `IsFar` | `SceneBatcher` | Each rendered frame | The ranking and the shadow slots |
| Shadow slot | `SceneBatcher` | Each rendered frame | `ShadowPass`, the atlas pass, `GpuLight`, the shaders |
| Occluder ranges | `SceneBatcher` | Each rendered frame | The shaders, and `Eclipse.Visibility` in the impostor signature |
| Cascade sets | `ShadowPass` | Each rendered frame | The lighting pass, the transparent pass, the lit puffs |
| Exposure of the previous frame | Automatic exposure | Each rendered frame | Every pass that writes the HDR image or the emissive target |

### The flow of each kind of light

| Kind | Path | Shadow slot |
|---|---|---|
| The brightest star | Builder, `RenderLight`, batcher, `GpuLight`, one shading loop | `CascadeSet` |
| A second star, bright enough | The same path | `FarShadowsOnly` |
| A dim star | The same path | None |
| A star beyond its reach | Culled by the batcher | None |
| A lamp with tiles | The same path | `AtlasTiles`, six tiles |
| A spot light with a tile | The same path | `AtlasTiles`, one tile |
| A near light past the tile budget, or without `CastsShadow` | The same path | None |
| A light whose power is zero or less | Not added by the builder | None |

Every kind takes one path. Only the shadow slot differs, and pure predicates choose it.

### Compared with a design from scratch

Built from scratch around "a light is a property of an entity, in physical units", a renderer gets one list of lights. It shades them per pixel, and it picks a shadow technique for each light from geometry. This design is that. It keeps the far shadows and the cascades of today as the techniques for far lights. The hemisphere ambient light stays a world setting and is not derived from the lights. Light that a planet reflects onto a ship is out of scope.

### Scenes and content files

- Each scene gives its star a point light with the real power and radius, on the star's entity. A root frame is an entity, so a scene with the star at the root gives the light to the root's entity.
- A scene that puts its "sun" frame near moves the star to a real distance. An example is the shuttle scene, with the sun frame at `(-30, 50, 40)` m. If the star stays near, it becomes a near light.
- Every lamp gets a power in watts, a radius and `CastsShadow`.
- Every emissive material, puff look and ambient color gets a radiance in W/(m²·sr).
- Every scene is retuned. Its screenshots and reference images get a real review again.

## Steps

1. Write CPU tests first: ranking, the budget, cache hits and misses, and culling.
2. Add the atlas, the shadow pass for local lights and the sampling in the lighting pass.
3. Add demo scenes: a spot light over a box, and a point light in a room. Review them by eye in headless screenshots. The snapshot tests epic (`ter-rtxu`) comes after this epic and turns them into reference images.
4. Measure the cost with GPU timestamps for 4, 16 and 64 shadowed lights. Report the numbers. Keep the feature switchable.
5. Lights instead of one sun (`ter-bpvl`), after automatic exposure (`ter-zzyr`) is merged. Write its tests first: `IsFar`, the reach, the ranking of far lights, the shadow slots, the occluder lists, the interpolation of lights and the impostor signature. Then replace the special-cased sun, and move the scenes to SI units.
6. Measure the cost of one, two and four far lights, with and without cascade sets. Report the numbers.
