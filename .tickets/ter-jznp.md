---
id: ter-jznp
status: closed
deps: []
links: []
created: 2026-10-30T20:32:13Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
---
# Make hook reports emphasize findings on commit and project ratios on push

Pre-push leads with project ratios and changes, with constituent counts in parentheses. Pre-commit leads with threshold and rule findings, always prompts for touched findings even when ratios are flat or falling, and ends with a compact ratio-only addendum. Keep existing metric definitions and eligibility rules.

## Implementation

- User confirmed touched-only finding lists.
- Pre-commit lists touched length, complexity, verbosity-rule and clone findings, then any refactoring prompt, then ratio-only project statistics with one baseline label.
- Findings and the prompt do not depend on project ratios increasing. A summary failure cannot suppress an existing prompt.
- Pre-push leads with erosion, verbosity, TCC and depth, each with its comparable change before constituent absolute counts in parentheses. Absolute raises retain their existing ranking.
- No metric definitions, thresholds, note schema or historical notes changed. TCC/depth remain summary-only without agreed finding thresholds.

## Verification

- Red: new layout tests failed against the prior output. Added a regression test for warnings preceding type ratios, then fixed the ordering.
- Green: Slop Checked 508 passed; Python 14 passed.
- Coverage: all executable C# lines in PushSummary and ClassMetricSummary covered. Branch coverage: PushSummary 63/64; ClassMetricSummary 26/28. The latter misses compiler-generated null-conditional paths after comparability already proves the baseline exists. The remaining push path is a missing baseline with current type metrics; missing baselines and type summaries are covered separately.
- Python reporting functions: 90/90 executable lines covered using a line tracer. Unchanged Git/SARIF parsing is outside this presentation change.
- Hand mutations: 8 Python and 4 C# behavior mutations killed, none survived or timed out. They checked touched-only filtering, either clone copy, each finding kind prompting independently, no prompt for untouched code, provenance comparisons, prompt ordering, ratio deltas, absolute deltas, counts without baselines and depth count deltas. One first attempt was rejected by the unused-assignment analyzer; its compiling replacement was killed by a test.
- Full solution builds passed with zero warnings/errors. Checked: 9369 passed, 14 existing skips, 0 failed across 8 modules; Release: 9356 passed, 14 existing skips, 0 failed across 8 modules.
- Normal pre-commit hook passed: 508 Slop tests, ratio-only addendum, no prompt for untouched findings.
- Merged to main as c7005a7a. Normal pre-push validation follows ticket closure.
