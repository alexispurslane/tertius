---
id: ter-4jhz
status: open
deps: []
links: []
created: 2026-10-24T16:22:41Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Slop metrics: erosion of src

A console tool tools/Tertius.Slop (Roslyn syntax only, the 5.x Roslyn that parses C# 15 preview syntax) that computes the erosion of src from https://earendil.com/posts/measuring-code-sloppiness/ and SlopCodeBench: mass(f) = CC(f) * sqrt(SLOC(f)), erosion = sum of mass over functions with CC > 10 / sum of all mass. Functions are methods, constructors, accessors, operators and local functions; a lambda counts toward the function that holds it. CC is syntactic: 1 plus each if, loop, catch, conditional expression, ?? and short-circuit && or ||, and each switch statement or switch expression counts as ONE branch no matter how many arms it has (the user's choice, 2026-10-24, because the switch rule asks for one arm per case). SLOC counts lines with code, not blank lines or comments. Skips obj, bin and generated files. Prints JSON: erosion, total mass, high-CC mass, function count, high-CC function count, SLOC.

