---
id: tiunr-g9ia
status: open
deps: []
links: [tiunr-7haj]
created: 2026-11-08T05:21:55Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# The light-age tests without settings run with a black ambient light, not the documented gray 0.5

Found under tiunr-7haj on 2026-11-08. LightAgeHistoryTests.WithRenderer takes 'Settings settings = chosen ?? new();'. Settings is a readonly record struct whose primary constructor gives Gray = 0.5f, but new() of a struct calls the zeroing parameterless constructor, so every test without settings gets Gray = 0 (a black ambient light). The doc comment says 'a scene of the ambient gray'. tiunr-7haj met the same trap with a new Roughness default (0.2 became 0, a mirror plate) and made that field nullable. The tests pass with Gray 0, but they do not run the scene that their comments describe. Fix: give WithRenderer a default of new(Gray: 0.5f) or make Gray nullable, then check which tests change their margins.

