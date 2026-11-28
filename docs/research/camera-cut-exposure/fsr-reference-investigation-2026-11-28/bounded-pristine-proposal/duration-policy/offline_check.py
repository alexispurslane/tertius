"""Reproduce duration arithmetic from canonical code. Never submit or contact a service."""

import argparse
import contextlib
import datetime
import difflib
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import runpy
import sys
import tempfile
from unittest.mock import patch


def forbid_external_effects(event, args):
    if event.startswith("socket.") or event in {"subprocess.Popen", "os.system", "os.posix_spawn"}:
        raise RuntimeError("Offline proof forbids network and child processes: " + event)


sys.addaudithook(forbid_external_effects)
sys.dont_write_bytecode = True
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--repo", type=Path, required=True)
parser.add_argument("--proposed-config", type=Path, required=True)
args = parser.parse_args()
source = args.repo / "tools/runpod/gpu_job.py"
spec = importlib.util.spec_from_file_location("gpu_job", source)
gpu_job = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gpu_job
spec.loader.exec_module(gpu_job)
current = gpu_job.Config.load(args.repo / "tools/runpod/endpoint.json")
proposed = gpu_job.Config.load(args.proposed_config)
current_bytes = (args.repo / "tools/runpod/endpoint.json").read_bytes()
proposed_bytes = args.proposed_config.read_bytes()
expected = current_bytes.replace(b'"job_seconds": 840,', b'"job_seconds": 1920,').replace(
    b'"endpoint_timeout_seconds": 900', b'"endpoint_timeout_seconds": 1980')
assert expected == proposed_bytes
diff = "".join(difflib.unified_diff(current_bytes.decode().splitlines(keepends=True),
    proposed_bytes.decode().splitlines(keepends=True), fromfile="a/tools/runpod/endpoint.json",
    tofile="b/tools/runpod/endpoint.json"))
assert diff == Path(__file__).with_name("endpoint-duration-only.patch").read_text()
assert current.kinds["cpu"] == proposed.kinds["cpu"]
assert current.daily_caps_usd == proposed.daily_caps_usd
assert current.total_cap_usd == proposed.total_cap_usd == 30
assert current.kinds["gpu"].workers == proposed.kinds["gpu"].workers == 5
assert current.kinds["gpu"].rate_per_second == proposed.kinds["gpu"].rate_per_second == .00016
day = datetime.date(2026, 11, 28)
assert current.daily_cap(day) == proposed.daily_cap(day) == 5
rows = []
for name, kind in [("checked-in", current.kinds["gpu"]), ("external-proposal", proposed.kinds["gpu"]),
        ("only-command-limit-raised", gpu_job.Kind(.00016, 5, 1920, 900)),
        ("only-execution-limit-raised", gpu_job.Kind(.00016, 5, 840, 1980))]:
    seconds = min(1920, kind.job_seconds)  # The exact clamp in canonical submit, without calling submit.
    execution = gpu_job.execution_seconds(kind, seconds)
    cost = gpu_job.cost_usd(kind, execution)
    assert cost == gpu_job.worst_case_usd(kind, 1920)
    rows.append({"config": name, "requested_command_seconds": 1920, "command_seconds": seconds,
        "execution_seconds": execution, "canonical_cost_usd": cost,
        "handler_margin_seconds": execution - seconds})
assert [(row["command_seconds"], row["execution_seconds"]) for row in rows] == [
    (840, 900), (1920, 1980), (1920, 900), (840, 900)]
assert abs(rows[0]["canonical_cost_usd"] - .1552) < 1e-12
assert abs(rows[1]["canonical_cost_usd"] - .328) < 1e-12

# Check CLI rejection before key lookup, config loading or any submission.
errors = io.StringIO()
with contextlib.redirect_stderr(errors), patch.object(gpu_job, "key_file", side_effect=AssertionError("key read")):
    try:
        gpu_job.main(["submit", "--agent", "offline-proof", "--tree", "before=HEAD", "--out", "/unused",
            "--config", str(args.proposed_config)])
        raise AssertionError("CLI unexpectedly accepted --config")
    except SystemExit as error:
        assert error.code == 2
assert "unrecognized arguments: --config" in errors.getvalue()


