---
id: ter-n693
status: open
deps: []
links: []
created: 2026-10-27T19:00:04Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Slop notes and reports carry the absolute high-CC mass and verbose lines beside the two ratios

The user's question (2026-10-27): the erosion fell from 0.354 to 0.200 in three days. Is that real, or is it dilution, new simple code that grows the denominator? Both metrics are ratios, so new simple code lowers them without any fix.

Measured with `Tertius.Slop measure <commit> --details` on main:

| Commit | Date | Erosion | High-CC mass | Total mass | Functions above CC 10 | SLOC | Verbose lines |
|---|---|---|---|---|---|---|---|
| 1d5c2e84 | 10-24 10:10 | 0.354 | 12,041 | 34,007 | 92 | 35,499 | 1,531 |
| 34c14519 | 10-27 01:11 | 0.289 | 12,014 | 41,629 | 92 | 52,531 | 1,871 |
| c3a7b982 | 10-27 04:23 (after ter-6pzm) | 0.239 | 9,817 | 41,050 | 82 | 53,460 | 1,666 |
| 425971fc | 10-27 14:46 | 0.200 | 8,261 | 41,307 | 73 | 55,783 | 1,854 |

So the first drop, 0.354 to 0.289, was all dilution: the high-CC mass and the count stayed the same. The second, 0.289 to 0.200, was real: the high-CC mass fell by 31 % at a flat total mass. The verbosity ratio fell a little while the verbose lines grew by 21 %.

The change: the slop note, the history CSV, the push summary and the commit report carry the absolute numbers beside the two ratios: `highCcMass` and `highCcFunctionCount` beside the erosion, and `verboseLines` beside the verbosity. The commit report prints the change of each against the baseline note, so that a commit that raises the high-CC mass shows it even when the ratio falls. The JSON of `all` already holds all three, so only the note and its readers change.

## Design

Open point for the user at the start: whether to backfill the old notes (about 414 noted commits, a measure of each, some minutes of CPU) or let the absolute numbers start at the first commit after the change. Without a backfill, a baseline note without the new fields gives the value without a change, as a missing note does today.

The push summary's list of the commits that raised a metric the most ranks by the absolute numbers then, because the ratio rewards dilution.

