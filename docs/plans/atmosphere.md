# The atmosphere: a painted sky, haze and a sun tint per planet

Ticket: `ter-6vhj`, the last member of the rendering upgrade epic (`ter-sglu`, [rendering-upgrade.md](rendering-upgrade.md)). The user chose the painted design on 2026-10-31, over a scattering design (the ticket's note of that date). This doc writes down the settled rules, the types and the formulas with their units. It also gives the snapshot record and the choices that the rules left to the builder.

## Goal

A planet's air gets a look that the planet paints. Three parts make it:

- The sky. Inside the air, the sky behind every surface shows the planet's sky color, and the stars fade. Above the air, the sky stays black. At night, the sky is dark.
- The haze. Where the planet sets a haze, every surface that the eye sees through the air fades toward the haze's color with distance.
- The sun's tint. Where the planet sets a tint, the light of its sun takes that color at every point in the air.

The look reaches the environment maps through `skyRadiance` and through the shared haze function. So the ambient light and the reflections follow it with no change to the maps.

## Settled rules (User, 2026-10-31)

1. The look lives on `PlanetMedium`, beside the density that gives the density share. It is one store: no second record holds a copy.
2. The sky is a painted sky radiance, RGB in W/(m²·sr): the full-day value at the surface. Its brightness follows two rules. It is multiplied by a day factor of the Sun's elevation, so the sky is dark at night. It is multiplied by the density share at the eye, so space above the air stays black.
3. The haze is a painted haze color and a haze distance per planet. It is on only where the planet sets it. It applies in every pass that shades a surface seen through the air, the environment faces included, through one shared function.
4. The sun's tint is a fixed painted tint per planet on the Sun's light, for what lies in the air. It does not change with the time of day.
5. The sky comes from the nearest frame on the camera's chain that has a planet medium, at the eye's height in that frame. So a cabin on the surface sees the planet's sky through its windows, and orbit sees black.
6. The stars fade by day through auto exposure alone. No star rule dims them.
7. The tone curve does not change. The hemisphere ambient light, with the map off, stays as it is.
8. The render snapshot carries one atmosphere record: the planet's center relative to the camera, its parameters, and which light is its sun.

## Terms

| Term | Meaning |
|---|---|
| The planet | The frame whose medium is a `PlanetMedium`. Its origin is the planet's center. |
| The atmosphere's frame | The nearest frame of `RenderSnapshot.Chain`, camera first, whose medium is a `PlanetMedium`. If no chain frame has one, there is none. |
| R | The planet's radius, `PlanetMedium.Radius`, in m. |
| H | The scale height, `PlanetMedium.ScaleHeight`, in m. |
| c | The planet's center relative to the eye, in the camera's frame axes, in m. |
| Height h(p) | The height of a point p above the surface: max(0, \|p − c\| − R), in m. The density stays at the surface value below R, so a point below the surface has height 0. |
| Density share s(p) | e^(−h(p) / H): the density at p over the surface density, from 0 to 1. `MediumField.DensityAt` gives the same ratio. |
| Eye share s_e | s at the eye, e^(−h_e / H) with h_e = max(0, \|c\| − R). |
| The sun | The light of the snapshot that the atmosphere record names. |
| Sun elevation e | The angle of the sun above the eye's horizon, in rad, from −π/2 to π/2: asin(up · toward the sun), with up = −c / \|c\|. |
| Day factor D(e) | A number from 0 to 1 that scales the sky and the haze light by the time of day. |

## The types

`PlanetMedium` gains three optional fields. Each defaults to no look, so every planet medium of today keeps its look, which is black.

```csharp
public readonly record struct PlanetMedium(
    double Radius,
    float SurfaceDensity,
    float ScaleHeight,
    float Drag,
    Vector3 SkyRadiance = default,
    PlanetHaze? Haze = null,
    Vector3? SunTint = null
);

public readonly record struct PlanetHaze(Vector3 Radiance, float Distance);
```

| Field | Unit | Range for `MediumField.IsValid` | Meaning |
|---|---|---|---|
| `SkyRadiance` | W/(m²·sr), RGB | Finite, each channel 0 or more | The full-day sky at the surface. Zero is a black sky. |
| `Haze.Radiance` | W/(m²·sr), RGB | Finite, each channel 0 or more | The light of the haze in full day: the color that a surface far away fades to. |
| `Haze.Distance` | m | Finite and above 0 | At the surface density, the distance over which the haze lets through 1/e of the light behind it. |
| `SunTint` | A fraction per channel | Finite, each channel from 0 to 1 | What the air lets through of the sun's light at the surface. |

The snapshot record:

```csharp
public readonly record struct RenderAtmosphere(Vector3 Center, Vector3 PreviousCenter, PlanetMedium Medium, int? Sun);

// On RenderSnapshot, null without an atmosphere:
public RenderAtmosphere? Atmosphere;
```

| Field | Meaning |
|---|---|
| `Center` | The origin of the atmosphere's frame relative to the render origin, in the axes of the camera's frame, in m, as an instance's position. |
| `PreviousCenter` | The center at the previous build, relative to the same origin, as an instance's previous position. The renderer interpolates the two. |
| `Medium` | The planet medium of the atmosphere's frame, the look included. |
| `Sun` | The index in `RenderSnapshot.Lights` of the planet's sun. If no light is a point light, it is null. |

## The model

The renderer computes three values per frame on the CPU, at the interpolated eye. The shaders compute two values per shaded point.

### The sky

```
sky = min(SkyRadiance · D(e) · s_e, MaxRadiance)        in W/(m²·sr), per channel
skyRadiance(direction) = sky                            the same in every direction
```

The cap keeps a face texel of the environment map inside the half float's range, as `ARCHITECTURE.md`, "Portable Vulkan", asks of `skyRadiance`.

### The day factor

```
D(e) = smoothstep(−6°, +6°, e)
```

The sky is full from 6° of sun elevation up, half at sunset, and dark from 6° below the horizon. 6° below is the end of civil twilight, where the real sky is about a thousandth of its daytime brightness. Over a lit ground, auto exposure then shows it as black.

Without a sun, D is 0: a planet with no point light has night. An eye at the planet's center has no horizon, and D counts the sun as overhead there.

### The haze

For a surface point p, at distance d = \|p\| from the eye:

```
x  = (h(p) − h_e) / H
m  = (s_e − s(p)) / x               when |x| ≥ 0.01
m  = (s_e + s(p)) / 2               when |x| < 0.01
τ  = d · m / Haze.Distance          the optical depth, 0 or more
T  = e^(−τ)                         the transmittance, from 0 to 1
hazeLight = min(Haze.Radiance · D(e), MaxRadiance)       in W/(m²·sr)
```

m is the mean density share along the straight ray from the eye to p. The air's density falls by the factor e per H of height. The mean is exact for a flat ground, and the ray's heights at both ends come from the real sphere. So a ray along the ground takes the full haze, and a ray that climbs takes less. A ray from above the air down to the ground takes the haze of the air column that it crosses. Near |x| = 0 the quotient loses its digits, and the mean of the two ends differs from it by less than x²/12 of s_e.

The shared function, `throughAir` in `atmosphere.glsl`, gives the light that reaches the eye from a surface at p:

```
throughAir(light, cover, p) = light · T(p) + hazeLight · (1 − T(p)) · cover
```

`cover` is the share of the haze's light that the surface stands in for, per channel: 1 for an opaque surface, 1 − t for a pane of glass that lets through t, and 0 for an additive puff. With that share, the glass and the opaque surface behind it compose exactly: the haze before the pane, the pane, and the hazed light behind it add to the same sum as one haze over the whole ray.

| Pass | Where the haze applies |
|---|---|
| The haze pass (new, `haze.frag`) | Every opaque pixel of the main view, after the reflections and before the transparent pass. A dual-source blend gives `hazeLight · (1 − T) + target · T`, the opaque case of `throughAir`. |
| `transparent.frag`, `fringe.frag` | The pane's light, with `cover` = 1 − t. |
| `puff.vert` | The puff's light at its center, with `cover` = 0. |
| `environment_face.frag` | Every surface of a face, in plain radiance, with `cover` = 1. |

A sky pixel takes no haze: the sky is the painted sky. The impostor captures take none either, because they store the light at the surface. An impostor in the main view lies in the G-buffer, so the haze pass hazes it as any opaque pixel.

The haze pass runs only in a frame whose atmosphere has a haze. A frame without one records nothing in it.

### The sun's tint

At a point p, the sun's light is multiplied by:

```
tint(p) = mix(1, SunTint, s(p))      per channel
```

At the surface the light takes the whole tint, and above the air it takes none. `lightIrradiance` in `lighting.glsl` applies it, because every pass that adds a light's irradiance at a point calls it. The renderer marks the sun's GPU light, `GpuLight.airSun`, and only that light takes the tint.

## Choices that the rules left to the builder

| Choice | Decision | Reason |
|---|---|---|
| The day factor's curve | `smoothstep(−6°, +6°, e)` of the elevation angle | A smooth sunset, centered at the horizon. Dark at the end of civil twilight, full by the time the sun is a hand's width up. |
| How the haze fades with the density share | The optical depth takes the mean density share along the ray | With the eye's share alone, the haze vanishes over the ground seen from above the air, though that ground lies in the air. The mean along the ray is exact for a flat ground and costs two exponentials. |
| How the tint fades with the density share | `mix(1, SunTint, s(p))` at the lit point | Full at the surface, none above the air, and continuous between them. It is the tint of the point, not of the eye. So the ground seen from orbit keeps its tint, and a ship in orbit seen from the ground does not take it. |
| How the sun is chosen | The point light with the most irradiance at the planet's center, P · (r + g + b) / max(d², R²), in doubles. A tie keeps the earlier light of `RenderSnapshot.Lights`. | A planet's day comes from the light that outshines all the others there. The floor at R² keeps a light near the center from winning by its nearness. A spot light lights only its cone, so it cannot be a planet's sun. The list order is the slot order of the entities, which every run shares, so the choice is deterministic. |
| A horizon gradient | None: one color in every direction | The default of the ticket. |

## The snapshot build

`RenderSnapshotBuilder.Build` writes the atmosphere after the chain and the lights. It walks `RenderSnapshot.Chain` from the camera's frame up. The first frame whose medium is a `PlanetMedium` gives the record: its pose relative to the camera's physics home less the render origin, now and at the previous build, as an instance's positions are. Then it chooses the sun among the lights of the snapshot. A snapshot without a camera, or without such a frame, has no atmosphere.

## The renderer

`PaintedAtmosphere.Prepare` (new, `src/Tertius.Rendering/Scene/PaintedAtmosphere.cs`) turns the record into the frame's `GpuAtmosphere`, at the interpolated eye. The frame block (`frame.glsl`, `GpuFrameConstants`) holds it at its end:

```glsl
struct GpuAtmosphere
{
    vec4 centerRadius;   // xyz c: the planet's center relative to the eye, in m; w R
    vec4 sky;            // rgb the sky, plain radiance; w 1 / H, 0 without an atmosphere
    vec4 haze;           // rgb hazeLight, plain radiance; w 1 / Haze.Distance, 0 without a haze
    vec4 sunTint;        // rgb SunTint, 1 without a tint; w unused
};
```

Without an atmosphere, every value is 0 except the tint, which is 1. Then `skyRadiance` is black, the haze lets everything through, and no light takes a tint, so every scene of today keeps its look.

## Portable Vulkan

- The sky and the haze light are capped at MaxRadiance on the CPU, after `ShaderInput.Radiance`. So a face texel and the haze pass's output stay inside the half float's range.
- 1 / H and 1 / Haze.Distance are at most 2^62 per meter. Every height and distance is at most 2^62 m (`ShaderInput.PositionLimit`), so each product stays below 2^124 and finite. `exp` of a large negative number gives 0, never a NaN.
- If \|x\| < 0.01, the mean m does not divide by x. Both shares lie from 0 to 1, so m is finite.
- The haze pass blends with the dual-source blend that the transparent pass already uses (`BlendMode.Transmittance`, the device feature dualSrcBlend). It needs no new feature.
- The convex sum `light · T + hazeLight · (1 − T)` stays at most the larger of its two parts, so it keeps the surfaces' share of MaxRadiance.

## Tests

The tests define the behavior first, in the language of the domain:

- A planet medium with a sky, a haze or a tint out of its range is not valid.
- A camera in a frame with a planet medium gets the atmosphere record: the center, the medium and the sun. A cabin on the surface gets its planet's record. A chain without a planet medium gives none.
- The sun is the point light that gives the planet's center the most light.
- The day factor is full by day, half at sunset and zero at night.
- A frame with a painted sky shows that color behind its ground by day. The sky is black at night. The sky fades to black above the air.
- Haze grows with distance, and a planet that sets none has none.
- The sun's light takes the tint inside the air and not in orbit.
- The environment faces see the same sky and the same haze.

## Not in this ticket

- The exposure meter counts no sky pixel (`auto-exposure.md`). So a view of the sky alone keeps the exposure that the view before it set. With a sunlit surface in view, the exposure falls by day, and the stars fade.
- The record comes from the camera's chain. A camera that leaves the bubble of the atmosphere's frame loses the record, and with it the haze and the tint of the ground below. A planet frame whose bubble reaches well above its air, many scale heights, hides that edge: there the sky is black already, and the haze of a view down is small.
- The screen-space reflections read the lit image before the haze pass. A mirror shows a reflected surface without the haze along the reflected path, and the mirror's own haze applies to the sum.
