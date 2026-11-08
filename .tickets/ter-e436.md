---
id: ter-e436
status: open
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

