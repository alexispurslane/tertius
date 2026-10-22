#!/usr/bin/env bash
# Reports duplicated C# code with jscpd: exact copies, and with --similarity, methods with the same structure.
# The report never fails a build. See tools/README.md.
set -euo pipefail

usage() {
    cat >&2 <<'EOF'
Usage: tools/duplicates.sh [-t] [-r] [-s] [-o <folder>] [-- <jscpd args>]
  -t           Scan tests/ and benchmarks/ too. The default is src/ only.
  -r           Also report renamed copies: blocks that differ only in names and literals.
  -s           Also report near-miss methods: methods whose syntax trees are at least 80 percent alike.
  -o <folder>  Also write an HTML report to <folder>/jscpd-report.html.
  -- <args>    Pass the rest to jscpd unchanged, for example -- --min-tokens 50.
EOF
    exit 2
}

paths=(src)
extra=()
out=""
while getopts "trso:h" opt; do
    case "$opt" in
        t) paths+=(tests benchmarks) ;;
        r) extra+=(--ignore-identifiers --ignore-literals) ;;
        s) extra+=(--similarity) ;;
        o) out=$OPTARG ;;
        *) usage ;;
    esac
done
shift $((OPTIND - 1))
[[ "${1:-}" == "--" ]] && shift

if ! command -v jscpd > /dev/null; then
    echo "tools/duplicates.sh: jscpd is not on the PATH. Run the script in the dev shell: nix develop --command bash tools/duplicates.sh" >&2
    exit 2
fi

cd "$(git rev-parse --show-toplevel)"

reporters=console
if [[ -n "$out" ]]; then
    reporters=console,html
    extra+=(--output "$out")
fi

# 70 tokens is about seven lines of C#. Mild mode compares every token except whitespace and new lines.
jscpd "${paths[@]}" --format csharp --min-tokens 70 --mode mild --reporters "$reporters" "${extra[@]}" "$@"
