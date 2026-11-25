---
id: tb9-76ln
status: open
deps: []
links: []
created: 2026-11-25T01:14:03Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [tools, tests]
---
# A failed first full run in a new worktree crashes the test record's session end: File.Delete of a record whose folder does not exist

tools/Tertius.TestRecord/TestRecorder.cs, OnTestSessionFinishingAsync: after a full run with a failed test it calls File.Delete(record). File.Delete does not throw for a missing file, but it throws DirectoryNotFoundException when the folder tertius-tested/<configuration>/ does not exist. That is the case in a new worktree whose first full run of a configuration fails, because only Record creates the folder. Seen on ter-9wwx-art-import (2026-11-24). Fix: return when the folder is missing, or create it first; a test runs the session end in a git folder without tertius-tested.

