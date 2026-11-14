---
id: t0-izfl
status: open
deps: []
links: []
created: 2026-11-14T16:45:22Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# In a clamps-off still, the captures keep averaging past the history cap of 32 draws

Found 2026-11-14 by the robust-reference job (/tmp/t7-3e22/robust, scratch commit 7c2f17b87): in one clamps-off run of rect-lamps (TERTIUS_A_NO_CLAMP=1), the pixel-scale grain of the glow column falls as 1/sqrt(draws) from 30.5 % at draws 330-989 to 6.7 % at draws 15,510-16,169, the halves of the batches correlate at 0.64, and the mean rises 4 %, although the diffuse cap stays 32. So each capture is close to a running mean over the whole run. The survey's comment says each reference capture holds a history independent of the still's; this contradicts it. Find what accumulates without a limit, and whether the survey's normal reference captures share it.

