---
name: tertius-issues
description: Plan, write, organize, review and close Tertius work in GitHub Issues, the Tertius Kanban and draft pull requests. Use whenever an orchestrator plans work, creates or updates an issue or epic, starts an implementation, records a follow-up, reviews a pull request, moves board status, or links design documents. Load this before any Tertius project-management action, even if the user does not name GitHub.
---

# Tertius project management

GitHub Issues is the only active work record. The [Tertius project](https://github.com/users/alexispurslane/projects/5) is its Kanban. The [repository wiki](https://github.com/alexispurslane/tertius/wiki) holds design documents. Do not recreate a local ticket store or put progress in design documents.

Load the `ste80-humanizer` skill before writing or editing an issue, pull request, review or design document. Write for a person who follows the engine but did not join the implementation. Define engine terms and state visible outcomes, measurements and test evidence.

## Board and labels

The project is user project 5 under `alexispurslane`. Its columns have these meanings:

| Status | Actual state that requires this column | Leave this column when |
|---|---|---|
| Backlog | The idea is recorded, but it is not selected or is not ready for a builder | Acceptance criteria, labels and related links are complete and the orchestrator selects it |
| Todo | The issue is ready for a builder, but nobody has started its branch | A builder creates the worktree or branch |
| In Progress | The builder is editing, testing or pushing on a branch or draft pull request | The builder launches the review round |
| In Review | A ready pull request is waiting for CI or final resolution after its review round | The pull request merges, or it returns to draft for more work |
| Done | The pull request merged, the required evidence is linked, and the issue is closed | Reopen the issue and move it to Todo if the outcome regresses or was not delivered |

These are invariants, not suggestions:

- Change the status in the same action that changes the work. Do not leave status updates for the end of a session.
- Never leave a draft pull request in Todo or In Review. It belongs to In Progress. A ready pull request belongs to In Review. A merged pull request belongs to Done.
- An issue with an active branch belongs to In Progress even before its first pull request exists.
- An issue with no active worker, branch or pull request belongs to Todo or Backlog. Say which in a comment when work stops.
- A closed unmerged pull request cannot stay In Review. Return its issue to Todo with the reason and the next action.
- A merged pull request cannot leave its issue open or In Review. Link the evidence, close the issue and set Done immediately.
- An epic stays in its current column until every native sub-issue is Done. Then verify its outcome, close it and set it to Done.
- When a blocker stops active work, record the blocker and the unblocking condition. Return the issue to Todo if nobody is working on it.
- Keep a draft pull request In Progress during the review round. Move it to In Review only after it becomes ready.
- At the start and end of orchestration, compare every open issue with its branch and pull request. Correct any mismatch before continuing.

Set the native issue type to Bug or Feature. An epic has native type Feature and the `epic` label. A standalone feature has native type Feature and the `feature` label. A bug has native type Bug and the `bug` label.

Apply every relevant subsystem label:

| Label | Subsystem |
|---|---|
| `assets` | Asset pipeline, cooked content, blueprints and content formats |
| `core` | Core math, handles, frames and shared engine types |
| `simulation` | Entities, components, messages and deterministic gameplay |
| `physics` | Jolt integration, collision, motion and physics queries |
| `rendering` | Vulkan renderer, shaders, lighting and visual output |
| `host` | Windowing, application loop and scripted runs |
| `demo` | Demo scenes, game content and showcase films |

Apply the work labels that describe the change:

| Label | Use |
|---|---|
| `experiment` | A time-boxed measurement, investigation or prototype that will not ship unchanged |
| `housekeeping` | Cleanup, refactoring or repository maintenance |
| `production` | Engine behavior or game content intended for production |
| `bug` | A defect, also represented by native type Bug |
| `documentation` | Architecture, guides, design documents or project documentation |
| `tooling` | Developer tools, test harnesses, automation or infrastructure |

Use `experiment` instead of `production` for work whose result is evidence rather than a production change. Use `tooling` for harness work even when it also touches an engine subsystem.

## Reconcile the board

Run this audit at the start and end of every orchestration action, not only at the end of a session:

```powershell
python3 -I .agents/skills/tertius-issues/scripts/board_sync.py
```

The script reads issues, native sub-issues and pull requests, then prints every status change needed. Review its dry-run, then apply it:

```powershell
python3 -I .agents/skills/tertius-issues/scripts/board_sync.py --apply
```

Inspect any surprising change yourself before applying it. The script cannot know that an unlinked local branch exists, so an In Progress issue without a pull request keeps that status until you change it. Do not infer active work from the column itself.

## Plan the issue

Search the open epics, their native sub-issues and related closed issues before creating work. A standalone feature delivers one outcome that a person can verify. An epic groups outcomes that share a user-visible goal. Give an epic native sub-issues for those outcomes. Keep exactly one level: a sub-issue must not have sub-issues of its own.

Use these exact section names and order. Replace every placeholder with useful content. Do not drop a section.

For a standalone feature, a feature sub-issue or an epic:

```markdown
# Executive Summary

- <Highlight of the goal and main outcome.>
- <Highlight of the most important constraint or evidence.>
- <Optional third highlight.>

# Context

<Only the engine context needed to understand the goal. Link related issues and the wiki design section.>

# Description

<The goal and its intended result. An epic names the outcomes of its native sub-issues.>

# Acceptance Criteria

1. <The most important observable change and the test or evidence that proves it.>
2. <The next observable change and its test or evidence.>
3. <The remaining architecture, documentation or verification requirement.>
```

Order acceptance criteria by importance. Describe engine behavior, tests, architecture or other observable results. Do not prescribe a code-shaped checklist.

For a bug:

```markdown
# Executive Summary

- <The observed defect and its effect.>
- <The expected behavior and main evidence.>

# Context

<The engine context, reproduction location and related issue links.>

# Actual

<What happens now, with the reproduction and measured result.>

# Expected

<What must happen instead, with the test or measurement that proves it. This is the acceptance criterion.>

# Diagnosis

<The first supported hypothesis and the evidence that will confirm or reject it. Do not present a guess as a settled cause.>

# Preventative measures

<The tests, checks or design changes that will prevent this class of defect.>
```

The Expected section of a bug is its acceptance criterion.

Link every related issue explicitly:

1. Set the parent with `gh issue edit <child> --parent <epic>`.
2. Name related issues in Context with `#<number>` and say how they relate.
3. Add blocking relations when order matters with `gh issue edit <number> --add-blocked-by <other>`.
4. Link the epic to its wiki design section, including the `#section-anchor`.
5. Add each issue to the project and set its initial status to Backlog or Todo.

## Start implementation

Every issue or epic gets a new worktree, a new branch and a draft pull request. An epic can have an integration branch for epic-level work. Its component outcomes still need their own branches and pull requests.

1. Move the issue to In Progress.
2. Create the branch in a new worktree. Name it `issue-<number>-<short-kebab-title>`.
3. Commit a coherent first step and push it.
4. Create a draft pull request against `main`. Put `Closes #<number>` in the body so merging closes the issue.
5. Add the pull request to the project and set its status to In Progress.
6. Push coherent commits regularly. Each push is a progress update, so do not hold a large unseen change until the end.

Example commands for a builder using PowerShell:

```powershell
git worktree add -b issue-42-exposure-reset ../tertius-issue-42 main
Set-Location ../tertius-issue-42
# Edit, test and commit the first coherent step.
git push -u origin issue-42-exposure-reset
gh pr create --draft --base main --head issue-42-exposure-reset --title 'Reset exposure on a camera cut' --body "Closes #42"
gh project item-edit 5 --owner alexispurslane --url https://github.com/alexispurslane/tertius/issues/42 --field Status --value 'In Progress'
gh project item-add 5 --owner alexispurslane --url https://github.com/alexispurslane/tertius/pull/<number>
```

Read the branch back after a failed push or pull-request creation. Do not create a duplicate.

## Review once

The builder keeps the issue and pull request In Progress and launches one reviewer as a subagent. The reviewer reads the issue and acceptance criteria, inspects the diff against `main` locally, checks the tests and leaves inline comments on the pull-request diff. A review finding names the file, the changed line, the problem and the evidence needed to resolve it.

The reviewer can post comments directly. To avoid repeated GitHub calls, it can instead return one JSON array to the builder:

```json
[
  {
    "path": "src/Tertius.Rendering/Exposure.cs",
    "line": 42,
    "side": "RIGHT",
    "body": "This resets exposure during continuous motion too. Add a moving-camera test, then limit the reset to a camera cut."
  }
]
```

Save that array as `findings.json` and post only new comments with the current head commit:

```powershell
python3 -I .agents/skills/tertius-issues/scripts/review_comments.py post <pr-number> findings.json
```

The builder owns the response. Work through each finding in turn. Fix the code and tests, push the change, reply with the evidence and resolve that comment's thread before moving to the next finding. Use the inline comment's database id:

```powershell
python3 -I .agents/skills/tertius-issues/scripts/review_comments.py resolve <pr-number> <comment-id> '<evidence>'
```

Do not leave a fixed finding unresolved while other findings remain open. Use subagents for independent findings when that saves time. Do not start another review round by default.

## Use CI as the merge gate

A draft pull request is a progress stream. It does not run the required suite. When the builder marks the pull request ready, GitHub Actions runs every test in the seven CPU-only .NET test projects (Core, Simulation, Physics, Assets, Unions, Style and Slop) in Checked and every Python tools test on the exact branch head. Checked uses Release's optimizations and keeps the contracts. Every later push to that ready pull request runs those tests again. The same seven projects run in Release on `main` after merges to catch configuration-specific failures. GPU tests remain optional local feedback. Local runs help development, but they do not replace the required check.

After the tests pass, the workflow comments on the pull request with the branch's measured metrics. The table includes erosion, verbosity, high CC mass, high CC function count, verbose lines, SLOC, TCC, depth and the supporting counts and provenance reported by the metrics tool. Read the table as described in `AGENTS.md`, "Interpreting code quality metrics". A number alone does not prove an improvement.

The `Tests and metrics` check is required on `main`. A merge is allowed only when the current head has a passing check, every review thread is resolved and the metrics comment belongs to that head. If any later push changes the head, wait for the new check and the new comment.

## Merge and close

1. Confirm that every finding has a reply and a resolution.
2. Mark the pull request ready with `gh pr ready <number>`. Move its issue and pull request to In Review.
3. Wait for the required `Tests and metrics` check and read the metrics comment. Add any other evidence named by the issue.
4. Update `ARCHITECTURE.md` when engine behavior changed. Update the wiki when a design decision changed. Push again if needed and wait for the new check.
5. Merge with `gh pr merge <number> --merge`.
6. Verify that the issue closed. Close it manually with the result and evidence if the link did not close it.
7. Set both the issue and pull request to Done immediately. Close an epic only when every sub-issue is Done.
8. Pull `main` into other checkouts before their next dependent change or when their shared files need the merge.

## Keep design documents in the wiki

The wiki is canonical. Each design document lives only at `https://github.com/alexispurslane/tertius/wiki/<name>`. The repository has no `docs/plans` directory. Link issues and epics to a section with `#<github-slug>`.

Do not recreate design documents in the repository. Link between wiki pages by their wiki names. Leave research reports and binding architecture in the repository.

## Automation library

Use the scripts under `.agents/skills/tertius-issues/scripts/` instead of rebuilding GitHub API calls by hand:

| Script | Purpose |
|---|---|
| `board_sync.py` | Compute and apply status changes from native sub-issues and pull requests. Dry-run by default. |
| `review_comments.py` | Post only new inline findings, reply with evidence and resolve review threads. |
| `tools/pr_metrics.py` | Turn the `Tertius.Slop all` JSON into the branch metrics table. |

`tools/test_tertius_issues.py` covers the board and review planning logic. `tools/test_pr_metrics.py` covers the table. `tools/test_ci_workflow.py` covers the GitHub Actions policy. Run them before changing the workflow.

## Read back uncertain writes

After a timeout, 5xx response or unclear result, read the issue, parent relation, project item or pull request before retrying. `gh project item-add`, absolute status edits and `gh label create --force` are safe to repeat. Issue creation, sub-issue creation and pull-request creation need a read first.
