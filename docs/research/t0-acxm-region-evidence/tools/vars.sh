# Tool code of t0-acxm. vars.sh: the takes' folder and sides, sourced by the other tools. Each side's variable holds
# "<probe folder name> [ENV=VALUE ...]".
#   M    main c4c1f1f2f, which holds the shadow fix's merge dcd738552.
#   B    the build: the line's head with the remedy, the region's test for both signals.
#   B1   the build with spike/switches.patch and TERTIUS_SPIKE_REGION=one: the region's test for the bounce light only.
#   B6   the scratch cut to 6 draws (cut6.patch): region evidence cuts to at most 6 draws, and the ray plan counts a
#        pixel of a block with region evidence as mature.
#   A    the build with the probe's clamps off (TERTIUS_A_NO_CLAMP=1): the stills' reference for the daub.
W=${ACXM_TAKES:-/private/tmp/claude-501/-Users-alexispurslane--t3-worktrees-tertius-t3-4f7de0c4-f7a001451b483097/08bf0ad4-78e7-426c-baa9-fbd97b86a33d/scratchpad/acxm/takes}
MAIN_REV=c4c1f1f2f
M="main"
B="build"
B1="buildsw TERTIUS_SPIKE_REGION=one"
B6="cut6"
A="build TERTIUS_A_NO_CLAMP=1"
