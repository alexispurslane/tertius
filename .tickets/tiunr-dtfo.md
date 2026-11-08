---
id: tiunr-dtfo
status: closed
deps: []
links: []
created: 2026-11-08T19:13:03Z
type: bug
priority: 2
assignee: Alexis Purslane
---
# The code hash fails on a partly staged file outside the code

tools/code-tree.sh copies the index and runs git rm -r --cached without -f, first on every path outside the code list and then on every Markdown file. If a file such as ARCHITECTURE.md has staged content that differs from both the working tree and HEAD, git rm refuses it with 'the following file has staged content different from both the file and the HEAD'. The pre-commit hook then stops the commit. Found on 2026-11-08 while committing the reflected-light work in two commits. Fix: pass -f to both git rm calls, because they act on a temporary copy of the index. Add a test in tools/ that stages part of a Markdown file and runs code-tree.sh --staged.


## Notes

**2026-11-08T20:31:46Z**

Fixed on branch tiunr-dtfo-code-hash-partial-stage. Both git rm calls of tools/code-tree.sh now pass -f, because they act on a temporary copy of the index. tools/test_code_tree.py makes a new git repository under /private/tmp for each test. Before the fix, three of its four tests failed with "the following file has staged content different from both the file and the HEAD". After the fix, all four pass.

Coverage: no coverage tool runs on bash here. Each of the four tests runs both changed git rm lines.

Mutation testing: tools/mutate.py refuses tools/code-tree.sh, because no folder above it has a .csproj. A script outside the repository, /private/tmp/tertius-followups-20261108/dtfo/run_mutants.py, ran each mutant on a copy of the script and its test.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| B01 | tools/code-tree.sh:32 | first git rm without -f | killed | test_a_partly_staged_file_outside_the_code_leaves_the_staged_hash_as_it_was, test_a_partly_staged_file_outside_the_code_leaves_the_working_tree_hash_as_it_was | 1.1 |
| B02 | tools/code-tree.sh:43 | Markdown git rm without -f | killed | test_a_partly_staged_markdown_file_inside_the_code_leaves_the_staged_hash_as_it_was | 1.7 |
2 rows: killed 2, timeout 0, survived 0, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0
