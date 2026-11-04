"""Tool code (tools/README.md, "GPU calibration"): the counted work of compute shaders, and the roofline of a pass from
it (docs/research/temporal-renderer/gpu-calibration.md, "The roofline of a pass").

Usage, outside the dev shell, whose environment hides Xcode's Metal toolchain from xcrun:
    python3 -I tools/gpu-calibration/work.py count <MoltenVK's MSL dump folder> <out folder>
    python3 -I tools/gpu-calibration/work.py fsr <MoltenVK's MSL dump folder> <out folder> <folder of the engine's SPIR-V> <trace folders...>

"count" compiles each kernel of the dump with xcrun metal -O2 into AIR, Apple's intermediate code, unrolls every loop of
a constant trip count with the toolchain's air-opt, and prints per kernel:
- alu: the lane operations of one invocation. Each arithmetic, compare, select or conversion instruction and each call
  of a math function of AIR counts its vector width, such as 4 for a float4 add. A move of a vector element, a load, a
  store, an address, a branch and a texture access count nothing.
- texture: the texture reads of one invocation, each sample, gather or read call one.
- writes: the texture writes of one invocation.
- loops: the back edges that remain after the unroll, the loops whose trip count is not a constant. A kernel without
  loops runs at most its static count. A loop runs its body once in the count, so a kernel with loops can run more.
The counts are static: both sides of a branch count. So an invocation that takes one side runs fewer operations.

"fsr" adds FSR's dispatch sizes, the footprint of each pass's images in bytes, the three quotients of the roofline, its
prediction and the measured time of each pass at the top clock from fsr-passes.py's passes.json in the trace folders.
"""

import json
import re
import statistics
import struct
import subprocess
import sys
import hashlib
from pathlib import Path

# The measured rates of this spike's Vulkan probes, scaled to the top clock of 1296 MHz where the rate runs on the
# GPU's clock (gpu-calibration.md, "The shares of the peak").
ALU_RATE = 0.956 * 3072 * 1.296e9  # lane operations per second: the FMA probe, 0.956 of the FP32 peak
TEXEL_RATE = 152.8e9 * 1296 / 1293  # texture reads per second: the cached texture probe, at about 1293 MHz
BYTE_RATE = 346.2e9  # bytes per second: the bandwidth probe, which does not follow the GPU's clock

ARITHMETIC = re.compile(
    r"=\s+(?:fast\s+|nnan\s+|ninf\s+|nsz\s+|arcp\s+|contract\s+|afn\s+|reassoc\s+|nuw\s+|nsw\s+|exact\s+)*"
    r"(fadd|fsub|fmul|fdiv|frem|fneg|add|sub|mul|udiv|sdiv|urem|srem|shl|lshr|ashr|and|or|xor|icmp|fcmp|select|"
    r"fptrunc|fpext|fptoui|fptosi|uitofp|sitofp|trunc|zext|sext)\b"
)
TEXTURE_READ = re.compile(r"@air\.(sample|gather|read)_texture")
TEXTURE_WRITE = re.compile(r"@air\.write_texture")
NOT_MATH = re.compile(r"@air\.(sample|gather|read|write|get|wg|simdgroup|atomic|mem|fence|thread|quad|simd|convert)")
WIDTH = re.compile(r"<(\d+) x ")


def width(line: str) -> int:
    widths = [int(found) for found in WIDTH.findall(line)]
    return max(widths) if widths else 1


def count_air(text: str) -> dict:
    """The counts of every kernel of the AIR text, {kernel: {alu, texture, writes, loops}}."""
    kernels = {}
    name = None
    labels = {}
    for line in text.splitlines():
        if line.startswith("define "):
            name = re.search(r"@([A-Za-z0-9_.]+)\(", line)[1]
            kernels[name] = {"alu": 0, "texture": 0, "writes": 0, "loops": 0}
            labels = {"0": 0}
            order = 0
            continue
        if name is None:
            continue
        if line.startswith("}"):
            name = None
            continue
        if match := re.match(r"^(\d+):", line):
            order += 1
            labels[match[1]] = order
            continue
        counts = kernels[name]
        if " call " in line or line.lstrip().startswith("call ") or "= call" in line or "= tail call" in line:
            if TEXTURE_READ.search(line):
                counts["texture"] += 1
            elif TEXTURE_WRITE.search(line):
                counts["writes"] += 1
            elif "@air." in line and not NOT_MATH.search(line):
                counts["alu"] += width(line)
        elif ARITHMETIC.search(line):
            counts["alu"] += width(line)
        if line.lstrip().startswith("br "):
            # A branch to a block that the function defines before this one closes a loop.
            for target in re.findall(r"label %(\d+)", line):
                if target in labels:
                    counts["loops"] += 1
    return kernels


