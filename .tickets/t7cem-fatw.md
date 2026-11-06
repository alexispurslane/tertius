---
id: t7cem-fatw
status: closed
deps: []
links: []
created: 2026-11-05T22:39:01Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-7tnv
tags: [tooling, testing]
---
# Preserve staged inputs during scripted mutation runs

The listed-mutation runner rejects intentional staged merge inputs and its final HEAD-based dirty check rejects their correct restoration. This blocks the resolved box+FSR hardening merge. Extend the existing runner with explicit opt-in for stage-0 tracked inputs whose worktree bytes exactly match their staged bytes. Preserve default refusal of uncommitted mutated files. No generic dirty override, alternate runner, early prerequisite commit, or manual mutations.

## Acceptance Criteria

Red-green behavior tests cover explicit staged-input admission and byte-exact restoration, unchanged staged entries/merge metadata/HEAD, default refusal, rejection of unstaged or unmerged inputs, and error/interruption/corruption detection. Snapshot initial source and semantic Git staging state once; never restore from HEAD or rewrite staging to conceal a mismatch. Keep clean and staged inputs on one restoration/checking path. Update tool documentation and run the existing Python runner suite through Nix before using the mode for box+FSR mutations. Preserve all existing staged prerequisites, final combined checks, normal merge hooks and parent integration ownership.

## Authorized hardening result, 2026-11-05

Parent explicitly authorized this narrow tooling change within the existing box hardening allocation and pending normal merge. The complete authorization and later concurrency/disk receipts are preserved in `/private/tmp/tertius-hardening-20261105/ter-7tnv/staged-runner-authorization.md`; the complete authoritative brief is `authoritative-brief.md` in the same folder, SHA256 `c8169101340947b3cc2ed615e2cfee6454ad05c49a58efc886e6c94aec663f1b`. No separate workflow, checkout, production-renderer change or early merge commit was used.

The public `--allow-staged` option accepts only supported tracked stage-0 regular files whose bytes and executable mode match the staged object. Default refusal remains. Clean and opted-in staged files use one saved baseline of source bytes/modes, all semantic staged entries, HEAD and merge metadata. Guarded restoration and final checks retain unexpected edits and fail explicitly, rather than restoring from HEAD, rewriting the index or accepting a changed baseline. `inputs.json` records source hashes and Git identity for each actual listed run. Original build/output freshness, source-position, baseline-test and signal safeguards remain.

Meaningful red: `runner-red.log` contains two assertion failures on the old runner: correctly restored staged source was rejected by its HEAD-based final check, and restoration overwrote another writer. These are not compiler/setup failures. Final `runner-final-tests.log` passes all 70 Python tests, including actual pending-merge temporary repositories, unrelated staged prerequisites, successful orchestration, exceptional/interrupted/real-SIGTERM restoration, default/opt-in admission, SIGKILL leftovers, unsupported inputs, semantic index refresh, changed staged objects/bytes/merge metadata, and corruption that must prevent a final rebuild. All four further safety cases are included. The normal rerun and traced discovery each ran the same 70 tests before the public box/FSR table.

The standard-library trace records 279 distinct executed runner lines; its summary without `--missing` is not a full statement-coverage denominator and measures no branches. The earlier zero-test trace attempt is retained as insufficient evidence, not coverage. The existing mutation runner supports C#/GLSL, not Python; no tooling self-mutation or Python branch-coverage claim is made. This evidence is separate from renderer Cobertura and the deliberate box/FSR fault table.

Both public listed runs completed with saved source hashes, all semantic staged entries and HEAD/MERGE_HEAD unchanged across each run. The initial 20 faults had 18 kills and two survivors; the scoped public rerun kills both survivors with the added actual-renderer native/FSR rotated-map resize theory. No private runner or manual mutant cycle was used. The final combined .NET source identity, including this enhancement and its tests, is `2f6a0844cdf514a19bbdbb1fc15daaf816b0b295`; complete Checked/Release suites pass 10,082/10,069 tests respectively, zero failures and 26 explicit skips each.

Remaining safety limit: guarded check/write restoration is not an OS-atomic compare-and-swap against hostile concurrent writers. Parent inspected and accepted this limit for the serialized, exclusively owned maintenance checkout and promised no concurrent staging/edits during the table. Keep this child open for parent integration; stage completeness is not integration or final acceptance.

## Parent integration and closure, 2026-11-05

The parent integrated the tested public runner and its 70 passing safety tests into main at f7fe318bdb3894a50561f4dfae00dee1e7556461. The code identity remains 2f6a0844cdf514a19bbdbb1fc15daaf816b0b295. The parent closed this child only after integration. Default dirty-input refusal, explicit staged-input admission and the accepted exclusive-checkout restoration limit remain unchanged. No tooling self-mutation, Python branch coverage or hostile-writer atomicity is claimed.
