"""The highest register and the instruction count of the direct shaders on Apple's M1 GPU, from an offline compile.

On the M1, the threads that a fragment shader keeps resident fall in steps with its highest register: 448 up to
register 115, 384 from register 116 (ARCHITECTURE.md, "Key and fill"). direct_key_fill.frag sits at 115 with no room,
so a change to any file that it includes can cross the step and cost 0.1 ms to 0.4 ms of the direct pass in every view
(t2-x53j, t1-gout). This tool reads the count before a GPU take does.

Usage, in the dev shell, from the repository root:

    nix develop --command python3 -I tools/shader_registers.py <out folder> [<shader folder>] [<label>]

The shader folder defaults to src/Tertius.Rendering/Shaders. The label names the variant in the output, default "head".
Each line of the output is "<label> <shader> r<highest register> <instructions> instructions".

It needs, besides the dev shell's glslc, spirv-opt and MoltenVKShaderConverter (MoltenVK's own converter):

- Xcode's Metal toolchain with metal and metal-tt (`xcrun -f metal-tt`). The dev shell does not hold them.
- The applegpu decoder of Dougall Johnson, https://github.com/dougallj/applegpu, at revision
  28dd80468d4a6c94fa4dac34ed488196f5fedc5f, in the folder that the environment variable APPLEGPU names. The dev shell
  does not hold it. Clone it into an empty folder outside the repository.

It submits no GPU work. The shaders compile as MoltenVK runs them: spirv-opt freezes the specialization constants with
the tiles' corner means on (constant 11), MoltenVKShaderConverter writes Metal source, metal builds a library and metal-tt
compiles a render pipeline for applegpu_g13s, the M1 family, with two RGBA16Float attachments. The decoder then reads
the fragment's machine code. The highest register is the largest 32-bit register index that an instruction names; the
runtime may allocate more. The method and its first results are in docs/research/light-pairs/pair/notes.md, "Offline
native shader evidence".
"""
from pathlib import Path
import json
import os
import re
import struct
import subprocess
import sys

# The shaders of the direct pass whose registers the tool reads: the file and its pipeline's color formats.
SHADERS = [
    ("direct.frag", ["RGBA16Float", "RGBA16Float"]),
    ("direct_key_fill.frag", ["RGBA16Float", "RGBA16Float"]),
]

# The specialization constants that MoltenVK runs on the Mac: ShaderConstants.TileCornerMeans on.
CONSTANTS = "11:true"


def run(*args):
    subprocess.run([str(x) for x in args], check=True)


def xcrun(*args):
    """The system's xcrun, without the dev shell's SDK variables, which point it at Nix's SDK and its older metal."""
    env = {k: v for k, v in os.environ.items() if k not in ("SDKROOT", "DEVELOPER_DIR")}
    return subprocess.run(["/usr/bin/xcrun", *args], check=True, capture_output=True, text=True, env=env).stdout.strip()


def highest_register(lines):
    """The largest register index that the disassembled instructions in `lines` name, or -1 for none. A register is
    rN, with an optional half suffix l or h, also as either half of a pair such as r9_r10; uniform registers (uN) do not
    count."""
    found = [int(x) for line in lines for x in re.findall(r"(?<![A-Za-z0-9])r(\d+)", line)]
    return max(found, default=-1)


def metal_library(spirv, folder, name):
    frozen = folder / f"{name}.frozen.spv"
    source = folder / f"{name}.metal"
    library = folder / f"{name}.metallib"
    run("spirv-opt", "--set-spec-const-default-value", CONSTANTS, "--freeze-spec-const", spirv, "-o", frozen)
    run("MoltenVKShaderConverter", "-si", frozen, "-mo", source, "-mab", "-q")
    run(xcrun("-f", "metal"), "-isysroot", xcrun("--show-sdk-path"), source, "-o", library)
    return library


def native_pipeline(vertex, fragment, formats, folder, name):
    config = {
        "libraries": {"paths": [{"label": "Vertex", "path": str(vertex)}, {"label": "Fragment", "path": str(fragment)}]},
        "pipelines": {"render_pipelines": [{
            "vertex_function": "alias:Vertex#main0",
            "fragment_function": "alias:Fragment#main0",
            "color_attachments": [{"pixel_format": f} for f in formats],
        }]},
    }
    config_path = folder / f"{name}.mtlp-json"
    config_path.write_text(json.dumps(config, indent=2) + "\n")
    archive = folder / f"{name}.native.metallib"
    run(xcrun("-f", "metal-tt"), config_path, "-arch", "applegpu_g13s", "-platform_version", "macos", "26.0", "27.0",
        "-sysroot", xcrun("--show-sdk-path"), "-o", archive)
    return archive


