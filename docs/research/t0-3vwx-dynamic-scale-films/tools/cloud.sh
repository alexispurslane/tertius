#!/usr/bin/env bash
# Tool code of t0-5tyj, copied from 726bf338c into t0-3vwx's record with these changes: the trees are x = X5 (the
# commit 4851ba7db that carries the line's merge tree e232080f) and m = main ac5f23915, the outputs land in
# /private/tmp/t0-gbgj/cloud5, the agent is t0-3vwx, and the job's files come from this record's folder.
# The original: cloud.sh shard <i> | sizes | video | regions | switch: the cloud jobs of take T18 (Linux), through tools/runpod/gpu_job.py
# only, which enforces the spending caps. Outputs land in /private/tmp/t0-5tyj/cloud/<job>, the log beside it.
#   shard <i>: shard i of 6 of the Checked Rendering tests on the line's head (tools/runpod/WORKBENCH.md, "Tests in the
#     cloud"). The GPU tests turn the validation layer on themselves, and the dev shell carries the layer.
#   sizes <targets...>: the size logs of the walk and the switch on M and X at each frame time target, from runs without
#     pictures (cloudvideo.sh sizes), in one job with nothing else in it.
#   video <walk|switch> <m|x> <target ms> [survey]: one side's video of the scene, its render scale played back from the
#     sizes job's log (cloudvideo.sh video), with its region means and, with "survey", the survey's settle take.
#   regions <walk|switch> <targets...>: X's region log of the scene at each target, its sizes played back from the sizes
#     job's log, without pictures (cloudvideo.sh regions).
#   switch: the survey's switch bay-sun-off on M (main c4c1f1f2f) and on X (the line's head), each a probe build of its
#     own tree with its own probe patch, both run by the head's survey tool, in one job.
set -uo pipefail
R=$(cd "$(dirname "$0")/../../../.." && pwd)
C=${C:-/private/tmp/t0-gbgj/cloud5}; mkdir -p $C
X=${X:-4851ba7db147563e5b8a94856710dd5c37efe2d6}
# t0-3vwx: M names main's side (default ac5f23915) and C the output folder (default cloud5), for the final take.
# GPU_JOB=<path> runs another checkout's gpu_job.py (and so its endpoint.json's caps), such as main's.
M=${M:-ac5f23915}
submit() { local name=$1; shift
  (cd "$R" && python3 -I "${GPU_JOB:-tools/runpod/gpu_job.py}" submit --agent t0-3vwx "$@" --out "$C/$name") > "$C/$name.log" 2>&1
  echo "$(date +%T) exit $? $name: $(tail -1 "$C/$name.log" | cut -c1-200)"; }
case $1 in
shard)
  i=$2
  submit "shard$i" --tree x=$X \
    --run "cd x && dotnet build tests/Tertius.Rendering.Tests -c Checked -nologo -v:q" \
    --run "mkdir -p out; cd x && dll=tests/Tertius.Rendering.Tests/bin/Checked/net11.0/Tertius.Rendering.Tests.dll && dotnet \$dll -maxThreads 2 -longRunning 60 -noColor -result-ctrf ../out/shard.json \$(python3 -I tools/runpod/shards.py \$dll 6 $i)" \
    --output out/shard.json --timeout 900 ;;
sizes|video|regions|times)
  # The worker checks out no docs/ (tools/runpod/handler.py, SPARSE), so the first command unpacks the job's scripts, the
  # scratch patches and the sizes job's logs (sizes/, for the video jobs) from this folder into the tree x, as a base64
  # tar of the working files.
  D=docs/research/t0-3vwx-dynamic-scale-films
  stage=$(mktemp -d); mkdir -p $stage/$D/tools
  cp "$R/$D"/scratch-*.patch $stage/$D/; cp "$R/$D"/tools/cloudvideo.* $stage/$D/tools/
  # t0-3vwx, final take: a probe patch fitted to X (probe-<first 9 digits of X>.patch) goes along as probe-x.patch.
  [ -f "$R/$D/probe-${X:0:9}.patch" ] && cp "$R/$D/probe-${X:0:9}.patch" $stage/$D/probe-x.patch
  [ -d $C/sizes/out/sizes ] && cp -r $C/sizes/out/sizes $stage/$D/
  pack=$(COPYFILE_DISABLE=1 tar -czf - -C $stage docs | base64 | tr -d '\n'); rm -rf $stage
  if [ $1 = times ]; then
    # t0-3vwx: the time and per-pass logs (cloudvideo.sh times).
    shift
    submit times --tree x=$X --tree m=$M --run "echo $pack | base64 -d | tar -xz -C x" \
      --run "bash x/$D/tools/cloudvideo.sh times $*" --output 'out/times/*/*' --timeout 600
  elif [ $1 = sizes ]; then
    shift
    submit sizes --tree x=$X --tree m=$M --run "echo $pack | base64 -d | tar -xz -C x" \
      --run "bash x/$D/tools/cloudvideo.sh sizes $*" --output 'out/sizes/*/*.txt' --timeout 830
  elif [ $1 = regions ]; then
    submit "regions-$2" --tree x=$X --tree m=$M --run "echo $pack | base64 -d | tar -xz -C x" \
      --run "bash x/$D/tools/cloudvideo.sh $*" --output 'out/*-replay/*.txt' --timeout 600
  else
    shift; name="video-$1-$2-$3"
    submit "$name" --tree x=$X --tree m=$M --run "echo $pack | base64 -d | tar -xz -C x" \
      --run "bash x/$D/tools/cloudvideo.sh video $*" \
      --output 'out/*.mp4' --output 'out/*-means.json' --output 'out/*-replay/*.txt' --output 'out/*-survey/*.txt' \
      --output 'out-*/results/*/*.json' --output 'out/*-run.log' --timeout 830
  fi ;;
switch)
  B='cd @T@ && patch -p1 --forward -i tools/renderer-survey/probe.patch > /dev/null && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o ../out-@T@/probe-bin | tail -2'
  S='mkdir -p out-@T@ && cd x && TERTIUS_SURVEY_HOLDS_LOCK=1 python3 -I tools/renderer-survey.py run ../out-@T@ --parts switch --only bay-sun-off --no-build 2>&1 | grep -v "^  " | tail -3'
  submit switch --tree x=$X --tree m=$M \
    --run "${B//@T@/m}" --run "${B//@T@/x}" --run "${S//@T@/m}" --run "${S//@T@/x}" \
    --output 'out-*/results/*/*.json' --timeout 1500 ;;
esac
