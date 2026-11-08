# Automatic box projection for semantic-frame environment maps

Ticket: `ter-7tnv`, under epic `ter-89xq`. This records the user's settled replacement decision on November 5, 2026. It supersedes the distance-cube search as the production corrected-read algorithm. The existing map model remains in [environment-maps-per-frame.md](environment-maps-per-frame.md).

## Selected model

A semantic frame groups objects that move and nest together. Each served semantic frame on the camera's chain already has an environment map. A projection box is a geometry enclosure used to choose a direction in that map. It is not a new room, a collision boundary, or a traced surface.

Derive one projection box for each existing map group. Use the objects already assigned to that group and the axes of its semantic frame. Do not introduce authored room volumes, scene-supplied box settings, a second ownership hierarchy, or a second object-to-map classifier.

The user rejected authored boxes because this engine has no fixed set of rooms. Its semantic frames move and nest. The earlier parent recommendation of authored boxes did not fit that model. The source of box dimensions is now settled; do not ask the user to choose it again.

## Ownership and coordinates

- Reuse `EnvironmentChain.MapOf`, `RenderInstance.ChainDepth`, and the existing prepared instance map assignment. Each geometry instance contributes to its assigned group's box.
- An off-chain descendant belongs to its nearest ancestor on the served camera chain, as it does today. Frame owners retain their existing assignment to the frame they own. Numeric frames do not create maps or boxes.
- Map indices remain outermost first. Keep the existing served-chain limit of 42; do not introduce another truncation rule.
- Fit geometry before main-view visibility culling. An off-screen contributor must still affect the enclosure. Include full geometry extents, not only object centers, and account for scale, rotation, deformation and membership changes.
- Use finite geometry that participates in the existing environment representation. Lights without geometry, stars and sky are not physical walls and do not make an infinite box.
- Express bounds in the map's semantic-frame axes, in meters, using the renderer's existing eye-relative positions and map rotations. No absolute-world origin API is required merely to represent a box.
- Rigid parent motion must not inflate an enclosure through world-axis fitting. Object motion within a frame can resize its enclosure. Recompute derived bounds during preparation; do not invent temporal smoothing or retained proxy state for this ticket.
- Existing mesh-radius bounds can supply conservative extents if suitable. Describe sphere-derived boxes as conservative, not tight mesh boxes. Do not change unrelated culling-radius behavior to obtain projection bounds. `FrameBounds` is an ownership ellipsoid, not automatically the geometry enclosure.

The map's draw rule is deliberately broader than its box group. Map i captures objects assigned to map i and ancestor groups. Those captured ancestor objects must not enlarge map i's local box. Unioning every captured object into every box would erase the semantic separation that the user selected.

## Corrected specular read

Transform the receiving position and reflection direction into the selected map's axes. Intersect that ray analytically with the derived box. Use the direction from the existing capture eye to the forward box intersection to read the existing prefiltered environment map.

Keep one implementation of this direction correction for all corrected specular readers. Replace the per-pixel distance-cube search; do not shorten, retune or preserve that search as a production alternative. Preserve the receiving-position ownership improvement: `ShadingInput.position` remains authoritative.

Empty, invalid, degenerate or unusable boxes, rays without a usable forward intersection, and zero-length lookup directions need a finite directional fallback. Parallel ray axes must not depend on undefined division or driver-specific NaN handling. Do not invent authored fallback dimensions.

The intersection is a lookup aid, not evidence of a real geometry hit. Never pass the distance to the box into reflected-motion or temporal-history logic as a real hit distance.

Keep diffuse environment reads, stars' original world direction, sky handling, stochastic ray counts/settings, hit-light composition, brightness policy, temporal accumulation and exposure policy unchanged. Do not import either noise prototype.

## Approximation and applicability

One box cannot reconstruct the depths of separate objects captured in a map. A child map also contains ancestor radiance. Correcting all that radiance with a local box remains approximate. Inspect this limitation rather than silently adding ownership textures, new map compositing or another hierarchy walk.

Large or sparse root groups can produce loose boxes and weak correction. Dynamic object membership can change a box. The implementation must show those limits honestly; this ticket does not promise exact reflections or solve tracer grain.

The target is this engine's existing camera-chain cubemaps and moving semantic frames. The default face size is 128 texels and the served chain has at most 42 maps. Validate on the available M1 Max through MoltenVK, but write to Vulkan 1.3 behavior and keep Linux execution explicitly deferred under `ter-cgif`.

Fitting can use one linear reduction over prepared geometry. Each corrected read uses one analytic box intersection instead of repeated distance-map reads. This removes search work but does not establish a whole-frame speedup. Any remaining distance-cube producers still cost time. If their removal would delay the cheap read replacement substantially, file a follow-up ticket instead of expanding this change.

## Preserved work and prerequisite reconciliation

Continue in `/Users/alexispurslane/.t3/worktrees/tertius/ter-7tnv-corrected-environment-misses`, branch `ter-7tnv-corrected-environment-misses`, from `a541601534246e87dad7e4c74b51da3f5c693eee`. Preserve the parent's dirty ticket redirect and this plan. Do not reset, replace the checkout, or remove old commits.

The original ticket review base remains `34c28466e641a864fd6a915c029e08f07b3b9ccc`. Original build `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75` and reviewed ownership fix `a541601534246e87dad7e4c74b51da3f5c693eee` remain in history. Old measurements of +2.762 ms on deck and +2.175 ms on floor at 1296 MHz describe the discarded search only.

