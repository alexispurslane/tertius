#!/usr/bin/env python3
"""The planning logic of the Tertius project-management scripts."""

import importlib.util
import unittest
from pathlib import Path

ROOT = Path(__file__).parents[1]
SCRIPTS = ROOT / ".agents" / "skills" / "tertius-issues" / "scripts"


def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


board_sync = load("board_sync")
review_comments = load("review_comments")


def issue(number, status="Todo", state="OPEN", children=None):
    return {
        "id": f"issue-{number}",
        "url": f"https://github.com/alexispurslane/tertius/issues/{number}",
        "status": status,
        "type": "Issue",
        "number": number,
        "state": state,
        "children": children or [],
    }


class BoardSyncTests(unittest.TestCase):
    def test_a_draft_pull_request_moves_its_issue_and_pr_to_in_progress(self):
        items = [issue(9, status="Todo"), issue(10, status="In Review", state="OPEN")]
        items[1]["type"] = "PullRequest"
        items[1]["url"] = "https://github.com/alexispurslane/tertius/pull/10"
        prs = [{"number": 10, "state": "OPEN", "isDraft": True, "url": items[1]["url"], "closingIssuesReferences": [{"number": 9, "url": items[0]["url"]}]}]

        changes = board_sync.plan(items, prs)

        self.assertEqual([(change["url"], change["to"]) for change in changes], [
            (items[0]["url"], "In Progress"),
            (items[1]["url"], "In Progress"),
        ])

    def test_a_ready_pull_request_moves_to_in_review(self):
        items = [issue(9), issue(10, state="OPEN")]
        items[1]["type"] = "PullRequest"
        items[1]["url"] = "https://github.com/alexispurslane/tertius/pull/10"
        prs = [{"number": 10, "state": "OPEN", "isDraft": False, "url": items[1]["url"], "closingIssuesReferences": [{"number": 9, "url": items[0]["url"]}]}]

        changes = board_sync.plan(items, prs)

        self.assertEqual([(change["url"], change["to"]) for change in changes], [
            (items[0]["url"], "In Review"),
            (items[1]["url"], "In Review"),
        ])

    def test_an_issue_without_an_active_pull_request_stays_todo_or_backlog(self):
        items = [issue(1, status="Todo"), issue(2, status="Backlog")]

        self.assertEqual(board_sync.plan(items, []), [])

    def test_an_epic_follows_its_native_sub_issues(self):
        parent = issue(1, status="Todo", children=[2, 3])
        child_active = issue(2, status="In Progress")
        child_done = issue(3, status="Done", state="CLOSED")
        parent["children"] = [child_active, child_done]

        changes = board_sync.plan([parent, child_active, child_done], [])

        self.assertEqual([(change["url"], change["to"]) for change in changes], [(parent["url"], "In Progress")])
        child_active["state"] = "CLOSED"
        child_active["status"] = "Done"
        self.assertEqual([(change["url"], change["to"]) for change in board_sync.plan([parent, child_active, child_done], [])], [
            (parent["url"], "Done")
        ])

    def test_a_merged_pull_request_closes_the_issue_and_marks_done(self):
        items = [issue(9, status="In Review", state="CLOSED"), issue(10, status="In Review", state="MERGED")]
        items[1]["type"] = "PullRequest"
        items[1]["url"] = "https://github.com/alexispurslane/tertius/pull/10"
        prs = [{"number": 10, "state": "MERGED", "isDraft": False, "url": items[1]["url"], "closingIssuesReferences": [{"number": 9, "url": items[0]["url"]}]}]

        changes = board_sync.plan(items, prs)

        self.assertEqual([(change["url"], change["to"]) for change in changes], [
            (items[0]["url"], "Done"),
            (items[1]["url"], "Done"),
        ])


class ReviewCommentTests(unittest.TestCase):
    def test_only_new_findings_are_posted(self):
        findings = [{"path": "a.cs", "line": 4, "body": "Fix this"}]
        existing = [{"path": "a.cs", "line": 4, "body": "Fix this", "originalLine": 4}]

        self.assertEqual(review_comments.new_findings(findings, existing), [])
        self.assertEqual(review_comments.new_findings(findings, []), findings)

    def test_the_reply_uses_the_github_in_reply_to_parameter(self):
        self.assertEqual(review_comments.reply_arguments(100, "done"), ["-F", "in_reply_to=100", "-f", "body=done"])

    def test_the_review_thread_is_found_by_its_comment_database_id(self):
        threads = [
            {"id": "thread-1", "comments": {"nodes": [{"databaseId": 100}]}}
        ]

        self.assertEqual(review_comments.thread_id_for_comment(threads, 100), "thread-1")
        with self.assertRaisesRegex(ValueError, "comment 999"):
            review_comments.thread_id_for_comment(threads, 999)


if __name__ == "__main__":
    unittest.main()
