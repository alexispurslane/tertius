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

**2026-10-24T19:55:56Z**

Built on branch ter-rnzy-slop-history (Claude, 2026-10-24). Decision 18 of docs/plans/code-quality-tooling.md has every choice with its reason. In short:
- A note is one JSON line under refs/notes/slop: erosion, verbosity, sloc, branch, source (pre-commit, pre-push or seed) and the time of the measurement. It holds no ticket ids: the commit message is their one store, and the tool reads them from it.
- The pre-commit hook measures the staged tree (git write-tree, then Tertius.Slop measure, from a git archive copy), in the background beside the tests. After the tests pass, it writes the tree and the note to slop-pending in the worktree's own git folder, so worktrees never share the file.
- The post-commit hook is plain bash, never enters the dev shell, and attaches the note only when HEAD's tree is the measured tree. Without a pending note (--no-verify, no C# change, a merge), it skips.
- The pre-push hook runs Tertius.Slop note on the first-parent commits since the last push: it notes the tip and each commit without a note that changes src/*.cs. So merges and the cleanup commit get notes. Then it prints the summary, fetches the remote notes, merges them with cat_sort_uniq, and pushes refs/notes/slop with a second git push. It never stops the push.
- The CSV is the subcommand history of the tool, so the note format lives in one place: Tertius.Slop history --first-parent main.
- Cost: the measure step took 2.0 s +- 0.7 wall, 6.7 s CPU on a busy machine, 0.7 s more than Tertius.Slop all on the working tree. It runs beside the tests, so a commit takes about as long as before. A first push that noted 2 commits spent 2.9 s in the slop part.
- Seed: Tertius.Slop note e7afadb main --source seed noted 16 commits of today's main, tip 1ce2750 included. No commit changed. The notes are local until the first push.
