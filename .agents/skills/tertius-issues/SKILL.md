---
name: tertius-issues
description: Plan, write, organize, review and close Tertius work in GitHub Issues, the Tertius Kanban and draft pull requests. Use whenever an orchestrator plans work, creates or updates an issue or epic, starts an implementation, records a follow-up, reviews a pull request, moves board status, or links design documents. Load this before any Tertius project-management action, even if the user does not name GitHub.
---

# Tertius project management

GitHub Issues is the only active work record. The [Tertius project](https://github.com/users/alexispurslane/projects/5) is its Kanban. The [repository wiki](https://github.com/alexispurslane/tertius/wiki) holds design documents. Do not recreate a local ticket store or put progress in design documents.

Load the `ste80-humanizer` skill before writing or editing an issue, pull request, review or design document. Write for a person who follows the engine but did not join the implementation. Define engine terms and state visible outcomes, measurements and test evidence.

## Board and labels

The project is user project 5 under `alexispurslane`. Its columns have these meanings:

| Status | Meaning |
|---|---|
| Backlog | Recorded, but not selected for work |
| Todo | Selected and ready for a builder |
| In Progress | A builder has a branch and an active or draft pull request |
| In Review | The pull request has its one review round |
| Done | Merged and verified, with the issue closed |

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

## Plan the issue

Search the open epics, their native sub-issues and related closed issues before creating work. A standalone feature delivers one outcome that a person can verify. An epic groups outcomes that share a user-visible goal. Give an epic native sub-issues for those outcomes. Keep exactly one level: a sub-issue must not have sub-issues of its own.

Read [issue-bodies.md](references/issue-bodies.md) before writing. Use the feature format for standalone features, feature sub-issues and epics. Use the bug format for defects. Keep the section names and order. The Expected section of a bug is its acceptance criterion.

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

The builder launches one reviewer as a subagent. The reviewer reads the issue and acceptance criteria, inspects the diff against `main` locally, checks the tests and leaves inline comments on the pull-request diff. A review finding names the file, the changed line, the problem and the evidence needed to resolve it.

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

The builder posts each finding as an inline comment with the pull request's current head commit:

```powershell
$head = gh pr view <pr-number> --json headRefOid --jq .headRefOid
gh api repos/alexispurslane/tertius/pulls/<pr-number>/comments -f body='<finding>' -f path='<file>' -F line=<changed-line> -f side=RIGHT -f commit_id=$head
```

The builder owns the response. Read every inline comment, fix the code and tests, reply with the evidence, resolve each thread and push. Use subagents for independent findings when that saves time. Do not start another review round by default.

## Merge and close

1. Confirm that every finding has a reply and a resolution.
2. Run the required Checked and Release tests and any evidence named by the issue.
3. Update `ARCHITECTURE.md` when engine behavior changed. Update the wiki when a design decision changed.
4. Mark the pull request ready with `gh pr ready <number>`, then merge it with `gh pr merge <number> --merge`.
5. Verify that the issue closed. Close it manually with the result and evidence if the link did not close it.
6. Verify Done on the board. Close an epic only when every sub-issue is Done.
7. Pull `main` into other checkouts before their next dependent change or when their shared files need the merge.

## Keep design documents in the wiki

The wiki is canonical. Each page in `docs/plans/<name>.md` has a corresponding wiki page at `https://github.com/alexispurslane/tertius/wiki/<name>`. The repository path contains only a short link to that page. Link issues and epics to a section with `#<github-slug>`.

Do not edit a full design document in the repository and a wiki copy separately. Move links between wiki pages to their wiki names. Leave research reports and binding architecture in the repository.

## Read back uncertain writes

After a timeout, 5xx response or unclear result, read the issue, parent relation, project item or pull request before retrying. `gh project item-add`, absolute status edits and `gh label create --force` are safe to repeat. Issue creation, sub-issue creation and pull-request creation need a read first.
