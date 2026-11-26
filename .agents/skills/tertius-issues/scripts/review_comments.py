#!/usr/bin/env python3
"""Post new inline review findings, reply with evidence and resolve review threads."""

import argparse
import json
import subprocess

REPO = "alexispurslane/tertius"


def new_findings(findings, existing):
    """Findings that are not already inline comments at the same changed line."""
    seen = {(comment["path"], comment.get("line", comment.get("originalLine")), comment["body"]) for comment in existing}
    return [finding for finding in findings if (finding["path"], finding["line"], finding["body"]) not in seen]


def thread_id_for_comment(threads, database_id):
    """The native review-thread id for an inline comment's database id."""
    for thread in threads:
        if any(comment["databaseId"] == database_id for comment in thread["comments"]["nodes"]):
            return thread["id"]
    raise ValueError(f"No review thread contains comment {database_id}")


def run(*args, input_text=None):
    return subprocess.run(args, input=input_text, check=True, capture_output=True, text=True).stdout


def api_objects(*args):
    output = run(*args)
    return [json.loads(line) for line in output.splitlines() if line.strip()]


def post_findings(pr, findings_path):
    findings = json.loads(findings_path.read_text())
    existing = api_objects("gh", "api", f"repos/{REPO}/pulls/{pr}/comments", "--paginate")
    pending = new_findings(findings, existing)
    if not pending:
        print("No new findings.")
        return
    head = run("gh", "pr", "view", str(pr), "--json", "headRefOid", "--jq", ".headRefOid").strip()
    for finding in pending:
        result = run(
            "gh", "api", "-X", "POST", f"repos/{REPO}/pulls/{pr}/comments",
            "-f", f"body={finding['body']}",
            "-f", f"path={finding['path']}",
            "-F", f"line={finding['line']}",
            "-f", f"side={finding.get('side', 'RIGHT')}",
            "-f", f"commit_id={head}",
        )
        print(json.loads(result)["html_url"])


def reply_arguments(database_id, body):
    return ["-F", f"in_reply_to={database_id}", "-f", f"body={body}"]


def resolve_comment(pr, database_id, body):
    reply = run("gh", "api", "-X", "POST", f"repos/{REPO}/pulls/{pr}/comments", *reply_arguments(database_id, body))
    query = f'''query {{
      repository(owner: "alexispurslane", name: "tertius") {{
        pullRequest(number: {pr}) {{
          reviewThreads(first: 100) {{
            nodes {{ id comments(first: 100) {{ nodes {{ databaseId }} }} }}
          }}
        }}
      }}
    }}'''
    response = json.loads(run("gh", "api", "graphql", "-f", f"query={query}"))
    threads = response["data"]["repository"]["pullRequest"]["reviewThreads"]["nodes"]
    thread_id = thread_id_for_comment(threads, database_id)
    mutation = f'''mutation {{
      resolveReviewThread(input: {{threadId: "{thread_id}"}}) {{ thread {{ id isResolved }} }}
    }}'''
    resolved = json.loads(run("gh", "api", "graphql", "-f", f"query={mutation}"))
    print(json.dumps({
        "reply": json.loads(reply)["html_url"],
        "thread": resolved["data"]["resolveReviewThread"]["thread"],
    }))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    post = commands.add_parser("post", help="Post new findings from a JSON array")
    post.add_argument("pr", type=int)
    post.add_argument("findings", type=argparse.FileType("r"))
    resolve = commands.add_parser("resolve", help="Reply to and resolve one inline comment")
    resolve.add_argument("pr", type=int)
    resolve.add_argument("comment", type=int, help="The inline comment's database id")
    resolve.add_argument("body", help="The evidence to reply with")
    args = parser.parse_args()
    if args.command == "post":
        from pathlib import Path
        post_findings(args.pr, Path(args.findings.name))
    else:
        resolve_comment(args.pr, args.comment, args.body)


if __name__ == "__main__":
    main()
