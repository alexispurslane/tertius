---
id: ter-3ckd
status: closed
deps: []
links: []
created: 2026-10-30T14:52:45Z
type: chore
priority: 2
assignee: Alexis Purslane
---
# Remove the Blender test suite from the repository and test runner

Remove tools/blender/tests and the Blender module in tools/test-all.sh. Update project instructions and asset-pipeline documentation that describe or prescribe these tests. Keep the Blender scripted-model build tool, assets shell, and MPFB integration.

## Acceptance Criteria

No tracked Blender tests remain; tools/test-all.sh runs only .NET test modules in Checked and Release; stale references to the removed suite are gone; the revised runner passes.


## Notes

**2026-10-30T14:58:26Z**

Removed the eight tracked files under tools/blender/tests, the Blender path in tools/test-all.sh, and current-suite references in project instructions, documentation, Nix comments, and the procedural-modeling guide. Full tools/test-all.sh passed all eight .NET modules in Checked and Release; -m Blender exits 2 as an invalid module; bash -n and git diff --check passed.