def compile_air(msl: Path, out: Path) -> str:
    """The AIR of msl at -O2, with every loop of a constant trip count unrolled in full by the toolchain's air-opt, so
    that a static count holds each pass of such a loop."""
    ll = out / (msl.stem + ".ll")
    unrolled = out / (msl.stem + ".unrolled.ll")
    subprocess.run(["/usr/bin/xcrun", "metal", "-O2", "-S", "-emit-llvm", "-w", "-o", str(ll), str(msl)], check=True)
    toolchain = Path(subprocess.run(["/usr/bin/xcrun", "--find", "metal"], capture_output=True, text=True, check=True).stdout.strip()).parent
    passes = "function(loop-simplify,lcssa,loop(loop-rotate,indvars),loop-unroll-full,simplifycfg,instcombine)"
    subprocess.run(
        [str(toolchain / "air-opt"), "-S", f"-passes={passes}", "-unroll-threshold=10000000", "-unroll-full-max-count=100000",
         "-o", str(unrolled), str(ll)],
        check=True,
    )
    return unrolled.read_text()


def count(dump: Path, out: Path) -> dict:
    out.mkdir(parents=True, exist_ok=True)
    return {msl.stem: count_air(compile_air(msl, out)) for msl in sorted(dump.glob("*.metal"))}


