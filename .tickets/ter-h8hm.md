---
id: ter-h8hm
status: closed
deps: [ter-9xl7]
links: []
created: 2026-10-24T22:41:17Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [style]
---
# Convert the Try methods of src to T? and Result, and change TS0009

Convert the 29 Try methods in src to the rules of STYLE.md, 'Expected failures': T? for a failure without a reason, Result<T, TProblem> for one with a reason. A Try method stays only where a benchmark shows that the copy of a large struct costs real time, with a comment that names the benchmark. Change TS0009 from 'a Try method has the Try shape' to 'a Try method needs a benchmark comment', with tests and hand mutations. The BCL's own Try methods stay usable at the outer shell.

