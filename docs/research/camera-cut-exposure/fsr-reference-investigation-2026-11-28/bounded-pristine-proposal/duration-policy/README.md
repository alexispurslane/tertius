# Duration policy prerequisite

The pristine proposal cannot run with the checked-in duration policy. A request for 1,920 command seconds still gives 840 command seconds and a 900-second execution timeout. The proposed 1,920/1,980 values require an explicit policy decision. The parent authorizes neither a policy change nor a paid submission here.

The parent accepted the independent unbinned raw-scene CPU target for the extreme synthetic FSR case, within the unchanged 1/16-stop tolerance. The historical Linux disagreement and all local provenance remain intact. The parent also accepted both real film cuts and ordinary steady frames, in native and FSR modes. Four fresh processes run in baseline/candidate then candidate/baseline order. Those decisions do not approve spending or duration changes.

## Supported configuration paths

The [canonical CLI](../../../../../../tools/runpod/gpu_job.py) has no `--config` argument or endpoint-id selector. Its `main` always calls `Config.load()` without a path. The proposal worker's `--config` reads the measurement fixture settings only. It does not configure canonical submission limits.

The canonical Python interfaces do accept `Config.load(path)` and `submit(..., config=...)`. Tests use that injection. It gives no supported isolated CLI command or endpoint binding. Normal routing still shares the endpoint pool by worker kind and image. It can create an endpoint, delete idle endpoints of that kind, or increase worker counts within the existing quota. No wrapper, copied canonical tool, environment override or new launch mechanism was built.

The [offline proof](offline-check.json) calls the real CLI parser with an external `--config` and confirms rejection before key lookup. It loads both configs through the real `Config.load`, which constructs the real `Kind` values. It calls `execution_seconds`, `cost_usd`, `worst_case_usd`, `endpoint_request`, and the reuse branch of `route` against an in-memory endpoint. It never calls canonical `submit`, constructs a real API client, reads credentials, or attempts budget admission. An audit hook forbids sockets and child processes within the proof.

## Exact inert proposal

The external proposed file is `/tmp/tertius-issue4-duration-policy/endpoint.proposed.json`. Its external diff is `/tmp/tertius-issue4-duration-policy/endpoint-duration-only.patch`. The [retained exact diff](endpoint-duration-only.patch) changes only GPU `job_seconds` from 840 to 1,920 and GPU `endpoint_timeout_seconds` from 900 to 1,980. The canonical config retains its original bytes.

| Artifact | SHA-256 |
|---|---|
| Unchanged canonical `tools/runpod/endpoint.json` | `d6579e48c7fa847e54c07e2cfac3757a77451882882ea4d4856bfd2920cbc52f` |
| External proposed config | `6cc6990f6a024f9cb6c326de260612047841dc42c1eaa395bfd3402024776c6b` |
| Exact duration-only diff | `4634919df61d04f9f6ec0ce08fd73643d60527fad60cfb22ac36185d88672d84` |

Every other byte remains the same. The unchanged GPU settings allow five workers at $0.00016 per second. The CPU settings remain three workers, 7,140 command seconds and 7,200 execution seconds at $0.00002 per second. The project still has a $5 daily cap on 2026-11-28 and a $30 total cap, shared across both worker kinds and all agents. Offline arithmetic establishes no available balance. Canonical admission must still count billing, audit records and jobs in flight before any separately authorized submission.

In canonical `submit`, the duration calculation is exactly:

```python
        seconds = min(request.seconds, kind.job_seconds)
        execution = execution_seconds(kind, seconds)
        worst = worst_case_usd(kind, seconds)
```

where

- `request.seconds` is the requested time for all commands together, in seconds.
- `kind` is the configured GPU or CPU worker policy. `kind.job_seconds` is its command limit, in seconds.
- `min` chooses the smaller value. `seconds` is the command time sent to the handler.
- `execution_seconds` adds the 60-second handler allowance and bounds the result by `kind.endpoint_timeout_seconds`.
- `execution` is the per-job execution limit, in seconds. The request sends it as `policy.executionTimeout` in milliseconds.
- `worst_case_usd` applies the canonical cost model to that execution limit. `worst` is the resulting budget reservation, in USD.

| Config used for a 1,920-second request | Command seconds sent | Execution seconds | Canonical reservation, USD | Result |
|---|---:|---:|---:|---|
| Checked-in config | 840 | 900 | 0.1552 | Cannot run the proposed scope within its projected duration |
| External proposal, both fields changed | 1,920 | 1,980 | 0.328 | Supplies the proposed limits, conditional on approval and admission |
| Only command limit changed | 1,920 | 900 | 0.1552 | Execution ends before the command allowance, with no handler margin |
| Only execution limit changed | 840 | 900 | 0.1552 | Command clamp remains |

The $0.328 reservation follows the existing canonical model: `(1980 + 60 + 10) * 0.00016`. It retains the model's existing assumptions. It is not a fresh measurement or a new guarantee about actual billing. The [capacity estimate](../capacity.json) remains 1,838.569819 command seconds with setup/build hard caps. No new runtime claim follows from this correction.

