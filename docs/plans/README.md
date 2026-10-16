# Design docs

This folder holds one design doc for each feature that the user asked for. A design doc says what to build and why. It does not track progress.

The tickets in `.tickets/` track the work: what comes next, what depends on what, and what is done. Each epic ticket names its design doc, and each design doc names its epic ticket. Run `tk ready` to list the work whose dependencies are done, and `tk dep tree <id>` to see what a ticket waits for. The tickets hold the order.

ARCHITECTURE.md stays the binding design. When a feature is done, its decisions move into ARCHITECTURE.md.

| Design doc | Epic ticket |
|---|---|
| [Anti-aliasing](anti-aliasing.md) | `ter-gcm3` |
| [Asset pipeline](asset-pipeline.md) | `ter-kge6` |
| [Content formats: blueprints and scenes](content-formats.md) | `ter-jwzd` |
| [Content packages and mods](content-packages.md) | `ter-ynqi` |
| [Design review](design-review.md) | `ter-tanj` |
| [Editor](editor.md) | `ter-631h` |
| [Host and scripted runs](host-and-scripted-runs.md) | `ter-dtw2` |
| [Numeric frames](numeric-frames.md) | `ter-s02r` |
| [Point and spot light shadows](local-light-shadows.md) | `ter-s9cv` |
| [Queries and sensors](queries-and-sensors.md) | `ter-1yed` |
| [Real physics in every test](real-physics-in-tests.md) | `ter-se84` |
| [Renderer snapshot tests](renderer-snapshot-tests.md) | `ter-rtxu` |
| [Test hardening](test-hardening.md) | `ter-7e55` |
