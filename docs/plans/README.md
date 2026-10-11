# Design docs

This folder holds one design doc for each feature that the user asked for. A design doc says what to build and why. It does not track progress.

The tickets in `.tickets/` track the work: what comes next, what depends on what, and what is done. Each epic ticket names its design doc, and each design doc names its epic ticket. Run `tk ready` to list the work whose dependencies are done, and `tk dep tree <id>` to see what a ticket waits for. The numbers in the file names are names only, and the tickets hold the order.

ARCHITECTURE.md stays the binding design. When a feature is done, its decisions move into ARCHITECTURE.md.

| Design doc | Epic ticket |
|---|---|
| [00 Real physics in every test](00-real-physics-in-tests.md) | `ter-se84` |
| [01 Test hardening](01-test-hardening.md) | `ter-7e55` |
| [02 Design review](02-design-review.md) | `ter-tanj` |
| [03 Renderer snapshot tests](03-renderer-snapshot-tests.md) | `ter-rtxu` |
| [04 Anti-aliasing](04-anti-aliasing.md) | `ter-gcm3` |
| [05 Point and spot light shadows](05-local-light-shadows.md) | `ter-s9cv` |
| [06 Host and scripted runs](06-host-and-scripted-runs.md) | `ter-dtw2` |
| [07 Content formats: blueprints and scenes](07-content-formats.md) | `ter-jwzd` |
| [08 Content packages and mods](08-content-packages.md) | `ter-ynqi` |
| [09 Asset pipeline](09-asset-pipeline.md) | `ter-kge6` |
| [10 Editor](10-editor.md) | `ter-631h` |
