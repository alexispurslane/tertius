#!/usr/bin/env bash
# Tool code of t0-5tyj. queue-mac.sh: the Mac's takes in order, into /private/tmp/t0-5tyj/runs, on the builds of
# builds.sh. Each run is one group of cases of one part on one side. M runs before X in each group, so that each pair
# runs close in time on one machine. The survey takes the shared GPU lock for each render.
T=$(cd "$(dirname "$0")" && pwd)
export BASE=/private/tmp/t0-5tyj
X=$BASE/x/probe-bin; M=$BASE/m/probe-bin
A="TERTIUS_A_NO_CLAMP=1"
take() { bash "$T/take.sh" "$@"; }
pair() { local name=$1; shift; PROBE=$M take "$name-M" "$@"; PROBE=$X take "$name-X" "$@"; }
# 1. The film rect-bay: X against the cited numbers of t0-acxm, and M for the sheet.
pair f-bay motion rect-bay
# 2. The stills, the survey's whole still part, in six groups of 15.
G=(
airlock-door,airlock-hull,bounce-raised-box,bounce-red-wall,cabin-light-cabin,cabin-light-canopy,deep-yard,docking-bay,docking-cabin,deck-cuts,exit-cabin,exit-deck,exit-chute,exit-floor,exit-stack
launch-pad,launch-climb,meshes,shuttle,surface-terminal,surface-apron,surface-cabin,surface-overview,doorway-inside,doorway-outside,drop-box,earth-station,earth-pad,earth-far,hair-bent-cards
hair-rugs,lamp-hall-16-hair,lamp-hall-4,lamp-hall-4-spot,lamp-hall-4-moving,lamp-hall-4-spot-moving,lamp-hall-16,lamp-hall-16-spot,lamp-hall-16-moving,lamp-hall-16-spot-moving,lamp-hall-64,lamp-hall-64-spot,lamp-hall-64-moving,lamp-hall-64-spot-moving,mars-day
mars-dusk,mars-edge,mirror-balls,mirror-plate,orbit-cluster,orbit-umbra,orbit-chase,orbit-far,pbr-spheres,pillar-room,puff-yard,rect-bay,rect-lamps,rect-lights,reflections-front
reflections-low,reflections-edge,rigs-crowd,rigs,skin-head,skin-hair-fringed,skin-hair,spacewalk,spin-standing,spin-ring,spot-box,stars-1,stars-2,stars-4,station-watch
station-cabin,yard-ahead,yard-end,box-cabin,floor,pbr-front,terminal,throw,walk,push-side,push-astronaut,windows-earth,windows-cabin,danube-hold,bay-sunbeam
)
for i in "${!G[@]}"; do pair "s$((i + 1))" still "${G[$i]}"; done
# The reference A (X with both clamps off) for the daub and the light share of the three light stills, and the same
# three stills at the fixed exposure of the light share on all sides: 4.08 stops for rect-lamps, -4.0 for the bay's.
PROBE=$X take s-A still rect-lamps,bay-sunbeam,docking-bay $A
for side in M X A; do
  probe=$X; [ $side = M ] && probe=$M; env=""; [ $side = A ] && env=$A
  PROBE=$probe take "e-lamps-$side" still rect-lamps TERTIUS_PROBE_EXPOSURE=4.08 $env
  PROBE=$probe take "e-bay-$side" still bay-sunbeam,docking-bay TERTIUS_PROBE_EXPOSURE=-4.0 $env
done
# 3. The switches, 4. the lag films.
pair w switch bay-sun-off,lamp-hall-16
pair l lag lamp-hall-16-moving,lamp-hall-64-spot-moving
# 2. The other 17 motion films, in groups.
pair f1 motion lamp-hall-16-moving,lamp-hall-4-spot-moving,lamp-hall-64-moving,rect-lamps,rect-lights,reflections
pair f2 motion rigs-crowd,skin-head,hair-bent-cards,pillar-room,spot-box,exit-cabin
pair f3 motion exit-deck,exit-floor,orbit-umbra,docking-landing,bay-sunbeam
# 5. The walk and the climb.
pair k walk,climb cabin-walk,cabin-climb
echo "$(date +%T) queue-mac done"