def macho_sections_and_symbols(data):
    """The sections and the symbols of a 64-bit Mach-O image."""
    assert struct.unpack_from("<I", data)[0] == 0xFEEDFACF, "not a 64-bit Mach-O image"
    sections, symbols = [], []
    offset = 32
    for _ in range(struct.unpack_from("<I", data, 16)[0]):
        command, size = struct.unpack_from("<II", data, offset)
        if command == 25:  # LC_SEGMENT_64
            for i in range(struct.unpack_from("<I", data, offset + 64)[0]):
                at = offset + 72 + 80 * i
                name = data[at:at + 16].split(b"\0")[0].decode()
                segment = data[at + 16:at + 32].split(b"\0")[0].decode()
                address, length, file_offset = struct.unpack_from("<QQI", data, at + 32)
                sections.append(dict(name=name, segment=segment, address=address, length=length, offset=file_offset))
        if command == 2:  # LC_SYMTAB
            table, count, strings, _ = struct.unpack_from("<IIII", data, offset + 8)
            for i in range(count):
                string_offset, _, _, _, address = struct.unpack_from("<IBBHQ", data, table + 16 * i)
                name = data[strings + string_offset:].split(b"\0")[0].decode()
                symbols.append(dict(name=name, address=address))
        offset += size
    return sections, symbols


def fragment_code(archive):
    """The machine code of the fragment stage's main function in a native pipeline archive."""
    data = archive.read_bytes()
    sections, _ = macho_sections_and_symbols(data)
    fragment = next(s for s in sections if s["name"] == "__fragment" and s["segment"] == "__TEXT")
    image = data[fragment["offset"]:fragment["offset"] + fragment["length"]]
    sections, symbols = macho_sections_and_symbols(image)
    text = next(s for s in sections if s["name"] == "__text")
    main = next(s for s in symbols if s["name"] == "_agc.main")
    return image[text["offset"] + main["address"]:text["offset"] + text["length"]]


def disassemble(applegpu, code):
    """The instructions of `code`, one line each. Raises when a word does not decode, since the count would be wrong."""
    lines = []
    offset = 0
    while offset < len(code):
        number = applegpu.opcode_to_number(code[offset:offset + 16])
        descriptor = next((d for d in applegpu.instruction_descriptors if d.matches(number)), None)
        if descriptor is None:
            raise RuntimeError(f"the decoder does not know the word at offset {offset:#x}")
        lines.append(str(descriptor.disassemble(number, pc=offset)))
        offset += descriptor.decode_size(number)
    return lines


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    out = Path(argv[1]).resolve()
    shaders = Path(argv[2] if len(argv) > 2 else "src/Tertius.Rendering/Shaders").resolve()
    label = argv[3] if len(argv) > 3 else "head"
    decoder = os.environ.get("APPLEGPU")
    if not decoder or not (Path(decoder) / "applegpu.py").exists():
        sys.exit("Set APPLEGPU to a clone of https://github.com/dougallj/applegpu at 28dd80468d4a6c94fa4dac34ed488196f5fedc5f.")
    sys.path.insert(0, decoder)
    import applegpu

    out.mkdir(parents=True, exist_ok=True)
    compile_spirv = lambda name: run("glslc", "--target-env=vulkan1.3", "-O", "-Werror", "-I", shaders,
                                     "-o", out / f"{name}.spv", shaders / name)
    compile_spirv("fullscreen.vert")
    vertex = metal_library(out / "fullscreen.vert.spv", out, "fullscreen.vert")
    for name, formats in SHADERS:
        compile_spirv(name)
        fragment = metal_library(out / f"{name}.spv", out, name)
        archive = native_pipeline(vertex, fragment, formats, out, name)
        lines = disassemble(applegpu, fragment_code(archive))
        (out / f"{name}.disasm").write_text("\n".join(lines) + "\n")
        print(f"{label} {name} r{highest_register(lines)} {len(lines)} instructions", flush=True)


if __name__ == "__main__":
    main(sys.argv)
