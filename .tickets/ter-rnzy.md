---
id: ter-rnzy
status: open
deps: [ter-2x99]
links: []
created: 2026-10-24T16:22:41Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Slop metrics: record erosion and verbosity for every commit

The pre-commit hook runs tools/Tertius.Slop on every commit and stores erosion and verbosity under a key that stays stable after a merge, so that sloppiness over time maps to commits, branches, tickets and epics. A script prints the history as CSV.


## Notes

**2026-10-24T16:27:43Z**

Storage, the user's choice on 2026-10-24: git notes under refs/notes/slop. The pre-commit hook computes the numbers and leaves them in a pending file in the git common dir; the post-commit hook attaches a note to HEAD with erosion, verbosity, SLOC, the branch and the ticket id from the commit message. The pre-push hook also pushes refs/notes/slop. A script prints the notes joined with git log as CSV. A tracked CSV cannot hold the hash of the commit that contains it.