def local_size(spirv: bytes) -> tuple:
    """The LocalSize of the module's OpExecutionMode, from its words."""
    words = struct.unpack(f"<{len(spirv) // 4}I", spirv[: len(spirv) // 4 * 4])
    index = 5
    while index < len(words):
        length, opcode = words[index] >> 16, words[index] & 0xFFFF
        if opcode == 16 and words[index + 2] == 17:
            return words[index + 3], words[index + 4], words[index + 5]
        index += max(length, 1)
    return 1, 1, 1


# FSR from 1280 x 720 to 1920 x 1080, as the batched measurement runs it. Each image of a pass: its bytes per texel and
# its size, from Fsr3Upscaler.ImageDescriptions and the bench's inputs (Fsr3UpscalerTests.Fsr3Bench).
RENDER = 1280 * 720
HALF = 640 * 360
OUTPUT = 1920 * 1080
IMAGES = {
    "input_color_jittered": (8, RENDER), "input_opaque_only": (8, RENDER), "input_depth": (4, RENDER),
    "input_motion_vectors": (8, RENDER), "input_exposure": (8, 1), "reactive_mask": (1, RENDER),
    "output_autoreactive": (1, RENDER), "transparency_and_composition_mask": (1, 1), "current_luma": (2, RENDER),
    "previous_luma": (2, RENDER), "dilated_depth": (4, RENDER), "dilated_motion_vectors": (4, RENDER),
    "farthest_depth": (2, RENDER), "reconstructed_previous_nearest_depth": (4, RENDER), "farthest_depth_mip1": (2, HALF),
    # The pyramid's first five levels of RG16F from half size; the fifth is 1/1024 of it.
    "spd_mip0": (4, HALF), "spd_mip1": (4, HALF // 4), "spd_mip2": (4, HALF // 16), "spd_mip3": (4, HALF // 64),
    "spd_mip4": (4, HALF // 256), "spd_mip5": (4, HALF // 1024), "spd_global_atomic": (4, 1), "frame_info": (16, 1),
    "spd_mips": (4, HALF * 4 // 3), "shading_change": (1, HALF), "accumulation": (1, RENDER), "dilated_reactive_masks": (4, RENDER),
    "new_locks": (1, OUTPUT), "luma_history": (8, RENDER), "luma_instability": (2, RENDER),
    "internal_upscaled_color": (8, OUTPUT), "upscaled_output": (8, OUTPUT),
}
SHADERS = {
    "Exposure": "fsr_exposure.comp.spv",
    "GenerateReactive": "ffx_fsr3upscaler_autogen_reactive_pass.glsl.spv",
    "PrepareInputs": "ffx_fsr3upscaler_prepare_inputs_pass.glsl.spv",
    "LumaPyramid": "ffx_fsr3upscaler_luma_pyramid_pass.glsl.spv",
    "ShadingChangePyramid": "ffx_fsr3upscaler_shading_change_pyramid_pass.glsl.spv",
    "ShadingChange": "ffx_fsr3upscaler_shading_change_pass.glsl.spv",
    "PrepareReactivity": "ffx_fsr3upscaler_prepare_reactivity_pass.glsl.spv",
    "LumaInstability": "ffx_fsr3upscaler_luma_instability_pass.glsl.spv",
    "Accumulate": "ffx_fsr3upscaler_accumulate_pass.glsl.spv",
}
# The workgroups of each pass (Fsr3Upscaler.Groups): 8 x 8 groups over the render size, the half size or the output
# size, and the pyramids' groups of 64 x 64 texels over the render size.
GROUPS = {
    "Exposure": 1, "GenerateReactive": 160 * 90, "PrepareInputs": 160 * 90, "LumaPyramid": 20 * 12,
    "ShadingChangePyramid": 20 * 12, "ShadingChange": 80 * 45, "PrepareReactivity": 160 * 90,
    "LumaInstability": 160 * 90, "Accumulate": 240 * 135,
}


def footprint(msl: str) -> int:
    """The bytes of the images that the kernel binds, each read or written once: a lower bound of its memory traffic."""
    names = set(re.findall(r"texture2d<[^>]*> (?:r|rw)_([a-z0-9_]+)", msl))
    return sum(IMAGES[name][0] * IMAGES[name][1] for name in names)


def roofline(alu: float, texture: float, bytes_moved: float) -> dict:
    """The three quotients of a pass's counted work in ms at the top clock, and the largest of them, the prediction."""
    terms = {"alu_ms": alu / ALU_RATE * 1e3, "texture_ms": texture / TEXEL_RATE * 1e3, "bytes_ms": bytes_moved / BYTE_RATE * 1e3}
    terms["prediction_ms"] = max(terms.values())
    terms["bound"] = max(("alu_ms", "texture_ms", "bytes_ms"), key=terms.get).removesuffix("_ms")
    return terms


def ratio(measured: float, prediction: float) -> str:
    """Measured over predicted, or "fixed cost" for a dispatch whose counted work takes less than a microsecond: its time
    is the cost of a dispatch, not of its work."""
    return "fixed cost" if prediction < 1e-3 else f"{measured / prediction:.1f}"


def fsr(dump: Path, out: Path, shaders: Path, traces: list) -> int:
    counts = count(dump, out)
    by_hash = {hashlib.sha256(path.read_bytes()).hexdigest(): path.name for path in shaders.glob("*.spv")}
    measured = {}
    for trace in traces:
        frames = json.loads((Path(trace) / "passes.json").read_text())["frames"]
        sums = sorted(sum(each[name] for name in SHADERS) for each in frames)
        bound = 1.1 * sums[min(len(sums) - 1, len(sums) // 10)]
        top = [each for each in frames if sum(each[name] for name in SHADERS) <= bound]
        for name in SHADERS:
            measured.setdefault(name, []).append(statistics.median(each[name] for each in top) / 1e6)
    print("| Pass | Invocations | ALU lane operations per invocation | Texture reads per invocation | Loops | Image bytes, MB | ALU, ms | Texture, ms | Bytes, ms | Prediction, ms | Bound | Measured at the top clock, ms | Measured over the prediction |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|")
    total = {"prediction": 0.0, "measured": 0.0}
    for name, shader in SHADERS.items():
        spirv = next(path for path in dump.glob("*.spv") if by_hash.get(hashlib.sha256(path.read_bytes()).hexdigest()) == shader)
        size = local_size(spirv.read_bytes())
        invocations = GROUPS[name] * size[0] * size[1] * size[2]
        kernel = counts[spirv.stem]["main0"]
        moved = footprint((dump / (spirv.stem + ".metal")).read_text())
        terms = roofline(invocations * kernel["alu"], invocations * kernel["texture"], moved)
        time = statistics.median(measured[name])
        total["prediction"] += terms["prediction_ms"]
        total["measured"] += time
        print(
            f"| {name} | {invocations:,} | {kernel['alu']} | {kernel['texture']} | {kernel['loops']} | {moved / 1e6:.1f} | "
            f"{terms['alu_ms']:.3f} | {terms['texture_ms']:.3f} | {terms['bytes_ms']:.3f} | {terms['prediction_ms']:.3f} | "
            f"{terms['bound']} | {time:.3f} | {ratio(time, terms['prediction_ms'])} |"
        )
    print(f"\nThe sum of the predictions: {total['prediction']:.3f} ms. The sum of the measured times: {total['measured']:.3f} ms.")
    return 0


def main() -> int:
    if sys.argv[1] == "count":
        for kernel_file, kernels in count(Path(sys.argv[2]), Path(sys.argv[3])).items():
            for name, counts in kernels.items():
                print(f"{kernel_file} {name}: " + ", ".join(f"{key} {value}" for key, value in counts.items()))
        return 0
    if sys.argv[1] == "fsr":
        return fsr(Path(sys.argv[2]), Path(sys.argv[3]), Path(sys.argv[4]), sys.argv[5:])
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
