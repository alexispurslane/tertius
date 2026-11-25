#!/usr/bin/env python3
"""Tool code. The tests of the fetch and the check of the Blue Danube art (tools/blue-danube-assets/fetch.py): the
approval record, the source cache, the staging folder, the download and the list of sidecars and cooked models.

    nix develop --command python3 -I tools/test_blue_danube_assets.py

The standard library only. No test touches the network or the real package: each one works in a new folder, and a fake
download function stands in for HTTPS.
"""

import hashlib
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, TOOLS / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fetch = _load("fetch", "blue-danube-assets/fetch.py")
pioneer = _load("pioneer", "blue-danube-assets/pioneer.py")


def pin(data):
    return {"url": "https://example.org/" + hashlib.md5(data).hexdigest(), "bytes": len(data),
            "sha256": "sha256:" + hashlib.sha256(data).hexdigest()}


class FakeServer:
    """A download function that serves fixed bytes per URL and counts its requests."""

    def __init__(self, files=None):
        self.files = dict(files or {})
        self.requests = []

    def __call__(self, url, limit):
        self.requests.append((url, limit))
        if url not in self.files:
            raise fetch.FetchError(f"AS9005: {url} did not answer")
        return self.files[url][: limit + 1]


class Folder(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.root = Path(self._dir.name)

    def tearDown(self):
        self._dir.cleanup()


class TheApprovalRecord(unittest.TestCase):
    """The approval pins the fetch record: a changed URL, size or hash needs a new approval (asset-pipeline.md, AS9007)."""

    def source(self):
        files = {"a.dae": pin(b"mesh"), "a.dds": pin(b"texture")}
        source = {"files": files, "approved": {"on": "2026-11-24", "record": ""}}
        source["approved"]["record"] = fetch.approval_record(source)
        return source

    def test_the_record_is_the_sha256_of_the_canonical_json_of_the_files_and_archives(self):
        source = self.source()
        canonical = json.dumps({"archives": {}, "files": source["files"]}, sort_keys=True, separators=(",", ":"))
        self.assertEqual(fetch.approval_record(source), "sha256:" + hashlib.sha256(canonical.encode()).hexdigest())

    def test_a_record_that_matches_its_approval_passes(self):
        fetch.check_approval(self.source())

    def test_a_file_whose_hash_changed_after_the_approval_is_refused(self):
        source = self.source()
        source["files"]["a.dae"]["sha256"] = "sha256:" + "0" * 64
        with self.assertRaisesRegex(fetch.FetchError, "AS9007"):
            fetch.check_approval(source)

    def test_a_record_without_an_approval_is_refused(self):
        source = self.source()
        del source["approved"]
        with self.assertRaisesRegex(fetch.FetchError, "AS9007"):
            fetch.check_approval(source)


class TheSourceCache(Folder):
    """ensure_file puts exactly the pinned bytes at the destination, or nothing."""

    def test_a_cached_file_with_the_pinned_hash_is_ready_and_nothing_downloads(self):
        data = b"cached"
        dest = self.root / "cache" / "a.dae"
        dest.parent.mkdir(parents=True)
        dest.write_bytes(data)
        server = FakeServer()
        status = fetch.ensure_file(pin(data), dest, fetch.Staging(None), server)
        self.assertEqual(status, "ready")
        self.assertEqual(server.requests, [])

    def test_a_missing_file_downloads_from_its_pinned_url(self):
        data = b"downloaded bytes"
        entry = pin(data)
        dest = self.root / "cache" / "sub" / "a.dae"
        status = fetch.ensure_file(entry, dest, fetch.Staging(None), FakeServer({entry["url"]: data}))
        self.assertEqual(status, "downloaded")
        self.assertEqual(dest.read_bytes(), data)

    def test_a_cached_file_with_another_hash_counts_as_missing_and_is_replaced(self):
        data = b"the approved bytes"
        entry = pin(data)
        dest = self.root / "a.dae"
        dest.write_bytes(b"other bytes of a tampered copy")
        status = fetch.ensure_file(entry, dest, fetch.Staging(None), FakeServer({entry["url"]: data}))
        self.assertEqual(status, "downloaded")
        self.assertEqual(dest.read_bytes(), data)

    def test_a_download_with_other_bytes_fails_and_leaves_no_file(self):
        entry = pin(b"the approved bytes")
        dest = self.root / "a.dae"
        server = FakeServer({entry["url"]: b"the served bytes!!"})
        with self.assertRaisesRegex(fetch.FetchError, "AS9006"):
            fetch.ensure_file(entry, dest, fetch.Staging(None), server)
        self.assertEqual(list(self.root.iterdir()), [])

    def test_the_download_reads_at_most_one_byte_past_the_pinned_size(self):
        data = b"12345"
        entry = pin(data)
        server = FakeServer({entry["url"]: data + b"a file that grew"})
        with self.assertRaisesRegex(fetch.FetchError, "AS9006"):
            fetch.ensure_file(entry, self.root / "a.dae", fetch.Staging(None), server)
        self.assertEqual(server.requests, [(entry["url"], 5)])

    def test_a_failed_request_keeps_the_error_of_the_download(self):
        with self.assertRaisesRegex(fetch.FetchError, "AS9005"):
            fetch.ensure_file(pin(b"x"), self.root / "a.dae", fetch.Staging(None), FakeServer())


class TheStagingFolder(Folder):
    """A staging folder holds copies of approved files at any path; only a copy with the pinned hash is used."""

    def test_a_staged_copy_with_the_pinned_hash_is_copied_and_nothing_downloads(self):
        data = b"staged texture"
        staged = self.root / "staging" / "deep" / "other-name.dds"
        staged.parent.mkdir(parents=True)
        staged.write_bytes(data)
        server = FakeServer()
        dest = self.root / "cache" / "a.dds"
        status = fetch.ensure_file(pin(data), dest, fetch.Staging(self.root / "staging"), server)
        self.assertEqual(status, "staged")
        self.assertEqual(dest.read_bytes(), data)
        self.assertEqual(server.requests, [])

    def test_a_staged_file_of_the_same_size_and_another_hash_is_skipped_for_the_download(self):
        data = b"approved"
        staged = self.root / "staging" / "a.dds"
        staged.parent.mkdir(parents=True)
        staged.write_bytes(b"tampered")
        entry = pin(data)
        dest = self.root / "cache" / "a.dds"
        status = fetch.ensure_file(entry, dest, fetch.Staging(self.root / "staging"), FakeServer({entry["url"]: data}))
        self.assertEqual(status, "downloaded")
        self.assertEqual(dest.read_bytes(), data)


class TheSidecars(Folder):
    """load_sidecars finds every sidecar below the art folder, and names its cooked model."""

    def write(self, relative, content):
        path = self.root / "assets" / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(content))
        return path

    def test_each_sidecar_names_its_source_and_its_cooked_model_by_the_source_file_name(self):
        self.write("ship/shuttle.model.import.json", {"source": {"files": {}}, "convert": {"kind": "pioneer"}})
        self.write("props/box.gltf.import.json", {"source": {"files": {}}, "convert": {"kind": "gltf"}})
        sidecars = fetch.load_sidecars(self.root / "assets")
        self.assertEqual([s.model for s in sidecars], ["props/box.gltf", "ship/shuttle.model"])
        self.assertEqual(sidecars[1].folder, Path("ship"))
        self.assertEqual(sidecars[1].convert, {"kind": "pioneer"})

    def test_the_cooked_model_lies_in_the_folder_named_after_its_source(self):
        self.write("ship/shuttle.model.import.json", {"source": {"files": {}}, "convert": {"kind": "pioneer"}})
        (sidecar,) = fetch.load_sidecars(self.root / "assets")
        self.assertEqual(sidecar.cooked(self.root / "bin" / "assets"),
                         self.root / "bin" / "assets" / "ship" / "shuttle.model" / "model.glb")

    def test_the_fetched_files_lie_in_the_source_cache_beside_the_sidecars_path(self):
        self.write("ship/shuttle.model.import.json",
                   {"source": {"files": {"textures/a.dds": pin(b"a")}}, "convert": {"kind": "pioneer"}})
        (sidecar,) = fetch.load_sidecars(self.root / "assets")
        self.assertEqual(sidecar.cached("textures/a.dds", self.root / "sources"),
                         self.root / "sources" / "ship" / "textures" / "a.dds")


