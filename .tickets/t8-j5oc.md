---
id: t8-j5oc
status: closed
deps: []
links: []
created: 2026-11-13T03:28:07Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# The cloud job script reads a stop only from the watchdog's marker file, not from any endpoint at 0 workers

On 2026-11-13 at 02:10:16 UTC the sweep agent's CPU job was refused with 'the endpoint fjcpzvnk4j8tvl is stopped at 0 workers; the orchestrator restarts it'. The watchdog had not stopped anything. Runpod's audit log shows a delete of that endpoint at 02:10:21 UTC with no update before it: a delete outside our scripts (most likely the console) sets the endpoint to 0 workers on its way out. Since 2026-11-13 the watchdog writes ~/.local/state/tertius/gpu-jobs/stopped-by-watchdog before it sets the endpoints to 0, and the orchestrator removes the file when it restarts them. Fix: gpu_job.py refuses on that file, and treats an endpoint at 0 workers without it as one on its way out: it never routes to it, grows it or deletes it.


## Notes

**2026-11-13T03:34:10Z**

Done. gpu_job.py refuses on ~/.local/state/tertius/gpu-jobs/stopped-by-watchdog alone (STOP_MARKER), read after each listing of the endpoints, before each deletion and before the growth. An endpoint at 0 workers without it is on its way out: ours() leaves it out, so the routing never uses, grows or deletes it, and a read just before each deletion skips one that a delete by hand took since the first read. Tests (91 pass): a watchdog stop refuses every job; the incident (a CPU endpoint of an older image at 0 workers, no marker) gives the CPU job a new endpoint and touches the old one not; an endpoint of the best image at 0 is not grown or used; an endpoint taken by hand during the routing is left to that delete; one taken before the growth refuses the job as gone. The two tests of the second read and of the growth's check each kill their mutant. README and WORKBENCH say that a stop by hand writes the file first. Agents in other worktrees keep the old check until they merge main.
