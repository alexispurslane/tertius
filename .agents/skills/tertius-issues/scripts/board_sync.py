#!/usr/bin/env python3
"""Reconcile Tertius project items with the actual issue, sub-issue and pull-request state."""

import argparse
import json
import subprocess

OWNER = "alexispurslane"
REPO = "alexispurslane/tertius"
PROJECT = 5


def plan(items, pull_requests):
    """The status changes needed for the current project items."""
    by_url = {pr["url"]: pr for pr in pull_requests}
    for pr in pull_requests:
        for reference in pr.get("closingIssuesReferences", []):
            by_url[reference["url"]] = pr
    changes = []
    for item in items:
        expected = expected_status(item, by_url.get(item["url"]))
        if item.get("status") != expected:
            changes.append({"id": item["id"], "url": item["url"], "from": item.get("status"), "to": expected})
    return changes


def expected_status(item, pull_request):
    if item.get("state") == "CLOSED" or (pull_request and pull_request["state"] == "MERGED"):
        return "Done"
    if pull_request:
        if pull_request["state"] == "CLOSED":
            return "Todo"
        return "In Progress" if pull_request["isDraft"] else "In Review"
    children = item.get("children", [])
    if children:
        if all(child["state"] == "CLOSED" for child in children):
            return "Done"
        if any(child.get("status") in {"In Progress", "In Review"} for child in children):
            return "In Progress"
    if item.get("status") == "Backlog":
        return "Backlog"
    return "In Progress" if item.get("status") == "In Progress" else "Todo"


def run(*args):
    return subprocess.run(args, check=True, capture_output=True, text=True).stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Apply the changes. The default only prints them.")
    args = parser.parse_args()
    items = json.loads(run("gh", "project", "item-list", str(PROJECT), "--owner", OWNER, "--limit", "200", "--format", "json"))["items"]
    issues = json.loads(run("gh", "issue", "list", "--repo", REPO, "--state", "all", "--limit", "200", "--json", "number,state,parent,subIssues,url"))
    pull_requests = json.loads(run("gh", "pr", "list", "--repo", REPO, "--state", "all", "--limit", "200", "--json", "number,state,isDraft,url,closingIssuesReferences"))
    issues_by_number = {issue["number"]: issue for issue in issues}
    for item in items:
        content = item.get("content", {})
        item["url"] = content["url"]
        if content.get("type") == "Issue":
            issue = issues_by_number[content["number"]]
            item["state"] = issue["state"]
            children = []
            for child in issue.get("subIssues", {}).get("nodes", []):
                child_issue = issues_by_number[child["number"]]
                child_item = next((candidate for candidate in items if candidate.get("content", {}).get("number") == child["number"] and candidate.get("content", {}).get("type") == "Issue"), {})
                children.append({"state": child_issue["state"], "status": child_item.get("status")})
            item["children"] = children
    changes = plan(items, pull_requests)
    if not changes:
        print("Board is current.")
        return
    for change in changes:
        print(f'{change["url"]}: {change["from"]} -> {change["to"]}')
        if args.apply:
            run("gh", "project", "item-edit", str(PROJECT), "--owner", OWNER, "--url", change["url"], "--field", "Status", "--value", change["to"])


if __name__ == "__main__":
    main()
