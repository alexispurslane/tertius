# Rendering upgrade

Ticket: `ter-sglu`. The user grouped these features into one epic on 2026-10-21.

## Goal

Bring the renderer to its intended look in one epic: a PBR BRDF, an HDR pipeline with automatic exposure, a tone curve and bloom, shadows for every light, and screen-space effects. Every feature in this epic changes the look of a frame. The snapshot tests and the asset pipeline come after it, so that reference images and imported materials meet the final look.

## Members

| Ticket | Feature | Waits for | Design |
|---|---|---|---|
| `ter-ptft` | A PBR BRDF: GGX with metallic-roughness materials | Nothing | Two designs, in the ticket |
| `ter-zzyr` | Automatic exposure | PBR | Two designs, in the ticket |
| `ter-2t5o` | The Hable tone curve | PBR | Two designs, in the ticket |
| `ter-63wr` | Bloom | PBR | Two designs, in the ticket |
| `ter-gcm3` | Anti-aliasing with SMAA 1x | PBR | In the ticket |
| `ter-b3h5` | Screen-space ambient occlusion | PBR | Two designs, in the ticket |
| `ter-a24f` | Screen-space global illumination | PBR, SSAO | Two designs, in the ticket |
| `ter-jxv9` | Screen-space reflections | PBR | Two designs, in the ticket |
| `ter-s9cv` | Point and spot light shadows, and lights instead of one sun (`ter-bpvl`) | PBR, exposure, the tone curve, bloom | [local-light-shadows.md](local-light-shadows.md) |

## Order

- PBR comes first. The look and the tuning of every other feature depend on the BRDF. SSR weights its reflections by the Fresnel term and blurs them by the roughness. SSAO and SSGI scale the diffuse and ambient light. Exposure, the tone curve and bloom are tuned against the radiance that the BRDF sends.
- The lights epic waits for exposure, the tone curve and bloom. Its move to SI units needs exposure and pre-exposed HDR values. The user also wants the HDR pipeline in place before the lights change.
- SSGI waits for SSAO, as before.
- The snapshot tests (`ter-rtxu`) wait for this whole epic. A snapshot locks in a look, and it cannot drive red-green TDD, because its reference image exists only after the feature works.
- The asset pipeline (`ter-kge6`) waits for this whole epic. Its decisions 12 and 13 convert glTF metallic-roughness materials to Blinn-Phong and accept that metals look wrong. After PBR, the importer takes glTF materials as they are.

## Rules for every member

- A `RendererOptions` switch for the feature, and its cost measured at 1080p.
- Red-green TDD for the C# parts and the math that CPU tests can reach.
- The look is reviewed by eye in headless screenshots of the demo scenes. No member adds reference images. The snapshot tests epic adds them after this epic.
- A temporal effect stays off under the deterministic switch that the snapshot tests epic adds. Until that switch exists, the feature's own switch turns it off.
- The last step of each member updates ARCHITECTURE.md to match the code.

## Out of scope

Reflection probes and image-based lighting. SSR reflects only what is on screen, and a ray that leaves the screen fades to the ambient light. The asset pipeline plan also names environment reflections as a later renderer plan.
