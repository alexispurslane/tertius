"""Compiles shaders of the worktree's Checked build offline for the M1's GPU, with the corner means of the shadow
tiles on, as MoltenVK runs them. No GPU work. Usage: worktree.py <tag>, run in the dev shell."""
from pathlib import Path
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("native", "/private/tmp/t2-mlvy/native/compile_native.py")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)
tag = sys.argv[1]
source = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(
    "/Users/alexispurslane/.t3/worktrees/tertius/t3-23bcaad4/src/Tertius.Rendering/obj/Checked/net11.0/shaders")
jobs = [
    ("direct.frag.spv", "direct", ["RGBA16Float", "RGBA16Float"]),
    ("direct_key_fill.frag.spv", "keyfill", ["RGBA16Float", "RGBA16Float"]),
    ("key_fill_corners.frag.spv", "corners", ["RGBA32Uint", "RGBA32Uint", "RGBA32Uint"]),
]
for file, name, formats in jobs:
    if not (source / file).exists():
        continue
    fragment = native.library(source / file, f"{tag}-{name}-native-input", constants="11:true")
    native.pipeline(f"{tag}-{name}", fragment, formats)
