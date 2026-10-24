---
id: ter-rnzy
status: in_progress
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

**2026-10-24T17:19:22Z**

The user asked on 2026-10-24 for a summary in the pre-push hook: erosion and verbosity at the last push against now, with the change. 'Now' is the tool run on the pushed tip at push time (the cleanup commit of the pre-push hook has no note of its own). 'The last push' is the note of the remote branch's tip, or of its nearest ancestor with a note. The summary also names the few commits since the last push whose notes raised a metric the most, with their ticket ids, so that an agent sees where a rise came from. It is a report: it never stops the push.
