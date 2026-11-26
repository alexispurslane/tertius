#!/usr/bin/env python3
"""Tool code. Render the metrics note of a Tertius branch as a pull-request comment."""

import argparse
import json
from pathlib import Path


def markdown(note: dict, head: str, run_url: str) -> str:
    """The Markdown table for the measured branch, with every available supporting count."""
    lines = [
        "<!-- tertius-metrics -->",
        f"## Branch metrics",
        "",
        f"Tests and metrics passed for `{head}`. [View the CI run]({run_url}).",
        "",
        "| Metric | Value | Evidence |",
        "|---|---:|---|",
        _ratio_row("Erosion", note.get("erosion"), "Ratio of high CC function mass to all function mass"),
        _absolute_row("High CC mass", note.get("highCcMass"), "CC above 10", lambda value: f"{value:.2f}"),
        _absolute_row("High CC functions", note.get("highCcFunctionCount"), "Functions with CC above 10", str),
        _ratio_row("Verbosity", note.get("verbosity"), "Rule-flagged or cloned lines divided by SLOC"),
        _absolute_row("Verbose lines", note.get("verboseLines"), "Distinct flagged or cloned lines", str),
        _absolute_row("SLOC", note.get("sloc"), "Source lines of code", str),
    ]
    metrics = note.get("classMetrics")
    if metrics is None:
        lines.extend([
            "| TCC | Unavailable | No class metrics in this note |",
            "| Depth | Unavailable | No class metrics in this note |",
        ])
    else:
        tcc = metrics["tcc"]
        depth = metrics["depth"]
        cohesion = metrics["cohesionPopulation"]
        depth_population = metrics["depthPopulation"]
        provenance = metrics["provenance"]
        lines.extend([
            f"| TCC | {tcc['ratio']:.4f} | {tcc['numerator']}/{tcc['denominator']} shared/possible method pairs |",
            f"| Depth | {depth['ratio']:.4f} | {depth['numerator']}/{depth['denominator']} WMC/API entries |",
            f"| Cohesion population | {cohesion['included']} included, {cohesion['notApplicable']} not applicable, {cohesion['incomplete']} incomplete | Type counts |",
            f"| Depth population | {depth_population['included']} included, {depth_population['notApplicable']} not applicable, {depth_population['incomplete']} incomplete | Type counts |",
            f"| Binding errors | {metrics['bindingErrorCount']} | Unresolved compiler bindings in source analysis |",
            f"| Unresolved state access | {metrics['unresolvedStateAccessCount']} | Locations with unresolved state access |",
            f"| Types measured | {metrics['typeCount']} | Types in source analysis |",
            f"| Metric version | {provenance['version']} | Counting rules |",
            f"| References | {provenance['references']} | Metadata references |",
        ])
    lines.extend(["", "Local test runs are optional feedback. This comment and the required check belong to the branch head above."])
    return "\n".join(lines) + "\n"


def _ratio_row(name: str, value, evidence: str) -> str:
    return _absolute_row(name, value, evidence, lambda number: f"{number:.4f}")


def _absolute_row(name: str, value, evidence: str, show) -> str:
    return f"| {name} | {show(value) if value is not None else 'Unavailable'} | {evidence if value is not None else 'No absolute count in this note'} |"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("note", type=Path, help="The JSON written by Tertius.Slop measure")
    parser.add_argument("--head", required=True, help="The full commit of the measured branch")
    parser.add_argument("--run-url", required=True, help="The URL of this GitHub Actions run")
    parser.add_argument("--output", type=Path, help="Write the comment here instead of stdout")
    args = parser.parse_args()
    text = markdown(json.loads(args.note.read_text()), args.head, args.run_url)
    if args.output:
        args.output.write_text(text)
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
