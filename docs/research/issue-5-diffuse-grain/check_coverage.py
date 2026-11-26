"""Line coverage of the scientific measures, using the standard library tracer."""
import json
from pathlib import Path
import sys
import trace
import unittest

HERE = Path(__file__).resolve().parent


def run_tests():
    suite = unittest.defaultTestLoader.discover(str(HERE), pattern='test_*.py')
    return unittest.TextTestRunner(stream=sys.stderr).run(suite)


def main():
    tracer = trace.Trace(count=True, trace=False)
    outcome = tracer.runfunc(run_tests)
    reports = []
    for filename in ['measure.py', 'spread_model.py']:
        source = HERE / filename
        executable = {line for line in trace._find_executable_linenos(str(source)) if isinstance(line, int) and line > 0}
        executed = {line for (path, line), count in tracer.results().counts.items()
                    if Path(path).resolve() == source and count}
        reports.append(dict(file=filename, executable_lines=len(executable),
            covered_lines=len(executable & executed), missed_lines=sorted(executable - executed),
            branch_coverage='Not measured by the standard-library tracer.'))
    print(json.dumps(reports, indent=2))
    return int(not outcome.wasSuccessful())


if __name__ == '__main__':
    raise SystemExit(main())
