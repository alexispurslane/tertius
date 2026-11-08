---
id: ter-e436
status: in_progress
deps: [tiunr-nuhk, tiunr-i1ho]
links: []
created: 2026-11-08T21:04:29Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-icn3
---
# A rectangle light lets a glowing panel light the scene, with LTC shine

User decision 2026-11-08: after the tube light's follow-ups, add a rectangle light, and compute its specular shine with LTC (linearly transformed cosines, Heitz, Dupuy, Hill and Neubelt 2016), not the tube light's representative point. LTC was made for real-time GPU games with a few polygon lights per pixel and GGX materials, with no noise, so no denoiser. That matches Tertius's exact loop over tens of lights. Shape: a one-sided rectangle (Width, Height) in its pose's local XY plane that shines toward local +Z, a fourth case RectLightData of LightData and RectRenderLight of RenderLight, power in W. Specular: transform the clipped rectangle by the 3x3 matrix from a 64x64 table indexed by roughness and view angle, then the exact polygon edge sum, scaled by a second table for the Fresnel and the magnitude. Fit the tables to our own GGX lobe (GgxLobe.cs), or use the published ones only if their license and their BRDF match ours, and record which. Diffuse: the exact polygon edge sum with no transform, clipped at the horizon. Shadow from the rectangle's center, as the tube light. The glowSent rule holds: a panel that holds the light sends no glow to rays. Rays that hit lit surfaces shade them with the same code. The demo's cabin and bay lamp panels change from point lights to rectangle lights. Check the shine and the diffuse against a numerical integral. Vulkan on Linux: the tables are sampled images in a format with mandatory linear filtering. Later, a separate ticket can move the tube light to LTC's line formula so that both shapes share one method; that is not part of this ticket. Show the user two to four screenshots, smooth and rough floors.


## Notes

**2026-11-08T23:14:16Z**

Design record (implementation, 2026-11-08):
- Table source: our own fit (tools/Tertius.LtcFit, tools/README.md "The LTC table"), not the published tables. Reasons: the published tables fit a GGX with another masking term and parameterization than GgxLobe, so they would not match our lobe; a table of our own also carries no license question. Heitz's method: Nelder-Mead over (m11, m22, m13), cubic error over the sum of densities, 32 x 32 samples of each distribution. The fit takes about 6 s and is deterministic; LtcTableTests checks rows 3, 32 and 63 bit for bit.
- Second table: deviation from the brief. The magnitude and Fresnel come from the existing DFG table (specularWeight), not from a second LTC table. Both are the same integrals of the same lobe, so one store holds them; the LTC table carries only the shape.
- Format: R16G16B16A16_SFLOAT, binding 19 of the frame set, the existing linearClamp sampler. Linear filtering of this format is a mandatory format feature in Vulkan (NVIDIA, RADV, ANV, lavapipe).
- Horizon: clip the polygon (diffuse at the shading normal's horizon; specular at z = 0 of the LTC space, as Heitz). Chosen over the clipped-sphere form factor because a quad clips in one loop over four edges and the clip is exact (diffuse within 0.1 % of the integral).
- Puffs: a lit puff takes the scalar irradiance, face radiance times the solid angle, with no direction. A medium has no surface, so that is the honest answer.
- Fibers: a hair fiber shades the face as a point light along the vector irradiance, dimmed by its length over the solid angle, as for a tube.
- Shadow: from the face's center with radius 0, six atlas tiles. Sharp.
- Accuracy against the numerical integral (white metal, F = 1, units of face radiance): up to 0.6 rad off the normal, within 2.6 % + 0.005; at 1.2 rad, up to +10 % / -13 %, worst 0.048, tail loss up to 0.016. Tolerances in RectLightTests: 3 % + 0.006 and 15 % + 0.02. Why: the fitted cosine cannot follow GGX's long tail and loses up to 10 % below its horizon at grazing views; Heitz's method has this error by construction. Mirror: within 0.02 %. Rough floor energy: 4.6 % short from 40 deg (tolerance 6 %), 22.6 % short from 65 deg (tolerance 25 %). Diffuse and solid angle: within 0.1 % (tolerance 0.1 %).
- Left out: soft area shadows, two-sided panels, textured panels.
- Demo: converted to rectangle lights: the cabin lamps (orbit ship and station), the bay lamps (demo station), the shuttle's cabin lamps, the spin station's panels, the station exit's wall and ceiling lamps. Stay point lights: the lamp halls' bulbs and the spin station's hub lamp (bulbs, not panels). Glowing flat panels that hold no light: the hair-rugs scene's glowing panel (GlowingBox). The marker glows of the orbit cluster, the weightless push and the station's crate are small markers, not panels. New demo scene rect-lights with scripts/rect-lights.json; scripts/rect-lamps.json and scripts/rect-bay.json for the shots.
