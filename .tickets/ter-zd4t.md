---
id: ter-zd4t
status: closed
deps: []
links: []
created: 2026-10-21T12:06:46Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
---
# Demo score: a synth score grown from the Blue Danube, around the waltz

The user chose on 2026-10-21 (design A of the chat): the Blue Danube recording plays only from the closing approach through the docking, about 3 min 20 s, as in 2001, and ends on a cadence at the landing. Around it, an original score in the manner of Vangelis's Blade Runner: detuned sawtooth brass and pads, a slow filter, chorus and a large reverb. It is built on the waltz's opening horn call, slowed down, in the recording's own key, so that the synth swells into the orchestra and grows out of its last chord. The score is code in tools/music (a synthesizer in Python with numpy and scipy, which the user allowed to fetch), and the rendered audio stays outside the repository. The work goes in short stints, and after each the user hears an audio file and gives notes: 1 the instrument and the motif, 2 the opening cue over the launch and the orbit, 3 the bridge into the waltz, 4 the coda over the exit, 5 the whole track in the edit through --music. The composer's notes, with the user's feedback, live in tools/music/NOTES.md.


## Notes

**2026-10-21T14:17:10Z**

The user decided on 2026-10-21, after the comparison of the first 65 s with the synth score (stint 3, 65bc4a8) and with the recording: the recording of The Blue Danube plays for the whole video, from its first second, with no edit of the music. The edit's existing rule stays (--music-start 0, a 2 s fade in, a 5 s fade out over the closing card). So the score is not needed, and this ticket closes without a merge. The branch ter-zd4t-demo-score keeps the synthesizer, the analysis of the recording, the cues and NOTES.md (ten commits, ff7c83e to 65bc4a8), and it stays unmerged, so the dev shell does not grow numpy and scipy for a tool with no user. A later ticket can merge it.
