# Sources on surface thickness for screen-space rays (ter-kh4g)

Research date: 2026-11-14. This file records what each source says. It does not recommend a design.

A research child agent of stage 1 of `ter-kh4g` wrote this file. "I" in this file is that agent. [README.md](README.md) is the record of the stage, and it holds the comparison with Tertius.

## How to read this file

Terms:

- Screen walk: a ray march across the depth image of the screen.
- Depth layer: one depth value per pixel. The usual depth image is the first layer, the nearest surface.
- Thickness: how far a surface is treated as solid behind its depth value.
- Back face: a triangle that faces away from the camera.
- Depth peeling: extra geometry passes that store the second, third, and later surfaces of each pixel.
- Depth pyramid (Hi-Z, HZB): smaller copies of the depth image that store the nearest or farthest depth of each block.
- Fallback: the light source for a ray that has no trusted hit from the screen walk.

Marks on each fact:

- `[read]` = read on the page in this session. An exact quote follows, set in code type. The URL is the one in field 1 unless the line gives another.
- `[reader]` = read on the page in this session by a page-reader sub-agent. I did not see the page text myself. The quotes come from its report.
- `[memory]` = from memory, not checked.
- `[my reading]` = my interpretation of facts that I read. It is not a statement of the source.

Each record has five fields:

1. The citation and the URL that I read.
2. What the source does about thickness.
3. Its situation: scene scale, hardware, year, quality target, and the systems that it relies on.
4. Its measured cost.
5. Surfaces with no back face, and moving objects.

## Pages that I did not read

| Page | What happened | What I used |
|---|---|---|
| Uludag, `Hi-Z Screen-Space Cone-Traced Reflections`, GPU Pro 5 | O'Reilly, Taylor and Francis, and gamedev.net gave HTTP 403. The book is not free. | Three secondary pages that describe the chapter (record P7). |
| Wyman 2005, full paper | scispace.com gave an empty reply. cwyman.org gave 404. ACM gave 403. The web archive has no copy. | The abstract page and a student report (record P9). |
| Ritschel 2009 at people.mpi-inf.mpg.de | 404. | A web archive copy of the same PDF. I read it in full. |
| NVIDIA page of the 2014 deep G-buffer report | 404. | The copy on casual-effects.com. |
| Unreal Engine source code | The repository is private. I did not open it. | The public docs, the console variable reference, and the 2022 course notes. |
| Blender EEVEE shader source | My guessed GitHub paths gave 404. | The Blender manual. |
| Bevy `raymarch.wgsl` | The guessed path gave 404. | The doc comment in `ssr/mod.rs`. |
| GPUOpen-Effects/Hybrid-Reflections repository | 404. | The hybrid reflections sample of FidelityFX SDK v1.1.4. |
| kode80 blog, Bitsquid blog, original URLs | 404. | Web archive copies. |
| Activision GTAO slides | Not needed. I read the technical report. | The report PDF. |

I did not search these leads: the Crytek 2011 course slides, the Killzone Shadow Fall talk (Valient 2014), Ubisoft talks, O3DE, and Kajiya.

---

# Part 1. Papers, talks, and articles

## P1. McGuire and Mara 2014

1. Morgan McGuire, Michael Mara, `Efficient GPU Screen-Space Ray Tracing`, JCGT vol. 3 no. 4, 2014. https://jcgt.org/published/0003/04/04/paper.pdf
2. Thickness:
   - `[read]` Each depth value is the front of a box with one constant thickness for the whole image: `It assumes that each represents the front of a frustum-shaped voxel that extends away zThickness in z.`
   - `[read]` The thickness is a tuned setting: `Adjust the layer thickness to match the expected object thickness to improve image quality. This allows rays to pass behind objects. It is most useful for a multi-layered depth buffer.`
   - `[read]` For one layer, the paper accepts a very large thickness: `a single buffer, where near-infinite thickness is also a reasonable choice.` Figure 3 shows `zThickness = { 1, 2, 5, 1000} meters`.
   - `[read]` The code takes up to four depth layers: `These depth layers are those that are produced by depth peeling. However the depth peeling algorithm is slow, so on modern GPUs we prefer faster single-pass methods (e.g., [Mara et al. 2014])`.
   - `[read]` The word `backfaces` occurs once, about Crytek's older method: `heuristics to detect depth discontinuities and hits against “backfaces.”` The paper draws no back-face buffer.
3. Situation: `[read]` A research ray caster in GLSL, 2014, with a per-pixel line walk (no depth pyramid). A missed ray uses a fallback: `fall back to tracing a different data structure, such as sparse voxels, or simply fetch from an environment cube map based on the ray direction.`
4. Cost: `[read]` `At 1920 × 1080 resolution, casting a diagonal ray for 25 iterations of the inner loop at every pixel costs 1.2 ms on a desktop NVIDIA GeForce Titan and 6.4 ms on a MacBook NVIDIA GeForce 650M.`
5. No back face, moving objects: The paper says nothing about either.

## P2. Mara, McGuire, Nowrouzezahrai, Luebke 2016 (and the 2014 report)

1. `Deep G-Buffers for Stable Global Illumination Approximation`, HPG 2016. https://casual-effects.com/research/Mara2016DeepGBuffer/Mara2016DeepGBuffer.pdf . The 2014 NVIDIA report `Fast Global Illumination Approximations on Deep G-Buffers`: https://casual-effects.com/research/Mara2014DeepGBuffer/Mara2014DeepGBuffer.pdf
2. Thickness:
   - `[read]` The second layer is not a back-face layer. It is the next surface that lies at least a set distance behind the first: `we select only those fragments that are immediately accessible after a certain distance ∆z past the visible surfaces.`
   - `[read]` The reason for the minimum separation: `traditional depth peeling in Sponza reveals the second fold of the column’s molding, and not the more radiometrically relevant red tapestry behind the column`.
   - `[read]` The reflection ray still uses a constant thickness, equal to the separation, on both layers: `we treat rays that lie within [z, z + ∆z] of either of the G-buffers’ (x, y, z) positions for a pixel as a hit`.
   - `[read]` One value for all scenes: `We use ∆z = 50 cm for every result in this paper.`
   - `[read]` One geometry pass writes both layers. It needs a prediction of the first layer, and the chosen variant takes it from the previous frame: `we apply reverse reprojection [NSL∗ 07] to perform a minimum separation test against the first depth layer from frame t − 1`.
   - `[read]` The pass uses a geometry shader and layered targets: `each frame buffer render target is a texture array, a feature supported by current GPUs` and `The geometry shader applies the current transformation Tt to each triangle`.
