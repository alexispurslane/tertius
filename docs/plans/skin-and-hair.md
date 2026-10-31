# Skin and hair shading

Ticket: `ter-7lg5`, the design of the epic `ter-n7lw`, a member of the rendering upgrade (`ter-sglu`, [rendering-upgrade.md](rendering-upgrade.md)). The user added the epic on 2026-10-26 and said that it closes the upgrade's scope. This doc is written against main at `c2152136`. It assumes two merges that are close:

| Design | Branch | What this doc takes from it |
|---|---|---|
| The lights, [local-light-shadows.md](local-light-shadows.md) (`ter-s9cv`) | `ter-s9cv-light-shadows` | One loop over the light list, and the shadow atlas, whose tiles draw casters as the cascades do (`LocalShadowPass`) |
| The bounce light, `docs/plans/screen-space-illumination.md` (`ter-a24f`) | `ter-a24f-ssgi` | The split of the lighting pass into the direct pass (`direct.frag`, the direct image) and the ambient pass (`ambient.frag`), the stages `DirectLight`, `AmbientOcclusion` and `AmbientLight`, and G-buffer binding 9 |

State on 2026-10-27: Parts 1 to 6 are built on the branch `ter-n7lw-skin-hair`. Both merges that the doc assumed came in: the lights before Part 3, the bounce light before Part 4. With Part 4 (`ter-k1ne`), hair cards draw from both sides (`ter-xlp8`, "Both sides of a hair card"). ARCHITECTURE.md describes the built code, and "Cost" below holds the measured cost of Part 6 in place of the estimates. Left for later tickets: the hair pixel's plane in the screen-space passes (`ter-qsfz`), the importer's mapping (`ter-kge6`) and the MPFB extras (`ter-omuj`).

The skinning and morph targets (`ter-4vzc`, `docs/plans/skinning-and-morphs.md`) are designed beside this doc. "Other tickets" says where the two meet.

## Goal

The humans of the demo come from MPFB, the MakeHuman add-on for Blender (`ter-1qce`, [asset-pipeline.md](asset-pipeline.md), "Humans from MPFB"): an astronaut and a crew member. Their skin is MakeHuman's skin image, opaque. Their hair (afro01), eyebrows and eyelashes are meshes of cards whose image has alpha, and they export as glTF `BLEND`. Their renders so far come from Blender's Cycles, because the importer does not exist yet. In the engine today:

- Skin shades as any surface, with Lambert and GGX. The light stops at the terminator with a gray edge, and a shadow edge on a face is as hard as one on a wall.
- A `BLEND` part draws as glass (ARCHITECTURE.md, "Transparent objects"). The glass rule adds the specular part of every light and of the ambient light in full on every texel, also where the alpha is 0. So each card shows as a glossy rectangle.
- The cards of one hair mesh draw in the order of their triangles, not in depth order, and write no depth. So an inner card can cover an outer one, and the occlusion passes, the reflections and the bounce light do not see the hair.
- A card with `CastsShadow` casts the shadow of its whole rectangle, because `CastsShadow` alone decides what the shadow pass draws.
- A card's light follows the card's plane: each card has its own flat highlight and its own Lambert shade, so the planes show.

After this epic:

- A material names its shading model: standard, skin or hair.
- Skin scatters its diffuse light under the surface in a screen-space pass. Red light reaches past the terminator and into the edges of shadows, and fine detail softens.
- A material names its alpha mode: opaque, cutout, fringed or glass. A hair card is fringed. Its texels at or above a cutoff are opaque in every pass that draws opaque surfaces, with depth, shadows and the crease shadowing. Its texels below the cutoff blend over what lies behind by their coverage. A clear texel draws nothing.
- Hair shades as fibers along its strands, not as the plane of its card: a diffuse part and two highlights shifted along the strand.
- glTF `MASK` materials draw as cutouts.

Not in scope, by the user's list: an eye material, two specular lobes for skin, and contact shadows.

## Terms

| Term | Meaning |
|---|---|
| Shading model | How a surface sends the light that reaches it: standard, skin or hair. `MaterialDesc.Model`, a `ShadingModel`. |
| Alpha mode | How a material's alpha draws: opaque, cutout, fringed or glass. `MaterialDesc.Alpha`, an `AlphaMode`. |
| Alpha, a | The base color's alpha times the alpha of its map, from 0 to 1. |
| Coverage | The share of a texel that the surface covers. Under a cutout or a fringed alpha mode, the alpha is the coverage. |
| Opacity | Under the glass alpha mode, the alpha is the pane's opacity, as today. |
| The cutoff | The alpha at and above which a texel of a cutout or fringed material counts as fully covered: glTF's `alphaCutoff`. |
| The core | The texels of a cutout or fringed material whose alpha is at least the cutoff. They draw in every opaque pass. |
| The fringe | The texels of a fringed material whose alpha is above 0 and below the cutoff. They draw in the transparent pass, blended by their coverage. |
| An opaque pass | A pass that draws opaque surfaces with depth: the G-buffer pass, the shadow maps (the cascades and the atlas tiles), the environment faces and the impostor captures. |
| A solid draw, a cutout draw | An opaque pass's draw of an instance whose material is opaque or glass, and of one whose material is cutout or fringed. |
| The strand, T | A hair fiber's unit direction at a point, from root to tip, in world axes. |
| Strand direction | The texture axis along which a hair material's strands run from root to tip: +u, −u, +v or −v. `HairShading.Strands`. |
| The fiber normal, n_f | The unit vector across the strand toward the eye: V − T (T·V), normalized. The side of the fiber that the eye sees. |
| The longitudinal angle, θ | The signed angle of a direction from the plane at right angles to the strand, positive toward the tip: sin θ = T·d. θ_i for the light, θ_r for the eye, θ_h = (θ_i + θ_r) / 2. |
| The azimuth, φ | The angle between the light and the eye around the strand, between their parts at right angles to it. 0 when both lie on one side. |
| The shading target | The new G-buffer target, `R8G8B8A8_UINT`: R and G a hair pixel's strand, B 0, A the shading model's code. Since `ter-vtc7`, B holds the light mark: 1 where the pixel's entity holds a light (docs/plans/screen-space-illumination.md, "The bounce source"). |
| The diffuse light, D | For a skin pixel, the diffuse part of the light that it sends toward the eye, pre-exposed: the diffuse terms of the lights and of the ambient light, with the occlusion and the bounce applied, all with the diffuse color in them. |
| The scatter image | Full resolution, `RGBA16F`: D in rgb and 1 in a at a skin pixel, 0 at every other pixel. |
| The row image | Full resolution, `RGBA16F`: the scatter image after the pass along the rows. |
| The scattered light, S | D after the pass along the rows and the pass along the columns. |
| The profile, R_c(r) | The skin's diffusion profile in color channel c: the share of the light that enters at a point and leaves at distance r from it, per square meter. A sum of six Gaussians (d'Eon, Luebke and Enderton 2007), widened by the width scale. |
| The width scale | 4: how many times wider than the measured profile the skin scatters, `SkinProfile.WidthScale` (decision 34). |
| The kernel, P_c(x) | The profile's marginal along one axis: P_c(x) = ∫ R_c(√(x² + y²)) dy. |
| A tap | One sample of the kernel: an offset x_i in meters and a weight w_{i,c} per channel. |
| The reach | 32 mm, the largest tap offset: 8 mm of the measured profile times the width scale. |
| The view depth, z | A pixel's distance along the camera's forward axis: d / \|r\|, with d its distance and r its view ray with a forward component of 1, as in [ambient-occlusion.md](ambient-occlusion.md). |
| Pixels per meter, s(z) | How many pixels one meter across the view covers at view depth z: H / (2 z tan(φ_y / 2)), with H the image's height and φ_y the vertical field of view. |
| The slope limit | tan 60°, √3: the steepest rise of depth per meter of the measured profile across the view that a tap still counts as the center's surface. A tap's offset counts in the measured profile, its offset in meters over the width scale (decision 34). |
| k, MaxRadiance | As in ARCHITECTURE.md, "Exposure" and "Portable Vulkan": pre-exposed light is radiance times the exposure scale k, and a surface writes at most MaxRadiance, 32,256, into the HDR image. |

## Decisions

### Made by the user

1. Skin gets basic screen-space subsurface scattering, not a pre-integrated table. (User, 2026-10-26)
2. Hair gets whatever the renderer needs so that its cards do not show. (User, 2026-10-26)
3. Out of scope: an eye material, two specular lobes for skin, and contact shadows. With this epic and the skinning, the rendering upgrade's scope is closed. (User, 2026-10-26)
4. No technique blends a pixel across frames: no temporal accumulation, no TAA, no temporal denoise or upscaling. (User, 2026-10-25, [rendering-performance.md](rendering-performance.md), decision 8) This rules out the dithered and hashed transparency of hair that most engines use.
5. The renderer runs on regular Vulkan on Linux, not only on MoltenVK. (User, 2026-10-25)

Made at the fix of Part 4's darkened cards (`ter-qsfz`), and so numbered after the decisions below:

33. Under the screen-space light, a hair receiver's own card blocks nothing: its own surface blocks no sector, and a step whose pixel carries the receiver's card is left out, at any bend of the card. Other cards, other instances and other surfaces still block it and give it their bounce. A curl that folds back over its own card never shades it. (User, 2026-10-27, option A of `ter-qsfz`; user, 2026-10-31, design A of `ter-3yy5`, the card number) A card is a connected strip of triangles, and the G-buffer writes a hair pixel's card, mixed with its instance, into the shading target's 13 spare bits ("The card number"). Reason: half of a turned card's own points lie above the plane of a hair pixel's fiber normal, so they blocked its hemisphere, and a card turned 40° about its strand shaded 7% darker than one that faces the eye: the card planes showed. The first rule took the card as the plane at right angles to the pixel's geometric normal, which covers only flat cards: on a smooth-shaded strip that bends, the interpolated normal leans off the triangle and the next segment lies off the plane, so a card bent by 15° to each side of its ridge shaded 8% darker again. Declined: a cone of half angle θ around the card's plane, which also skips the other cards within θ, sin²θ of the cosine-weighted light, 33% at 35°.

Made after a side-by-side capture of the test head (`ter-t2w6`):

34. Skin scatters four times wider than the measured profile of d'Eon, Luebke and Enderton 2007: `SkinProfile.WidthScale` = 4 multiplies every Gaussian's width, so every variance by 16, and the reach by 4. It is an artistic choice of the user over the measured profile. At the measured width, the scatter on the 0.1 m head at 0.45 m was a band of about 3 px. Four times wider, the nose's shadow line turns clearly red and soft. The user: "wider is much better in my opinion!" No material parameter and no renderer option set the width, because only one skin exists. (User, 2026-10-27)

    The width scale does not widen the slope rule (decision 16). The slope rule divides each tap's offset by the width scale, so a tap tolerates the same depth step as at the measured width. At the full width, light crossed the 11 mm step at the edge of the test head's nose, and a faint red ring showed beside the nose. The user chose this rule over the ring and over an absolute limit on the depth step, which needs a new constant. The cost: the scatter narrows wherever the surface turns more than about 23° from the eye. A tap starts to give way there and gives way fully at about 41°. On a round head, that is the outer 60 % of its radius, not only the pixels near its silhouette. (User, 2026-10-31)

Made at the review of the hair reflections (`ter-nwhv`):

35. A hair ray that points below its card, or less than a cosine of `SurfaceFadeCosine` above it, traces nothing. A normal map tilts the strand off its card (decision 28), so the eye's direction mirrored about the strand can point below the card, for a card far from edge-on too. When the dominant direction of a hair pixel has a cosine below `SurfaceFadeCosine`, 0.05, with the card's geometric normal, the pixel starts no ray, at either trace resolution. It keeps the reflection that the ambient pass gave it, the environment map's while the map is on. The surface fade blends nothing for hair, because a ray inside its range grazes the card and hits the card a few pixels from its start: a share of those hits ends the screen's reflection at a ragged, speckled edge. A pixel of any other model keeps the surface fade. `reflectingPixel` in `reflection_pixel.glsl` holds the rule, and the trace, the half-resolution trace and the upsample all call it. The cost: a tilted strand whose reflection points less than a cosine of 0.05 above its card shows the environment map's reflection, not the screen's, and the two meet at a straight edge. (User, 2026-10-31, design 2 of `ter-nwhv`, with the cut at `SurfaceFadeCosine`) The user declined design 1, in which a hair pixel's own card is no surface to its ray, so that a ray below the card traces and skips the hits on its own card through the card number of `ter-3yy5`. It can come later if a normal-mapped hair asset looks patchy.

### Settled by this design

Each by the user's decisions and the project's rules, with its reason.

