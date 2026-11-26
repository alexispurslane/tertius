# Issue body formats

Use these exact section names and order. Replace every placeholder with useful content. Delete no section. Keep the writing concise and follow `ste80-humanizer`.

## Feature or epic

```markdown
# Executive Summary

- <Highlight of the goal and main outcome.>
- <Highlight of the most important constraint or evidence.>
- <Optional third highlight.>

# Context

<Only the engine context needed to understand the goal. Link related issues and the wiki design section.>

# Description

<The goal and its intended result. An epic names the outcomes of its native sub-issues.>

# Acceptance Criteria

1. <The most important observable change and the test or evidence that proves it.>
2. <The next observable change and its test or evidence.>
3. <The remaining architecture, documentation or verification requirement.>
```

Order acceptance criteria by importance. Describe engine behavior, tests, architecture or other observable results. Do not prescribe a code-shaped checklist.

## Bug

```markdown
# Executive Summary

- <The observed defect and its effect.>
- <The expected behavior and main evidence.>

# Context

<The engine context, reproduction location and related issue links.>

# Actual

<What happens now, with the reproduction and measured result.>

# Expected

<What must happen instead, with the test or measurement that proves it. This is the acceptance criterion.>

# Diagnosis

<The first supported hypothesis and the evidence that will confirm or reject it. Do not present a guess as a settled cause.>

# Preventative measures

<The tests, checks or design changes that will prevent this class of defect.>
```