class ExistingEndpoint:
    """In-memory endpoint list. All service mutations fail this proof."""

    def __init__(self):
        self.endpoint = {"id": "offline-existing", "name": "tertius-gpu-" + "a" * 12 + "-1234",
            "image": proposed.image_repository + ":" + "a" * 40,
            "workers": {"max": 5}, "timeout": 900000}

    def endpoints(self):
        return [self.endpoint]

    def workers(self, endpoint):
        return []

    def __getattr__(self, name):
        raise AssertionError("Unexpected API method: " + name)


with tempfile.TemporaryDirectory(prefix="issue4-offline-duration-") as temporary:
    root = Path(temporary)
    api = ExistingEndpoint()
    assert gpu_job.route(proposed, api, root, ["a" * 40], "gpu") == ("offline-existing", "a" * 40)
    assert api.endpoint["timeout"] == 900000
    creation = gpu_job.endpoint_request(proposed, "gpu", "a" * 40)
    assert creation["timeout"] == 1980000
    handler = gpu_job.handler
    assert handler.Limits().seconds == 7140
    # Empty trees avoid fetching. Replace the command runner so no process starts.
    observed = []
    def observe_commands(commands, work, shell, seconds, steps, log):
        observed.append(seconds)
        return "ok"
    with patch.object(handler, "run_commands", observe_commands):
        result = handler.handle({"protocol": handler.PROTOCOL, "repository": "unused", "trees": {},
            "commands": [], "outputs": [], "seconds": 1920}, baked=root, work=root / "work", shell=[])
    assert result["status"] == "ok" and observed == [1920]
    proposal = Path(__file__).resolve().parent.parent
    with contextlib.chdir(root), patch.object(sys, "argv", ["worker.py", "--config", str(proposal / "config.json")]):
        try:
            runpy.run_path(str(proposal / "worker.py"), run_name="__main__")
            raise AssertionError("Worker unexpectedly passed the inert gate")
        except RuntimeError as error:
            assert str(error) == "INERT: new Issue #4 paid approval and coordinator review are required before worker execution."
    assert not (root / "linux-out").exists()

print(json.dumps({"status": "PASS", "network_and_child_processes": "forbidden by audit hook",
    "canonical_submit_called": False, "real_api_created": False, "budget_admission_attempted": False,
    "canonical_source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "handler_source_sha256": hashlib.sha256(source.with_name("handler.py").read_bytes()).hexdigest(),
    "worker_source_sha256": hashlib.sha256(source.with_name("worker.py").read_bytes()).hexdigest(),
    "checked_in_config_sha256": hashlib.sha256(current_bytes).hexdigest(),
    "external_proposed_config": str(args.proposed_config),
    "external_proposed_config_sha256": hashlib.sha256(proposed_bytes).hexdigest(),
    "exact_diff_sha256": hashlib.sha256(diff.encode()).hexdigest(),
    "cases": rows, "cli_config_override": "rejected before key lookup",
    "python_config_injection": "Config.load(path) and submit(config=...) exist; no isolated CLI or endpoint selector",
    "fake_existing_endpoint_timeout_ms_after_reuse": api.endpoint["timeout"],
    "proposed_new_endpoint_timeout_ms": creation["timeout"],
    "handler_command_ceiling_seconds": handler.Limits().seconds, "observed_handler_seconds": observed,
    "proposal_worker_refused_before_output": True,
    "unchanged_daily_cap_usd_on_2026_11_28": proposed.daily_cap(day), "unchanged_total_cap_usd": proposed.total_cap_usd,
    "unchanged_gpu_workers": proposed.kinds["gpu"].workers, "unchanged_cpu": True,
    "queue_and_execution_ttl_seconds": gpu_job.QUEUE_SECONDS,
    "latest_start_seconds_after_submission_for_full_proposed_execution": gpu_job.QUEUE_SECONDS - 1980,
    "compressed_row_gate_bytes": 6500000, "that_gate_in_base64_bytes": 4 * ((6500000 + 2) // 3),
    "handler_total_base64_output_limit_bytes": handler.Limits().output_bytes,
    "arithmetic_is_not_a_live_endpoint_test": True}, indent=2))
