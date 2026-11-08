---
id: tiunr-dtfo
status: open
deps: []
links: []
created: 2026-11-08T19:13:03Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# The code hash fails on a partly staged file outside the code

tools/code-tree.sh copies the index and runs git rm -r --cached without -f, first on every path outside the code list and then on every Markdown file. If a file such as ARCHITECTURE.md has staged content that differs from both the working tree and HEAD, git rm refuses it with 'the following file has staged content different from both the file and the HEAD'. The pre-commit hook then stops the commit. Found on 2026-11-08 while committing the reflected-light work in two commits. Fix: pass -f to both git rm calls, because they act on a temporary copy of the index. Add a test in tools/ that stages part of a Markdown file and runs code-tree.sh --staged.

