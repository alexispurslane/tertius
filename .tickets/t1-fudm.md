---
id: t1-fudm
status: open
deps: []
links: []
created: 2026-11-14T22:44:54Z
type: bug
priority: 3
assignee: Alexis Purslane
---
# tools/gpu-lock.sh fails outside the dev shell: SRANDOM is unbound under the Mac's own bash 3.2

Found 2026-11-14 in stage 1 of ter-kh4g. tools/README.md, "The GPU lock", shows the shared-mode call as 'bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared <log> <command>'. Run like that from a plain shell, the Mac's /bin/bash 3.2 stops at line 63 with 'SRANDOM: unbound variable', takes no lock and runs no command. SRANDOM exists only from bash 5.1 on. Inside 'nix develop --command bash ...' the helper works. Fix one of the two: the helper falls back to another source of a random token, or it prints that it needs the dev shell's bash, and the README's example names the dev shell.