3. Situation: `[read]` `All results were measured at 1080p (i.e., 1920×1080) on a NVIDIA GeForce GTX 980 GPU.` Table 3 lists scenes from 10k triangles (Office) to 5.9M triangles (San Miguel). The lighting uses a temporal filter (`We generally recommend α = 0.85`) and a spatial filter. A missed reflection ray uses an environment map: `After a maximum distance, or once the ray exits the guard band, we revert to mirror reflection environment map lookups.`
4. Cost: `[read]` Table 5 gives three times in ms at 1080p. They are layer 1, a second layer by depth peeling, and a second layer by the optimized single pass.

   | Scene | Layer 1 | Second layer, depth peel | Second layer, optimized single pass |
   |---|---:|---:|---:|
   | San Miguel | 4.1 | 4.0 | 1.7 |
   | Kitchen | 2.2 | 2.2 | 0.3 |
   | Sponza | 1.5 | 1.5 | 0.9 |
   | Dockside | 2.1 | 2.0 | 1.1 |
   | Op925 | 2.7 | 2.8 | 0.8 |
   | Old City | 1.2 | 1.1 | 0.7 |
   | Office | 0.2 | 0.2 | 0.1 |
   | Warehouse | 2.4 | 2.4 | 0.9 |

   - `[read]` `Our optimized REPROJECT variant produces the second layer in about half the time of single-layer rendering, on all scenes.`
   - `[read]` Reflection ray cost, first layer plus second layer: `Kitchen: 1.3 + 0.3; Dockside: 1.7 + 0.1; and San Miguel: 1.0 + 0.2.`
   - `[read]` The 2014 report, on older hardware: `computed from a two-layer deep G-buffer in 5 ms at 1080p on NVIDIA GeForce Titan. The G-buffer was generated in a single 30 ms geometry pass.`
5. No back face, moving objects:
   - The method needs no back faces, because it stores the next surface of any kind. The paper does not discuss open meshes, foliage, or glass.
   - `[read]` Moving objects give errors in the second layer near their outlines: `This is susceptible to errors around moving objects` and `REPROJECT limits errors to tight regions around silhouettes and adds no latency.`

## P3. Hofmann, Bogendörfer, Stamminger, Selgrad 2017

1. `Hierarchical Multi-Layer Screen-Space Ray Tracing`, HPG 2017. https://diglib.eg.org/bitstreams/4831e217-ce72-454b-8b27-43cb916944d7/download
2. Thickness:
   - `[read]` Many layers, from one geometry pass that fills per-pixel lists: `We usually collect between 4 to 8 layers of fragments for each pixel`.
   - `[read]` Each stored fragment still needs a thickness: `each fragment needs to have an associated depth-extent [McGuire and Mara 2014]. This is important to determine if a ray hit a fragment in question, or travels behind it.`
   - `[read]` That thickness is a constant or comes from a merge of near fragments: `the z-thickness (either fixed, scene-dependent [McGuire and Mara 2014] or adaptive after merging)`.
   - `[read]` The paper does not pair front and back faces. It names that as future work: `it would be interesting to investigate how more orthogonal rays can be kept from traversing in-between fragments that actually represent closed geometry.`
3. Situation: `[read]` Research, 2017, `All at 720p with 4 layers on Geforce 1070 GTX`. The abstract says of many layers: `might not be afforable on severely limited time-budgets`. A missed ray uses neighbor rays and an `environment lookup`.
4. Cost: `[read]` Sponza: `Render times for multi-layer framebuffer generation are 1.34 ms, computing our hierarchy takes 1.98 ms and tracing two reflections bounces 4.93 ms`. San Miguel: `render 5.62 ms, hierarchy 2.09 ms and tracing two bounces 5.25 ms`.
5. No back face, moving objects: The layers come from the current frame, so the paper reports no motion problem. `[read]` Long lists get cut: `truncation of long per-pixel linked lists may lead to geometry misses.`

## P4. Widmer, Pająk, Schulz, Pulli, Kautz, Goesele, Luebke 2015

1. `An Adaptive Acceleration Structure for Screen-space Ray Tracing`, HPG 2015. https://research.nvidia.com/sites/default/files/pubs/2015-08_An-Adaptive-Acceleration/AcceleratedSSRT_HPG15.pdf
2. Thickness:
   - `[read]` A layer hides everything behind it. The paper calls that space the occlusion volume: `An occlusion volume describes the 3D space occluded by the data in a depth layer (similar to a shadow volume).`
   - `[read]` A ray that enters that space asks the next layer: `rays that hit it, i.e., rays that would pass between occlusion boundaries, need intersection information from the background layer.`
   - `[read]` With one layer there is no answer: `The occlusion volume logic is not useful for single-layer depth+color case, where no information about occluded geometry is available.` The single-layer version then returns a hit on the edge of the occlusion volume.
   - `[read]` The layers come from a k-buffer (a per-pixel store of the k nearest surfaces): `We decided to use a simple k-buffer algorithm`.
3. Situation: `[read]` Research, 2015: `NVIDIA Geforce GTX 980 with 4GB of RAM. All images were rendered at 1600 × 900 resolution`. The uses are depth of field, stereo warping, and reflections from cube maps.
4. Cost: `[read]` Quad-tree build `is about 0.6ms per frame`. Reflections: `1.062ms to trace the reflections` with one layer and `The two layer version takes 2.55ms to trace`. `[read]` For depth of field, `less than 1% of rays end up in the background layer.`
5. No back face, moving objects: The paper says nothing about either.

## P5. Ritschel, Grosch, Seidel 2009

1. `Approximating Dynamic Global Illumination in Image Space`, I3D 2009. Read from the web archive: http://web.archive.org/web/20131206120329id_/http://www.mpi-inf.mpg.de/~ritschel/Papers/SSDO.pdf
2. Thickness:
   - `[read]` This is the clearest source of the front-and-back pair idea. With two peeled layers, the second depth is the back of the object: `When using two-manifold geometry, the first and second depth value correspond to a front - and a backface of a solid object, so a sampling point between the two faces must be inside this object`.
   - `[read]` The test changes from `behind the first depth` to `between the two depths`: `we additionally test if the sampling point is in front of the second depth value z2.`
   - `[read]` One layer cannot give the width of a blocker: `A correct shadow can only be displayed with depth peeling (avg. overhead +30%).`
   - `[read]` The other extension is extra cameras: `we used four additional cameras with a standard depth buffer, all directed to the same center-of-interest as the viewer camera.` They use `framebuffers with a lower resolution`.
3. Situation: `[read]` 2009, `a 3 GHz CPU and an NVIDIA GeForce 8800 GTX`. The main scene `contains 537k polygons and runs at 20.4 fps at 1600×1200 pixels.` The effect is short-range occlusion and one diffuse bounce, with a blur pass.
4. Cost: `[read]` `Including depth peeling and additional cameras results in an overhead of 30% − 160% for our test scenes (2 depth layers or 4 cameras).`
5. No back face, moving objects:
   - `[read]` The pair test assumes closed meshes (`two-manifold geometry`). The paper does not say what happens with open meshes.
   - `[read]` The authors often leave the extensions off: `For environment map illumination, we observed good results without them, since illumination comes from many directions and most of the visible errors are hidden.`

