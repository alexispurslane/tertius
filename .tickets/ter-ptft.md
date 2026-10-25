---
id: ter-ptft
status: in_progress
deps: []
links: []
created: 2026-10-22T00:25:05Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Switch the BRDF to PBR: GGX with metallic-roughness materials

Today the lighting model is energy-normalized Blinn-Phong with Schlick Fresnel (blinnPhong in lighting.glsl): F0 = 0.16 * specular^2, so F0 is at most 0.16, specular is always white, and the diffuse term is always present. Metals cannot look right, and highlights on rough surfaces have the short tail of Blinn-Phong. On 2026-10-21 the user decided to move to a PBR BRDF, and every other rendering feature waits for it, because its look and its tuning depend on the BRDF.

What changes: the BRDF in lighting.glsl, which the lighting pass, the transparent pass, the impostor captures and the lit puffs share; the material model (MaterialDesc, GpuMaterial, material.glsl, the specular-gloss texture); the meaning of the R8G8 specular-gloss target of the G-buffer, which holds two values in either model; every demo material; ARCHITECTURE.md, "Shading"; and the scope line in CLAUDE.md that names Blinn-Phong.

Design to decide (two designs, then the user chooses): the material parameters (metallic-roughness, which glTF and the asset pipeline use, or specular-glossiness); the distribution and the visibility term (for example GGX with the height-correlated Smith term); the diffuse term (Lambert or Burley); the remap from perceptual roughness; whether to compensate the energy that single scattering loses at high roughness; and whether the hemisphere ambient light gets a specular part before reflection probes exist. Units stay as they are: the move to SI units belongs to ter-bpvl.

Per the project rules: test-first CPU tests of the BRDF math (energy bounds, reciprocity, the Fresnel limits), the cost measured, and the demo scenes reviewed by eye in headless screenshots.

## Acceptance Criteria

A metal shows a specular color and no diffuse light, a dielectric shows white specular with F0 near 0.04, and a rough surface shows the long highlight tail of GGX. Every demo material is converted, the scenes are reviewed in screenshots, and ARCHITECTURE.md and CLAUDE.md name the new model.


## Design decisions (user, 2026-10-24)

Two designs were shown in the chat: A, glTF core and lean, and B, Filament-like and complete. The user chose B with Lambert diffuse:

- Metallic-roughness parameters: base color, metallic, perceptual roughness. Khronos archived glTF's specular-glossiness extension.
- Non-metals keep a reflectance value: today's specular intensity, renamed Reflectance, with F0 = 0.16 · reflectance² (0.5 gives 0.04). The canopy keeps its anti-reflective coating.
- Diffuse: Lambert, diffuse color / π, with diffuse color = base color × (1 − metallic).
- Specular: GGX with the height-correlated Smith visibility term (Heitz 2014) and Schlick Fresnel. α = roughness², with a roughness floor of 0.045.
- The light that single-bounce GGX loses at high roughness is restored with a DFG table that the renderer computes once at start.
- The hemisphere ambient light gets a specular part: the hemisphere's colors along the specular lobe, weighted by the DFG table. SSR later fades its off-screen rays to it.
- The G-buffer's specular-gloss target becomes RGBA8: metallic, roughness, reflectance, and the sky share along the specular lobe, which only the G-buffer pass can compute.
- The C# copy of the BRDF is production code, because it computes the DFG table. The CPU tests test it, and one GPU test pins the shader to it.
- The texture uses glTF's layout: roughness in G, metallic in B.
- The demo materials convert by α = √(2 / (n + 2)) from the Blinn-Phong exponent n, so each highlight keeps about its size.
- No RendererOptions switch back to Blinn-Phong, because the material model changes with the BRDF.