## Existing endpoint versus per-job policy

Runpod's [execution policies](https://docs.runpod.io/serverless/endpoints/send-requests#execution-policies) state:

> Setting `executionTimeout` in a request overrides the default endpoint setting for that specific job only.

The [endpoint settings](https://docs.runpod.io/serverless/endpoints/endpoint-configurations#execution-timeout) also describe a per-request override. [Retained source excerpts](runpod-docs-evidence.json) record the fetched Markdown hashes and exact lines. These statements describe the published contract. The investigation did not test a live endpoint.

With the proposed config, canonical submission would send `policy.executionTimeout = 1980000` milliseconds. An existing endpoint's 900,000-millisecond default needs no explicit duration update for that job under the published contract. The in-memory reuse check confirms that canonical routing leaves that default untouched. `grow` only updates worker counts when there is quota room. It never updates duration.

If routing instead creates an endpoint, `endpoint_request` uses the proposed config and sends an endpoint default of 1,980,000 milliseconds. That creation would be a persistent service change performed during a later authorized submission. Config editing alone contacts no service. Other requests that omit a per-job policy would inherit the new endpoint default. The investigation made no Runpod API calls.

## Handler, output and lifetime margins

The [worker entry](../../../../../../tools/runpod/worker.py) passes the input to the [handler](../../../../../../tools/runpod/handler.py) with its default `Limits`. That command ceiling is 7,140 seconds, so it does not clamp a 1,920-second input. The offline check observes the real handler passing 1,920 seconds to its replaced command runner, without fetching a tree or starting a command.

The 60 seconds between the proposed command and execution limits cover tree fetch/checkout before commands, then output collection, encoding and return. The handler starts its command deadline after tree preparation. Git fetch has no separate subprocess timeout. Collection has a size limit but no separate time limit. This work shares the 60-second allowance, which cannot guarantee output delivery.

The proposal worker stops starting subprocess work ten seconds before its command deadline. That reserve does not impose a hard deadline on final asset hashing, gzip compression or restoration. Existing per-phase caps and the capacity gate remain unchanged. A timeout remains a failed run, with no automatic retry.

The canonical request sends a 7,200-second `policy.ttl`. Runpod documents that TTL covers queue time and execution together, from submission. A full 1,980-second execution needs to start before 5,220 seconds after submission, with additional room to retrieve the result before TTL expiry. The local pending record expires after 7,200 + execution seconds. That local expiry does not extend the service TTL. The proposal keeps the current TTL.

The existing proposal's 6,500,000-byte compressed-row gate is not proof that every output fits. Those bytes alone encode to 8,666,668 base64 bytes, above the handler's 8,000,000-byte aggregate file limit. JSON and other files need room too. The handler skips files that do not fit, so complete returned artifacts need a separate check. This output completeness limit stays explicit and unresolved here. The fixture and canonical handler remain unchanged.

## Owner decision and rollback

There is no reviewed isolated launch command. Two policy options remain for the parent:

| Option | What the owner authorizes | Cost or limitation |
|---|---|---|
| A: approve the exact duration-only policy proposal | Separately review and apply the two-field diff in the checkout used for canonical submission, then approve at most one exact $0.328 modeled job if admission allows it | That checkout's other canonical GPU submissions inherit a higher default and ceiling while the policy is present. A newly created shared endpoint also receives the higher default. |
| B: keep the existing duration policy | Retain 840/900 seconds and replan the workload or a supported isolated configuration path as separate work | The current four-process scope is not launchable. No new mechanism or substitute experiment is supplied here. |

For A, the owner must approve both the policy exposure and the exact paid job. Approval must also cover normal canonical routing's possible endpoint creation and worker changes. The owner must review the output completeness limit and result-lifetime margin before launch. A config-only approval does not approve spending. Nothing here requires a direct push to protected `main`.

After the authorized job settles, restore the two local config values to 840/900. Check the canonical config hash above. This restores later canonical command defaults and per-job execution policies for that checkout. It does not shorten an already-submitted job or restore a newly created endpoint's default.

If a new endpoint's default needs restoration, the owner must authorize a separate duration update for that endpoint. Restore 900,000 milliseconds and read it back. Endpoint deletion, worker changes and new jobs are outside that rollback.

The paid approval flags remain false, the authorized submission count stays zero, and automatic retry stays false. The existing worker still refuses execution before it creates output. Linux correctness evidence remains platform-labeled. Readiness, CI, merge and spending remain with the parent.

## Replay the local proof

The external config is inert and may disappear with `/tmp` cleanup. Recreate it outside the repository from the unchanged canonical config and the retained exact diff before replay. Check both hashes above. This replay command only reads config and runs offline checks:

```powershell
nix develop --command python3 -I docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/duration-policy/offline_check.py --repo . --proposed-config '/tmp/tertius-issue4-duration-policy/endpoint.proposed.json'
```
