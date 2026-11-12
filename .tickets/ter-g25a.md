---
id: ter-g25a
status: open
deps: [ter-b2da]
links: []
created: 2026-11-01T13:09:14Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [rendering, performance, tools]
---
# gpu-trace: delete the recording that xctrace leaves in the temporary folder after each run

Found on 2026-11-01. Each xctrace record leaves a file $TMPDIR/instruments*.ktrace of 4 to 10 GB (on this Mac /var/folders/_5/.../T/), and xctrace never deletes it. Over the share tickets' traces, 244 GB of these files filled the disk (ENOSPC), and all four share builders stopped. The user approved deleting them all and asked for this ticket. tools/gpu-trace.sh must leave no recording behind: run xctrace with TMPDIR set to a folder inside $out and delete that folder after the report, also when the run fails (a trap). Delete run.trace after the report too, unless --keep asks to keep it; the briefs now tell builders to delete it by hand.

## Acceptance Criteria

After a gpu-trace.sh run, with or without a failure, no instruments*.ktrace file of that run stays in the system's temporary folder, and run.trace stays only with --keep. tools/README.md 'GPU trace' says so.

## Notes

**2026-11-03T12:35:27Z**

On 2026-11-03 its epic ter-pq5y closed as superseded by the temporal renderer. This tool fix stays valid for every gpu-trace run, so it stays open with no parent.

**2026-11-12T09:31:14Z**

2026-11-12 orchestrator: on 2026-11-11 at about 21:30, 152 of these leftovers (82 GB) filled the disk and broke every agent's work for about 10 minutes. Details and the stopgap janitor in ter-kba1, now closed as a duplicate of this ticket. Priority raised to 1.