class PioneersMaterialFile(unittest.TestCase):
    """parse_model_file reads the textures and flags of each material block of a .model file."""

    TEXT = """version 2
material "facade_window" {
	shader "multi"
	texture diffuse  "facades_diff.dds"
	texture glow     "facades_glow.dds"
	opacity 40
		render_state {
		blend alpha
		depth_write off
	}
}
# a comment
material "facade" {
	texture diffuse "facades_diff.dds"
	diffuse 0.75 0.75 0.75
	unlit
}
lod 100 {
	mesh "station_hub_B.dae"
}
"""

    def test_a_material_keeps_its_diffuse_and_glow_textures(self):
        window = pioneer.parse_model_file(self.TEXT)["facade_window"]
        self.assertEqual((window.diffuse, window.glow), ("facades_diff.dds", "facades_glow.dds"))

    def test_the_render_state_block_inside_a_material_does_not_end_it(self):
        window = pioneer.parse_model_file(self.TEXT)["facade_window"]
        self.assertEqual((window.opacity, window.blend_alpha), (40.0, True))

    def test_unlit_and_the_diffuse_color_are_read(self):
        facade = pioneer.parse_model_file(self.TEXT)["facade"]
        self.assertEqual((facade.unlit, facade.diffuse_color, facade.glow), (True, (0.75, 0.75, 0.75), None))

    def test_blocks_outside_materials_make_no_material(self):
        self.assertEqual(sorted(pioneer.parse_model_file(self.TEXT)), ["facade", "facade_window"])


