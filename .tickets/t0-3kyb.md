---
id: t0-3kyb
status: open
deps: []
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Find why the cabin stays about 4 % low after the lamps come on under the narrow box

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 10, take T11 (section 21). Takes T4 to T6 measured Design A with RetainedSigmas 0 (the narrow box) on the Mac: the cabin switch, bounce light alone, settles 75 / 95 draws, and the whole image stays about 4 % low after the lamps come on. First establish what the 4 % is measured against (the same build's lamps-on control, main or the truth). Then bisect on the branch as it stands with a scratch probe patch, on the Mac under the shared GPU lock (gpu-lock.sh --shared), holds of 20 minutes or less: narrow box; plus the feedback step's sigma_r carry off; plus the diffuse light-age cut off; plus the filter off; main; the truth. Suspects: (H1) the carried sigma_r opens the luminance stop wider and longer than main's 4-draw deviation, the filter moves more light into dim pixels, and the narrow clamp cuts it (the td-hjn6 class); (H2) the lamp-on step inflates sigma_r for about 32 draws; (H3) the diffuse light-age cut (ter-wkvw, not on main) restarts histories from one sample plus the repair; (H4) the one-sided drop test that RetainedSigmas 0 turns on for the bounce light. Design X keeps the narrow box during evidence, the carry and the light-age cut, so a cause among them gets a fix ticket before step 16.

