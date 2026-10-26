# Design docs

This folder holds one design doc for each feature that the user asked for. A design doc says what to build and why. It does not track progress.

The tickets in `.tickets/` track the work: what comes next, what depends on what, and what is done. Each epic ticket names its design doc, and each design doc names its epic ticket. Run `tk ready` to list the work whose dependencies are done, and `tk dep tree <id>` to see what a ticket waits for. The tickets hold the order.

ARCHITECTURE.md stays the binding design. When a feature is done, its decisions move into ARCHITECTURE.md.

| Design doc | Epic ticket |
|---|---|
| [Ambient occlusion](ambient-occlusion.md) | `ter-b3h5` |
| [Anti-aliasing](anti-aliasing.md) | `ter-gcm3` |
| [Asset pipeline](asset-pipeline.md) | `ter-kge6` |
| [Automatic exposure](auto-exposure.md) | `ter-zzyr` |
| [Code quality tooling](code-quality-tooling.md) | `ter-9gj5` |
| [Complete ghosts between semantic frames](complete-ghosts.md) | `ter-imi5` |
| [Content formats: blueprints and scenes](content-formats.md) | `ter-jwzd` |
| [Content packages and mods](content-packages.md) | `ter-ynqi` |
| [Design review](design-review.md) | `ter-tanj` |
| [Editor](editor.md) | `ter-631h` |
| [Scene environment map](environment-map.md) | `ter-nl5u` |
| [Environment maps per frame on the camera's chain](environment-maps-per-frame.md) | `ter-uy7y` |
| [Host and scripted runs](host-and-scripted-runs.md) | `ter-dtw2` |
| [Live world](live-world.md) | `ter-xr2b` |
| [Numeric frames](numeric-frames.md) | `ter-s02r` |
| [Particles: puffs](particles.md) | `ter-4zt6` |
| [Parts: attached entities that push their chain's root](parts.md) | `ter-ftk4` |
| [PBR BRDF](pbr-brdf.md) | `ter-ptft` |
| [Point and spot light shadows](local-light-shadows.md) | `ter-s9cv` |
| [Queries and sensors](queries-and-sensors.md) | `ter-1yed` |
| [Rails components](rails-components.md) | `ter-ozm8` |
| [Real physics in every test](real-physics-in-tests.md) | `ter-se84` |
| [Screen-space reflections](screen-space-reflections.md) | `ter-jxv9` |
| [Spin gravity](spin-gravity.md) | `ter-s9m0` |
| [Renderer snapshot tests](renderer-snapshot-tests.md) | `ter-rtxu` |
| [Rendering performance](rendering-performance.md) | `ter-pq5y` |
| [Test hardening](test-hardening.md) | `ter-7e55` |
