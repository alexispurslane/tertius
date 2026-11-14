#!/usr/bin/env python3
"""Tool code. The shards of a test assembly for cloud GPU workers (tools/runpod/shards.py): every test class that the
built assembly lists, nested ones included, runs in exactly one shard (ticket ter-eugk).

    python3 -I tools/runpod/test_shards.py

The standard library only.
"""

import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("shards", Path(__file__).with_name("shards.py"))
shards = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shards)

# The start of `Tertius.Rendering.Tests.dll -list classes -noColor` on 2026-11-14: the runner's banner, then one class
# per line, a nested class as Outer+Inner.
LISTING = """xUnit.net v3 In-Process Runner v4.0.2+0c7aa4991f (64-bit .NET 11.0.0-rc.1.26425.128)
Tertius.Rendering.Tests.AccumulatorRenderTests
Tertius.Rendering.Tests.AccumulatorRenderTests+NeighborLimiterPrototype
Tertius.Rendering.Tests.RectLightTests+RoughFloorEnergy
Tertius.Rendering.Tests.TubeLightRenderTests
"""


class Listing(unittest.TestCase):
    def test_the_listing_keeps_nested_classes_and_drops_the_runners_banner(self):
        self.assertEqual(shards.classes_of(LISTING), [
            "Tertius.Rendering.Tests.AccumulatorRenderTests",
            "Tertius.Rendering.Tests.AccumulatorRenderTests+NeighborLimiterPrototype",
            "Tertius.Rendering.Tests.RectLightTests+RoughFloorEnergy",
            "Tertius.Rendering.Tests.TubeLightRenderTests",
        ])


class Split(unittest.TestCase):
    CLASSES = [f"T.C{k}" for k in range(11)] + ["T.C3+Inner", "T.C7+Inner"]

    def test_every_listed_class_nested_ones_included_lands_in_exactly_one_shard(self):
        for count in range(1, 8):
            parts = [shards.shard(self.CLASSES, count, index) for index in range(count)]
            placed = [name for part in parts for name in part]
            self.assertEqual(sorted(placed), sorted(self.CLASSES), f"{count} shards")

    def test_a_shard_is_the_same_whatever_order_the_runner_lists_the_classes_in(self):
        self.assertEqual(shards.shard(self.CLASSES, 3, 1), shards.shard(list(reversed(self.CLASSES)), 3, 1))

    def test_a_class_in_no_shard_is_reported(self):
        parts = [["T.C1", "T.C2"], ["T.C3"]]
        self.assertEqual(shards.uncovered(["T.C1", "T.C2", "T.C3", "T.C3+Inner"], parts), ["T.C3+Inner"])

    def test_a_shard_gives_the_runners_class_filter_of_each_of_its_classes(self):
        self.assertEqual(shards.filters(["T.A", "T.A+B"]), ["-class", "T.A", "-class", "T.A+B"])


if __name__ == "__main__":
    unittest.main()
