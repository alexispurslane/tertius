#!/usr/bin/env python3
"""Tool code. Render the metrics report of a Tertius branch as a pull-request comment."""

import argparse
import json
from pathlib import Path


def loads(text: str) -> dict:
    """Read the first JSON object, ignoring tool logs before it."""
    return json.JSONDecoder().raw_decode(text[text.index("{"):])[0]


def markdown(report: dict, head: str, run_url: str) -> str:
    """The Markdown table for the measured branch, with every available supporting count."""
    erosion = report["erosion"]
    verbosity = report["verbosity"]
    high_cc_mass = erosion.get("highCcMass")
    high_cc_count = erosion.get("highCcFunctionCount")
    if high_cc_mass is None or high_cc_count is None:
        erosion_evidence = "High CC mass unavailable; function counts unavailable"
    else:
        erosion_evidence = (
            f"High CC mass {high_cc_mass:.1f} of {erosion['totalMass']:.1f}, "
            f"from {high_cc_count} of {erosion['functionCount']} functions above CC {erosion['complexityLimit']}"
        )
    verbose_lines = verbosity.get("verboseLines")
    verbosity_evidence = (
        f"{verbose_lines} of {verbosity['sloc']} lines: {verbosity['flaggedLines']} flagged, {verbosity['cloneLines']} cloned"
        if verbose_lines is not None
        else "Line counts unavailable"
    )
    lines = [
        "<!-- tertius-metrics -->",
        "## Branch metrics",
        "",
        f"CPU Checked and Python tools tests passed for `{head}`. [View the CI run]({run_url}).",
        "",
        "| Metric | Value | Evidence |",
        "|---|---:|---|",
        f"| Erosion | {erosion['erosion']:.3f} | {erosion_evidence} |",
        f"| Verbosity | {verbosity['verbosity']:.3f} | {verbosity_evidence} |",
    ]
    metrics = report.get("classMetrics")
    if metrics is None or metrics.get("summary") is None:
        lines.extend([
            "| TCC | Unavailable | No class metrics in this report |",
            "| Depth | Unavailable | No class metrics in this report |",
        ])
    else:
        summary = metrics["summary"]
        tcc = summary["tcc"]
        depth = summary["depth"]
        cohesion = summary["cohesionPopulation"]
        depth_population = summary["depthPopulation"]
        provenance = summary["provenance"]
        tcc_text = (
            "incomplete"
            if cohesion["included"] == 0 and cohesion["incomplete"] > 0
            else f"{tcc['ratio']:.4f}" if tcc.get("ratio") is not None else "n/a"
        )
        depth_text = f"{depth['ratio']:.4f}" if depth.get("ratio") is not None else "n/a"
        lines.extend([
            f"| TCC | {tcc_text} | {tcc['numerator']}/{tcc['denominator']} shared/possible method pairs |",
            f"| Depth | {depth_text} | {depth['numerator']}/{depth['denominator']} WMC/API entries |",
        ])
    lines.append(f"| SLOC | {erosion['sloc']} | {erosion.get('fileCount', 'Unknown number of')} files |")
    if metrics is not None and metrics.get("summary") is not None:
        summary = metrics["summary"]
        cohesion = summary["cohesionPopulation"]
        depth_population = summary["depthPopulation"]
        provenance = summary["provenance"]
        lines.extend([
            f"| Cohesion population | {cohesion['included']} included, {cohesion['notApplicable']} not applicable, {cohesion['incomplete']} incomplete | Type counts |",
            f"| Depth population | {depth_population['included']} included, {depth_population['notApplicable']} not applicable, {depth_population['incomplete']} incomplete | Type counts |",
            f"| Binding errors | {summary['bindingErrorCount']} | Unresolved compiler bindings in source analysis |",
            f"| Unresolved state access | {summary['unresolvedStateAccessCount']} | Locations with unresolved state access |",
            f"| Types measured | {summary['typeCount']} | Types in source analysis |",
            f"| Metric version | {provenance['version']} | Counting rules |",
            f"| References | {provenance['references']} | Metadata references |",
        ])
    lines.extend(["", "Local test runs are optional feedback. This comment and the required check belong to the branch head above."])
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report", type=Path, help="The JSON written by Tertius.Slop all")
    parser.add_argument("--head", required=True, help="The full commit of the measured branch")
    parser.add_argument("--run-url", required=True, help="The URL of this GitHub Actions run")
    parser.add_argument("--output", type=Path, help="Write the comment here instead of stdout")
    args = parser.parse_args()
    text = markdown(loads(args.report.read_text()), args.head, args.run_url)
    if args.output:
        args.output.write_text(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