## P6. Bavoil and Sainz 2009

1. Louis Bavoil, Miguel Sainz, `Multi-Layer Dual-Resolution Screen-Space Ambient Occlusion`, SIGGRAPH 2009 talk slides. https://developer.download.nvidia.com/presentations/2009/SIGGRAPH/Bavoil_MultiLayerDualResolutionSSAO.pdf
2. Thickness:
   - `[read]` The problem statement: `Problem #1: Single Depth Layer` and `Missing information behind the Z-Buffer`.
   - `[read]` The answer is depth peeling, with the occlusion taken as the largest over all layers: `Use AO(uv) = max{ AO(uv,zi) }`.
   - `[read]` The slides note the closed-mesh assumption of P5: `[Ritschel et al. 09] Assumes all objects are solid objects`.
   - `[read]` The limits of peeling: `Not enough layers`, `Surfaces at grazing angle with view rays`, `Occluders with large depth complexity`.
3. Situation: `[read]` 2009, GeForce GTX 280 and 9800 GT, ambient occlusion only.
4. Cost: `[read]` `1-Layer SSAO, 90 fps`, `3-Layer SSAO, 35 fps`, `(800+100)x(600+100) resolution, GeForce 9800 GT`.
5. No back face, moving objects: The slides say nothing about either.

## P7. Uludag 2014 (chapter not read)

1. Yasin Uludag, `Hi-Z Screen-Space Cone-Traced Reflections`, GPU Pro 5, CRC Press 2014, pages 149 to 192. I did not read the chapter. All facts below come from other pages that describe it.
2. Thickness:
   - `[read]` The Frostbite talk (P10) describes the chapter's pyramid: `We have a min-z pyramid, which is basically a quad-tree.` URL: https://advances.realtimerendering.com/s2015/Stochastic%20Screen-Space%20Reflections.pptx
   - `[read]` The same talk states the chapter's limit: `The depth buffer only contains the first visible surface, so we don’t acutally know the thickness of objects. We can do some guesswork, or assume a constant thickness.` and `We would need to perform depth peeling, or have other information about the scene structure.`
   - `[read]` The Bitsquid post (P15) reports a remark on a min-max pyramid: `At the end of the GPU-Pro chapter there is a small mention that raymarching towards the camera with hiz tracing would require storing both the minimum and maximum depth value in the hiz structure (requiring to bump the format to a R32G32F format).`
   - `[reader]` A Google Books snippet of the chapter names the failure: `occlusion as well, where a ray starts traveling behind an object and fails to find a proper intersection.`
   - `[read]` Sugu Lee's re-implementation adds a thickness limit at the finest level: `We also move the ray to the next cell when the thickness is thicker than ‘MAX_THICKNESS’. This is a way to allow the ray to pass through behind an object.` The page does not say that this limit is in the book. URL: https://sugulee.wordpress.com/2021/01/19/screen-space-reflections-implementation-and-optimization-part-2-hi-z-tracing-method/
3. Situation: `[memory]` The method came from early work on a DICE game.
4. Cost: Not found.
5. No back face, moving objects: Not found.

## P8. Therrien, Levesque, Gilet 2023

1. `Screen Space Indirect Lighting with Visibility Bitmask`, 2023. https://arxiv.org/pdf/2301.11376
2. Thickness:
   - `[read]` One constant thickness for every depth sample: `It allows light to pass behind surfaces of constant thickness while keeping the efficiency of horizon-based methods.`
   - `[read]` The authors chose one layer on cost: `As rendering multiple layers is very expensive, we chose to limit ourselves to only one layer.`
   - `[read]` Why a small constant: `We propose using a small constant value since artifacts of over-occlusion around thin objects are much more noticeable than light leaks behind thick objects.`
   - `[read]` The real thickness is named as the ideal: `Ideally we would want to use the per-pixel surface thickness of objects as value for t, unfortunately this is more expensive to compute in realtime and impossible to know precisely using a single layer depth buffer.`
   - `[read]` The constant can grow with distance: `we give the option to increase t linearly over the distance to counter this effect.`
   - `[read]` Open problem: `Finding an efficient heuristic to estimate an accurate thickness for each sample would further improve the accuracy of the method but remains a difficult problem we leave for future work.`
3. Situation: `[read]` An extension `of Unity’s GTAO`, measured `at 1920x1080` `on an Nvidia RTX 2080 GPU`. It relies on a denoise pass and on samples `jittered over multiple frames`.
4. Cost: `[read]` Sampling pass, the older horizon method against this one: `0.49 ms` against `0.51 ms` (radius 0.8, 8 samples), up to `1.12 ms` against `1.13 ms`. `The denoising pass has a constant cost of 0.3 ms in 1080p.`
5. No back face, moving objects: `[read]` The known error is a leak at depth edges: `our method using visibility bitmasks with fixed thickness causes light leaks at depth discontinuities.`

## P9. Wyman 2005 (paper not read)

