---
id: t0-8jfj
status: open
deps: []
links: []
created: 2026-11-14T23:41:15Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# gpu-trace.sh passes an empty --env value, and xctrace then records nothing

tools/gpu-trace.sh gives xctrace one --env option for each of DOTNET_ROOT, VK_ICD_FILENAMES, DYLD_FALLBACK_LIBRARY_PATH and TERTIUS_DIFFUSE_FILTER. When TERTIUS_DIFFUSE_FILTER is not set, the option is '--env TERTIUS_DIFFUSE_FILTER='. xctrace on this Mac (Darwin 27.0.0) refuses it: 'Option --env <VAR=value> cannot be parsed with provided value: TERTIUS_DIFFUSE_FILTER=', exit code 49, and the script reports 'xctrace recorded no trace'. Seen on 2026-11-14 with main ab54c785e and with the branch t7-3e22-retained-deviation. Workaround: run the script with TERTIUS_DIFFUSE_FILTER=on, which is the default kernel. Fix: pass --env only for a variable that is set and not empty.

