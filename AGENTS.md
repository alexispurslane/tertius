Read [CLAUDE.md](CLAUDE.md) for Tertius project instructions. For every project-management action, use the [Tertius issues skill](.agents/skills/tertius-issues/SKILL.md). GitHub Issues and the [Tertius project board](https://github.com/users/alexispurslane/projects/5) hold active work. The [repository wiki](https://github.com/alexispurslane/tertius/wiki) holds design documents.

## Keep the board current

Set each status from the work itself, in the same action that changes it:

| Status | Required actual state |
|---|---|
| Backlog | Unselected work, or work that is not ready for a builder |
| Todo | Ready work with no active branch or pull request |
| In Progress | An active worktree, branch or draft pull request |
| In Review | An active review round, findings being resolved, or CI running on a ready pull request |
| Done | A merged pull request, linked evidence and a closed issue |

- Never leave a draft pull request in Todo or In Review.
- Move an issue to In Review when the builder launches the reviewer.
- Return an issue to Todo if work stops or its pull request closes unmerged. Record the reason and next action.
- Close the issue and set Done immediately after its pull request merges. Do not leave merged work open or In Review.
- Close an epic only after every native sub-issue is Done.
- At the start and end of orchestration, compare every open issue with its branch, pull request and check state. Correct every mismatch before continuing.

## Merge through CI

Keep a pull request as a draft while the builder pushes progress. Mark it ready after the review findings are resolved. GitHub Actions then runs the full Checked and Release suites on the branch head and on every later push. The required `Tests and metrics` check must pass before merge.

After the tests pass, the workflow comments with erosion, verbosity, high CC mass, high CC function count, verbose lines, SLOC, TCC, depth and the supporting counts and provenance from the metrics tool. Read the comment and judge the numbers as `CLAUDE.md` instructs. The comment and the passing check must belong to the head being merged.