1. Chris Wyman, `An Approximate Image-Space Approach for Interactive Refraction`, SIGGRAPH 2005. I read only the abstract page and a student report.
2. Thickness:
   - `[reader]` The abstract (https://history.siggraph.org/?p=113948): `Our method requires two passes on a GPU`.
   - `[read]` A student report on the paper (secondary, https://www.sci.utah.edu/~leenak/Finalreport_gl.pdf) says that the distance through the object comes from back faces: `dV is approximated by the distance between back-facing and front-facing polygons whereas dN is approximated by the size of the object.`
   - `[read]` The same report describes the first pass: `rendering back faces using culling and storing normals and depth values into texture memory`.
3. Situation: `[my reading]` One refractive object in front of a distant environment. This is not a screen walk for reflections or occlusion.
4. Cost: Not found on the pages that I read.
5. No back face, moving objects: Not found on the pages that I read.

## P10. Stachowiak and Uludag 2015 (Frostbite)

1. Tomasz Stachowiak, Yasin Uludag, `Stochastic Screen-Space Reflections`, SIGGRAPH 2015 course `Advances in Real-Time Rendering`. https://advances.realtimerendering.com/s2015/Stochastic%20Screen-Space%20Reflections.pptx (slide text and speaker notes)
2. Thickness:
   - `[read]` The slide text has the word `thickness` in one note only, about the older cone-trace method: `We can do some guesswork, or assume a constant thickness. But the algorithm still won’t know whether it just hit a solid wall, and should stop, or whether the thing it hit was a thin surface, and it should continue.`
   - `[read]` Two tracers: `the super precise hierarchical Z tracer` for smooth surfaces, and `a low quality linear marcher` for rough ones.
   - The slides show no back-face buffer and no second layer.
3. Situation: `[read]` Frostbite, 2015, `PS4 timings (Frostbite testbed)`, `1600 x 900; all pixels reflective`. Rays run at half resolution. The result relies on ray reuse between pixels, a temporal filter, and a probe fallback (`SSR On Fallback On`).
4. Cost: `[read]` With 4 resolve samples and 1 ray per half-resolution pixel: tile classify 0.16 ms, ray allocation 0.24 ms, linear trace 0.20 ms, Hi-Z trace 0.37 ms, resolve 0.81 ms, temporal 0.30 ms, total 2.19 ms.
5. No back face, moving objects: The slide text that I searched says nothing on either.

## P11. Bend Studio 2023 (screen-space shadows)

1. Graham Aldridge, `Screen Space Shadows`, Sony Creators Conference at SIGGRAPH 2023. Page: https://www.bendstudio.com/blog/inside-bend-screen-space-shadows/ . I read the slide text and the code from the two download links on that page.
2. Thickness:
   - `[read]` The slides name the assumption: `Generate a shadow term from these comparisons (e.g., make assumptions about sample ‘thickness’)`.
   - `[read]` The code uses one constant, relative to the remaining depth range: `This is the assumed thickness of each pixel for shadow-casting, measured as a percentage of the difference in non-linear depth between the sample and FarDepthValue.` The default is `SurfaceThickness = 0.005;`.
   - `[read]` The slides state the limit: `This is a fundamental limitation of Screen Space Shadows` and `This is why they cannot replace shadow maps, only complement them`.
   - No back-face buffer.
3. Situation: `[read]` `Days Gone (PS4) was the first title to use this technique for Sun Light shadows`. The shadow map stays the main shadow source.
4. Cost: `[read]` `PS5`, `1440p resolution`, `60 sample shadow`, `full-screen coverage`, `0.19ms`.
5. No back face, moving objects: `[read]` An edge option `can also thin out otherwise valid shadows, especially on foliage edges.`

## P12. Jimenez, Wu, Pesce, Jarabo 2016 (GTAO), and Intel XeGTAO

1. `Practical Real-Time Strategies for Accurate Indirect Occlusion`, Activision technical report, 2016. https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf . XeGTAO: https://raw.githubusercontent.com/GameTechDev/XeGTAO/master/README.md
2. Thickness:
   - `[read]` The paper rejects depth peeling on cost and uses a guess: `thin features at depth discontinuities cast too much occlusion. While this could be solved with e.g. depth peeling, it is impractical in our case. Instead, we introduce a conservative heuristic derived from the assumption that the thickness of an object is similar to its size in screen space.`
   - `[reader]` XeGTAO states the same cause: `the depth buffer is effectively a viewspace heightmap and does not correctly represent the actual scene geometry`. Its setting `Thin occluder compensation` is off by default: `we disabled it by default for performance reasons`.
3. Situation: `[read]` A console game at 60 frames per second. The method relies on `temporal reprojection and spatial filtering`.
4. Cost: `[read]` `0.5 ms per frame on a Sony Playstation 4, for a game running at 1080p (using a standard halfres occlusion buffer)`. `[reader]` XeGTAO: `0.56ms at 1920x1080 on RTX 2060`.
5. No back face, moving objects: Not discussed.

## P13. Wronski 2014 (blog)

1. Bart Wronski, `The future of screenspace reflections`, 2014. https://bartwronski.com/2014/01/25/the-future-of-screenspace-reflections/
2. Thickness:
   - `[read]` `Depth buffer is essentially a heightfield and you need to assume some depth of objects in z-buffer. Depending on this value you will get some rays killed too soon (causing weird “shadowing” under some objects) or too late (missing obvious reflectors).`
   - `[read]` A second depth buffer is named as a future idea, not as a built feature: `Using scene / depth information from additional surfaces – second depth buffer (depth peeling?), shadowmaps or RSMs. It could really help to verify some assumptions we take about for example object thickness that can go wrong`.
3. Situation: `[memory]` The author wrote the reflections of a shipped Ubisoft game.
4. Cost: None given.
5. No back face, moving objects: Not discussed.

## P14. kode80 2015 (back-face depth for reflections)

1. kode80 (Ben Hopkins), `Screen Space Reflections in Unity 5`, 2015. Web archive: http://web.archive.org/web/2016id_/http://www.kode80.com/blog/2015/03/11/screen-space-reflections-in-unity-5/ . README: https://raw.githubusercontent.com/kode80/kode80SSR/master/README.md
2. Thickness:
   - `[read]` This source draws back faces to bound thickness for a reflection ray: `By rendering a second depth buffer with frontface culling enabled we can calculate per-pixel thickness of rendered geometry by simply subtracting frontface depth from backface depth.`
   - `[read]` The stated reason: a hit test with no thickness `results in many false positives, rays cannot travel behind objects`, and a constant thickness `results in false negatives, particularly when a scene contains a wide range of depths`.
   - `[read]` The back-face buffer can be small: `In certain test scenes I was able to use a backface depth buffer 6x smaller than screen resolution while still maintaining acceptable fidelity.`
   - `[read]` A constant is still added: `Pixel Z Size Offset: This value is added to the calculated pixel "thickness" and can help mitigate artifacts caused by thin geometry or a downsampled backface depth pass.`
   - `[read]` The ray walk follows P1: `GPU DDA for screen space ray casting (derived from Morgan McGuire & Mike Mara's work)`.
3. Situation: An open-source image effect for Unity 5 by one author, 2015. The pages give no scene sizes and no hardware.
4. Cost: None given. `[read]` `For every step of a ray the frontface and backface depth textures are sampled`.
5. No back face, moving objects: The pages do not discuss open meshes. The back-face pass is drawn in the same frame as the front depth.

## P15. Bitsquid (Stingray) 2017 (blog)

1. `Notes On Screen Space HIZ Tracing`, Bitsquid blog, 2017. Web archive: http://web.archive.org/web/20240502131330id_/http://bitsquid.blogspot.com/2017/08/notes-on-screen-space-hiz-tracing.html
2. Thickness:
   - `[read]` A constant thickness on the cells of the depth pyramid: `So instead of tracing against extruded hiz cells you trace against "floating" hiz cells.`
   - `[read]` The cost of that with a nearest-depth pyramid: `Unfortunately this often means that the traced rays travelling behind a surface degenerate into a linear search and the cost can skyrocket for these traced pixels`.
   - `[read]` The threshold flickers: `rays can osciliate from passing and failing the depth threshold test. It is essentially an amplified alliasing problem caused by the finite resolution of the depth buffer`.
   - `[read]` `Unless you can fully commit to the idea of tracing objects with infinite thicknesses, you will need to use some kind of depth threshold`.
3. Situation: The Stingray engine's reflections. No scene sizes or hardware on the page.
4. Cost: Only an iteration picture, no times.
5. No back face, moving objects: Not discussed.

---

# Part 2. Engines

## E1. Unreal Engine 5 (Lumen, classic screen-space effects, contact shadows)

1. Pages:
   - Wright, Narkowicz, Kelly, `Lumen: Real-time Global Illumination in Unreal Engine 5`, SIGGRAPH 2022 course. https://advances.realtimerendering.com/s2022/SIGGRAPH2022-Advances-Lumen-Wright%20et%20al.pdf
   - `Lumen Technical Details`. https://dev.epicgames.com/documentation/en-us/unreal-engine/lumen-technical-details-in-unreal-engine
   - `Unreal Engine Console Variables Reference` (the page title says Unreal Engine 5.9). https://dev.epicgames.com/documentation/en-us/unreal-engine/unreal-engine-console-variables-reference
2. Thickness:
   - `[read]` The screen walk runs first and another tracer takes over (docs): `Lumen features trace rays against the screen first (called Screen Tracing or Screen Space Tracing), before using a more reliable method if no hit is found, or the ray passes behind a surface.`
   - `[read]` The ray that goes behind a surface is not a hit. It is handed over from its last visible point (course): `We do this by stepping back to the last unoccluded position, whenever the ray goes behind a surface or off the screen.`
   - `[read]` The walk uses a nearest-depth pyramid (course): `Instead we use a HZB traversal, which is a stackless walk of the Closest HZB mips.`
   - `[read]` The thickness is a constant, relative to depth (variable reference). Diffuse rays: `r.Lumen.ScreenProbeGather.ScreenTraces.HZBTraversal.RelativeDepthThickness`, default `0.02`, `Determines depth thickness of objects hit by HZB tracing, as a relative depth threshold.`
   - `[read]` Reflection rays: `r.Lumen.Reflections.HierarchicalScreenTraces.RelativeDepthThickness`, default `0.005`, same help text. Shadow rays of many lights: `r.ManyLights.ScreenTraces.RelativeDepthThickness`, default `0.005`.
   - `[read]` With no fallback tracer, the surfaces count as thicker: `r.Lumen.ScreenProbeGather.ScreenTraces.ThicknessScaleWhenNoFallback`, default `2`, `Larger scales effectively treat depth buffer surfaces as thicker for screen traces when there is no Distance Field present to resume the occluded ray.`
   - `[read]` A check for thin features: `...HZBTraversal.NumThicknessStepsToDetermineCertainty`, default `4`, `Number of linear search steps to determine if a hit feature is thin and should be ignored.`
   - `[read]` A hit must be near the depth image to take the screen color: `r.Lumen.Reflections.SampleSceneColorRelativeDepthThickness`, default `0.05`.
   - `[read]` Classic screen-space diffuse light: `r.SSGI.RejectUncertainRays`, default `1`, `Rejects the screen space ray if it was uncertain due to going behind screen geometry.`
   - `[read]` The `r.SSR.*` list on that page has no thickness variable.
   - `[read]` Contact shadows use the Bend Studio method (P11) with a constant: `r.ContactShadows.Bend.OverrideSurfaceThickness`, default `0.005`, `How thick the surface represented by a pixel is assumed to be when determining whether a ray intersects it.`
   - `[read]` Occlusion: `r.GTAO.ThicknessBlend`, default `0.5`, `A heuristic to bias occlusion for thin or thick objects.`
   - I found no back-face layer and no second depth layer on these pages.
3. Situation: `[read]` The fallback is a world-space tracer: `Lumen supports two different ray tracing methods: Hardware Ray Tracing and Software Ray Tracing through signed distance fields.` The course names the target `60 fps on consoles`. The result goes through probes, spatial reuse, and temporal filters (`Spatial and temporal reuse`).
4. Cost: `[read]` The variable reference compares the two walks: `HZB tracing is much more accurate, in particular not missing thin features, but is about ~3x slower.` The course gives no time for the screen walk alone.
5. No back face, moving objects:
   - `[read]` Foliage: `...HZBTraversal.SkipFoliageHits`, default `1`, `Whether to allow screen traces to hit Subsurface and TwoSided Foliage shading models. Can be used to work around aliasing from high frequency grass geometry.`
   - `[read]` Animated characters are the reason to keep the screen walk (course): `our Software Ray Tracing doesn’t handle skinned meshes, but we can still get indirect shadows from a third person character using Screen traces.`
   - `[read]` The course's verdict: `Ultimately, Screen traces are a net quality win, although the more they do, the more you notice when they fail.`
   - `[read]` Far from the camera there is no fallback (docs): `Past the highest Lumen Scene distance, only Screen Traces are active for global illumination.`

## E2. Unity HDRP

1. Pages (all under https://raw.githubusercontent.com/Unity-Technologies/Graphics/master/Packages/com.unity.render-pipelines.high-definition/):
   - `Documentation~/reference-screen-space-reflection.md`
   - `Documentation~/reference-screen-space-global-illumination.md`
   - `Documentation~/Compute-Thickness.md`
   - `Runtime/Lighting/ScreenSpaceLighting/ScreenSpaceReflections.compute` and `RayMarching.hlsl`
   - `Runtime/RenderPipeline/ShaderPass/ComputeThickness.shader`
   - Older engine: https://docs.unity3d.com/560/Documentation/Manual/PostProcessing-ScreenSpaceReflection.html
2. Thickness:
   - `[read]` Reflections use one tuned value for the whole screen: `Object Thickness | Use the slider to control the thickness of the GameObjects on screen. Because the SSR algorithm can not distinguish thin GameObjects from thick ones, this property helps trace rays behind GameObjects. The algorithm applies this property to every GameObject uniformly.`
   - `[read]` Diffuse light has the same kind of setting. On the master branch its name is `Depth Tolerance`: `Use the slider to control the tolerance when comparing the depth of the GameObjects on screen and the depth buffer.`
   - `[read]` In the shader the thickness is relative to the linear depth: `// b = DeviceDepth((1 + thickness) * LinearDepth(d))`.
   - `[read]` The pyramid stores one bound only, and the code warns about the cost: `// Warning: both rays towards the eye, and tracing behind objects has linear // rather than logarithmic complexity! This is due to the fact that we only store // the maximum value of depth, and not the min-max.`
   - `[read]` Compute Thickness is a separate feature. It measures the path length through a mesh: `HDRP can use the optical path to make transparent or opaque materials appear more dense in larger internal areas of a mesh.`
   - `[read]` Its pass draws both faces with no culling and adds the results: `Cull Off`, `Blend One One`, `float sign = isFrontFace ? -1.0f : 1.0f;`, `outColor = float2(value, 1.0f);`. The first channel sums signed depths, which gives the thickness. The second channel counts the faces.
   - `[read]` Its named users are materials: `Compute thickness is ideal for transparent materials that use subsurface scattering or refraction. You can also use it on flat geometry like grass and leaves.` A material reads it through the `HD Sample Buffer node`.
   - `[read]` `ScreenSpaceReflections.compute` has no reference to the Compute Thickness texture. Its only thickness inputs are `_SsrThicknessScale` and `_SsrThicknessBias`.
   - `[read]` The older engine (5.6) had a back-face option for reflections, for color and not for thickness: `Reflect Backfaces | Renders the scene by culling all front faces and uses the resulting texture for estimating what the backfaces might look like when a point on the depth map is hit from behind.` Its thickness was still a constant: `Width Modifier | Typical thickness of columns, walls, furniture, and other objects that reflection rays might pass behind.`
3. Situation: A general engine. The reference pages list a `Ray Miss` fallback setting, denoiser settings for diffuse light, and an accumulation mode for reflections. Reflection probes are the other reflection source.
4. Cost: The pages give no times. `[read]` Compute Thickness: `HDRP calculates thickness for the full screen which can have a high impact on memory usage.` Its `Half` resolution `is the best balance of detail and performance.` `[read]` Unity 5.6 on back-face reflection: `This process vastly increases the cost of the effect`.
5. No back face, moving objects:
   - `[read]` Compute Thickness separates closed and open meshes: `Thickness: ... Use this output for GameObjects with closed geometry.` and `Overlap Count: This is useful for vegetation or flat surfaces.`
   - `[read]` `Assign GameObjects with closed (solid) geometry to a different layer from GameObjects with open or flat geometry. If you assign them both to the same layer, it can create black artifacts.`
   - `[read]` Transparent objects in reflections: `HDRP incorrectly reflects a transparent GameObject using the depth of the surface behind it`.

## E3. Godot 4

1. Pages:
   - Docs: https://raw.githubusercontent.com/godotengine/godot-docs/master/tutorials/3d/environment_and_post_processing.rst
   - Class reference: https://raw.githubusercontent.com/godotengine/godot/master/doc/classes/Environment.xml
   - Shaders: https://raw.githubusercontent.com/godotengine/godot/master/servers/rendering/renderer_rd/shaders/effects/screen_space_reflection.glsl , the same path on branch `4.4`, and `ssil.glsl` on master.
2. Thickness:
   - `[read]` One tuned value: `Depth Tolerance: Can be used to allow screen-space rays to pass behind objects. The rays will treat each object as if it has this depth in determining if it can pass behind the object. Higher values will make screen-space reflections exhibit fewer "breakups", at the cost of some objects creating physically incorrect reflections.`
   - `[read]` The default of `ssr_depth_tolerance` is `0.5`.
   - `[read]` The master shader walks a depth pyramid (`source_hiz`) and applies the tolerance at the finest level in linear depth: `if ((z0 - z1) > params.depth_tolerance) { hit = false;`.
   - `[read]` Its hit check follows AMD (E5): `// Hit validation logic is referenced from here: // https://github.com/GPUOpen-Effects/FidelityFX-SSSR/blob/master/ffx-sssr/ffx_sssr.h`.
   - `[read]` The 4.4 shader is a linear walk with the same constant: `if (depth <= max(z_to, z_from) + params.depth_tolerance && -depth < params.camera_z_far * 0.95)`.
   - `[read]` The indirect light effect (SSIL) is an occlusion shader that was converted: `2021-05-27: clayjohn: convert SSAO to SSIL`. It has no thickness setting. It rejects by normal: `Normal rejection uses the normal of a given sample point to reject samples that are facing away from the current pixel.`
   - I found no back-face data in these files.
3. Situation: A general engine. `[read]` `By default, screen-space reflections are rendered at half resolution`. Other reflection sources are the fallback. SSIL `is meant to be used as a *complement* to other global illumination techniques such as VoxelGI, SDFGI and LightmapGI.`
4. Cost: None given.
5. No back face, moving objects: Not discussed.

## E4. Blender EEVEE

1. Pages:
   - Legacy (3.6): https://docs.blender.org/manual/en/3.6/render/eevee/render_settings/screen_space_reflections.html
   - Current (the `latest` manual, labeled 5.3): https://docs.blender.org/manual/en/latest/render/eevee/render_settings/raytracing.html and https://docs.blender.org/manual/en/latest/render/eevee/material_settings.html
2. Thickness:
   - `[read]` Legacy: `Thickness: How thick to consider the pixels of the depth buffer during the tracing. Higher values will stretch the reflections and add flickering. Lower values may make the ray miss surfaces.`
   - `[read]` Current screen trace: `Thickness: Controls how much thickness is assumed for surfaces represented by the depth buffer during ray tracing.`
   - `[read]` Current, hits on the back side of a visible surface: `Backface: ... When enabled, backface intersections are treated as valid hits instead of misses. This can reduce light leaking when using screen-space global illumination.` A `Radiance Scale` sets how much front light such a hit gives.
   - `[read]` The horizon scan (Fast GI) has two constants. `Thickness Near: Geometric thickness of the surfaces when computing fast GI and ambient occlusion. Reduces light leaking and missing contact occlusion. The effectiveness decreases proportionally to the distance from the shading point, following the inverse square law.`
   - `[read]` `Far: Angular thickness of the surfaces when computing fast GI and ambient occlusion. Reduces energy loss and missing occlusion of far geometry. Higher values will make the very thin objects block or reflect too much light.`
   - `[read]` The material `Thickness` output is a different thing. It describes the inside of one object for light that passes through it: `This is currently used for Subsurface Scattering, Translucent BSDF, Refraction BSDF, and the nodes containing these effects.`
   - `[read]` `From Shadow: Use the shadow maps from shadow casting lights to refine the thickness defined by the material node tree. This takes the minimum thickness between the shadow map and the material node tree value.`
   - The manual pages do not say that the screen trace reads the material thickness. I did not read the shader source.
3. Situation: A viewport and final-frame renderer that adds up many samples per image. `[read]` `Screen-Trace: Trace ray against the screen depth buffer. Fallback to light-probes if ray exits the view.` Its denoise steps are `Spatial Reuse`, `Temporal Accumulation`, and `Bilateral Filter`.
4. Cost: No times. `[read]` Thickness from shadow `will have a performance impact that scale with the number of render samples.`
5. No back face, moving objects: `[read]` The material thickness has a mode for thin parts: `Slab: Approximate the object as an infinite slab ... (e.g. glass panels, grass blades).` `[read]` A material thickness of zero treats `the object as having only one interface.`

## E5. AMD FidelityFX SSSR and Hybrid Reflections

1. Pages:
   - https://raw.githubusercontent.com/GPUOpen-Effects/FidelityFX-SSSR/master/ffx-sssr/ffx_sssr.h
   - https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.4/docs/techniques/stochastic-screen-space-reflections.md
   - https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.4/sdk/include/FidelityFX/host/ffx_sssr.h
   - Hybrid sample: https://raw.githubusercontent.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK/v1.1.4/samples/hybridreflections/shaders/Intersect.hlsl and `hybridreflectionsrendermodule.h` in the folder above it.
2. Thickness:
   - `[read]` The march itself has no thickness. `FFX_SSSR_HierarchicalRaymarch` walks the depth pyramid to the first surface in front of the ray.
   - `[read]` The thickness is a check after the march: `// We accept all hits that are within a reasonable minimum distance below the surface.` with `float confidence = 1 - smoothstep(0, depth_buffer_thickness, distance);`.
   - `[read]` The setting: `depthBufferThickness; ///< A bias for accepting hits. Larger values can cause streaks, lower values can cause holes.`
   - `[read]` A hit from behind is refused by the normal: `// We check if we hit the surface from the back, these should be rejected.`
   - `[read]` The check gives a blend weight, not a yes or no: `This level is used to interpolate between the ray marching result and a sample from the environment map ensuring a smooth transition at the edges.`
   - `[read]` The hybrid sample grows the constant with roughness and with ray length: `// Add thickness for rough surfaces` and `// Add thickness with distance`. Its defaults are `m_DepthBufferThickness = 0.2f;` and `m_SSRThicknessLengthFactor = 0.01f;`.
   - `[read]` In the hybrid sample, a weak screen hit goes to ray tracing hardware: `do_hw = (g_hsr_mask & HSR_FLAGS_USE_RAY_TRACING) && ... (confidence < g_frame_info.ssr_confidence_threshold)`, with `m_SSRConfidenceThreshold = 0.9f;`.
   - No back-face layer in these files.
3. Situation: `[read]` SSSR `is a highly optimized hierarchical screen space traversal kernel for reflections`, and `the result is denoised to provide a temporally and spatially stable image`. An environment map is `a fallback when the intersection fails.` `[reader]` The hybrid sample needs `A graphics card with DirectX® 12 and DXR support.`
4. Cost: None on the pages that I read.
5. No back face, moving objects: `[read]` A developer blog reads the thickness as a fix for thin geometry: it `stops very thin geometry (like grass, hair) occluding more than they should be`. URL: https://interplayoflight.wordpress.com/2022/09/28/notes-on-screenspace-reflections-with-fidelityfx-sssr/

## E6. Babylon.js (optional back-face depth)

1. Pages: https://doc.babylonjs.com/features/featuresDeepDive/postProcesses/SSRRenderingPipeline and https://unpkg.com/@babylonjs/core@8.12.1/PostProcesses/RenderPipeline/Pipelines/ssrRenderingPipeline.d.ts
2. Thickness:
   - `[read]` The default is a constant: `By default we use a constant thickness value, which is set to 0.5 by default.`
   - `[read]` An option draws back faces to get the real thickness: `which is to use additional depth rendering to generate the depth of the back faces. In this way, we can calculate the thickness as the difference between the depth value read from this back depth buffer and that read from the front depth buffer`.
   - `[read]` The option is off by default: `Gets or sets a boolean defining if geometry thickness should be computed automatically (default: false). When enabled, a depth renderer is created which will render the back faces of the scene to a depth texture (meaning additional work for the GPU).`
   - `[read]` The constant stays as an added offset: `In this mode, thickness is always used as an additional offset, but you can typically use a much lower value, and even 0 works well in many cases.`
   - `[read]` The docs state the limit: `However, this is not a magic mode. It will not solve all SSR problems, and only a single thickness can be calculated this way. It is also more demanding in terms of performance because the scene must be rendered again to generate the back depth buffer.`
3. Situation: A web engine (WebGL and WebGPU). The page gives no scene sizes.
4. Cost: No times. `[read]` The back-face texture can be smaller, with a warning: `you will get rendering artefacts when using a value different from 0: it's a tradeoff between image quality and performances.`
5. No back face, moving objects:
   - `[read]` Open meshes break it: `the back sides must exist, so that a thickness can be accurately calculated by subtracting the depth read from the back and front depth textures. If some of your objects have only front faces, you will get nasty artifacts`.
   - `[read]` The named fixes are to draw the mesh with no culling (`backFaceCulling = false`), to add back faces, or `to simply disable the automatic thickness calculation!`
   - `[read]` Transparent meshes have their own switch, `backfaceForceDepthWriteTransparentMeshes`, default true.

## E7. Bevy, Filament, Wicked Engine (short)

| Engine | What I read | URL |
|---|---|---|
| Bevy | `[read]` `When marching the depth buffer, we only have 2.5D information and don't know how thick surfaces are. We shall assume that the depth buffer fragments are cuboids with a constant thickness defined by this parameter.` The default is `thickness: 0.25`. | https://raw.githubusercontent.com/bevyengine/bevy/main/crates/bevy_pbr/src/ssr/mod.rs |
| Filament | `[read]` `/** ray thickness, in world units */ float thickness = 0.1f;` | https://raw.githubusercontent.com/google/filament/main/filament/include/filament/Options.h |
| Wicked Engine | `[read]` `static const float traceThickness = 1.5;` with the same check as AMD: `float confidence = 1.0 - smoothstep(0.0, traceThickness, distance);` | https://raw.githubusercontent.com/turanszkij/WickedEngine/master/WickedEngine/shaders/ssr_raytraceCS.hlsl |

None of the three files has a back-face layer.

---

# What the sources show

## Facts: who uses what

| Approach | Sources (record) |
|---|---|
| One layer, a constant or tuned thickness | Unreal Lumen screen traces, Unreal contact shadows (E1). Unity HDRP reflections and diffuse light (E2). Godot (E3). Blender EEVEE (E4). AMD SSSR (E5). Bevy, Filament, Wicked Engine (E7). Bend Studio (P11). Frostbite (P10). The bitmask paper (P8). McGuire and Mara with one layer (P1). |
| One layer, a guess from screen size or from depth edges | GTAO and XeGTAO (P12). Unreal `r.GTAO.ThicknessBlend` (E1). |
| A second layer of the next surface, not of back faces | Deep G-buffers (P2). Widmer (P4). Hofmann, with 4 to 8 layers (P3). Bavoil and Sainz (P6). |
| Front and back depth as a pair that bounds one object | Ritschel 2009 with two peeled layers (P5). kode80 (P14). Babylon.js as an option (E6). Wyman 2005 for refraction, by a secondary page (P9). |
| Front and back depth, but not read by the screen walk | Unity Compute Thickness, read by materials (E2). Blender material thickness and `From Shadow`, read by materials (E4). |
| Back faces drawn for their color, not for thickness | Unity 5.6 `Reflect Backfaces` (E2). |

More facts:

- I read five shipped engines or kits: Unreal, Unity HDRP, Godot, Blender, and AMD's kit. None feeds a back-face layer or a second depth layer to its screen walk.
- Two sources feed back-face depth to a reflection walk. One is a Unity effect of 2015 by one author (P14). The other is an optional mode of a web engine (E6). Neither gives a time in milliseconds.
- I found no studio talk (Crytek, Frostbite, Bend, Guerrilla, Ubisoft) that draws back faces to bound thickness for reflections, occlusion, or shadows. The Frostbite and Bend talks use a constant. I did not read the Crytek, Guerrilla, or Ubisoft talks.
- Three sources name a second depth buffer as a possible answer and do not build it: Frostbite (P10), Wronski (P13), and GTAO, which calls depth peeling `impractical in our case` (P12).
- The multi-layer papers still use a constant thickness per layer (P1, P2, P3). In P2 the hit range is `[z, z + ∆z]` with ∆z = 50 cm on both layers.
- The thickness is relative to depth in Unreal, in Unity HDRP, and in Bend's shadows. Unreal's defaults are 0.02 for diffuse rays and 0.005 for reflection rays. Bend's default is 0.005 of the remaining depth range.
- The thickness is an absolute distance in Godot, Bevy, Filament, Wicked Engine, AMD SSSR, and P1.

## Facts: what hides the errors of a constant thickness

| Source | What it relies on |
|---|---|
| Unreal Lumen (E1) | A ray that goes behind a surface is handed to distance fields or to ray tracing hardware from its last visible point. With no such fallback, a scale of 2 makes surfaces thicker. Extra steps test whether a hit feature is thin. Foliage hits can be skipped. |
| AMD SSSR (E5) | The thickness check gives a weight that blends the hit with the environment map. A denoiser follows. The hybrid sample sends weak hits to ray tracing hardware. |
| Unity HDRP (E2) | A ray-miss fallback, reflection probes, denoisers, and an accumulation mode. |
| Godot (E3) | Half resolution by default, fades, and other reflection sources. |
| Blender (E4) | Light probes for a ray that leaves the view, an option that accepts back-side hits, and three denoise steps. |
| Frostbite (P10) | Half-resolution rays, ray reuse, a temporal filter, and a probe fallback. |
| Bitmask paper (P8) | A small constant, because `light leaks behind thick objects` show less than too much shadow near thin objects. A denoise pass follows. |
| GTAO (P12) | A guess that thickness is close to screen size, plus temporal and spatial filters. |
| Bend Studio (P11) | Shadow maps stay the main shadow source. |
| Ritschel 2009 (P5) | Light from many directions hides the errors, so the extensions are often off. |

## Facts: each technique's situation beside the Tertius situation

The Tertius situation, as given in the task:

- Scenes: ship and station interiors with tens of lights.
- Image: 1280 × 720 render size, upscaled by FSR 3.1 with sub-pixel jitter.
- Hardware: Apple M1 Max through MoltenVK, and NVIDIA RTX 2000 Ada on Linux Vulkan.
- Frame goal: under 12 ms.
- Fallback: an environment cube map only. There is no ray tracing hardware path and no distance field.
- Walk: a hierarchical march on a min-max depth pyramid.
- Filters: temporal accumulation of reflection rays and diffuse rays, and a spatial filter on diffuse light.
- G-buffer: opaque geometry drawn in three depth ranges, each with its own projection.

| Source | Points that match | Points that differ |
|---|---|---|
| Unreal Lumen (E1) | Hierarchical walk, temporal and spatial reuse, 60 frames per second target on consoles. | Lumen has a world-space tracer that resumes the ray. Tertius has none. Lumen's own answer for that case is a thicker surface (scale 2), not a second layer. |
| AMD SSSR (E5) | Hierarchical walk, environment map fallback, denoiser, no ray tracing hardware in the plain version. | The march has no thickness inside the loop. The check runs once, on the end point. |
| Unity HDRP (E2) | A relative thickness, like Tertius's 10 %. | Its pyramid stores one bound. The code states that tracing behind objects is then linear, `not the min-max`. Tertius has a min-max pyramid. |
| Deep G-buffers (P2) | Scenes of similar or larger size, a temporal filter, an environment map fallback. | Measured at 1080p on a GTX 980. The single pass needs a geometry shader, layered targets, and the previous frame's depth. Moving objects give errors near outlines. |
| Hofmann (P3), Widmer (P4) | 720p in P3. | Per-pixel lists or a k-buffer, 4 to 8 layers, a separate tree build each frame. P3 spent 1.34 ms to 5.62 ms on layer generation alone on a GTX 1070. |
| Ritschel 2009 (P5) | The front and back pair is the idea under study. | A second peel pass, hardware of 2009, and closed meshes are assumed. |
| kode80 (P14), Babylon.js (E6) | A back-face depth buffer read by a reflection walk. | Both use a linear walk with no depth pyramid. Neither gives times. Babylon.js reports `nasty artifacts` for meshes with no back faces. |
| Bitmask paper (P8) | One layer, a constant thickness, a denoiser, measured near Tertius's hardware class (RTX 2080). | A horizon method for diffuse light and occlusion, not a ray walk. |

Three more facts for the comparison:

- No source discusses a G-buffer drawn in several depth ranges with separate projections.
- No source describes a min-max pyramid that holds a second layer.
- Sources on walks that pass behind surfaces report a cost: Unity's code comment (E2), Bitsquid's `degenerate into a linear search` (P15), and Unreal's `about ~3x slower` for the hierarchical walk against fixed steps (E1).

## My reading (kept apart from the facts)

- `[my reading]` Tertius's 10 % relative thickness is 5 times Lumen's default for diffuse rays (0.02) and 20 times its default for reflection rays (0.005). With Lumen's no-fallback scale of 2, the diffuse value is 0.04. I did not read how Unreal's shader applies these numbers, so the formulas can differ.
- `[my reading]` The sources show two different things under the name `second layer`. Deep G-buffers store the next surface that is far enough behind. The back-face designs store the end of the first object. Only the second kind gives a thickness.
- `[my reading]` The engines with a strong fallback tracer (Lumen, AMD hybrid) stop the screen ray early and hand it over. The engines with only probes or an environment map keep a tuned constant and filter the result. Tertius is in the second group.
- `[my reading]` Every source that pairs front and back depth either assumes closed meshes (P5) or reports faults on open meshes (E6, Unity Compute Thickness). The sources give no measured cost for a back-face depth pass on current hardware.
- `[memory]` MoltenVK does not offer geometry shaders. If that is right, the single-pass method of P2 does not run on the Mac target as published. I did not check this.