class PioneersColladaMeshes(unittest.TestCase):
    """read_collada returns the triangles of each geometry and the world matrix of each placed instance."""

    TEXT = """<?xml version="1.0"?>
<COLLADA xmlns="http://www.collada.org/2005/11/COLLADASchema" version="1.4.1">
 <library_materials><material id="hull-material" name="hull"/></library_materials>
 <library_geometries><geometry id="quad-mesh"><mesh>
  <source id="p"><float_array count="12">0 0 0 1 0 0 1 1 0 0 1 0</float_array>
   <technique_common><accessor stride="3"/></technique_common></source>
  <source id="n"><float_array count="3">0 0 1</float_array><technique_common><accessor stride="3"/></technique_common></source>
  <source id="t0"><float_array count="8">0 0 1 0 1 1 0 1</float_array><technique_common><accessor stride="2"/></technique_common></source>
  <source id="t1"><float_array count="2">9 9</float_array><technique_common><accessor stride="2"/></technique_common></source>
  <vertices id="v"><input semantic="POSITION" source="#p"/></vertices>
  <polylist material="hull-material" count="1">
   <input semantic="VERTEX" source="#v" offset="0"/><input semantic="NORMAL" source="#n" offset="1"/>
   <input semantic="TEXCOORD" source="#t1" offset="2" set="1"/><input semantic="TEXCOORD" source="#t0" offset="3" set="0"/>
   <vcount>4</vcount><p>0 0 0 0 1 0 0 1 2 0 0 2 3 0 0 3</p></polylist>
  <lines count="1"><input semantic="VERTEX" source="#v" offset="0"/><p>0 1</p></lines>
 </mesh></geometry></library_geometries>
 <library_visual_scenes><visual_scene id="s">
  <node name="body"><matrix>1 0 0 10 0 1 0 0 0 0 1 0 0 0 0 1</matrix>
   <node name="panel"><matrix>2 0 0 0 0 2 0 0 0 0 2 0 0 0 0 1</matrix>
    <instance_geometry url="#quad-mesh"><bind_material><technique_common>
     <instance_material symbol="hull-material" target="#hull-material"/></technique_common></bind_material></instance_geometry>
   </node>
   <node name="navlight_red_001"><matrix>1 0 0 0 0 1 0 0 0 0 1 0 0 0 0 1</matrix>
    <instance_geometry url="#quad-mesh"/></node>
  </node>
 </visual_scene></library_visual_scenes>
</COLLADA>"""

    def read(self):
        return pioneer.read_collada(self.TEXT, ["navlight"])

    def test_a_quad_becomes_two_triangles_that_keep_its_winding(self):
        geometries, _, _ = self.read()
        (primitive,) = geometries["#quad-mesh"].primitives
        self.assertEqual([c[0] for c in primitive.corners], [0, 1, 2, 0, 2, 3])

    def test_the_first_texture_set_gives_the_uvs(self):
        geometries, _, _ = self.read()
        (primitive,) = geometries["#quad-mesh"].primitives
        self.assertEqual([c[2] for c in primitive.corners[:3]], [(0, 0), (1, 0), (1, 1)])

    def test_an_instance_carries_the_product_of_its_parents_matrices(self):
        _, instances, _ = self.read()
        (panel,) = instances
        self.assertEqual(panel.matrix[0], (2.0, 0.0, 0.0, 10.0))
        self.assertEqual(panel.materials, {"hull-material": "hull"})

    def test_a_node_with_a_dropped_prefix_is_left_out_and_listed(self):
        _, instances, dropped = self.read()
        self.assertEqual(([i.node for i in instances], dropped), (["panel"], ["navlight_red_001"]))


if __name__ == "__main__":
    unittest.main()
