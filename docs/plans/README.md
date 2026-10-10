# Plans

This folder holds the work that the user asked for and that no running workflow covers yet. Each feature has one plan file. The number in a file name gives the planned order of the work.

ARCHITECTURE.md stays the binding design. When a plan is done, its decisions move into ARCHITECTURE.md, and the plan's status changes to done.

## Plan files

| Number | Plan | Status | Depends on |
|---|---|---|---|
| 01 | [Test hardening](01-test-hardening.md) | Planned | The refactor workflow |
| 02 | [Design review](02-design-review.md) | Planned | 01 |
| 03 | [Renderer snapshot tests](03-renderer-snapshot-tests.md) | Planned | 02 |
| 04 | [Anti-aliasing](04-anti-aliasing.md) | Planned | 03 |
| 05 | [Point and spot light shadows](05-local-light-shadows.md) | Planned | 03 |
| 06 | [Host and scripted runs](06-host-and-scripted-runs.md) | Planned | 02 |
| 07 | [Content formats: blueprints and scenes](07-content-formats.md) | Being drafted | 06 |
| 08 | [Content packages and mods](08-content-packages.md) | Being drafted | 07 |
| 09 | [Asset pipeline](09-asset-pipeline.md) | Being drafted | 08 |
| 10 | [Editor](10-editor.md) | Being drafted | 06, 07, 08, 09 |

## Running now

- The workflow `wf_6fef3166-23c` fixes the four bugs that the Simulation test stage found, test-first, and a reviewer checks each fix.
- The workflow `wf_5b5b785e-538` drafts plans 07 to 10.

The Simulation test stage is done and merged: 1,225 tests, with 4 skipped until their bugs are fixed.

## Chores

- Delete the branch `backup/before-history-rewrite` once the user agrees. It keeps the history from before the rewrite of commit `a8d8ae4`.

## Not planned yet

These came up during the work, but nobody asked for them yet.

- Saving and loading a running world. Scenes describe a starting state, and a save game needs the state of every component too.
- Audio.
