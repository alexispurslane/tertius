---
id: ter-a3ya
status: open
deps: [ter-rnzy]
links: []
created: 2026-10-24T19:16:28Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Verbosity: a narrower single-use local, and more slop rules

1. single-use-local flags only a local whose name adds nothing: a generic name (result, res, ret, temp, tmp, value, val, data, output, obj, item), or a next statement that only returns it or passes it to a parameter of the same name. The user said on 2026-10-24 that a named intermediate value makes expressions clearer, so every other single-use local is fine. 2. New verbosity rules, each from syntax alone over all of src, with tests, hand mutations and the measured counts: a doc comment whose summary or param text repeats the member or parameter name; catch (...) { throw; }; log and rethrow; wrap and rethrow around one call; .ToList()/.ToArray() only to loop; two or more bool parameters; a sentinel return (-1, empty string) for a failure in a method that is not a Try method; an interface with one implementation and no test double; a class with one field whose every method forwards to it; a contract and a guard on the same value; a parameter that every call site passes the same constant; an optional parameter that no call site sets; commented-out code; a TODO or FIXME without a ticket id; #region; Console output in src outside the Host's output; a type named *Manager, *Handler, *Processor, *Service, *Data or *Info. Leave out a rule whose hits in src are mostly false positives, and say so.


## Notes

**2026-10-24T19:19:37Z**

More rules from the user, 2026-10-24, all accepted together with the list above: (1) a catch that black-holes the error: its body neither throws, nor reads the caught exception, nor calls anything that could report it (extend except-pass-silence, which covers only an empty catch today); (2) a wall of statements: a block with more than 10 statements in a row, no blank line between any of them, and no branch, so that nothing groups them; (3) unused code across projects: an internal or public type, method, property or field whose name no other file in src, tests, scripts or content (JSON blueprints and scripts load components by type name through reflection) references. A member used only by tests counts as used, but list such members separately in the report. Unused private members and parameters are the style analyzer's job (IDE0051, IDE0052, IDE0060 in ter-ghts), and unused locals already fail the build through CS0168 and CS0219.
