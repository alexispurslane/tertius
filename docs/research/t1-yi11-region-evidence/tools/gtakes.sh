#!/usr/bin/env bash
# Tool code of t1-yi11. gtakes.sh <kind> <column>...: frozen bay takes of the probe, each with the ring log
# (TERTIUS_X_REGION_LOG) in its folder where the build is yg. The columns (build: knobs):
#   a: yg, cap 32 (main; yg draws what ya draws); b: yg, cap 128; c: yc, cap 128 (the repaired test);
#   d: yg, cap 128, region evidence off (TERTIUS_X_REGION_OFF=1);
#   g: yg, cap 128, the cause gate TERTIUS_X_REGION_GATE=128; r: yg, cap 128, the signed rank test TERTIUS_X_REGION_RANK=1;
#   gr: yg, cap 128, both, TERTIUS_X_REGION_GATE=36 TERTIUS_X_REGION_RANK=1 (today's test while the gate is open, the rank
#       test while it is closed);
#   g128r: yg, cap 128, both with a gate of 128 draws, TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1 (the selected rule);
#   byi, g128ryi: b and g128r drawn by build yi (scratch-yi.patch); w36, hyst: yi, cap 128, gate 128, the closed reset
#       and the closed-gate test TERTIUS_X_REGION_CLOSED=w36 or hyst (2026-11-24);
#   n32, n128: yg, cap 32 or 128, region evidence off and the probe's clamp off (TERTIUS_A_NO_CLAMP=1: ClampSigmas -1 for
#       every signal, the diffuse and the specular one).
# The kinds: film (bay-<column>, the full picture, and bay-<column>-dif, the diffuse signal alone, 660 draws, every draw
# from 300 kept: the stills and the frozen film), log (bay-<column>-log, the diffuse signal alone, 660 draws), long
# (bay-<column>-long-dif, the diffuse signal alone, 1,660 draws, every 50th draw from 660) and longfull (bay-<column>-long,
# the full picture, the same draws), sparse (bay-<column>-sp and bay-<column>-sp-dif: 660 draws, the full picture and the
# diffuse signal alone, keeping only the draws in KEEP_LIST, a list of draw numbers) and sparse-dif (bay-<column>-sp-dif
# alone). Run it with bash from the checkout's root.
set -uo pipefail
Y=$(cd "$(dirname "$0")" && pwd); kind=$1; shift
R=/private/tmp/t1-yi11/runs
declare -A build=([a]=yg [b]=yg [c]=yc [d]=yg [g]=yg [r]=yg [gr]=yg [g128r]=yg [n32]=yg [n128]=yg
                  [byi]=yi [g128ryi]=yi [w36]=yi [hyst]=yi)
declare -A knobs=([a]="TERTIUS_X_DIFFUSE_CAP=32" [b]="TERTIUS_X_DIFFUSE_CAP=128" [c]="TERTIUS_X_DIFFUSE_CAP=128"
                  [d]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1"
                  [g]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128"
                  [r]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_RANK=1"
                  [gr]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=36 TERTIUS_X_REGION_RANK=1"
                  [g128r]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1"
                  [n32]="TERTIUS_X_DIFFUSE_CAP=32 TERTIUS_X_REGION_OFF=1 TERTIUS_A_NO_CLAMP=1"
                  [n128]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_OFF=1 TERTIUS_A_NO_CLAMP=1"
                  [byi]="TERTIUS_X_DIFFUSE_CAP=128"
                  [g128ryi]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_RANK=1"
                  [w36]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=w36"
                  [hyst]="TERTIUS_X_DIFFUSE_CAP=128 TERTIUS_X_REGION_GATE=128 TERTIUS_X_REGION_CLOSED=hyst")
for col in "$@"; do
  b=${build[$col]}
  log() { [ "$b" = yg ] || [ "$b" = yi ] && echo "TERTIUS_X_REGION_LOG=$R/$1"; }
  # shellcheck disable=SC2086
  case $kind in
    film)
      KEEP_FROM=300 bash "$Y/bayrun.sh" $b bay-$col ${knobs[$col]} $(log bay-$col)
      KEEP_FROM=300 bash "$Y/bayrun.sh" $b bay-$col-dif ${knobs[$col]} TERTIUS_PROBE_SIGNAL=2 $(log bay-$col-dif) ;;
    log)
      bash "$Y/bayrun.sh" $b bay-$col-log ${knobs[$col]} TERTIUS_PROBE_SIGNAL=2 $(log bay-$col-log) ;;
    long)
      DRAWS=1660 EVERY=50 bash "$Y/bayrun.sh" $b bay-$col-long-dif ${knobs[$col]} TERTIUS_PROBE_SIGNAL=2 $(log bay-$col-long-dif) ;;
    longfull)
      DRAWS=1660 EVERY=50 bash "$Y/bayrun.sh" $b bay-$col-long ${knobs[$col]} $(log bay-$col-long) ;;
    sparse)
      KEEP_LIST=$KEEP_LIST bash "$Y/bayrun.sh" $b bay-$col-sp ${knobs[$col]} $(log bay-$col-sp)
      KEEP_LIST=$KEEP_LIST bash "$Y/bayrun.sh" $b bay-$col-sp-dif ${knobs[$col]} TERTIUS_PROBE_SIGNAL=2 $(log bay-$col-sp-dif) ;;
    sparse-dif)
      KEEP_LIST=$KEEP_LIST bash "$Y/bayrun.sh" $b bay-$col-sp-dif ${knobs[$col]} TERTIUS_PROBE_SIGNAL=2 $(log bay-$col-sp-dif) ;;
  esac
done
