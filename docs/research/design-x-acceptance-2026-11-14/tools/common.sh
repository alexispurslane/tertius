# Cloud jobs of t0-5tyj: tree x is the fixed candidate (scratch commit), tree m is main. The survey tool is x's.
C=$(cat /private/tmp/t7-3e22/x2/candidate.sha)
B='set -o pipefail; cd @T@ && patch -p1 --forward -i tools/renderer-survey/probe.patch > /dev/null && dotnet build src/Tertius.Demo -c Release -nologo -v:q -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o ../out-@T@/probe-bin | tail -3 && echo built @T@ $(( $(date +%s) - $(cat ../t0) ))'
build() { echo "$B" | sed "s/@T@/$1/g"; }
# survey <out> <parts> <cases> [env]: a survey run of tree x's tool into out-<out>, with the probe of the tree before the first dash.
survey() { local tree=${1%%-*}; echo "mkdir -p out-$1 && { [ -e out-$1/probe-bin ] || ln -sfn ../out-$tree/probe-bin out-$1/probe-bin; } && cd x && $4 TERTIUS_SURVEY_HOLDS_LOCK=1 python3 -I tools/renderer-survey.py run ../out-$1 --parts $2 --only $3 --no-build 2>&1 | grep -v '^  ' | cut -c1-400 | tail -6; echo done $1 $2 \$(( \$(date +%s) - \$(cat ../t0) ))"; }
START="date +%s > t0; nproc"
submit() { local name=$1 timeout=$2; shift 2; local args=(); for c in "$@"; do args+=(--run "$c"); done
  cd /Users/alexispurslane/.t3/worktrees/tertius/t3-09c31e02 && python3 -I tools/runpod/gpu_job.py submit --agent t0-5tyj \
    --tree x=$C --tree m=main "${args[@]}" \
    --output 'out-*/results/*/*.json' --output 'out-*/census/*/*/census.txt' --output 'out-*/census/*/*/header.json' \
    --timeout "$timeout" --out "/private/tmp/t7-3e22/x2/cloud/$name" > "/private/tmp/t7-3e22/x2/cloud/$name.log" 2>&1; echo "exit $? $name"; tail -2 "/private/tmp/t7-3e22/x2/cloud/$name.log" | cut -c1-300; }