The parent explicitly authorizes one prerequisite merge of integrated main `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf` into this ticket branch. Preserve both ticket histories. Do not modify main. A pending `--no-commit` merge followed by final validated changes can avoid an extra prerequisite-only hook run; do not bypass normal hooks or repeat the failed indexed-stash approach. Preserve dirty files before any operation that could replace them. Do not import the active FSR fixer checkout.

FSR remains an independent workflow. Preserve its existing render-size, jitter, motion and image contracts. The parent will reconcile its completed result separately; box projection must not depend on an unfinished FSR import.

## Behavioral evidence

Use red-green tests for the replacement behavior, not compilation failures. Existing search-specific tests can remain historical evidence in git, but do not weaken a quality assertion merely to make a different approximation pass. Explain each changed expectation against the selected box model.

Cover these behaviors:

- Nested ownership, frame owners, off-chain assignment and the served-chain limit reuse the existing map assignment.
- Ancestor geometry captured in a child map does not inflate the child's box.
- Full scaled/rotated geometry, deformed geometry and off-screen contributors remain enclosed.
- Common rigid frame motion preserves local bounds and the corrected lookup relationship.
- Empty/degenerate bounds, boundary points, parallel ray axes and non-intersections return finite results or the directional fallback.
- Corrected specular reads depend on the receiving position in the real renderer. Diffuse reads and open-sky behavior remain unchanged.
- Advancing snapshot clocks produce actual accumulated history, not repeated resets. Proxy distances do not become real reflected hit distances.

Use focused tests while iterating. Finish the required full Checked and Release solution suites, solution/Release benchmark compilation, scoped cleanup, whitespace checks and Vulkan 1.3 shader validation. Parent-owned coverage and mutations follow readiness, not concurrently with implementation.

Capture two to four inspected warmed real-renderer screenshots, including an interior/reflection case and nested or moving-frame evidence. Record exact commit/source identity, exposure, AA, resolution, tracer settings, warm-up, advancing clocks and resets. Preserve old images and separate controls from replacement images.

Take bounded matched whole-frame measurements with clock provenance where available. Keep the workload, output, exposure, tracer and history state comparable. Do not relabel old-search costs or unclocked observations as measured box savings. No isolated pass-cost claim without evidence.

Update the temporal-renderer plan's superseded search decision, this ticket's evidence, and `ARCHITECTURE.md` to describe actual behavior. Keep historical search results labeled as superseded. Leave integration, ticket closure, push and final budget/quality acceptance to the parent.

## Execution allocation

One build/render allocation is assigned to this continuation. Run one foreground heavy command at a time, every build/test/render/numerical probe through Nix, and GPU work through the normal `tools/gpu-lock.sh` queue. Never exceed four heavy agents machine-wide. At launch the parent observed 9.2 GB free disk; stop starting heavy work below 5 GB and do not delete another worker's artifacts. Release the allocation when all owned commands finish.

## Implementation record, 2026-11-05

Preparation uses each prepared instance's existing map assignment. Its complete prepared mesh sphere, including deformation and interpolated scale, expands only that group's enclosure before main-view culling. The enclosure uses the map's axes and eye-relative meters. Bounds reset each preparation. Culling-radius overrides and ownership `FrameBounds` do not supply the enclosure.

`environment_box.glsl` supplies the shared analytic lookup. `environmentSpecular` keeps its existing receiving position and unchanged roughness. Fully open original sky directions remain directional through the existing level-zero alpha; two exact sky controls demonstrated why pure projection needs that exception. Empty or unusable boxes and intersections fall back explicitly, including parallel and zero-length cases. The helper returns no hit distance.

The superseded search, cone helper and CPU twins are removed. Distance-cube producers remain for separate cleanup ticket `t7cem-d5f9`; they still consume allocation, writes and downsampling time. The replacement evidence and limitations are recorded in [automatic-box-projection.md](../research/temporal-renderer/automatic-box-projection.md). Earlier search measurements do not measure this replacement.

Final implementation evidence uses code/build-input identity `cda9d325a704aa321e2f921a7d3b60144373705d`: 37 focused enclosure cases and 30 deformation cases pass; complete Checked/Release suites pass 9,990/9,977 cases with 20 explicit skips each. Both solution builds, Release benchmark compilation and all 89 retained compiled Release Vulkan 1.3 modules pass. Three inspected warmed real-renderer views include the nested four-map cabin; matching observation runs confirm advancing clocks and one reset. Three matched timing pairs per deck/floor window retain 1296 MHz clocks, with bounded paired GPU frame-span differences −2.355/−1.986 ms against the integrated discarded-search control. All evidence was refreshed after the normal hook requested a two-blank-line grouping fix in the reset block. This is not final budget or image-quality acceptance. The report records the terminal's planet-scale enclosure and the retained producer cost explicitly. Parent-owned hardening and Linux execution are not claimed here.

## Boundary review correction, 2026-11-05

The shared lookup accepts only strictly positive ray parameters. An inward ray on a face uses the opposite exit. An outward ray with no positive intersection keeps its finite original direction. A zero-parameter self-intersection is not a forward intersection.

The fix worker reproduced all twelve inward/outward face failures and a real-reader radiance failure before correcting the shared helper. The earlier outward-face probe also failed its revised directional expectation. All 50 focused enclosure cases then passed. The current validation, CPU preparation measurements, matched GPU spans and refreshed capture identities are in the report's boundary-review section and `docs/research/temporal-renderer/automatic-box-projection/review-fix.json`. The preceding implementation record remains the evidence for the reviewed pre-fix commit, not the final boundary fix.