6. A material's shading model is a closed set: the generated union `ShadingModel` of `StandardShading(Metallic)`, `SkinShading` and `HairShading(Strands)`, with the standard case as its zero value. `MaterialDesc.Metallic` moves into `StandardShading`. Reason: a skin or a hair has no metallic, so a metallic beside the model would mean something for one model only (STYLE.md, "Make illegal states unrepresentable"). The metallic-roughness map keeps glTF's layout, and only the standard model reads its B channel.
7. A material's alpha mode is a second closed set: the generated union `AlphaMode` of `OpaqueAlpha`, `CutoutAlpha(Cutoff)`, `FringedAlpha(Cutoff)` and `GlassAlpha`, with opaque as its zero value. It is orthogonal to the shading model: a hair card is a hair model with a fringed alpha, a leaf a standard model with a cutout, a face a skin model, opaque. Reason: the pure model has two independent questions, how a surface sends light and how much of a texel it covers, and every pair of answers has a meaning.
8. Glass moves into the alpha mode as `GlassAlpha`, and `RenderFlags.Transparent` goes (fork 2, settled as A on 2026-10-26). Reason: one store of how a material's alpha draws. With the flag kept, a transparent instance with a fringed material has two answers.
9. The G-buffer gets a sixth target, the shading target, `R8G8B8A8_UINT` (see "The shading target", which gives the cost of the other place). Reason: every channel of the five targets has a job, and the free codes of the map code would tie the number of models to the number of maps.
10. A hair pixel stores its fiber normal as its shading normal, and its strand in the shading target. Reason: every reader of the shading normal, the ambient light, the occlusion passes, the bounce light and the reflections, then works for hair unchanged and sees the fiber, not the card's plane. The occlusion sectors pass is the one reader that also needs to know a hair pixel: its own card blocks nothing there (decision 33).
11. Skin scatters by the separable screen-space method of Jimenez et al. 2015 ("Separable Subsurface Scattering", Computer Graphics Forum 34(6)), with the skin profile of d'Eon, Luebke and Enderton 2007 ("Efficient Rendering of Human Skin", EGSR; GPU Gems 3, chapter 14). Reason: it is the spatial method that needs no temporal step: two passes of a fixed kernel, the same image in every frame. Burley's normalized diffusion in screen space (Golubev 2018) samples a disk with few taps and noise, and relies on temporal accumulation (decision 4). Texture-space diffusion (d'Eon 2007) needs an irradiance texture per object and passes over every skinned mesh's unwrap. A pre-integrated table is out by decision 1.
12. The scatter blurs the diffuse light with the diffuse color in it, "pre-scatter texturing" in d'Eon's terms. Reason: the lighting passes already compute the diffuse terms with the color, so the scatter needs no division by the color and the composite no read of it. Its cost: detail of the skin image softens a little, most in red, where the profile is widest. Post-scatter texturing is under "Later".
13. The scatter adds a difference to the HDR image: HDR += S − D at each skin pixel. Reason: the lighting passes keep writing the whole light of skin, so the bounce light's direct image keeps the direct light that a skin blocker bounces, and a renderer with the scatter off draws skin with no second path. It has the form of the reflections' delta, which adds W · C − a · k · T_s.
14. The direct pass writes D's direct part into the scatter image, and the ambient pass adds its ambient part through an additive blend on the scatter image alone. That needs the device feature `independentBlend` (see "Portable Vulkan"). Reason: D needs both parts, and neither pass sees the other's. A second image for the direct part costs 16.6 MB at 1080p, and a second light loop in the ambient pass costs the direct pass's lights again at every skin pixel.
15. Each tap reads the one pixel nearest its position, with `imageLoad`, and no filtering. Reason: each tap's tests of the mask and the depth then hold for the very pixel whose light it takes, and the C# copy gives the same sums. A tap less than half a pixel from the center lands on the center.
16. A tap gives way to the center's own light when its pixel is not skin, and when the depth between it and the center rises faster than the slope limit, across the tap's offset in the measured profile (decision 34). Reason: Jimenez's rule that the kernel follows the surface, with a tolerance that grows with the tap's offset, not one constant. In the measured profile, past 60°, the surface turns so far from the eye that the true scatter covers fewer pixels than the screen-space kernel. In the rendered skin, four times wider, a tap starts to give way where the surface turns about 23° from the eye and gives way fully at about 41° (decision 34). The sky, the impostor billboards and every surface that is not skin are not skin.
17. The kernel is computed once in C#, in doubles, when the renderer is made, and uploaded with the first frame. Reason: it depends only on the profile and the tap count, and the C# copy is then the one source of the weights.
18. The scatter runs after the ambient pass and before the reflections' radiance pyramid. Reason: D must hold the ambient light, the crease shadowing and the bounce, which the ambient pass adds, and a mirror must show the scattered skin. The transparent pass, the exposure and bloom come after, as for every surface.
19. The environment faces and the impostor captures shade skin as the standard model, with no scatter. Reason: a face texel at W = 128 covers 1.2 cm at 1 m, and an impostor of a person of 1.8 m is below 200 px, about 9 mm per pixel. Both are as wide as the red light's scatter and wider than the green and blue light's.
20. No transmission through thin skin, such as an ear before a light (fork 1, settled as A on 2026-10-26). Reason: the user asked for basic scattering, and transmission costs a part of its own. See "Later".
21. Hair cards draw as a cutout core in every opaque pass and as a blended fringe in the transparent pass. Reason: the core gets depth, shadows, the crease shadowing, the bounce and the reflections, as a surface, and the fringe keeps the soft edges. The other methods: alpha to coverage needs multisampling, which this deferred renderer does not have. Hashed and dithered alpha need a temporal step (decision 4). Order-independent transparency by lists per pixel needs atomics and memory, and a sort per pixel to fix its order. Weighted blended transparency (McGuire and Bavoil 2013) approximates the colors of every overlap. A sort of the cards per frame costs CPU time per triangle.
22. The fringe blends by its coverage, Porter and Duff's "over": radiance a · L and transmittance 1 − a, through the glass's dual-source blend, `BlendMode.Transmittance`. Reason: one blend mode, and the glass's proof that a pane keeps the shares of the HDR image holds, because the fringe writes at most MaxRadiance · a = MaxRadiance · (1 − T).
23. The fringe of one instance is not sorted inside the instance: its triangles draw in index order. Reason: a sort needs work per triangle per frame. Two fringe texels in the wrong order give the right total coverage and mix their colors in the wrong weights, and the fringes of one hair have one color.
24. A fringed or glass instance never becomes an impostor. A cutout instance can, and its capture tests the cutoff. Reason: a capture has no transparent pass, and a fringe drawn as a core would thicken the hair's edge.
25. In each view of an opaque pass, the cutout draws run after the solid draws, with a pipeline whose fragment shader can discard. Reason: the solid pipeline keeps its early depth writes, and the solid surfaces drawn first hide many hair fragments before their shader runs.
26. Hair shades as fibers: a diffuse part along the strand (Kajiya and Kay 1989), normalized so that an even light returns the fiber's color, and two highlights shifted along the strand (Scheuermann 2004), each a Gaussian of unit area in the longitudinal angle with Marschner's azimuthal term (Marschner, Jensen, Cammarano, Worley and Hanrahan 2003). Its ambient light is the ambient source's at the fiber normal. Reason: the light then depends on the strand, the eye and the light, never on the card's plane, and the ambient light, its occlusion and the reflections need no hair path.
27. A hair pixel takes the light of every light, from either side of its card, and its shadow lookups use the geometric normal turned toward the light. Reason: a fiber is lit from every side. The shadow maps, in which the cards cast, say what hides it.
28. The strand comes from the shading frame of the vertex: the vertex tangent, made perpendicular to the shading normal, along +u, and the opposite of the bitangent along +v, in the frame that `normalMapped` builds. v shrinks along that bitangent, in glTF and on the engine's own meshes, such as `Primitives.Plane`. The strand direction picks one of the four. A normal map tilts the strand with the frame. Reason: glTF meshes carry tangents, and the importer makes them where a file has none (asset-pipeline.md, "Meshes"), so the vertex format does not change.
29. The setting is `RendererOptions.Subsurface`, the generated union `SubsurfaceSetting` of `NoSubsurface`, its zero case, and `SeparableSubsurface(TapsPerSide)`. The default is `SeparableSubsurface.Default`: 8 taps per side, 17 per pass, as Jimenez's high setting. The test helpers set `NoSubsurface`, as they set `NoReflections`. Reason: the rule of every member, a switch and its measured cost.
30. No renderer switch for the fringe or for the hair model. Reason: a material chooses them, and a switch beside the material would be a second store that can disagree with it. A `CutoutAlpha` in place of a `FringedAlpha` turns a fringe off.
31. The importer names a material's shading model and its fringe through glTF `extras` (see "The importer's mapping from glTF"). Reason: no ratified KHR extension describes a diffusion profile or a hair fiber.
32. A hair card draws from both sides in every pass that shades, and its normals turn toward the eye (`ter-xlp8`, settled at the Part 4 review on 2026-10-27; see "Both sides of a hair card"). Reason: a hair card is a sheet of strands with no back, and MPFB's cards are authored to be seen from either side, such as those at the back of a head. Decision 2 asks that the cards do not show, and a card seen from behind drew nothing.

## The model

This section describes the model as a renderer built from scratch would have it. The next section lists how it differs from today's code.

A material says two things about a surface. Its shading model says how the surface sends the light that reaches it. Its alpha mode says how much of each texel the surface covers. The two are independent.

A deferred renderer shades in screen space, so each pixel must name its shading model and carry the data of that model. A standard pixel needs nothing more. A skin pixel needs only the fact that it is skin. A hair pixel needs its strand, because a fiber sends light by its direction, not by a surface normal.

Skin is a standard surface whose diffuse light does not leave where it entered. It enters, scatters under the surface and leaves within a few millimeters, more for red light than for blue. On the screen, that is a convolution of the skin pixels' diffuse light with the skin's profile, scaled to each pixel's depth, and limited to the skin's own surface. The specular light reflects at the surface and does not scatter.

Hair is fibers, and a card is a sheet of them with holes. Where a texel is fully covered, the card is a surface with depth, which every screen-space effect and the shadow maps can use. Where it is partly covered, only a blend can draw it, and where it is not covered, nothing is there. The light of a fiber depends on its direction, so the card's plane never enters the hair's light.

```mermaid
flowchart TB
    M["MaterialDesc"] --> SM["Model: standard, skin or hair"]
    M --> AM["Alpha: opaque, cutout, fringed or glass"]
    SM --> GB["G-buffer: the shading target names the model; a hair pixel adds its strand and stores its fiber normal"]
    AM --> PASSES["Opaque passes draw the solid and the core; the transparent pass draws the fringe and the glass"]
    GB --> DL["Direct and ambient passes: each light's response by the model"]
    DL --> SC["Skin scatter: HDR += S − D at skin pixels"]
    SC --> SSR["Reflections"]
    PASSES --> GB
    SSR --> TR["Transparent pass: glass and fringes, back to front"]
```

### How it differs from today's code

| Today | This design |
|---|---|
| `MaterialDesc.Metallic` | `StandardShading(Metallic)` in `MaterialDesc.Model` |
| `RenderFlags.Transparent` on an instance makes it glass | `GlassAlpha` in `MaterialDesc.Alpha`. The flag goes. |
| glTF `MASK` draws as glass (asset-pipeline.md, AS3003) | `CutoutAlpha(Cutoff)` |
| `GpuMaterial` holds 52 bytes of fields | 60 bytes: a shading word and the cutoff, in the same stride of 64 |
| The batcher knows the material slots' count only | It reads each slot's `MaterialInfo`, its alpha mode |
| One opaque command range per depth range, per cascade, per atlas tile and per face range | `OpaqueRuns(Solid, Cutout)` in each, and in the main view and the faces a `SidedRuns(OneSided, TwoSided)` of two (decision 32) |
| `TransparentRun` is `MeshCommands` or `PuffSprites` | `GlassCommands` (the renamed `MeshCommands`), `FringeCommands` or `PuffSprites`. The glass and fringe runs carry their sides, `TwoSided` (decision 32) |
| Five G-buffer targets | Six: the shading target |
| `brdf()` returns the reflected light of one light as one sum | `respond()` returns `DirectTerms` (diffuse, specular) by the shading model |
| `ShadingInput` and `SurfaceSample` have no model | Both gain `model` and `strand` |
| No subsurface pass | Two compute passes, "scatter rows" and "scatter columns", in the new stage `SkinScatter` |
| The device needs no `independentBlend` | `VulkanDevice` requires it |

What stays: the BRDF and its tables, the ambient sources, the occlusion and the bounce, the reflections, the glass rule, the puffs, the exposure, bloom, the tone map, the vertex format and every existing pass's place in the frame.

## The material API

`src/Tertius.Core/Seams/ShadingModel.cs` (new):

```csharp
/// <summary>The direction in a hair card's texture along which its strands run from root to tip.</summary>
public enum StrandDirection : byte { PlusU, MinusU, PlusV, MinusV }

/// <summary>Lambert diffuse and GGX with glTF's metallic (docs/plans/pbr-brdf.md).</summary>
public readonly record struct StandardShading(float Metallic);

/// <summary>The standard light of a non-metal, whose diffuse light scatters under the skin (docs/plans/skin-and-hair.md).</summary>
public readonly record struct SkinShading;

/// <summary>Fibers along the strands of the card's texture (docs/plans/skin-and-hair.md, "The hair model").</summary>
public readonly record struct HairShading(StrandDirection Strands);

[GeneratedUnion(typeof(StandardShading), typeof(SkinShading), typeof(HairShading), ZeroCase = typeof(StandardShading))]
public readonly partial struct ShadingModel;
```

`src/Tertius.Core/Seams/AlphaMode.cs` (new):

```csharp
/// <summary>The alpha is not drawn: every texel is covered.</summary>
public readonly record struct OpaqueAlpha;

/// <summary>glTF's MASK: a texel whose alpha is below the cutoff is not there.</summary>
public readonly record struct CutoutAlpha(float Cutoff);

/// <summary>A cutout core, and the texels below the cutoff blended over what lies behind by their coverage.</summary>
public readonly record struct FringedAlpha(float Cutoff);

/// <summary>A pane whose alpha is its opacity, which reflects in full (ARCHITECTURE.md, "Transparent objects").</summary>
public readonly record struct GlassAlpha;

[GeneratedUnion(typeof(OpaqueAlpha), typeof(CutoutAlpha), typeof(FringedAlpha), typeof(GlassAlpha), ZeroCase = typeof(OpaqueAlpha))]
public readonly partial struct AlphaMode;
```

`MaterialDesc` in `RenderSeam.cs`:

```csharp
public sealed record MaterialDesc(
    Vector4 BaseColor,
    float Roughness,
    float Reflectance = 0.5f,
    Vector3 Emissive = default,
    TextureId BaseColorMap = default,
    TextureId NormalMap = default,
    TextureId MetallicRoughnessMap = default,
    ShadingModel Model = default,
    AlphaMode Alpha = default
)
{
    public static MaterialDesc Color(Vector4 color, float roughness) => new(BaseColor: color, Roughness: roughness);
}
```

where

- `Model` = the shading model. The default is `StandardShading(0)`, a non-metal.
- `Alpha` = the alpha mode. The default is `OpaqueAlpha`. `BaseColor.W` is the coverage under a cutout or fringed mode, the opacity under glass, and unread under opaque.
- `Cutoff` = a value above 0 and at most 1. `VulkanRenderer.CreateMaterial` throws `ArgumentOutOfRangeException` for any other value, a NaN included. A cutoff of 0 would make every texel core, which `OpaqueAlpha` says.

Usage:

```csharp
MaterialId gold = renderer.CreateMaterial(new(
    BaseColor: new(1, 0.71f, 0.29f, 1), Roughness: 0.1f, Model: new StandardShading(Metallic: 1)));
// Skin's F0 is about 0.028, a reflectance of 0.42.
MaterialId skin = renderer.CreateMaterial(new(
    BaseColor: Vector4.One, Roughness: 0.45f, Reflectance: 0.42f, BaseColorMap: skinImage, Model: new SkinShading()));
// Keratin's F0 is about 0.046 (an index of 1.55), a reflectance of 0.54.
MaterialId hair = renderer.CreateMaterial(new(
    BaseColor: Vector4.One, Roughness: 0.35f, Reflectance: 0.54f, BaseColorMap: hairImage,
    Model: new HairShading(Strands: StrandDirection.PlusV), Alpha: new FringedAlpha(Cutoff: 0.5f)));
MaterialId canopy = renderer.CreateMaterial(new(
    BaseColor: new(0.8f, 0.9f, 1, 0.2f), Roughness: 0.05f, Alpha: new GlassAlpha()));
```

The callers change in two ways. `Metallic: 1` becomes `Model: new StandardShading(Metallic: 1)`, at 4 places in the demo and about 20 in the tests. An instance with `RenderFlags.Transparent` takes a material with `Alpha: new GlassAlpha()`, at 2 places in the demo and about 30 in the tests. The flag's bit, 1 << 2, stays unused.

### The material on the GPU

`GpuMaterial` gains two fields in its padding:

```csharp
internal struct GpuMaterial
{
    public Vector4 BaseColor;
    public Vector3 Emissive;
    public float Metallic;         // StandardShading.Metallic, 0 for skin and hair
    public float Roughness;
    public float Reflectance;
    public uint BaseColorMap;
    public uint NormalMap;
    public uint MetallicRoughnessMap;
    public uint Shading;           // bits 0 and 1: the model's code; bits 2 and 3: a hair's strand direction
    public float AlphaCutoff;      // the cutoff of a cutout or fringed material, 0 otherwise
}                                  // 60 bytes of fields, a stride of 64 as before
```

where

- `Shading` = `ShadingCode.Word(model)`: the code 0 standard, 1 skin, 2 hair, and for hair the `StrandDirection` in bits 2 and 3. `frame.glsl` mirrors the struct.
- `AlphaCutoff` = read only by the pipelines that test the cutoff, so 0 under opaque and glass is never read.

`MaterialStore` keeps the CPU's facts of each slot beside the buffer:

```csharp
/// <summary>What the batcher needs of a material slot: how its alpha draws.</summary>
internal readonly record struct MaterialInfo(AlphaMode Alpha);

public ReadOnlySpan<MaterialInfo> Infos { get; }   // one per slot, the default material's first, filled by Upload
```

A material never changes after `CreateMaterial`, and `Upload` writes its GPU row and its `MaterialInfo` from one `MaterialDesc`, in slot order. So the two cannot disagree. `SceneBatcher.Prepare` takes `ReadOnlySpan<MaterialInfo> materials` in place of `int materialCount`, as it takes the meshes' `MeshInfo`.

## The shading target

The two places that the G-buffer offers, at 1920 × 1080:

| Place | Memory | Cost per pixel | What it ties |
|---|---|---|---|
| A sixth target, `R8G8B8A8_UINT` (chosen) | 8.3 MB | One more 4-byte write per G-buffer fragment, one more 4-byte read in the direct and ambient passes | Nothing |
| The free codes of the map code, 1 to 172, for the model, and two 8-bit channels for the strand | 0 | None | The map code would name a pair of map and model: 42 maps times 3 models uses 126 of its 128 indices, so a fourth model would lower `MaxMaps`. The strand needs two channels, and the only free one beside the specular target's metallic is that target's alpha, the lobe's sky share while the map is off. |

The shading target's channels:

| Channel | Standard | Skin | Hair |
|---|---|---|---|
| R, G | 0 | 0 | The strand's octahedral code, 8 bits each |
| B, bit 0 | The light mark | The light mark | The light mark |
| B, bits 1 to 7 | 0 | 0 | The card code's bits 0 to 6 |
| A, bits 0 and 1 | 0 | 1 | 2 |
| A, bits 2 to 7 | 0 | 0 | The card code's bits 7 to 12 |

Since `ter-vtc7`, bit 0 of B holds the light mark: 1 where the pixel's entity holds a light, else 0 (docs/plans/screen-space-illumination.md, "The bounce source"). Since `ter-3yy5`, the 13 bits above the light mark and the model's code hold a hair pixel's card code ("The card number"). `lightModelAndCardWords` in `frame.glsl` writes B and A, and `lightMarkOfWord`, `modelOfWord` and `cardOfWords` read them; `CardCode.Channels` and `CardCode.Read` are the C# twins.

```glsl
// The strand's octahedral code (docs/plans/skin-and-hair.md, "The shading target"). StrandCode is the C# twin. floor(x + 0.5)
// rounds, because GLSL's round leaves its ties to the driver.
uvec2 strandCode(vec3 strand) { return uvec2(clamp(floor((octEncode(strand) * 0.5 + 0.5) * 255.0 + 0.5), 0.0, 255.0)); }
vec3 strandOfCode(uvec2 code) { return octDecode(vec2(code) / 255.0 * 2.0 - 1.0); }
```

where

- `octEncode`, `octDecode` = the normals target's encoding (`octahedral.glsl`).
- `strand` = a unit vector, so its octahedral code lies in [−1, 1]² and the clamp only bounds rounding.

The target is a `UINT` format, so it holds integers and its codes need no rule for the rounding of a conversion: every bit survives on every driver. It is cleared to 0, so the sky and every pixel that nothing draws read as standard. `gbuffer.frag` writes it for every lit fragment, and `impostor.frag` writes 0: a billboard's pixel is unlit, which the base color's alpha of 0 already says. `surfaceAt` reads it at G-buffer set binding 10, after the lights' atlas at 8 and the bounce's direct image at 9. `StrandCode` and `ShadingCode` are the C# twins, and a GPU test pins the shaders to them.

## Hair

### The alpha modes in the passes

| Alpha mode | G-buffer pass | Shadow maps (cascades, atlas tiles) | Environment faces | Impostor | Transparent pass |
|---|---|---|---|---|---|
| Opaque | A solid draw | A solid draw, with `CastsShadow` | A solid draw | May become one, as today | No |
| Cutout | A cutout draw: a texel below the cutoff is discarded | A cutout draw, with `CastsShadow` | A cutout draw | May become one: its capture discards a texel below the cutoff | No |
| Fringed | A cutout draw: the core | A cutout draw of the core, with `CastsShadow` | A cutout draw: the core | Never | The fringe: discards a ≥ cutoff and a = 0, and blends by coverage |
| Glass | No | A solid draw, with `CastsShadow`, as today | No | Never | The glass rule, as today |

The batcher reads each instance's alpha mode once, from its material slot's `MaterialInfo`, into `PreparedInstance.Alpha`, and every decision of the frame reads that field:

- The opaque sort key becomes (range, cutout, two-sided, mesh), with solid before cutout and one-sided before two-sided, so each view's commands form the runs one-sided solid, two-sided solid, one-sided cutout and two-sided cutout. Decision 32 added the two-sided bit ("Both sides of a hair card").
- `ShadowCommands` per cascade and each atlas tile's commands become an `OpaqueRuns`. `PreparedScene.Opaque` per depth range and each `EnvironmentFaceDraw`'s `Far`, `Middle` and `Near` become a `SidedRuns` of two `OpaqueRuns`, one per side (decision 32):

  ```csharp
  /// <summary>The opaque commands of one view: the solid draws, then the cutout draws.</summary>
  internal readonly record struct OpaqueRuns(CommandRange Solid, CommandRange Cutout);

  /// <summary>The opaque commands of one view of a pass that shades: the runs of the one-sided materials and of the two-sided ones.</summary>
  internal readonly record struct SidedRuns(OpaqueRuns OneSided, OpaqueRuns TwoSided);
  ```

- A fringed instance goes into the opaque list and into the transparent list. The transparent list sorts it with the glass and the puffs by its center, back to front, as today.
- `TransparentRun` gains a case:

  ```csharp
  internal readonly record struct GlassCommands(CommandRange Commands, bool TwoSided);   // the renamed MeshCommands
  internal readonly record struct FringeCommands(CommandRange Commands, bool TwoSided);

  [GeneratedUnion(typeof(GlassCommands), typeof(FringeCommands), typeof(PuffSprites))]
  internal readonly partial struct TransparentRun;
  ```

  `TwoSided` is the sides of the run's materials (decision 32): a run breaks where the sides change, and the pass draws a two-sided run with the variant of its pipeline that culls no face. The transparent pass draws a `FringeCommands` run with the fringe pipeline. It has the scene layout, so the sets and the push constants stay bound between runs, as for the puffs.
- `ImpostorManager` keeps out `NoImpostor` and `Celestial` instances by their flags, as today, and a glass or fringed instance by `PreparedInstance.Alpha`.

### The cutout test

The specialization constant `cutoutAlpha` makes the cutout variant of each opaque pass's pipeline. `material.glsl` holds the one test:

```glsl
layout(constant_id = ShaderConstants.CutoutAlpha) const bool cutoutAlpha = false;

SurfaceSample sampleMaterial(uint materialIndex, vec2 uv, vec3 vertexNormal, vec4 vertexTangent, vec3 V)
{
    GpuMaterial m = materials[materialIndex];
    SurfaceSample s;
    s.baseColor = m.baseColor * sampleTexture(m.baseColorMap, uv);
    ...
    // After every sample, so the implicit derivatives of the quad stay defined (discard demotes to a helper).
    if (cutoutAlpha && s.baseColor.a < m.alphaCutoff)
        discard;
    return s;
}
```

where

- `cutoutAlpha` = false in the solid pipelines, so the driver drops the test and the pipeline keeps its early depth writes.
- `s.baseColor.a` = the alpha, read into [0, 1] by `ShaderInput.Fraction` and the UNORM map, so the comparison sees finite values.

The pipelines with a cutout variant: the G-buffer pass (`gbuffer.frag`), the environment faces (`environment_face.frag`), the impostor captures (`impostor_capture.frag`), and the shadow maps. The shadow pipelines are depth only today, with `shadow.vert`. Their cutout variant is `shadow_cutout.vert`, which also passes the texture coordinate and the material, and `shadow_cutout.frag`, which samples the base color's alpha and discards below the cutoff. The cascades' pass (`ShadowPass`) and the atlas's pass (`LocalShadowPass`) draw each view's solid run, then its cutout run.

### The fringe

`fringe.frag` (new) is `transparent.frag` with coverage in place of opacity:

```glsl
void main()
{
    vec3 V = towardEye(inPosition);
    SurfaceSample s = sampleMaterial(inMaterialAndMap.x, inTexCoord, inNormal, inTangent, V);
    ShadingInput shading = forwardShading(s, inPosition, inHemisphere, inMaterialAndMap.y, V);
    float coverage = s.baseColor.a;
    // The core drew in the G-buffer pass, and a clear texel covers nothing.
    if (coverage >= materials[inMaterialAndMap.x].alphaCutoff || coverage <= 0.0)
        discard;
    float opaqueDistance = texelFetch(gbufferDistance, ivec2(gl_FragCoord.xy), 0).r;
    if (opaqueDistance > 0.0 && length(inPosition - draw.eye.xyz) > opaqueDistance)
        discard;
    float exposure = exposureScale();
    vec3 radiance = surfaceRadiance(shading, exposed(s.emissive, exposure), inPosition, V, ShadowMapsMainView, exposure);
    outColor = vec4(radiance * coverage, 0.0);
    outTransmittance = vec4(vec3(1.0 - coverage), 0.0);
}
```

where

- `forwardShading` = the filling of `ShadingInput` that `transparent.frag`, `fringe.frag`, `impostor_capture.frag` and `environment_face.frag` share, from today's lines in `transparent.frag`: the reflection's footprint from derivatives before any discard, the sky shares while the map is off, and an occlusion of 1 and a bounce of 0, because the fringe is not in the G-buffer.
- `surfaceRadiance` = the whole light of the texel, pre-exposed and capped at MaxRadiance.
- `outColor`, `outTransmittance` = the dual-source outputs of `BlendMode.Transmittance`: the target becomes outColor + target · outTransmittance.

So a texel of coverage a over a target v writes a · L + (1 − a) · v, and a clear texel writes nothing. The fringe draws after the reflections and before the exposure, as the glass does.

### The hair model

The inputs at a hair point:

- T = the strand, V = the unit direction toward the eye, L = the unit direction toward the light.
- E = the light's irradiance on a surface that faces it: `frame.sunRadiance` times the sun's visibility, or a light's color times its intensity and its attenuation.
- b = the base color, ρ the reflectance, F0 = 0.16 ρ², r = the floored roughness, β = r².

The longitudinal angles and the azimuth:

```
sin θ_i = T·L          sin θ_r = T·V          θ_h = (θ_i + θ_r) / 2          cos θ_i = √(1 − sin² θ_i)
cos φ = L⊥ · V⊥        L⊥, V⊥ = the parts of L and V at right angles to T, normalized
```

The light that the point sends toward the eye, per light:

```
L_out = E · (f_d + f_p + f_s)

f_d = b · cos θ_i / π²                                               the diffuse part (Kajiya-Kay)
f_p = F(V·H) · M(θ_h − α_p, β_p) · N(φ) · cos θ_i                     the primary highlight
f_s = F0 · b · M(θ_h − α_s, β_s) · N(φ) · cos θ_i                     the secondary highlight

M(x, β) = exp(−x² / (2β²)) / (2β √(2π))          N(φ) = ¼ cos(φ / 2)
α_p = −5° (toward the root)     α_s = +7.5° (toward the tip)     β_p = β     β_s = 2β
```

where

- `cos θ_i` = the share of the light that the fiber intercepts: a fiber's width across the light.
- `f_d` = the fiber's diffuse part. Under an even light of radiance L_in from every direction, it sends b · L_in, because ∫ cos θ_i dω over the sphere is π². So a white fiber in an even light shows that light, as a white Lambert surface does.
- `F(V·H)` = Schlick's Fresnel term at the half vector of L and V, `schlickFresnel(F0, V·H)`. V·H is the cosine of half the angle between L and V, Marschner's cosine for the primary highlight.
- `M(x, β)` = a Gaussian of unit area over θ_r around the lobe's cone. In θ_h it has the width β, so over θ_r the width 2β and the factor ½.
- `N(φ)` = Marschner's azimuthal term of the primary highlight, of unit area over φ from −π to π. Both highlights use it. It peaks where the eye lies on the light's side of the fiber.
- `α_p`, `α_s`, `β_s / β_p` = Marschner's shifts and widths for the cuticle's tilt: the primary highlight shifts toward the root, the secondary toward the tip by 1.5 times as much, and the secondary is twice as wide.
- The secondary highlight takes the base color: light that crossed the fiber twice, tinted as Scheuermann's second highlight.

So each part sends at most its weight of the light that the fiber intercepts: b for the diffuse part, F for the primary highlight and F0 · b for the secondary, because ∫ M N cos² θ_r dθ_r dφ ≤ 1. The parts add with no coupling, as the standard BRDF's do. A light along the strand adds nothing, because cos θ_i is 0.

The ambient light of a hair point is the ambient source's at the fiber normal, through `ambientTerms` with the shading normal n_f: the diffuse term b · D(n_f) and the specular term E · c · S(d, r) at the dominant direction about n_f. Under an even light the diffuse term is b · L_in, as `f_d` gives. The occlusion and the bounce apply in `occludedAmbient`, as for every surface.

`hair.glsl` (new) holds the response, and `HairFiber` is its C# copy:

```glsl
const float PrimaryShift = -0.08726646;     // −5°, toward the root
const float SecondaryShift = 0.13089969;    // +7.5°, toward the tip
const float InversePiSquared = 0.10132118;

// M: a Gaussian of unit area over θ_r around a lobe's cone. width ≥ 0.045², the floored roughness squared.
float longitudinalLobe(float thetaH, float shift, float width)
{
    float x = thetaH - shift;
    return exp(-x * x / (2.0 * width * width)) / (2.0 * width * sqrt(2.0 * PI));
}

DirectTerms hairResponse(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
{
    vec3 T = s.strand;
    float sinI = clamp(dot(T, L), -1.0, 1.0);
    float sinR = clamp(dot(T, V), -1.0, 1.0);
    float cosI = sqrt(1.0 - sinI * sinI);
    float thetaH = 0.5 * (asin(sinI) + asin(sinR));
    // A direction along the strand has no part across it, and takes the fiber normal.
    vec3 lAcross = normalizedOr(L - T * sinI, s.normal);
    vec3 vAcross = normalizedOr(V - T * sinR, s.normal);
    float azimuth = 0.25 * sqrt(0.5 + 0.5 * clamp(dot(lAcross, vAcross), -1.0, 1.0));
    vec3 primary = schlickFresnel(lobes.f0, dot(V, halfVectorOf(L, V, s.normal))) * longitudinalLobe(thetaH, PrimaryShift, lobes.alpha);
    vec3 secondary = lobes.f0 * lobes.diffuseColor * longitudinalLobe(thetaH, SecondaryShift, 2.0 * lobes.alpha);
    DirectTerms terms;
    terms.diffuse = lobes.diffuseColor * (cosI * InversePiSquared);
    terms.specular = (primary + secondary) * (azimuth * cosI);
    return terms;
}
```

where

- `s.strand` = T. `s.normal` = n_f.
- `lobes.alpha` = β, the floored roughness squared, at least 0.045² = 0.002.
- `lobes.diffuseColor` = b, because a hair has no metallic. `lobes.f0` = F0.
- `halfVectorOf(L, V, fallback)` = the unit half vector of L and V, with fallback (here the fiber normal) where L = −V. It is a new function of `lighting.glsl`, which Part 4 moves out of the lines in `brdf()` that compute the half vector today, so both responses use it.
- `DirectTerms` = the diffuse and the specular light of one light per unit irradiance (see "The light of each model").

### The strand and the fiber normal

`material.glsl`:

```glsl
// T: the strand from root to tip, the texture axis of the shading frame that the material names (StrandDirection).
vec3 strandOf(uint direction, vec3 n, vec4 vertexTangent)
{
    vec3 t = vertexTangent.xyz - n * dot(n, vertexTangent.xyz);
    float tangentLengthSq = dot(t, t);
    t = tangentLengthSq > 1e-12 ? t * inversesqrt(tangentLengthSq) : anyPerpendicular(n);
    vec3 alongV = cross(t, n) * (vertexTangent.w < 0.0 ? -1.0 : 1.0);
    vec3 axis = direction < 2u ? t : alongV;
    return (direction & 1u) == 0u ? axis : -axis;
}

// n_f: across the strand toward the eye. A strand along the line of sight takes the card's normal, which is across it.
vec3 fiberNormal(vec3 strand, vec3 V, vec3 cardNormal)
{
    return normalizedOr(V - strand * dot(strand, V), cardNormal);
}
```

where

- `direction` = the strand direction's code: 0 +u, 1 −u, 2 +v, 3 −v.
- `n` = the shading normal, after the normal map. `t` is the tangent of the frame that `normalMapped` builds, so a normal map tilts the strand.
- `alongV` = the direction in which the texture's v grows: the opposite of that frame's bitangent, cross(n, t) times the tangent's sign.
- `cardNormal` = the shading normal. `strandOf` made T perpendicular to it, so it is across the strand.

`sampleMaterial` sets, for a hair material, `s.strand = strandOf(...)` and then `s.normal = fiberNormal(s.strand, V, turnedToward(s.normal, V))`, with the card's normal turned toward the eye ("Both sides of a hair card"). Every pass that calls it, deferred or forward, so gets the fiber normal as the shading normal, and the G-buffer pass computes the sky shares at it while the map is off.

### Both sides of a hair card

A hair card is a sheet of strands with no back (decision 32). So a hair material is two-sided, and every other model is one-sided, because a solid's back faces are hidden: `MaterialStore.DrawsBothSides(model)` sets `MaterialInfo.TwoSided` once, when the material uploads, and the batcher copies it into `PreparedInstance.TwoSided`, as it copies the alpha mode.

- The opaque views that shade, the main view and the faces of the maps, split each `OpaqueRuns` by the sides into a `SidedRuns(OneSided, TwoSided)`. The sort key holds the run as two bits, cutout above two-sided, so the runs come in the order one-sided solid, two-sided solid, one-sided cutout, two-sided cutout, and both solid runs keep decision 25's early depth writes.
- Each pass that shades holds its pipelines as `SidedPipeline`s, one variant with the pass's cull mode and one with `CullModeFlags.None`: the G-buffer pass and the faces for their solid and cutout runs, the impostor captures for the solid and the cutout capture, chosen by the capture's `TwoSided`, and the transparent pass for its glass and fringe runs, which `GlassCommands` and `FringeCommands` mark by their `TwoSided`. A glass hair material is two-sided too, by the same rule.
- The shadow views already cull no face, so their runs stay one `OpaqueRuns`, and a card casts its shadow from either side.
- `sampleMaterial` takes the strand first, from the frame that the mesh gives, so the strand runs the same way seen from either side. Then it turns the card's normals toward the eye: the geometric normal, which the G-buffer stores and `forwardShading` takes, and the card's shading normal, which `fiberNormal` falls back to. So a card seen from behind stores and shades as the same card seen from the front.

### Hair in each pass

| Pass | What it does with a hair pixel |
|---|---|
| G-buffer | Draws both sides of the card. Writes n_f as the shading normal, the card's vertex normal turned toward the eye as the geometric normal, the strand's code and 2 into the shading target, and metallic 0 |
| Direct light | `hairResponse` for each light, which it takes from either side of its card. Its shadow lookups use the geometric normal turned toward the light. |
| Occlusion and bounce | Around n_f. Its own card blocks nothing (decision 33): its own surface blocks no sector, and a step whose pixel carries its card code is left out. Other cards, other instances and other surfaces block and bounce as for any surface |
| Ambient light | `ambientTerms` at n_f, with the occlusion and the bounce |
| Reflections | As any surface, along the dominant direction about n_f. A ray whose cosine with the card's normal is below `SurfaceFadeCosine` traces nothing, and the pixel keeps the ambient pass's reflection (decision 35) |
| Transparent (fringe) | The same functions, forward, with an occlusion of 1 and a bounce of 0 |
| Environment faces, impostor captures | The same functions, forward: the core only |

### The card number

A hair pixel's own card is the set of pixels that its card's triangles cover, at any bend of the card. Four steps carry it from the mesh to the occlusion sectors pass (decision 33):

1. `UploadQueue.AddMesh` numbers the cards of every mesh, on the caller's thread (`MeshCards.Number`), and builds each GPU vertex with its card (`StagedMesh.Of`). A card is a connected strip of triangles: two triangles lie on one card when a chain of triangles joins them, each with a vertex position of the next. So a strip whose exporter splits its vertices at a sharp bend or a seam, with one position and two normals, stays one card. The cards count from 0 in the order of their first triangles. The renderer does not know which material a mesh draws with, so it numbers every mesh.
2. Separate cards share no vertex, so every vertex of a triangle carries the triangle's card: `GpuVertex.Card`, a `uint` at offset 48 of the 52-byte vertex of the global vertex buffer, at location 4. The deform vertices carry it into the deformed copies (`GpuDeformVertex.Card`).
3. `mesh.vert` mixes the card with the instance's place among the frame's placed instances, `GpuInstance.PlacedIndex`, which every view and depth range of the instance shares, and passes the code flat as w of `outInstanceWords`:

   ```glsl
   uint cardCode(uint card, uint placedIndex)
   {
       return (card + placedIndex * 5063u) & 0x1FFFu;
   }
   ```

   where

   - `card` = the vertex's card number in its mesh, from 0.
   - `placedIndex` = the instance's place among the frame's placed instances, from 0.
   - 5063 = 2^13 / φ, rounded to an odd number: neighbor instances lie at least 3,129 codes apart, and the instances of one card take every code before one repeats.

4. `gbuffer.frag` writes a hair pixel's code into the shading target's 13 spare bits, and the sectors pass, for a hair receiver, reads each step's code at the step texel's representative pixel (`cardAt` in `occlusion.glsl`). A step whose code is the receiver's is left out. A pixel of another model has no code (`NoCard`), so it always blocks a hair receiver.

A collision makes another card's step count as the receiver's own, so that step blocks nothing. The cards of one instance never collide below 2^13 cards; in a mesh of more cards, cards i and i + 8,192 share a code. Two instances' cards collide in about one of 8,192 pairs: each card of one instance shares its code with at most one card of the other.

## Skin

### The light of each model

Each light's response becomes a pair, as the ambient source's already is. `lighting.glsl`:

```glsl
// The light of one light toward V per unit irradiance, its diffuse part and its specular part (docs/plans/skin-and-hair.md).
// The diffuse part has the diffuse color in it, as AmbientTerms.diffuse has.
struct DirectTerms
{
    vec3 diffuse;
    vec3 specular;
};

DirectTerms respond(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)
{
    return s.model == ShadingHair ? hairResponse(s, lobes, V, L) : surfaceResponse(s, lobes, V, L);
}

// Whether a light from L reaches the point at all, before its shadows: a fiber takes light from every side, a surface
// only from the side that both its normals face.
bool takesLight(ShadingInput s, vec3 L)
{
    return s.model == ShadingHair || (dot(s.normal, L) > 0.0 && dot(s.geometricNormal, L) > 0.0);
}
```

where

- `surfaceResponse` = today's `brdf()` with its two parts apart: c_diff / π · (n·l) and D · Vis · F · c · (n·l). Skin uses it, with metallic 0.
- `directLight` sums `DirectTerms` over the lights in the list's order, and `lobesRadiance` and `directRadiance` add the two parts where they added one sum before.
- A skin pixel's light in the lighting passes is the standard model's. Only the scatter makes it skin.

### The profile and the kernel

The profile of human skin, a sum of six Gaussians, G(v, r) = exp(−r² / (2v)) / (2πv), with these measured variances and weights per channel (d'Eon, Luebke and Enderton 2007):

| Variance (mm²) | Red | Green | Blue |
|---|---|---|---|
| 0.0064 | 0.233 | 0.455 | 0.649 |
| 0.0484 | 0.100 | 0.336 | 0.344 |
| 0.187 | 0.118 | 0.198 | 0 |
| 0.567 | 0.113 | 0.007 | 0.007 |
| 1.99 | 0.358 | 0.004 | 0 |
| 7.41 | 0.078 | 0 | 0 |

The skin scatters four times wider than this (decision 34). `SkinProfile` writes every length of the profile in one unit, a millimeter of the measured profile, which is WidthScale · 1 mm = 4 mm of the rendered skin. So every variance in the table counts 16 times as large, and the reach is 8 such units, 32 mm. A width scale on the Gaussians alone would leave the reach behind, and the outermost taps would cut the tails.

Each channel's weights sum to 1, so the profile keeps the light that enters. A 2D Gaussian is the product of two 1D Gaussians, so the kernel is the same sum in one dimension:

```
P_c(x) = Σ_k w_{k,c} · exp(−x² / (2 v_k)) / √(2π v_k)
```

The separable scatter applies P_c(x) along the rows and then P_c(y) along the columns: the product P_c(x) P_c(y). It keeps the profile's light and its marginal along each axis, and it is exact for one Gaussian. For the sum, it is Jimenez et al.'s separable approximation of the radial profile.

`SkinProfile.Kernel(tapsPerSide)` makes the taps, with n = tapsPerSide:

```
x_i = sign(i) · Reach · (i / n)²                          i = −n … n,  Reach = 32 mm
w_{i,c} = ∫ from b_i to b_{i+1} of P_c(x) dx              b_i = (x_{i−1} + x_i) / 2,  b_{−n} = −∞,  b_{n+1} = +∞
```

The taps crowd toward the center, where the profile is steep. The widest Gaussian has σ = 4 · 2.72 mm = 10.9 mm, so the reach is three of its widths at every width scale, and the outermost taps hold the tails beyond them. The center tap holds the narrowest Gaussians, which at n = 8 lie within its interval of ±0.25 mm. Each channel's weights sum to exactly 1. `SkinProfile` integrates each interval in doubles, by the error function. The kernel also carries the unit of its offsets for the slope rule, its width scale (`ScatterKernel.WidthScale`), which `SkinProfile.Kernel` sets to `SkinProfile.WidthScale`. The renderer uploads the kernel with the first frame as 2n + 1 `vec4`, rgb the weights and a the offset in meters, and the width scale after them.

### The diffuse light D

The direct pass and the ambient pass write the scatter image beside their own outputs, when the setting is `SeparableSubsurface`:

```glsl
// direct.frag
DirectTerms direct = directLight(s, lobes, position, V, ShadowMapsMainView, MainView);
outDirect = vec4(min(exposed(direct.diffuse + direct.specular, k) + emission, vec3(MaxRadiance)), 1.0);
outScatter = s.model == ShadingSkin ? vec4(exposed(direct.diffuse, k), 1.0) : vec4(0.0);

// ambient.frag; the blend adds outScatter to the scatter image
AmbientTerms ambient = ambientTerms(s, lobes, V);
outColor = vec4(min(direct + exposed(ambient.diffuse + ambient.specular, k), vec3(MaxRadiance)), 1.0);
outScatter = s.model == ShadingSkin ? vec4(exposed(ambient.diffuse, k), 0.0) : vec4(0.0);
```

where

- `k` = the exposure scale. `exposed(x, k)` = min(x, MaxRadiance / k) · k, at most MaxRadiance.
- `outDirect`, `outColor` = the direct image and the HDR image, as the bounce light's design writes them.
- `outScatter` = location 1 of each pass. The direct pass clears the scatter image and writes it. The ambient pass adds to it with the factors One and One. The HDR image takes no blend, so the two attachments of the ambient pass have different blend states, which `GraphicsPipelineDesc.ColorAttachments` gives per attachment.
- The location exists only in the shader variants `direct_scatter.frag` and `ambient_scatter.frag`, which define `SCATTER_OUTPUT` before they include the passes' bodies, `direct_pass.glsl` and `ambient_pass.glsl`. Under `NoSubsurface` the pipelines use `direct.frag` and `ambient.frag`, which have one output for their one attachment. A specialization constant around the write is not enough: Vulkan's validation reports a fragment output that no attachment takes, even when a constant turns its write off.

So the scatter image holds D = exposed(diffuse of the lights) + exposed(diffuse of the ambient light) at each skin pixel, at most 2 · MaxRadiance = 64,512, with 1 in alpha, and 0 everywhere else: at the sky, at an impostor billboard and at every pixel that is not skin.

### The two passes

`skin_scatter.glsl` holds the loop of one pass at one skin pixel, and `SkinScatter.Pass` is its C# copy:

```glsl
// One pass of the separable scatter (docs/plans/skin-and-hair.md, "The two passes"). source is the scatter image for the rows
// and the row image for the columns, center its value at the pixel. Each tap reads one pixel.
vec3 scatterAlong(ivec2 pixel, ivec2 axis, vec3 center)
{
    float depth = viewDepthAt(pixel);
    float pixelsPerMeter = frame.viewport.y / (2.0 * depth * length(frame.cameraUp.xyz));
    // The outermost tap's offset is the kernel's reach (ScatterKernel.Reach).
    if (kernel[2 * scatterTapsPerSide].a * pixelsPerMeter < 0.5)
        return center;
    ivec2 last = imageSize(scatterImage) - 1;
    vec3 sum = vec3(0.0);
    for (int i = 0; i <= 2 * scatterTapsPerSide; i++)
    {
        vec4 tap = kernel[i];
        // Clamped as a float first: a tap far past the image would overflow the conversion to an integer.
        float offset = clamp(floor(tap.a * pixelsPerMeter + 0.5), -MaxSide, MaxSide);
        ivec2 at = clamp(pixel + axis * int(offset), ivec2(0), last);
        vec3 light = center;
        if (i != scatterTapsPerSide && imageLoad(scatterImage, at).a > 0.0)
        {
            float run = abs(tap.a) / kernelWidthScale;
            float rise = abs(viewDepthAt(at) - depth) / (run * SlopeLimit);
            light = mix(center, sourceAt(at), clamp(2.0 - rise, 0.0, 1.0));
        }
        sum += tap.rgb * light;
    }
    return sum;
}
```

where

- `viewDepthAt(p)` = z at pixel p, from the distance target and the view ray.
- `frame.viewport.y` = H. `length(frame.cameraUp.xyz)` = tan(φ_y / 2). So `pixelsPerMeter` = s(z).
- `kernel[i]` = tap i of the array of 2 · 16 + 1 = 33 `vec4` at the start of the kernel buffer, zeros after the last tap: rgb its weights, a its offset x_i in meters. Tap n, `scatterTapsPerSide`, is the center, with offset 0. The kernel buffer is a uniform buffer that holds `GpuScatterKernel`, the C# copy of the block. A uniform buffer takes no storage-buffer slot of the compute stage, and the buffer's 532 bytes fit any device's uniform-buffer range.
- `MaxSide` = 16,384, the largest image side.
- `imageLoad(scatterImage, at).a` = 1 at a skin pixel, 0 elsewhere: the mask.
- `kernelWidthScale` = the kernel's width scale (`ScatterKernel.WidthScale`), the float after the 33 `vec4` in the same uniform block. The kernel that gives the offsets also gives their unit, so a kernel of another width never meets the slope rule of the skin's.
- `run` = the tap's offset in the measured profile, in meters: its offset over the kernel's width scale (decision 34).
- `rise` = the tap's depth step over the depth step of the slope limit across `run`. Up to 1 the tap keeps its own light, and from 1 to 2 it gives way to the center's.
- `sourceAt(at)` = the source image at the tap.

The passes:

1. "scatter rows" (`skin_scatter_rows.comp`): for each pixel, if the scatter image's alpha is 1, the row image gets `vec4(scatterAlong(pixel, ivec2(1, 0), D), 1.0)`. Every other pixel of the row image gets 0, so the image holds no value from an earlier frame.
2. "scatter columns" (`skin_scatter_columns.comp`): for each pixel whose scatter image's alpha is 1, S = `scatterAlong(pixel, ivec2(0, 1), rows)`, and the HDR image's pixel becomes clamp(HDR + S − D, 0, MaxRadiance). Every other pixel keeps its value.

The weights sum to 1 and every tap's light is a mix of finite values from 0 to 64,512, so S is too. A skin pixel lit evenly with its neighbors keeps its light: S = D up to rounding. A skin pixel that is lit beside one that is not loses light to it, and the red light goes farthest. That is the soft red edge at the terminator and at the edges of shadows.

Both passes are compute passes with a local size of 8 × 8. They bind the frame set and the G-buffer set of the scene layout, as the reflections' passes do, and their own set 3: the scatter image, the row image and the HDR image as storage images, and the kernel buffer. The HDR image has the storage usage when the reflections or the scatter are on.

### Where the scatter sits

```mermaid
flowchart TD
    GB["G-buffer pass: the shading target names skin"] --> DP["Direct pass: the direct image, and D's direct part into the scatter image"]
    DP --> OC["Occlusion passes: V and the bounce, from the direct image"]
    OC --> AP["Ambient pass: the HDR image, and D's ambient part added to the scatter image"]
    AP --> SR["scatter rows: the row image"]
    SR --> SCOL["scatter columns: HDR += S − D at skin pixels"]
    SCOL --> RP["Radiance pyramid and reflections: mirrors show the scattered skin"]
    RP --> TP["Transparent pass: glass and hair fringes over the skin"]
    TP --> EX["Exposure, bloom, tone map"]
```

| Pass | What it does with skin |
|---|---|
| G-buffer | Writes 1 into the shading target, and metallic 0 |
| Direct light | The standard response. Writes D's direct part. The direct image holds the whole direct light, which the bounce bounces |
| Occlusion and bounce | As any surface |
| Ambient light | The standard ambient light. Adds D's ambient part |
| Scatter rows, scatter columns | The scatter, at skin pixels only |
| Reflections | After the scatter: the pyramid holds the scattered skin, and the delta adds the specular reflection, which does not scatter |
| Transparent | A glass skin is shaded as standard, with no scatter |
| Environment faces, impostor captures | The standard model (decision 19) |
| Lit puffs | None |

With `NoSubsurface`, the renderer makes no scatter images, no kernel buffer and no scatter passes, and the direct and ambient pipelines have one color attachment each. A skin pixel then shades exactly as the standard model.

## Every new value, its writers and its readers

The stages are those of `FrameGraph.Stage` with the lights and the bounce merged. The uploads come before the graph, and batching on the CPU before it too.

| Value | Written by | When | Read by | When | Stored in | Analogue |
|---|---|---|---|---|---|---|
| `MaterialDesc.Model`, `MaterialDesc.Alpha` | Scenes, components, the importer later, through `CreateMaterial` | Any thread, before the material's upload | `MaterialStore.Upload` | The uploads | The upload queue | `MaterialDesc.Metallic` |
| `GpuMaterial.Shading`, `GpuMaterial.AlphaCutoff` | `MaterialStore.ToGpu` | The uploads | `sampleMaterial` in every mesh pass, `shadow_cutout.frag`, `fringe.frag` | `ShadowMaps` to `Transparent` | The material buffer | `GpuMaterial.NormalMap` |
| `MaterialInfo` of a slot | `MaterialStore.Upload` | The uploads | `SceneBatcher.Prepare` | Batching | `MaterialStore.Infos` | `MeshInfo` |
| `PreparedInstance.Alpha` | `SceneBatcher`, from the slot's `MaterialInfo` | Batching | The opaque sort, the runs, the transparent list, `ImpostorManager` | Batching | The prepared scene | `PreparedInstance.Flags` |
| `OpaqueRuns` of each view | `SceneBatcher`, `ShadowPass`, `LocalShadowPass` | Batching | `GeometryPass`, `ShadowPass`, `LocalShadowPass`, `EnvironmentFacePass` | `ShadowMaps`, `EnvironmentFaces`, `GBuffer` | `PreparedScene` | Today's `CommandRange` per view |
| `MaterialInfo.TwoSided`, `PreparedInstance.TwoSided` (decision 32) | `MaterialStore.Upload`, then `SceneBatcher` | The uploads, batching | The opaque sort, the runs, the transparent runs, `ImpostorManager` | Batching | `MaterialStore.Infos`, the prepared scene | `PreparedInstance.Alpha` |
| `ImpostorCapture.TwoSided` (decision 32) | `ImpostorManager`, from `PreparedInstance.TwoSided` | Batching | `ImpostorPass.PrepareCaptures` | `FrameGraph.Prepare`, before the stages | `ImpostorManager.Captures` | `ImpostorCapture.Alpha` |
| `PreparedCapture.TwoSided` (decision 32) | `ImpostorPass.PrepareCaptures` | `FrameGraph.Prepare` | `ImpostorPass`, which binds the capture pipeline's variant by it | `ImpostorCaptures` | `ImpostorPass`'s prepared captures | `PreparedCapture.Cutout` |
| `GlassCommands.TwoSided`, `FringeCommands.TwoSided` (decision 32) | `SceneBatcher`, from `PreparedInstance.TwoSided` | Batching | `TransparentPass`, which binds the glass or fringe pipeline's variant by it | `Transparent` | `PreparedScene.Transparent` | The run's kind |
| The `SidedPipeline` variants (decision 32) | The constructors of `GeometryPass`, `EnvironmentFacePass`, `ImpostorPass` and `TransparentPass` | The renderer's creation | `GeometryPass.DrawRuns` for the main view and the faces, `ImpostorPass`, `TransparentPass` | `EnvironmentFaces`, `ImpostorCaptures`, `GBuffer`, `Transparent` | Each pass | The cutout pipelines |
| `SidedRuns` of the main view and of each face (decision 32) | `SceneBatcher` | Batching | `GeometryPass`, `EnvironmentFacePass` | `EnvironmentFaces`, `GBuffer` | `PreparedScene` | `OpaqueRuns` |
| `FringeCommands` runs | `SceneBatcher` | Batching | `TransparentPass` | `Transparent` | `PreparedScene.Transparent` | `MeshCommands` |
| The kernel | `SkinProfile.Kernel`, once at creation, then uploaded | The first frame's uploads | The scatter passes | `SkinScatter` | The kernel buffer of `ScatterImages` | The BRDF tables |
| `SurfaceSample.model`, `.strand`, the fiber normal | `sampleMaterial` | Per fragment | `gbuffer.frag`, the forward passes | Per fragment | None: values in the shader | `SurfaceSample.normal` |
| The shading target | `gbuffer.frag`, `impostor.frag` | `GBuffer` | `surfaceAt` in the direct pass, the ambient pass and the reflections, and the test hook `ReadShading` | `DirectLight`, `AmbientLight`, `Reflections` | `RenderTargets.Shading`, G-buffer binding 10 | The specular target |
| `ShadingInput.model`, `.strand` | `surfaceAt`, `forwardShading` | Per pixel | `respond`, `takesLight`, the scatter writes | Per pixel | None | `ShadingInput.map` |
| `DirectTerms` | `respond` | Per light | `directLight` and its callers | Per pixel | None | `AmbientTerms` |
| The scatter image | The direct pass (clears, writes D's direct part and the mask), the ambient pass (adds D's ambient part) | `DirectLight`, `AmbientLight` | The rows pass (taps), the columns pass (mask, D at the center), the test hook `ReadScatter` | `SkinScatter` | `ScatterImages.Scatter` | The bounce's direct image |
| The row image | The rows pass | `SkinScatter` | The columns pass | `SkinScatter` | `ScatterImages.Rows` | Occlusion term B |
| The HDR image at skin pixels | The columns pass adds S − D | `SkinScatter` | The radiance pyramid and every later pass | `Reflections` and later | `RenderTargets.Hdr` | The reflections' delta |
| `RendererOptions.Subsurface` | The host (`--subsurface on|off`), tests | Before the renderer is made | `CheckOptions`, the choice of the direct and ambient passes' shader variants, `ScatterImages`, whose `Setting` alone gives the scatter passes their taps per side through `ShaderConstants.Subsurface`, the scatter passes | At creation | `RendererOptions` | `RendererOptions.Reflections` |

Each ordering rule has its reason:

- The uploads write a material's GPU row and its `MaterialInfo` before batching, so the batcher never sees a slot that the buffer lacks.
- The solid run comes before the cutout run in each view, so the solid pipeline's early depth writes hide the hair fragments behind solid surfaces (decision 25). Both solid runs, one-sided and two-sided, come before both cutout runs for the same reason.
- The direct pass writes the scatter image before the ambient pass adds to it, because the ambient pass reads the direct image and runs after the occlusion passes.
- The scatter runs after the ambient pass and before the reflections (decision 18).
- The fringe draws in the transparent pass, after the reflections, as the glass does, so the reflections neither show it nor lose it.

Stored state describes the last finished step: the shading target describes the G-buffer pass of this frame, the scatter image the lighting passes of this frame, the row image the rows pass of this frame. Nothing carries over between frames. Every sum runs in a fixed order and no pass uses atomics, so one GPU gives the same bits on every run.

## The flow of each kind of object

```mermaid
flowchart LR
    subgraph standard["Standard, opaque: a wall"]
        a1["Solid draws"] --> a2["G-buffer: code 0"] --> a3["Lighting: the BRDF"]
    end
    subgraph leaf["Standard, cutout: a fence"]
        b1["Cutout draws in every opaque pass"] --> b2["G-buffer: code 0, holes discarded"] --> b3["Lighting: the BRDF"]
    end
    subgraph hair["Hair, fringed: a card"]
        c1["Cutout draws of the core"] --> c2["G-buffer: code 2, the strand, n_f"] --> c3["Lighting: the fiber"]
        c4["Fringe draw in the transparent pass"] --> c5["The fiber, forward, blended by coverage"]
    end
    subgraph skin["Skin, opaque: a face"]
        d1["Solid draws"] --> d2["G-buffer: code 1"] --> d3["Lighting: the BRDF and D"] --> d4["Scatter: HDR += S − D"]
    end
    subgraph glass["Standard, glass: a canopy"]
        e1["Transparent pass only"] --> e2["The glass rule, as today"]
    end
```

| Kind | Opaque passes | G-buffer code | Lighting | Scatter | Transparent pass | Impostor |
|---|---|---|---|---|---|---|
| Standard, opaque | Solid | 0 | BRDF | No | No | May |
| Standard, cutout | Cutout | 0 | BRDF | No | No | May, with holes |
| Standard, fringed | Cutout (core) | 0 | BRDF | No | Fringe, BRDF | Never |
| Standard, glass | Shadow only | None | None | No | Glass rule | Never |
| Skin, opaque | Solid | 1 | BRDF, D | Yes | No | May, as standard |
| Skin, cutout or fringed | Cutout (core) | 1 | BRDF, D | The core | The fringe, BRDF | As its alpha mode |
| Hair, fringed | Cutout (core) | 2 | Fiber | No | Fringe, fiber | Never |
| Hair, cutout | Cutout | 2 | Fiber | No | No | May, with holes |
| Hair, opaque | Solid | 2 | Fiber | No | No | May |
| Sky, impostor billboard | None | 0 | None, as today | No | No | |

Every kind takes one path. Only the alpha mode's runs and the model's response differ, and pure predicates choose them.

## Portable Vulkan

### Required device features

| Requirement | Why the renderer needs it | Why the Linux drivers offer it |
|---|---|---|
| `independentBlend`, new | The ambient pass blends its scatter output additively and writes the HDR image with no blend (decision 14) | A Vulkan 1.0 feature that NVIDIA, RADV and ANV offer on every GPU that they support, as Direct3D 10.1 and later require a full blend state per render target, and every GPU these drivers support is Direct3D 11-class. lavapipe sets it, as llvmpipe blends each render target on its own. MoltenVK offers it on every Mac GPU. `VulkanDevice` checks it before it creates the device. The reviewer of Part 5 checks these claims against the drivers' feature tables. |
| `R8G8B8A8_UINT` as a color attachment and a sampled image | The shading target | The spec requires both for this format of every device |
| `R16G16B16A16_SFLOAT` as a blended color attachment and a storage image | The scatter image and the row image | The spec requires both for this format of every device. A storage image of this format needs no `shaderStorageImageExtendedFormats` |

### The new sites

| Site | The input that would break it | Verdict |
|---|---|---|
| The cutout test (`material.glsl`), the fringe's tests (`fringe.frag`, `shadow_cutout.frag`) | A NaN alpha or cutoff | Safe: `ShaderInput.Fraction` reads the base color's alpha into [0, 1] with a NaN as 0, a UNORM map gives [0, 1], and `CreateMaterial` refuses a cutoff that is not in (0, 1] |
| The fringe's output | A sum past 65,504 in the blend | Safe: `surfaceRadiance` caps the light at MaxRadiance, so the output is at most MaxRadiance · a = MaxRadiance · (1 − T): the glass's bound (ARCHITECTURE.md, "Portable Vulkan") |
| `strandOf` | A zero tangent, or one along the normal | Fixed: `anyPerpendicular(n)`, as `normalMapped` does |
| `fiberNormal` | A strand along the line of sight | Fixed: `normalizedOr` with the card's normal, which is across the strand |
| `hairResponse`: `asin`, `sqrt` | An operand past ±1 by rounding | Fixed: the sines are clamped to [−1, 1], and the operands of `sqrt` are 1 − sin² ≥ 0 and 0.5 + 0.5 · cos φ ≥ 0 |
| `hairResponse`: the projections across the strand | A light or the eye along the strand | Fixed: `normalizedOr` with the fiber normal. cos θ_i is then 0 for the light, and the response 0 |
| `longitudinalLobe` | A width of 0 | Safe: the width is at least 0.045² ≈ 0.002, so the divisions are finite. The exponent is at least −π² / (2 · 0.002²), and exp of it is 0 or a subnormal that a driver may flush to 0 |
| `strandCode` | An octahedral value past [−1, 1] by rounding | Safe: the clamp to [0, 255] runs on finite values, and the conversion to `uint` sees an integer value in range |
| The shading model's code | None: an integer comparison | Safe |
| `pixelsPerMeter` in `scatterAlong` | A view depth or a field of view of 0 | Safe: a skin pixel has a distance above 0, and its view ray lies inside the frustum, so its forward cosine is above 0. tan(φ_y / 2) ≥ tan(5 · 10⁻⁷) by the field of view's floor. A very near pixel with a narrow field gives a large but finite value |
| The tap's offset | A product past the range of an `int` | Fixed: the offset is clamped to ±16,384 as a float before the conversion, and the pixel is clamped to the image |
| `rise` | A tap offset of 0 | Safe: the center tap, the only one at offset 0, skips the test. Every other offset is at least Reach / n² = 0.125 mm at n = 16 |
| The scatter image's blend | A sum past 65,504 | Safe: each part is at most MaxRadiance, so the exact sum is at most 64,512, which a half float holds exactly, and a blend that rounds to nearest or toward zero gives at most that |
| The columns pass's store | A negative or a too large sum | Safe: every input is finite and at most 64,512, and the clamp to [0, MaxRadiance] acts on finite values |

No new value relies on a clamp to turn a NaN into a number, and no new value is tested for a NaN.

## Tests

The tests follow tests/STYLE.md: one behavior per test, named as a statement. Each is written first and seen to fail for the right reason. No test adds a reference image. The GPU tests use small outputs, 160 × 90 or 320 × 180, and the helpers' settings, with the feature under test turned on. Each builder makes a hand mutation for each new test (CLAUDE.md, "Testing") and names it in the report.

### Part 1: the material API and glass in the material

`MaterialDescTests` (new, Tertius.Rendering.Tests):

- `TheDefaultMaterialIsAStandardNonMetalAndOpaque`
- `ACutoffOutsideZeroToOneIsRefused`: 0, −0.1, 1.5 and NaN throw `ArgumentOutOfRangeException` from `CreateMaterial`. 1 does not.

`MaterialStoreTests` (new):

- `AStandardMaterialKeepsItsMetallic`
- `ASkinOrHairMaterialHasNoMetallic`
- `TheShadingWordNamesTheModelAndAHairsStrandDirection`: every model, every strand direction, against `ShadingCode.Word`.
- `ACutoutOrFringedMaterialCarriesItsCutoff`, `AnOpaqueOrGlassMaterialCarriesNone`
- `EachSlotsInfoHoldsItsAlphaMode`, `TheDefaultSlotIsOpaque`

`BatcherTests` changes its transparent instances to glass materials. New:

- `AGlassMaterialDrawsInTheTransparentPassOnly`
- `AnOpaqueMaterialWithTheGlassBitOfOldIsOpaque`: no flag of an instance makes it glass.

`ImpostorManagerTests`: `AGlassMaterialNeverBecomesAnImpostor`.

The existing GPU tests of glass, puffs and the transparent pass pass unchanged in their pixels, with glass materials.

### Part 2: the math in C#

`SkinProfileTests` (new):

- `EachChannelOfTheHumanProfileSumsToOne`
- `RedScattersFartherThanGreenAndGreenFartherThanBlue`: the marginals' variances, Σ w_k v_k per channel, in that order.
- `TheKernelsWeightsSumToOneInEachChannel`, for 1 to 16 taps per side.
- `TheKernelIsSymmetric`
- `RedLightSpreadsFourTimesAsFarAsInTheMeasuredSkin`: the red marginal's standard deviation is 4 times the measured profile's 1.176 mm (decision 34).
- `TheReachSpansTheSameWidthsOfTheWidestGaussianAtAnyScale`: Reach / σ of the widest Gaussian is 8 mm / 2.72 mm, so the width scale moves the reach and the Gaussians together.
- `TheTapsCrowdTowardTheCenter`: x_1 = Reach / n², x_n = Reach.
- `TheOutermostTapsHoldTheTails`: w_n is the integral from b_n to infinity.
- `TheKernelsSecondMomentIsTheProfilesVariance`: Σ w_i x_i² within 5 % of Σ w_k v_k at 8 taps per side. The test records the measured error.

`SkinScatterTests` (new), each on a small `ScatterField`:

- `AnEvenlyLitSkinRowScattersToItself`
- `LightCrossesALitEdgeIntoTheDarkAndRedGoesFarthest`
- `ATapOnAPixelThatIsNotSkinGivesWayToTheCenter`
- `ATapAcrossADepthStepGivesWayToTheCenter`
- `ATapWithinTheSlopeLimitKeepsItsOwnLight`
- `ATapAcrossTheNosesDepthStepGivesWayToTheCenter`: a tap at 8 mm across a step of 11 mm, the step at the edge of the test head's nose. The width scale widens the scatter, not the depth step that a tap may cross.
- `ATapWithinTheSlopeLimitOfItsMeasuredOffsetKeepsItsOwnLight`: a step of 0.9 times the slope limit's step across the tap's offset over the width scale.
- `ATapBetweenOneAndTwoSlopeLimitsMixesItsLightWithTheCenters`
- `AKernelAtTheMeasuredWidthJudgesADepthStepByItsWholeOffset`: the slope rule takes the unit of the offsets from the kernel that the pass runs, `ScatterKernel.WidthScale`, not from `SkinProfile.WidthScale` (`ter-o0a6`).
- `AKernelBelowHalfAPixelLeavesThePixelAsItWas`
- `TheKernelsOwnOutermostTapDecidesWhetherItReachesPastThePixel`: the pass takes the reach from the kernel that it runs, `ScatterKernel.Reach`, so the shader keeps no copy of `SkinProfile.Reach`.
- `TwiceTheDepthHalvesThePixelsOfTheKernel`
- `TapsPastTheImageReadTheEdgePixel`

`HairFiberTests` (new):

- `UnderEvenLightTheDiffusePartSendsTheBaseColorTimesTheLight`: a numerical integral over the sphere.
- `EachHighlightSendsAtMostItsWeightOfTheInterceptedLight`
- `ThePrimaryHighlightPeaksFiveDegreesTowardTheRootOfTheMirrorAngle`
- `TheSecondaryHighlightPeaksTowardTheTipAndTakesTheBaseColor`
- `ALightAlongTheStrandAddsNothing`
- `TheHighlightsPeakWhereTheEyeIsOnTheLightsSideOfTheFiber`
- `TheStrandFollowsTheTextureAxisThatTheMaterialNames`: all four directions.
- `ANormalMapTiltsTheStrandWithItsFrame`
- `AZeroTangentGivesAStrandAcrossTheNormal`
- `TheFiberNormalFacesTheEyeAcrossTheStrand`
- `AStrandAlongTheLineOfSightTakesTheCardsNormal`

`StrandCodeTests` (new): `EveryUnitStrandSurvivesTheEightBitCodeWithinOneDegree`, over a dense set of directions. The test records the largest error. `ShadingCodeTests` (new): `EachModelHasItsOwnCode`, `TheStandardCodeIsZeroAsInAClearedTarget`.

`DirectTermsTests` (new): `TheDiffusePartIsTheLambertTermAlone` and `TheSpecularPartIsTheCompensatedLobeTimesTheCosineOfTheLight`, each against its own expression of the BRDF, not against `MaterialSample.Reflected`, which is their sum.

### Part 3: cutout and fringe drawing

`BatcherTests` gains:

- `ACutoutMaterialDrawsAfterTheSolidOnesInItsRange`
- `AFringedMaterialDrawsItsCoreWithTheCutoutsAndItsFringeWithTheTransparentObjects`
- `AFringeSortsWithTheGlassAndThePuffsByItsCenter`
- `ACutoutCasterDrawsInTheCutoutRunOfEachCascade` and `...OfEachAtlasTile`
- `AFaceDrawsTheCoreOfAFringedMaterial`

`ImpostorManagerTests`: `AFringedMaterialNeverBecomesAnImpostor`, `ACutoutMaterialMayBecomeAnImpostor`.

`CutoutRenderTests` (new, GPU), with a card whose image has stripes of alpha 0, 0.4 and 1, and a cutoff of 0.5:

- `ATexelBelowTheCutoffShowsWhatLiesBehind`
- `ATexelAtTheCutoffOrAboveWritesTheGBuffer`
- `ACutoutCasterShadowsOnlyWhereItsTexelsReachTheCutoff`, in a cascade and in an atlas tile.
- `AnEnvironmentFaceSeesOnlyTheCore`
- `AnImpostorCaptureKeepsTheHolesOfACutout`
- `AnOpaqueMaterialIgnoresItsAlpha`: no shading reads an opaque material's alpha. It does not guard the routing into the solid run, which `AnOpaqueMaterialWithTheGlassBitOfOldIsOpaque` pins, because an opaque material's cutoff is 0.

`FringeRenderTests` (new, GPU):

- `ACoreTexelOfAFringedCardDrawsNothingInTheTransparentPass`: a lit, matte card of alpha 0.6, and of alpha 0.5 at the cutoff, drawn as fringed equals the same card drawn as cutout, bit for bit. The G-buffer's 8-bit base color and 16-bit normals make forward light differ from deferred light, so a second draw of the core shows.
- `AClearTexelOfAFringedCardLeavesThePixelBehindItAsItWas`: the acceptance of "the cards do not show". A strong light at the mirror angle of the card, where a glass pane shows its highlight. Behind the clear half, the pixels equal the scene without the card, bit for bit. The same card as glass shows the highlight there, which shows that the test can fail.
- `AFringeTexelCoversThePixelByItsAlpha`: 0.4 · the card's light + 0.6 · the background, within the half float's rounding.
- `AFringeBehindAnOpaqueSurfaceDrawsNothing`
- `TheFringeOfANearCardBlendsOverTheCoreOfAFarCard`
- `AFringeAtTheCapKeepsTheSurfacesShareOfTheHdrImage`

### Part 4: the shading target and hair shading

`ShadingTargetTests` (new, GPU):

- `TheGBufferWritesEachModelsCode`
- `TheGBufferWritesTheStrandThatStrandCodeGives`
- `AnImpostorBillboardAndTheSkyHoldTheStandardCode`

`HairRenderTests` (new, GPU), with a card facing the eye whose strands run along +v:

- `AHairPixelSendsTheLightThatHairFiberGives`: a point light and the sun at several angles, against the C# copy, within the half float's rounding.
- `TwoCardsWithOneStrandAndDifferentNormalsShadeAlike`: the acceptance of "the planes do not show", with the screen-space light off, within the half float's rounding, and under the default screen-space light, within 1% (decision 33). The same cards as standard shade apart.
- `UnderTheScreenSpaceLightAHairCardDarkensAHairCardBehindIt`: one card still shadows another (decision 33).
- `UnderTheScreenSpaceLightTheShadingTargetIsVisibleToTheComputeReadsOfTheOcclusionSectorsPass`: the sectors pass declares its read of the shading target, so its barrier makes the G-buffer pass's writes visible to the compute stage.
- `TheHighlightMovesAlongTheStrandAsTheLightMoves`
- `AHairPixelTakesLightFromBehindItsCard`
- `TheHairsAmbientLightIsTheSourcesAtTheFiberNormal`, with the map on and off, and with the screen-space light off and on, within 1%.
- `TheFringeShadesAsTheCoreBesideIt`: with an occlusion of 1, a fringe texel's light over its coverage equals a core pixel's of the same strand.
- `AnEnvironmentFaceAndACaptureShadeHairAsFibers`
- `StandardPixelsKeepTheirLight`: within the existing tests' tolerances. The six reference images pass.

`HairReflectionRenderTests` (new, GPU, `ter-nwhv`), on a hair floor whose normal map tilts its strands off the card, for decision 35:

- `AHairPixelWhoseRayPointsBelowTheSurfaceFadeKeepsTheEnvironmentMapsReflection`: at tilts of 10° and 45°, at both trace resolutions, each pixel whose ray has a cosine below `SurfaceFadeCosine` with the card shows what it shows with the reflections off, within the half float's rounding, and that light is above 0.
- `AHairPixelWhoseRayGrazesItsCardKeepsTheAmbientTermExactly`: at both trace resolutions, each pixel whose ray has a cosine between 0 and `SurfaceFadeCosine` with the card shows exactly what it shows with the reflections off.
- `AStandardPixelWhoseRayGrazesItsSurfaceTakesTheScreensReflectionInTheShareOfTheSurfaceFade`: on the same floor of the standard model, with the environment map off, a pixel of fade f changes by at most f · W times the brightest light in the scene, and more than half of the pixels change. So the hair rule does not reach other surfaces. The map is off because the map holds the wall, and under it a delta without its fade also keeps inside the bound. The other surfaces keep the same grazing hits that made the hair's edge ragged (`ter-acr3`).
- `AHairCardWhoseStrandsLieInItsPlaneReflectsTheWallThroughItsRays`: without a tilt, every ray points at least `SurfaceFadeCosine` above the card, and a hit replaces the ambient specular term W · gray with W · C, within 2 %.

`SectorSearchTests` (existing, C#), for decision 33, since `ter-3yy5` by the card code:

- `AHairReceiverTakesNoStepThatReadsItsOwnCard`
- `AHairReceiverTakesNoStepOfItsOwnCardWhereTheCardBendsAwayFromItsPlane`
- `AHairReceiverTakesTheStepsOfAnotherCard`
- `AHairReceiverTakesTheStepsOfASurfaceThatIsNotHair`
- `AHairReceiversOwnSurfaceBlocksNoSector`

`BounceLightShaderTests` (existing, GPU): `TheSectorPassGathersWhatTheCSharpCopyGathersForAHairReceiver`, a hair card turned 40° on the corner's floor, pins the sectors pass at three hair receivers to the C# copy with its `OwnCard` and the card codes that the shading target holds.

Added by `ter-3yy5`, the card number:

- `MeshCardsTests` (C#): `EachConnectedStripIsOneCardAndEachTriangleTakesItsStripsNumber`, `TrianglesThatMeetOnlyAtVerticesOfOnePositionAreOneCard`, `CardsAreNumberedInTheOrderOfTheirFirstTriangles`.
- `CardCodeTests` (C#): `TheCardsOfOneInstanceHaveDistinctCodesUpToEightThousandOneHundredNinetyTwoCards`, `OneCardOfTwoInstancesHasTwoCodes`, `TheCardsOfNeighborInstancesLieFarApartInTheCodes`, `AHairPixelsChannelsGiveBackItsCodeItsLightMarkAndItsModel`, `APixelOfAnotherModelHasNoCard`.
- `HairRenderTests` (GPU): `UnderTheScreenSpaceLightABentSmoothShadedCardShadesAsAFlatCard`, a card of two smooth-shaded segments, each turned 15° from its ridge, turned 40° about its strand, against a flat card that faces the eye, within 1% at the ridge and on each segment; `UnderTheScreenSpaceLightOneCardOfAHairMeshDarkensAnotherCardOfTheSameMeshBehindIt`. `UnderTheScreenSpaceLightAHairCardDarkensAHairCardBehindIt` draws two instances of one mesh, so it pins the instance in the code.
- `ShadingTargetTests` (GPU): `TheGBufferWritesAHairPixelsCardCodeBesideItsLightMarkAndItsModelsCode`, two cards of one mesh in two instances.
- `DeformationGpuTests` (GPU): `ADeformedCopyCarriesTheCardNumbersOfItsMesh`.

### Part 5: the skin scatter

`SubsurfaceSettingTests` (new): `TheZeroValueIsNoSubsurface`, `TheDefaultTakesEightTapsPerSide`, `TapsOutsideOneToSixteenAreRefused`.

`SkinScatterRenderTests` (new, GPU). The scenes: a skin sphere of radius 0.1 m, the size of a head, lit from the side by the sun, with a small sphere in front as a nose. A spot light at a finite distance moved the terminator by about 9 pixels from the one of a parallel light, so the test reads the columns beside the terminator of the sun. A skin plane beside an emissive plane that is not skin. A skin plane 0.5 m in front of another.

- `TheScatterImageHoldsTheDiffuseLightOfSkinPixelsAndNothingElse`: against the C# copy of the BRDF and the ambient light.
- `ThePassesMatchTheirCopyInCSharp`: HDR with the scatter minus HDR without it, against `SkinScatter` run on the read scatter image and distances.
- `ScatterCarriesRedPastTheTerminatorFartherThanBlue`: the acceptance. Past the terminator, the ratio of red to blue rises above that of the scatter off.
- `ScatterLeavesAnEvenlyLitSkinPlaneAsItWas`
- `ScatterLeavesEveryPixelThatIsNotSkinAsItWas`: bit for bit.
- `ScatterTakesNoLightFromASurfaceThatIsNotSkin`
- `ScatterCrossesNoDepthStep`
- `ScatterSpansHalfThePixelsAtTwiceTheDistance`
- `WithoutTheScatterSkinShadesAsTheStandardModel`: bit for bit against a standard material of metallic 0.
- `AMirrorShowsTheScatteredSkin`

`FrameGraphTests`: `TheScatterRunsAfterTheAmbientLightAndBeforeTheReflections`. `DeviceFeatureTests`: `TheDeviceRequiresIndependentBlend`. `GpuTimingTests`: the two passes appear only under `SeparableSubsurface`.

### The existing tests

| Test | What changes | Why |
|---|---|---|
| The six reference images | Unchanged | Their materials are standard and opaque, and the split of `brdf()` into two parts rounds within FLIP's tolerance. Any reference that changes gets a real review |
| Tests that set `Metallic` | `Model: new StandardShading(Metallic: ...)` | Decision 6 |
| Tests that set `RenderFlags.Transparent` | A glass material | Decision 8 |
| Tests that call `SceneBatcher.Prepare` | Pass the material infos | `MaterialInfo` |
| Tests of `GpuMaterial`'s layout | 60 bytes of fields, a stride of 64 | Two new fields |

## Cost

### Memory

At 1920 × 1080, 2,073,600 pixels:

| Item | Arithmetic | Memory |
|---|---|---|
| The shading target, always | 4 B × 2,073,600 | 8.3 MB |
| The scatter image and the row image, under `SeparableSubsurface` | 2 × 8 B × 2,073,600 | 33.2 MB |
| The kernel buffer | 33 × 16 B, room for 16 taps per side, and 4 B for the width scale | 532 B |
| `GpuMaterial` | Unchanged: the new fields take its padding | 0 |
| `PreparedInstance.Alpha`, on the CPU | 8 B × 20,000 instances | 160 KB |
| The card number, `ter-3yy5` | 4 B per static vertex and per region vertex, 16 B per deform vertex | 0.08 MB for a mesh of 20,000 vertices; if it deforms, 0.32 MB more once and 0.08 MB per deformed instance |

At 3840 × 2160 the images take four times as much: 33.2 MB and 132.7 MB.

The memory is the arithmetic of the formats as built: the shading target is R8G8B8A8_UINT, the scatter image and the row image R16G16B16A16_SFLOAT, and the kernel buffer `GpuScatterKernel`, 33 `vec4` and a float. `GpuMaterial` keeps its stride of 64 bytes, with 60 bytes of fields.

### GPU time, measured

Part 6 replaced the estimates of the design with traces (2026-10-27): `tools/gpu-trace.sh --no-counters` at 1920 × 1080 in Release, on the development Mac (Apple M1 Max, MoltenVK 1.4.2), 120 frames per run, one run at a time, each started after the 1-minute load fell below 10. The machine was shared with other agents, and the load rose to 26 during one run. So a difference counts only when it is larger than the difference between the runs of one configuration. The times are each pass's own GPU time from the trace, the mean in ms with the 95th percentile in parentheses. The frame span is the trace's; the GPU timer's span agrees with it within 0.2 ms in every run. Three of the 27 traces hung in xctrace's processing after the demo had finished (the first base run, which ran again, the first branch run with the scatter off and the second run of the cutout fan), so only their GPU timer's spans count. The review of Part 6 added five runs (the third with the scatter off on demo-exit, the second of each fan with the scatter off, and the third of the cutout fan with it on), so that each configuration has two traces.

The shading target's cost with no skin and no hair: the deck window of `scripts/demo-exit.json` (`--start 399`), the branch at 27e49735 against its base, the merge-base with main, dbd3011e. The load at the start was 6.9 to 9.9.

| Pass | Base, 3 runs | Branch, 3 runs | Branch, `--subsurface off`, 2 runs |
|---|---|---|---|
| G-buffer | 0.282, 0.310, 0.287 (0.33) | 0.358, 0.312, 0.324 (0.31 to 0.49) | 0.334, 0.339 (0.39 to 0.40) |
| direct light | 5.60, 5.37, 5.75 (6.1 to 6.9) | 7.12, 6.13, 6.28 (6.2 to 10.0) | 6.59, 6.69 (8.8 to 8.9) |
| ambient light | 0.420, 0.516, 0.434 (0.48 to 0.52) | 0.626, 0.582, 0.595 (0.59 to 0.74) | 0.433, 0.442 (0.56) |
| scatter rows | None | 0.071, 0.067, 0.066 (0.07 to 0.08) | None |
| scatter columns | None | 0.039, 0.035, 0.037 (0.04 to 0.05) | None |
| transparent | 1.64, 1.62, 1.71 (2.0 to 2.1) | 2.47, 2.10, 2.16 (2.2 to 3.4) | 2.28, 2.32 (3.0 to 3.1) |
| frame span | 16.87, 17.34, 17.35 (18.1 to 23.3) | 20.54, 18.07, 18.49 (18.2 to 27.4) | 18.82, 19.00 (24.0 to 24.9) |

The GPU timer's spans: base 17.28, 16.73, 17.23, 17.21; branch 20.38, 17.91, 18.32; branch with the scatter off 17.76, 18.67, 18.84. The first branch run was disturbed: every pass of it is slower. The two traces with the scatter off agree within 0.1 ms in each pass shown, and both have a high 95th percentile; most of their other passes, such as the reflections and the occlusion sectors, are also 0.05 to 0.2 ms slower than in the branch's runs with the scatter on.

- The epic costs demo-exit about 1 ms of frame span with the scatter on, 17.2 against 18.3 ms without the disturbed run, about 6 %. The design estimated about 0.3 ms.
- The direct pass grows by 0.4 to 0.9 ms with the scatter on (6.13 and 6.28 against 5.37 to 5.75 ms) and by 0.8 to 1.3 ms with it off (6.59 and 6.69 ms), against an estimate of 0.02 ms for the shading target's read. So the growth does not come from the scatter. That the pass takes 0.3 to 0.6 ms more with the scatter off, where it writes one attachment fewer, is not explained: part of it may be the state of the machine, as the off runs' other passes are slower too. No run isolates the cause of the growth. Ticket `ter-uqmn` isolated it and brought most of it down in both configurations. Its runs of 2026-10-31 with the scatter off took as long as with it on, so the extra time with the scatter off did not repeat (see "The shading models' cost" below).
- The transparent pass grows by 0.4 to 0.7 ms, with no fringe in the window (2.10 to 2.32 against 1.62 to 1.71 ms). The design did not estimate it. `ter-uqmn` measured its parts (see "The shading models' cost" below).
- The ambient pass grows by 0.14 to 0.16 ms under `SeparableSubsurface` against the runs with the scatter off, and not without it (0.433 and 0.442 against 0.420 to 0.516 ms): the blend of its second attachment, the scatter image, at every pixel (estimate 0.1 to 0.15 ms for both lighting passes). The direct pass's write of the scatter image does not show beyond the spread.
- The G-buffer pass grows by 0.03 to 0.05 ms, within the spread: the shading target's write (estimate 0.05 to 0.1 ms).
- The scatter passes with no skin on screen: 0.10 to 0.11 ms (estimate 0.05 to 0.1 ms).

The scatter passes, the fringe and the cutout draws: `scripts/skin-and-hair.json` (see Part 6 in "Order of the work"), a film of 120 frames of each view: the head (`--start 1`), the fringed fan (123), the cutout fan (244) and the glass fan (365), with the scatter on and off. The head's skin covers about 17 % of the frame. In the captures at 1280 × 720, about 9 % of the pixels show the fringed fan and about 7 % the cutout fan's cores, so the fringe adds about 2 %. The count takes a pixel whose red exceeds its blue by more than 3 to 6 of 255, which the brown hair does and the gray floor, the black sky and the shadows do not. The load at the start was 5.7 to 9.7.

| Pass | Head, on | Head, off | Fringed fan, on | Fringed fan, off | Cutout fan, on | Cutout fan, off | Glass fan, on | Glass fan, off |
|---|---|---|---|---|---|---|---|---|
| G-buffer | 0.357, 0.335 (0.37) | 0.354, 0.334 (0.37) | 0.433, 0.336 (0.44) | 0.428, 0.428 (0.44) | 0.420, 0.420 (0.44) | 0.420, 0.420 (0.44) | 0.294, 0.328 (0.35) | 0.344, 0.344 (0.35) |
| direct light | 1.69, 1.60 (1.89) | 1.71, 1.61 (1.91 to 1.93) | 1.79, 1.38 (1.88 to 2.01) | 1.78, 1.79 (1.97 to 1.99) | 1.76, 1.76 (1.98 to 2.01) | 1.80, 1.81 (2.00 to 2.09) | 1.29, 1.48 (1.76 to 1.78) | 1.58, 1.57 (1.78 to 1.80) |
| ambient light | 0.539, 0.520 (0.57) | 0.416, 0.397 (0.45) | 0.553, 0.475 (0.58 to 0.59) | 0.427, 0.430 (0.46) | 0.554, 0.551 (0.58) | 0.427, 0.430 (0.46) | 0.444, 0.481 (0.52) | 0.381, 0.379 (0.41) |
| scatter rows | 0.296, 0.278 (0.31) | None | 0.080, 0.076 (0.08) | None | 0.082, 0.079 (0.08 to 0.09) | None | 0.085, 0.078 (0.09 to 0.10) | None |
| scatter columns | 0.295, 0.276 (0.31) | None | 0.048, 0.042 (0.05) | None | 0.049, 0.047 (0.05) | None | 0.047, 0.046 (0.05) | None |
| reflection trace and reflections | 1.55, 1.46 (1.64) | 1.54, 1.47 (1.64) | 1.93, 1.51 (2.05) | 1.92, 1.92 (2.06) | 1.92, 1.93 (2.06) | 1.92, 1.91 (2.06) | 1.12, 1.25 (1.39) | 1.31, 1.30 (1.38) |
| transparent | None | None | 1.06, 0.81 (1.13 to 1.19) | 1.06, 1.06 (1.20 to 1.22) | None | None | 1.45, 1.64 (1.91 to 1.97) | 1.75, 1.73 (1.99 to 2.02) |
| frame span | 10.33, 9.76 (10.8) | 9.52, 9.16 (10.0 to 10.1) | 11.93, 9.99 (12.5 to 12.6) | 11.47, 11.43 (12.1 to 12.2) | 10.73, 10.69 (11.2 to 11.3) | 10.43, 10.36 (10.9 to 11.0) | 10.23, 10.90 (12.0 to 12.3) | 10.98, 10.96 (11.65 to 11.67) |

With the scatter on, the fringed fan's second run and the glass fan's first run have lower means in every pass with the same 95th percentiles, as some of their frames ran faster, so the two runs of each of these fans differ by up to 1.9 ms of frame span. The runs with the scatter off agree within 0.07 ms of frame span for each fan, so the comparisons of one fan with another below take them. The cutout fan's second trace with the scatter on hung, and its GPU timer's span was 10.75 ms. "None" is a pass with no GPU work in the run.

- The scatter passes with a face over 17 % of the frame: 0.55 to 0.59 ms for the two (estimate 0.3 to 0.6 ms for 20 %), and the ambient pass's blend 0.12 ms more in both runs. The frame span grows by 0.6 to 0.8 ms. The direct pass does not change.
- The scatter passes with no skin on screen: 0.12 to 0.13 ms. The ambient pass's blend takes 0.05 to 0.13 ms more, by the runs of each fan with the scatter on against the mean of its runs with it off; the two runs with lower means give 0.05 and 0.06 ms.
- The fringe of a fan of twelve cards, which shows on about 9 % of the frame: the transparent pass, 0.8 to 1.1 ms. The same cards as glass take 1.45 to 1.75 ms in that pass, so the fringe costs less than the glass that the cards drew as before this epic.
- The cutout draws: the G-buffer pass takes 0.08 ms more than with the cards as glass, which draw no core, by the runs with the scatter off. The runs with it on, whose means vary, give 0.01 to 0.14 ms. A fringed card's core costs the same as a cutout's. The shadow maps take the same time for all three fans (0.18 to 0.21 ms), as glass with `CastsShadow` casts its whole rectangle.
- The cores enter the screen-space passes as opaque surfaces: the reflections' two passes take about 0.6 ms more with cores than with the glass fan (0.61 to 0.62 ms by the runs with the scatter off), and the occlusion sectors 0.11 to 0.12 ms more by the runs with the scatter off. The runs with it on, whose means vary, give −0.12 to 0.29 ms for the sectors.
- The fan as fringed hair against the same fan as glass, by the two runs of each with the scatter off: 0.47 to 0.51 ms more frame span (11.43 and 11.47 against 10.96 and 10.98 ms); as cutout hair, 0.53 to 0.62 ms less (10.36 and 10.43 ms). Per pass, by the means of the same runs, the cores add to the G-buffer, direct, ambient, occlusion and reflection passes about 1.1 ms, and the transparent pass takes 0.68 ms less for the fringed fan and 1.74 ms less for the cutout fan, which has none. The sums of the passes' own times do not add up to the spans' differences: +0.16 and −1.14 ms against +0.48 and −0.58 ms. The span also holds the gaps between the passes, and the exposure pass takes 0.29 and 0.60 ms less with either fan than with the glass fan. So the frame's cost here is the span's difference. The design estimated 0.5 to 1 ms for a head of hair over 15 % of the screen.

The card number's cost (`ter-3yy5`, 2026-10-31): `scripts/hair-bent-cards.json`, 240 frames of the `bent-camera` view, five bent hair cards that fill much of the screen, at 1920 × 1080 in Release, by the GPU timer of a headless run with `--video none`, two runs of each build on one checkout, the base at e4244a5c with the scene added. The means in ms, with the 95th percentiles in parentheses:

| Pass | Base, 2 runs | Branch, 2 runs |
|---|---|---|
| G-buffer | 0.405, 0.391 (0.51, 0.51) | 0.384, 0.414 (0.52, 0.52) |
| occlusion sectors | 1.426, 1.389 (1.78, 1.85) | 1.389, 1.481 (1.84, 1.89) |
| frame span | 9.480, 9.254 (10.85, 10.86) | 9.246, 9.742 (11.27, 11.13) |

The differences of the means, +0.001 ms in the G-buffer pass and +0.03 ms in the sectors pass, are smaller than the differences between two runs of one build, 0.03 ms and 0.09 ms. So the card code costs no time that these runs can show: the G-buffer pass writes the same 4 bytes, and the sectors pass reads the shading target once per step of a hair receiver in place of the plane test's arithmetic.

### The shading models' cost

Ticket `ter-uqmn` (2026-10-31) isolated the growth of the direct and the transparent pass with no skin and no hair on screen, and removed most of it. Its traces took the deck window of `scripts/demo-exit.json`: 120 frames from frame 399, 1920 × 1080, Release, the default configuration (`SeparableSubsurface`), `tools/gpu-trace.sh --no-counters`. Three commits ran in turn: the commit before the merge of this epic (`bf812296`), the ticket's base (`cd8b233c`) and the ticket's branch. Other builds shared the machine, so the 1-minute load at the start of a run was 3.8 to 56. The numbers are the medians of each pass's own time in ms. The base also holds three merges after `bf812296`: `ter-qsfz` (89555b40), `ter-fs7b` (0fe2e820) and `ter-vtc7` (cf4a74cc). The tables leave out three runs that other work disturbed, each with a direct-pass median above every other run of its commit and a mean above their median by 0.5 ms or more: the run before the merge at a load of 8 (median 5.44, mean 5.81 ms, against a median of 5.27 ms of the other runs' means), the run before the merge at a load of 56 (median 7.10 ms), and a branch run (median 5.70, mean 6.25 ms). Two runs timed out and gave no trace, one of the base and one of the branch.

| Pass | Before the merge, 4 runs | Base, 5 runs | Branch, 2 runs |
|---|---|---|---|
| direct light | 5.22 to 5.25 | 6.07 to 6.10 | 5.53, 5.55 |
| transparent | 1.56 to 1.59 | 1.93 to 1.95 | 1.76, 1.77 |
| frame span | 16.01 to 16.91 | 18.04 to 18.50 | 17.47, 17.48 |

Diagnostic builds from the base removed one candidate after another, two runs of each:

| Build | Direct light | Transparent |
|---|---|---|
| Base | 6.07 to 6.10 | 1.93 to 1.95 |
| No hair code in `respond` and `takesLight` | 5.57, 5.60 | 1.87, 1.88 |
| Also no shadow normal turned toward the light | 5.59, 5.61 | 1.70, 1.71 |
| Also no fetch of the shading target | 5.47, 5.49 | 1.71, 1.77 |
| Also one sum in place of the two parts of `DirectTerms` | 5.42, 5.42 | 1.65, 1.66 |

- The cause in the direct pass: the hair code inside the light loop. `respond` and `takesLight` chose the model again for each light, and that cost about 0.5 ms of the gap of 0.85 ms with no hair on screen. The fetch of the shading target costs about 0.1 ms, and the two sums of `DirectTerms` about 0.05 ms.
- The shadow normal turned toward the light costs nothing measurable in the direct pass. In the transparent pass, the hair code and the turned normal together cost 0.22 to 0.25 ms.
- The fix: `directLight` chooses the model once per pixel, before the loop over the lights. It runs `modelLight` with `fiber` as a constant, so each copy of the loop holds the code of one model. A surface takes a light only where its geometric normal faces it, so only the fiber loop turns the shadow normal. The direct pass takes 0.52 to 0.57 ms less than the base, and the transparent pass 0.16 to 0.19 ms less.

`scripts/skin-and-hair.json` did not get slower. The same method took two runs each of the base and the loop per model, with the load at 12 to 59:

| View | Direct light, base | Direct light, loop per model | Frame span, base | Frame span, loop per model |
|---|---|---|---|---|
| Head (`--start 1`) | 1.68, 1.79 | 1.28, 1.44 | 10.54, 10.80 | 9.06, 9.88 |
| Fringed fan (123) | 1.28, 1.57 | 1.09, 1.11 | 10.38, 10.94 | 9.25, 9.40 |
| Cutout fan (244) | 1.80, 1.82 | 1.35, 1.38 | 11.24, 11.28 | 9.61, 10.01 |

The fringed fan's transparent pass took 0.79 and 0.93 ms on the base and 0.72 and 0.75 ms with the loop per model.

#### The fiber constant and the second series

The review of the loop per model found three open points. The transparent pass kept the fiber loop in every glass pipeline. A subgroup that holds hair pixels and other pixels runs both loops, and no measured view covered it. And no run measured `NoSubsurface`, where the growth of 2026-10-27 was larger. The fix stage answered them:

- The specialization constant `shadesFibers` (constant_id 3, `ShaderConstants.OneSided`) is false in the one-sided variant of each `SidedPipeline` and true in every other pipeline. Only hair draws both sides (`MaterialStore.DrawsBothSides`), so no hair material reaches the one-sided variant, which drops the fiber's loop and the hair branch of `sampleMaterial`. The fullscreen direct pass reads every model from the shading target, so it keeps the constant true.
- `modelLight` computes the geometric normal's cosine with the light once and passes it to `takesLight`, so the test of the surface's rule and the shadow lookups' input are one value.
- The new scene `lamp-hall-16-hair` (`scripts/lamp-hall-16-hair.json`) is the hall of `lamp-hall-16` with a wall of 96 cutout strand cards 0.9 m in front of the view. In its capture, about a third of the blocks of 8 × 4 pixels, as many as the M1 Max's SIMD width of 32, hold strand pixels and pixels of the floor or the pillars, by a count that takes a pixel as hair by its color.

The second series ran on 2026-10-31 from 13:35 to 15:04: the same method, one series per view, with the builds in turn and the GPU measurement lock taken for each series. The comparison builds are source exports: `bf812296`, the base, the base with the new scene, and the branch with and without the fix stage's changes. A run whose G-buffer median, the same work in every run of a build, lies more than 10 % from the other runs of its build is left out: two deck runs, slower in every pass, and three hall runs whose G-buffer pass took 0.333 to 0.335 ms against 0.427 to 0.439 ms, faster in every pass. Two runs timed out in xctrace and gave no trace.

The deck, by series, with the 1-minute load at the start of each run:

| Pass | Configuration | Before the merge | Base | Branch |
|---|---|---|---|---|
| direct light | default, load 7.2 to 52 | 5.25, 5.31 | 6.12, 6.09, 6.09 | 5.46, 5.48 |
| transparent | default | 1.58, 1.59 | 2.00, 1.91, 1.93 | 1.66, 1.69 |
| frame span | default | 16.20, 16.78 | 18.22, 17.98, 18.02 | 17.17, 17.35 |
| direct light | `NoSubsurface`, load 4.8 to 12.7 | 5.26, 5.26, 5.45 | 6.08, 6.20, 6.09 | 5.47, 5.57 |
| transparent | `NoSubsurface` | 1.56, 1.59, 1.60 | 1.92, 1.97, 1.94 | 1.66, 1.69 |
| frame span | `NoSubsurface` | 16.51, 16.86, 17.11 | 17.54, 18.66, 17.75 | 16.79, 17.51 |

`bf812296` has no subsurface setting and no scatter, so its runs in the `NoSubsurface` rows ran with no option. The loads of the default series rose above 8 in most runs, so it is no quiet repeat. The `NoSubsurface` series started at 4.8 to 12.7, and its four runs at a load below 8 agree with the others of their commits within 0.2 ms.

- With the scatter off, the base's direct pass takes 6.08 to 6.20 ms, the same as with it on. The 0.3 to 0.6 ms more with the scatter off of 2026-10-27 does not repeat, so it was the state of the machine. The branch removes about 0.6 ms in both configurations.
- The constant takes 0.07 to 0.11 ms off the transparent pass: 1.66 to 1.69 ms against 1.76 and 1.77 ms with the loop per model alone. Every glass material of the deck is one-sided.
- The G-buffer pass of the hall takes 0.430 to 0.431 ms on the branch against 0.437 to 0.439 ms on the base, by the hair branch that the one-sided variant drops.

The hall, `lamp-hall-16-hair.json` from frame 30, the direct pass, load 4.4 to 69:

| Build | Runs | Direct light |
|---|---|---|
| Base with the new scene | 8 | 6.12 to 6.58 |
| Branch without the cosine passed once | 5 | 6.03 to 6.67 |
| Branch | 2 | 6.38, 6.48 |
| Branch with the loop chosen per subgroup, two variants | 6 | 5.81 to 6.68 |

The second loop of a subgroup that holds both models costs no measurable time in this view. The per-subgroup builds chose the loop by `subgroupBallot` votes. In one, a subgroup of one model ran that model's loop, and a mixed subgroup one loop that chose the model per light (4 runs, 6.27 to 6.68 ms). In the other, a subgroup with no hair ran the surface loop, and every other subgroup the loop that chose per light (2 runs, 5.81 and 6.44 ms). Neither ran measurably faster, so the branch keeps the simpler choice per pixel. The ballots need no new device support: the renderer already requires the ballot operations in the fragment stage (ARCHITECTURE.md, "Required device features").

`skin-and-hair.json`, the branch against the base, two runs each, load 3.7 to 42:

| View | Direct light, base | Direct light, branch | Frame span, base | Frame span, branch |
|---|---|---|---|---|
| Head (`--start 1`) | 1.72, 1.81 | 1.61, 1.57 | 10.78, 10.76 | 10.49, 10.48 |
| Fringed fan (123) | 1.82, 1.82 | 1.58, 1.59 | 12.36, 12.33 | 11.96, 11.97 |
| Cutout fan (244) | 1.81, 1.82 | 1.58, 1.59 | 11.32, 11.38 | 11.00, 11.00 |

The fringed fan's transparent pass takes 1.08 and 1.09 ms on the base and 1.02 and 1.02 ms on the branch.

#### What remains

Against the commit before the merge, in both configurations:

- The direct pass takes 0.2 to 0.3 ms more (5.46 to 5.57 against 5.25 to 5.45 ms). The fetch of the shading target, about 0.1 ms, stays because the pass must know each pixel's model to choose its loop, and the shading target is the one store of the model. The two sums of `DirectTerms`, about 0.05 ms, stay because the diffuse part goes alone into the diffuse image. No build attributes the rest, 0.05 to 0.2 ms. The default screen-space light makes the diffuse image in both configurations, so the runs with the scatter off do not test the diffuse image.
- The transparent pass takes about 0.1 ms more (1.66 to 1.69 against 1.56 to 1.60 ms). The two sums cost about 0.05 ms (1.65 to 1.66 against 1.70 to 1.71 ms in the diagnostic builds). No build attributes the other 0.05 ms. Its candidates are `forwardShading` before the occlusion discard, the runs by sides, and the three merges in the base after `bf812296`.

Ticket `ter-qsye` isolates both remainders.

#### Pixels

The loop per model alone changed pixels on this Mac by rounding: 230 pixels in 3,888 frames of `demo-exit.json`, each by 1 of 255 in one channel, and 102 pixels of `skin-and-hair.json`, 99 by 1 of 255 and 3 adjacent pixels of the glass fan's frame `hair-glass-timing-000109` by up to 7. The Metal source that SPIRV-Cross gives holds the same text in both loops, but Metal's fast math rounds the separate surface loop differently. The user accepted this change for the time that it saves (2026-10-31). Two runs of the base on `skin-and-hair.json` give the same bytes in 486 of 486 frames.

The branch with the fix stage's changes changes fewer pixels. Against the base, no byte of the 3,888 frames of `demo-exit.json` differs, and 39 pixels in 33 frames of `skin-and-hair.json` differ, each by 1 of 255: 21 in the fringed fan's film and 18 in the cutout fan's. No build isolated which of the two changes restored the bytes.

## Known limits

- The scatter sees only what the screen shows. Light that would scatter in from skin behind an edge or past the image's edge is missing, and the taps there take the center's light.
- At grazing angles the screen-space kernel covers more of the surface than the scatter does. The slope limit cuts part of it.
- The separable kernel approximates the radial profile: a lit point spreads as a soft cross more than a disk.
- The scatter uses one profile, that of human skin, with no width per material.
- Pre-scatter texturing softens the skin image's detail, most in red (decision 12).
- In an extreme close-up, about 10 cm from the eye at 1080p, the kernel reaches about 300 pixels with 17 taps, and the outer taps show as steps at sharp shadow edges. More taps per side buy it back.
- The slope rule measures a tap's offset in the measured profile (decision 34). So the scatter narrows wherever the surface turns more than about 23° from the eye: a tap keeps all its light up to a slope of tan 60° / 4, about 23°, and none from twice that, about 41°. On a round head, that is the outer 60 % of its radius. The table gives the change of the test head's capture against the rule that the width scale widened, by the distance inside the silhouette, outside the nose's region (`ter-t2w6`, measured on 2026-10-31). Deeper than 20 px, no pixel changed by more than 5 of 255. From 40 to 80 px inside, the rule takes back most of the widening's mean gain in red: +0.08 of 255 against the measured width, where the widened rule gave +0.35.

  | Inside the silhouette | Pixels that changed by more than 1 of 255 | Mean change in red | Largest change in red |
  |---|---|---|---|
  | 0 to 5 px | 982 of 6,980 | +0.42 | −11 |
  | 5 to 10 px | 819 of 6,856 | +0.53 | −8 |
  | 10 to 20 px | 670 of 13,212 | +0.27 | −6 |
  | 20 to 40 px | 1,004 of 24,464 | −0.11 | −4 |
  | 40 to 80 px | 537 of 41,480 | −0.26 | −3 |

- A tap past 25.4 mm still keeps the light of a surface 11 mm nearer, as a tap past 6.4 mm did at the measured width. Beside the test head's nose, this light adds no measurable red. A capture that rejects every tap across a depth step of 4 mm gives the same red beside the nose within 0.03 of 255 (`ter-t2w6`, measured on 2026-10-31).
- Beside a nearer surface of skin, the scatter adds the light of the center where the head's own surface hides behind it. The wider scatter makes this visible: beside the test head's nose, on its lit side, it adds +1.8, +1.0 and +0.5 of 255 more red than the measured width on average, in the bands 0 to 10, 10 to 20 and 20 to 30 px from the nose's edge. No depth-step rule removes it. It is the first known limit at an edge: a tap that lands on the nose gives way to the center's light, and the head behind the nose, which the tap would take without the nose, is darker (62 against 102 of 255 in red at the scatter off). A model of only this rule on the capture without the nose gives +2.2, +1.4 and +0.7 (`ter-t2w6`, measured on 2026-10-31).
- No transmission: an ear in front of a light stays dark but for its ambient light and its scatter (fork 1).
- The environment faces and the impostor captures do not scatter (decision 19).
- The fringe of one hair is not sorted inside the hair (decision 23).
- At the core's edge the coverage steps from the cutoff to 1. SMAA softens the step. A higher cutoff makes the step smaller and the core and its shadows thinner.
- Mipmaps of an alpha image lower the alpha with the distance, so a far hair has less core and more fringe, and its shadows thin. The importer's mips that keep the coverage at the cutoff (Castaño 2010) are later work of the importer.
- The strand has 8 bits per axis in the shading target, an error below 1°. A highlight narrower than about 5°, a roughness below 0.3, shows the steps.
- The hair's ambient specular light is a GGX lobe about the fiber normal, so the environment map gives no band along the strands. The band comes from the lights.
- No light scatters from fiber to fiber, so light hair is darker than real hair.
- The highlights have no noise per strand, which Scheuermann's shift texture gives.
- The fringe casts no shadow, and the fringe takes no crease shadowing: its occlusion is 1.
- The highlights lose the light that Marschner's 1 / cos² θ_d returns at views along the strand. A card is rarely seen along its strands.
- A fringed instance never becomes an impostor, so a far human's hair draws as geometry.
- Under the screen-space light, a curl that folds back over its own card never shades it: the whole strip is the hair pixel's own card (decision 33).
- Two strips that touch at one exact vertex position are one card, so they never shade each other under the screen-space light. An example is two cards whose roots an exporter snaps to one scalp vertex. The join by position keeps a strip that an exporter splits at a sharp bend or a seam as one card ("The card number"). MakeHuman's `afro01` has no vertices that share a position: its 342 strips stay 342 cards.
- A strip is one card however large it is. MakeHuman's `afro01` holds two connected sheets of 378 quads each around the head, beside 340 single-quad cards, so neither sheet shades itself under the screen-space light.
- A step on another card whose card code collides with the receiver's blocks nothing: about one of 8,192 pairs of cards of two instances, and cards 8,192 apart in a mesh of more cards ("The card number").
- A strand that a normal map tilts off its card can point its reflection below the card, or less than a cosine of `SurfaceFadeCosine` above it. The pixel then shows the environment map's reflection, not the screen's (decision 35). A normal map that varies from texel to texel can make the hair's reflection patchy, with the screen's reflection on some texels and the map's on others.
- On a tilted strand, the screen's reflection and the map's meet at a straight edge where the cosine crosses `SurfaceFadeCosine`. The two differ where the map does not hold the reflected object, such as the glowing bar of the scene `hair-rugs` (`scripts/hair-rugs.json`) on its tilted rug. Without the cut, many rays inside the fade's range hit their own card a few pixels from their start, and the edge was ragged and speckled (`ter-nwhv`, 2026-10-31). If a normal-mapped hair asset shows the straight edge, the follow-up is design 1 of decision 35: a hair ray skips the hits on its own card through the card number of `ter-3yy5`, so it traces down to a cosine of 0.

## Compared with a design from scratch

| From scratch | This design | Why it stays |
|---|---|---|
| A diffuse HDR image and a specular HDR image for every pixel | One HDR image, a scatter image of skin's diffuse light, and a delta | A second full image costs 16.6 MB and a write at every pixel, and the bounce reads one direct image |
| A material index per pixel, which reads the material buffer | The shading model's code and the strand in the shading target | The strand varies per pixel with the normal map, and a material read per pixel per pass costs a dependent fetch |
| Strands as geometry | Cards | The assets are cards |
| Order-independent transparency for the fringe | An unsorted fringe over a cutout core | Lists per pixel need atomics, memory and a sort per pixel, and the error of one-colored hair is small (decision 23) |
| Burley's normalized diffusion with sampled disks | The separable sum of Gaussians | Its few noisy samples need a temporal step (decision 4) |
| A kernel in the tangent plane of each pixel | A kernel across the view, with the slope limit | Golubev's tangent-plane kernel needs a disk of samples. See "Later" |
| Transmission through thin skin | None | Fork 1 |

## Later

Each item is left out because nothing needs it yet.

| Item | What it adds | When it is needed |
|---|---|---|
| Transmission through thin skin (Jimenez, Whelan, Sundstedt and Gutierrez 2010) | Ears and nostrils glow red against a light: a raw-depth read of the shadow maps per light gives the thickness, and the profile its transmittance | When fork 1 takes option B, or a capture of a backlit face shows the need |
| Post-scatter texturing | The skin image's detail stays sharp: the scatter blurs the light without the diffuse color, and the composite multiplies it back | When a close-up shows the skin's detail too soft |
| A scatter width per material | Skin of other creatures, wax, marble | When a material needs another profile |
| Coverage-keeping alpha mips in the importer | Far hair keeps its core and its shadows | When the importer imports hair |
| A shift texture per strand | Broken highlights along the strands | When a close-up shows the bands too even |
| A kernel in the tangent plane | Less scatter at grazing angles | When a profile view shows too much scatter |
| Tiles of skin for the scatter passes | The passes skip the screen without skin | When the cost with no skin shows |

## The importer's mapping from glTF

The importer of [asset-pipeline.md](asset-pipeline.md) does not exist in src yet. When it is built, it maps a glTF material this way:

| glTF | Engine |
|---|---|
| `alphaMode` `OPAQUE` | `OpaqueAlpha` |
| `alphaMode` `MASK`, `alphaCutoff` c (default 0.5) | `CutoutAlpha(c)` |
| `alphaMode` `BLEND` | `GlassAlpha`, as asset-pipeline.md maps `BLEND` today |
| `alphaMode` `MASK` or `BLEND` with `extras.tertius_alpha` = `"fringe"` | `FringedAlpha(c)`, with c the `alphaCutoff` when the file has one, else 0.5 |
| No `extras.tertius_shading`, or `"standard"` | `StandardShading(metallicFactor)` |
| `extras.tertius_shading` = `"skin"` | `SkinShading` |
| `extras.tertius_shading` = `"hair"`, with `extras.tertius_strand` = `"+u"`, `"-u"`, `"+v"` or `"-v"` | `HairShading(...)`. A hair material without `tertius_strand` is the error AS3006, a new code beside AS3004 |

Blender writes a material's custom properties into its glTF `extras`, because the build tool exports with `export_extras=True`. glTF's `BLEND` composites by Porter and Duff's "over", which `FringedAlpha(1)` draws exactly. The importer keeps `GlassAlpha` as the default of `BLEND`, so a glass made in Blender stays the user's glass rule. No ratified KHR extension fits: `KHR_materials_volume` and `KHR_materials_diffuse_transmission` describe a thick or a thin translucent medium, not a diffusion profile or a fiber.

The changes to asset-pipeline.md: the key `alphaMask` and the warning AS3003 go, because `MASK` draws as a cutout. `ModelPart` loses `Transparent`, because the material holds it. The MPFB step `mpfb.freeze` sets `tertius_shading` on the skin, `tertius_shading`, `tertius_strand` and `tertius_alpha` on the hair, the eyebrows and the eyelashes. That is the work of `ter-omuj`, which waits for this epic.

## Doc changes

| Doc | Change |
|---|---|
| ARCHITECTURE.md, "Shading" | The material paragraph: `Model` and `Alpha`, the shading word and the cutoff, `MaterialInfo`. The G-buffer table: the shading target. `ShadingInput.model` and `.strand`, `DirectTerms`, `respond`, `takesLight`. The hair model as a subsection |
| ARCHITECTURE.md, "Transparent objects" | Glass from the material's alpha mode. The fringe, its blend and its order |
| ARCHITECTURE.md, "Instances and draws" | `OpaqueRuns`, the cutout runs, `GlassCommands`, `FringeCommands` |
| ARCHITECTURE.md, "Shadows" | Cutout casters in the cascades and the atlas |
| ARCHITECTURE.md, "Impostors" | A glass or fringed material never becomes an impostor. A cutout's capture keeps its holes |
| ARCHITECTURE.md, "The environment map", "The faces" | A face draws the cores |
| ARCHITECTURE.md, "The order of a rendered frame" | The stage `SkinScatter` between the ambient pass and the reflections |
| ARCHITECTURE.md, new section "Skin scatter" | The profile, the kernel, D, the two passes, the setting, the cost from Part 6, the known limits |
| ARCHITECTURE.md, "Switches" | `Subsurface` |
| ARCHITECTURE.md, "GPU timings" | "scatter rows" and "scatter columns" appear only under `SeparableSubsurface` |
| ARCHITECTURE.md, "Portable Vulkan" | The new site rows, and `independentBlend` in "Required device features" |
| docs/plans/asset-pipeline.md | "Materials", "Alpha modes": the mapping above. The key `alphaMask` and AS3003 go. `ModelPart.Transparent` goes |
| docs/plans/rendering-upgrade.md | A row for `ter-n7lw` that links this doc |
| docs/plans/README.md | A row for this doc |

## Other tickets

- `ter-4vzc`, skinning and morph targets. Its deformation applies in every vertex shader that draws a mesh. This doc adds one, `shadow_cutout.vert`, and changes none of the vertex inputs. The part that lands second adds the other's change: the deformation in `shadow_cutout.vert`, or the cutout variant over a skinned vertex shader. A skinned strand's tangent turns with the skin, which the deformation of the tangent gives.
- `ter-omuj`, hair cards that read as hair. It waits for this epic. It sets the extras of the MPFB parts and tunes the hair's cutoff and textures in the model scripts.
- `ter-kge6`, the asset pipeline. Its importer maps the materials as "The importer's mapping from glTF" says.
- `ter-pq5y`, the performance coda. It profiles the scatter passes and the hair's passes with the rest of the frame.
- `ter-rtxu`, the snapshot tests. A reference image of skin and hair comes with them.

## Order of the work

Six parts. Each part is a commit that builds and passes every test in Checked and Release. At most four builders run at once (CLAUDE.md).

| Part | Runs | Waits for |
|---|---|---|
| 1. The material API and glass in the material | At once | Nothing |
| 2. The math in C# | At once, beside Part 1, and beside Part 3 | Part 1's `ShadingModel.cs`, which Part 1 writes first |
| 3. Cutout and fringe drawing | After Part 1, beside Part 2 | Part 1, and the merge of the lights (`ter-s9cv`) |
| 4. The shading target and hair shading | After Parts 2 and 3 | Parts 2 and 3, and the merge of the bounce light (`ter-a24f`) |
| 5. The skin scatter | After Part 4 | Parts 2 and 4 |
| 6. The cost, the captures and the docs | After Part 5 | Part 5 |

Parts 3, 4 and 5 run one after another because they share the shaders of the opaque passes, `lighting.glsl`, `DescriptorLayouts.cs` and `VulkanRenderer.cs`.

### Part 1: the material API and glass in the material

- Files: `src/Tertius.Core/Seams/ShadingModel.cs` (new), `src/Tertius.Core/Seams/AlphaMode.cs` (new), `src/Tertius.Core/Seams/RenderSeam.cs` (`MaterialDesc`, `RenderFlags`); `src/Tertius.Rendering/Resources/MaterialStore.cs`, `Scene/GpuTypes.cs`, `Shaders/frame.glsl` (the `GpuMaterial` mirror), `Scene/SceneBatcher.cs`, `Scene/PreparedScene.cs` (`PreparedInstance.Alpha`, `GlassCommands`), `Scene/ImpostorManager.cs`, `Passes/TransparentPass.cs` (the renamed case), `VulkanRenderer.cs` (the cutoff's guard, `MaterialInfo` to the batcher); `src/Tertius.Rendering/Shading/ShadingCode.cs` (new); the demo's scenes; every test that sets `Metallic` or `Transparent` or calls `Prepare`.
- Writes first: `ShadingModel.cs` with `StrandDirection`, so Part 2 builds against it.
- Tests first: Part 1's tests of "Tests".
- Gives:

  ```csharp
  public readonly partial struct ShadingModel;      // StandardShading(float Metallic), SkinShading, HairShading(StrandDirection Strands)
  public readonly partial struct AlphaMode;         // OpaqueAlpha, CutoutAlpha(float Cutoff), FringedAlpha(float Cutoff), GlassAlpha
  public enum StrandDirection : byte { PlusU, MinusU, PlusV, MinusV }
  internal readonly record struct MaterialInfo(AlphaMode Alpha);
  internal static class ShadingCode { public const uint Standard = 0, Skin = 1, Hair = 2; public static uint Word(ShadingModel model); }
  public void Prepare(RenderSnapshot snapshot, float alpha, int width, int height, ReadOnlySpan<MeshInfo> meshes,
      ReadOnlySpan<MaterialInfo> materials, EnvironmentDraw environment, PreparedScene scene);
  ```

- Done: every frame draws as before, with glass from the material. A cutout or fringed material draws as opaque until Part 3, which no scene has.

### Part 2: the math in C#

- Files, all new in `src/Tertius.Rendering/Shading/`: `SkinProfile.cs`, `SkinScatter.cs`, `HairFiber.cs`, `StrandCode.cs`, `DirectTerms.cs`, and their tests.
- Tests first: Part 2's tests of "Tests".
- Gives:

  ```csharp
  internal readonly record struct ProfileGaussian(double Variance, Double3 Weight);           // variance in m²
  internal sealed record SkinProfile(ImmutableArray<ProfileGaussian> Gaussians)
  {
      public const double WidthScale = 4;                                                     // decision 34
      public const double Reach = 0.032;                                                      // m: 8 mm · WidthScale
      public static SkinProfile Human { get; }
      public Double3 Marginal(double x);                                                      // P_c(x), per meter
      public ScatterKernel Kernel(int tapsPerSide);
  }
  internal readonly record struct ScatterTap(double Offset, Double3 Weight);                 // offset in m
  internal sealed record ScatterKernel(ImmutableArray<ScatterTap> Taps, double WidthScale) { public double Reach { get; } }   // the outermost offset

  internal enum ScatterAxis { Rows, Columns }
  internal sealed record ScatterField(int Width, int Height, Double3[] Light, bool[] Skin, double[] ViewDepth, double TanHalfY);
  internal static class SkinScatter
  {
      public const double SlopeLimit = 1.7320508075688772;                                    // tan 60°, per m of the measured profile
      public static double PixelsPerMeter(double viewDepth, int height, double tanHalfY);
      public static Double3 Pass(ScatterField field, ReadOnlySpan<Double3> source, int x, int y, ScatterAxis axis, ScatterKernel kernel);
  }

  internal readonly record struct DirectTerms(Double3 Diffuse, Double3 Specular);
  internal readonly record struct HairFiber(Double3 BaseColor, double Roughness, double Reflectance)
  {
      public static Double3 Strand(Double3 normal, Double3 tangent, double bitangentSign, StrandDirection direction);
      public static Double3 FiberNormal(Double3 strand, Double3 toView, Double3 cardNormal);
      public DirectTerms Reflected(Double3 strand, Double3 toView, Double3 toLight);         // per unit irradiance
  }

  internal static class StrandCode
  {
      public static (byte X, byte Y) Encode(Vector3 strand);
      public static Vector3 Decode(byte x, byte y);
  }
  ```

- Done: every rule of the scatter and the fiber is tested on its own, with no GPU.

### Part 3: cutout and fringe drawing

- Files: `Scene/SceneBatcher.cs` (the sort key, `OpaqueRuns`, the fringe in the transparent list), `Scene/PreparedScene.cs` (`OpaqueRuns`, `FringeCommands`), `Scene/EnvironmentDraw.cs` (`EnvironmentFaceDraw`'s runs), `Scene/ImpostorManager.cs`; `Passes/GeometryPass.cs`, `Passes/ShadowPass.cs`, `Passes/LocalShadowPass.cs`, `Passes/EnvironmentFacePass.cs`, `Passes/ImpostorPass.cs`, `Passes/TransparentPass.cs`; `ShaderConstants.cs` (`CutoutAlpha`); `VulkanRenderer.cs` (the pipelines); the shaders `material.glsl` (the test), `shadow_cutout.vert` (new), `shadow_cutout.frag` (new), `fringe.frag` (new), `transparent.frag` (`forwardShading` moves out of it, into `lighting.glsl`).
- Tests first: Part 3's tests of "Tests". The GPU tests fail first because a cutout draws as opaque.
- Gives:

  ```csharp
  internal readonly record struct OpaqueRuns(CommandRange Solid, CommandRange Cutout);
  internal readonly record struct FringeCommands(CommandRange Commands);
  // TransparentRun: GlassCommands, FringeCommands, PuffSprites
  ```

  and in GLSL the constant `cutoutAlpha` and `ShadingInput forwardShading(SurfaceSample s, vec3 position, vec3 vertexNormal, vec4 hemisphere, uint map, vec3 V)`. Decision 32 removed `vertexNormal`: `forwardShading` takes `s.geometricNormal`, which `sampleMaterial` turns toward the eye for hair.
- Takes: Part 1's `AlphaMode`, `MaterialInfo`, `GpuMaterial.AlphaCutoff`.
- Done: a fringed card shows no rectangle, and a cutout casts the shadow of its core. The reviewer checks the Linux claims of the new site rows.

### Part 4: the shading target and hair shading

- Files: `Frame/RenderTargets.cs` (the shading target), `Frame/DescriptorLayouts.cs` (G-buffer binding 10), `Passes/GeometryPass.cs` (the sixth attachment), `Passes/DirectLightPass.cs`, `Passes/AmbientLightPass.cs` (the G-buffer set), `VulkanRenderer.cs` (the test hook `ReadShading`); the shaders `material.glsl` (`strandOf`, `fiberNormal`, the model), `hair.glsl` (new), `lighting.glsl` (`ShadingInput`, `DirectTerms`, `respond`, `takesLight`, the shadow lookups' normal for hair), `gbuffer.frag`, `impostor.frag`, `gbuffer_inputs.glsl`, `gbuffer_surface.glsl`, `direct.frag`, `ambient.frag`, `transparent.frag`, `fringe.frag`, `environment_face.frag`, `impostor_capture.frag`.
- Tests first: Part 4's tests of "Tests".
- Gives: in GLSL `struct DirectTerms`, `DirectTerms respond(ShadingInput s, SurfaceLobes lobes, vec3 V, vec3 L)`, `bool takesLight(ShadingInput s, vec3 L)`, `DirectTerms directLight(...)` with today's parameters, `ShadingInput.model`, `ShadingInput.strand`, the constants `ShadingStandard`, `ShadingSkin` and `ShadingHair`, and the shading target at G-buffer binding 10.
- Takes: Part 2's `HairFiber`, `StrandCode` and `DirectTerms` as the copies the GPU tests pin to, and Part 3's `forwardShading` and `fringe.frag`.
- Done: hair shades as fibers in every pass, and skin as standard with its code. The reviewer checks the hair's sites of "Portable Vulkan".

### Part 5: the skin scatter

- Files: `src/Tertius.Rendering/SubsurfaceSetting.cs` (new, public), `RendererOptions.cs`, `Frame/ScatterImages.cs` (new: the two images, the kernel buffer, set 3), `Passes/SkinScatterPass.cs` (new, one class with the step `Rows` or `Columns`, as `ReflectionPass`), `Frame/FrameGraph.cs` (the stage), `Frame/DescriptorLayouts.cs` (`ScatterLayout`), `Frame/RenderTargets.cs` (the HDR image's storage usage), `Passes/DirectLightPass.cs`, `Passes/AmbientLightPass.cs` (the second attachment and its blend), `Vulkan/GraphicsPipelineDesc.cs` (`ColorAttachment`: a format and a blend per attachment), `Vulkan/ResourceState.cs` (`BufferUse.ComputeUniformRead`), `Vulkan/VulkanDevice.cs` (`independentBlend`), `ShaderConstants.cs` (`ScatterTapsPerSide`, which only the scatter passes set, from `ScatterImages.Setting`), `VulkanRenderer.cs` (`CheckOptions`, the wiring, the test hook `ReadScatter`); the shaders `skin_scatter.glsl`, `skin_scatter_rows.comp`, `skin_scatter_columns.comp`, `direct_pass.glsl`, `ambient_pass.glsl`, `direct_scatter.frag`, `ambient_scatter.frag`, `subsurface.glsl` (new), `direct.frag`, `ambient.frag`; `src/Tertius.Host/ScriptCommand.cs` (`--subsurface on|off`); the test helpers `Gpu.Headless` and `GpuTesting.Headless` (`NoSubsurface`).
- Tests first: Part 5's tests of "Tests".
- Gives:

  ```csharp
  public readonly record struct NoSubsurface;
  public readonly record struct SeparableSubsurface(int TapsPerSide)
  {
      public const int MaxTapsPerSide = 16;
      public static SeparableSubsurface Default => new(TapsPerSide: 8);
  }
  [GeneratedUnion(typeof(NoSubsurface), typeof(SeparableSubsurface), ZeroCase = typeof(NoSubsurface))]
  public readonly partial struct SubsurfaceSetting { internal string? Problem { get; } }
  // RendererOptions.Subsurface, default SeparableSubsurface.Default
  ```

- Takes: Part 2's `SkinProfile.Kernel` and `SkinScatter`, Part 4's `DirectTerms`, the shading target and `ShadingSkin`, and the bounce light's direct and ambient passes.
- Done: skin scatters. Part 5 checks that the frame graph holds no more passes than `GpuTimer.MaxPasses`, 31, with every setting on. The reviewer checks the Linux claims of `independentBlend`.

### Part 6: the cost, the captures and the docs

- Files: `src/Tertius.Demo/DemoScenes.SkinAndHair.cs` (new), `scripts/skin-and-hair.json` (new), and the docs of "Doc changes".
- The scene, made in code: the head of "Tests", a skin sphere with a nose, lit from the side by the sun, for the terminator (built with the sun, not a spot light: Part 5's tests found that a spot light at a finite distance moved the terminator by about 9 pixels from that of parallel rays); a skin disk 3 mm thick, the size of an ear, between the camera and a point light, for thin skin before a light; a block of twelve hair cards in a fan, with an image of strands made in code along +v, soft across each strand and fading toward the tips, with dark and light brown bases; and a glass pane of the same image beside them, for comparison.
- As built (`DemoScenes.SkinAndHair.cs`, the scene `skin-and-hair`, `scripts/skin-and-hair.json`): the floor of the surface scenes under the sun from +X, 14° above the horizon. It sets the hemisphere colors of the hair card scenes, which light it only under `--environment-map off`; under the default environment map the map is the ambient light, the sky shows black, and the head's dark side takes the floor's light alone. The head is a skin sphere of 0.1 m with a nose of 0.015 m, seen from 0.45 m (`head-camera`), where it covers about a fifth of the frame. The ear is a skin disk of 3 cm radius and 3 mm thickness, with a glowing lamp of 60 W and 8 mm radius 5 cm behind it at its upper rim, seen from 0.25 m toward the lamp and the sun (`ear-camera`). The hair is three fans of twelve cards, 7.5 × 30 cm each: the roots 1.2 cm apart, each card turned in its plane by 6° more than the one before, about its strand by ±20° in turn, and set back 5 mm. The cards alternate dark and light brown bases over one strand image of 64 × 256 texels, 18 strands each soft across as cos² and fading over the last 35 % before its tip. One fan is fringed hair, one cutout hair, and one glass of the standard model, the cards' look before this epic, each with a view from 0.85 m with the sun behind it (`hair-fringed-camera`, `hair-cutout-camera`, `hair-glass-camera`), so the three views frame their fans alike. A flat glass pane of the light brown strand image, twice a card's size, stands beside the fringed fan, and `hair-camera` shows the fans and the pane together. The script captures each view once and films 120 frames of the head and of each fan for the cost.
- The cost first, on a quiet machine, by [rendering-upgrade.md](rendering-upgrade.md), "Measuring the cost of a feature": `scripts/demo-exit.json` at 1920 × 1080, the branch against its base, each build twice, for the shading target's cost with no skin and no hair; and `scripts/skin-and-hair.json` with the scatter on and off, for the scatter passes, the fringe and the cutout draws. The report gives each pass, its p95, the frame span and the memory by arithmetic, with the load of the machine.
- Captures, headless with the real renderer, each looked at with a one-line caption: the head with the scatter off and on; the ear before the light; the hair block as fringed hair, as cutout hair and as glass, the cards' look before this epic. The report sends two to four of them, the hair block first.
- Done: ARCHITECTURE.md matches the code, with the numbers of the cost. The other docs follow "Doc changes". The epic closes after the merge into main.

## Settled forks

The orchestrator settled both forks on 2026-10-26 by the reasons below and told the user: fork 1 as A, by the user's request for basic scattering, and fork 2 as A, by the project's rule of one store for each piece of state.

### 1. Transmission through thin skin

- Option A (recommended): leave it out. An ear or a nostril before a light shows its ambient light and the scatter from its lit neighbors, and stays dark against the light. It costs nothing.
- Option B: add it in Part 5. For each light with shadows, a skin pixel reads the raw depth of the light's shadow map at its position moved inward along its normal, takes the thickness to the light's side, and adds the light that the profile lets through that thickness (Jimenez et al. 2010). Ears glow red against a light. It costs a part's work: a view of the cascades and of the atlas without the comparison sampler, the depth of three projections, and its tests. At a face close-up it adds about 0.05 to 0.1 ms, an estimate from one more fetch per light per skin pixel.

The user asked for basic scattering, so A. The scene of Part 6 shows the ear before the light, so the user can see what B would add.

### 2. Where glass lives

- Option A (recommended): glass moves into the material as `GlassAlpha`, and `RenderFlags.Transparent` goes. One store says how a material's alpha draws. Game code makes a pane with a glass material, and an instance can no longer make an opaque material glass: that needs a second material. About 40 places change, most in the tests.
- Option B: the flag stays. The material's alpha mode has opaque, cutout and fringed only. An instance with the flag draws only in the transparent pass with the glass rule and ignores its material's alpha mode. Fewer places change, but two stores hold how an alpha draws, and the rule for their meeting is an exception.

The project's rule of one store for each piece of state and glTF's alpha mode, which is a material's, both point to A.
