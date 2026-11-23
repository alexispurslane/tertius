"""The highest register and the instruction count of the direct shaders on Apple's M1 GPU, from an offline compile.

On the M1, the threads that a fragment shader keeps resident fall in steps with its highest register: 448 up to
register 115, 384 from register 116 (ARCHITECTURE.md, "Key and fill"). direct_key_fill.frag sits at 115 with no room,
so a change to any file that it includes can cross the step and cost 0.1 ms to 0.4 ms of the direct pass in every view
(t2-x53j, t1-gout). This tool reads the count before a GPU take does.

Usage, from the repository root. The count, in the dev shell:

    nix develop --command python3 -I tools/shader_registers.py <out folder> [<shader folder>] [<label>]

The shader folder defaults to src/Tertius.Rendering/Shaders. The label names the variant in the output, default "head".
Each line of the output is "<label> <shader> r<highest register> <instructions> instructions".

The guard, which the pre-commit hook runs with the staged files (inside or outside the dev shell):

    python3 -I tools/shader_registers.py guard <out folder> <staged file>...

It reads the shader folder from git's index, so it judges what the commit holds. It counts each guarded shader whose
include closure holds a staged file, and prints one of three results per run, each with its exit code: pass (0), with
the register, the limit and the room, and a warning when the room is under 2 registers; fail (1), with the register,
the limit and what a register above it costs; and "does not apply" (3), with the reason: no staged file is in a guarded
closure, or Apple's Metal toolchain is absent, as on Linux and the cloud workers. An error of the tool itself exits with
2, and so does an absent decoder beside the Metal toolchain: the count applies there, so the guard asks for a dev shell
entered again. Outside the dev shell, the guard runs itself again inside it once it knows that it must count.

It needs, besides the dev shell's glslc, spirv-opt and MoltenVKShaderConverter (MoltenVK's own converter):

- Xcode's Metal toolchain with metal and metal-tt (`xcrun -f metal-tt`). The dev shell does not hold them.
- The applegpu decoder of Dougall Johnson, https://github.com/dougallj/applegpu, at revision
  28dd80468d4a6c94fa4dac34ed488196f5fedc5f, in the folder that the environment variable APPLEGPU names. The dev shell
  sets APPLEGPU to the flake's copy (native/applegpu.nix).

It submits no GPU work. The shaders compile as MoltenVK runs them: spirv-opt freezes the specialization constants with
the tiles' corner means on (constant 11), MoltenVKShaderConverter writes Metal source, metal builds a library and metal-tt
compiles a render pipeline for applegpu_g13s, the M1 family, with two RGBA16Float attachments. The decoder then reads
the fragment's machine code. The highest register is the largest 32-bit register index that an instruction names; the
runtime may allocate more. The method and its first results are in docs/research/light-pairs/pair/notes.md, "Offline
native shader evidence".
"""
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time

# The exit code of each result of the guard.
PASS, FAIL, ERROR, NOT_APPLICABLE = 0, 1, 2, 3

# The folder of the shaders, from the repository root: the guard maps a staged path to a shader file with it.
SHADER_FOLDER = PurePosixPath("src/Tertius.Rendering/Shaders")

# On the M1 a fragment shader keeps 448 threads resident up to register 115, and 384 from register 116
# (ARCHITECTURE.md, "Key and fill"). One register over the step cost the direct pass 0.36 ms on the deck (ter-8esk) and
# 0.10 to 0.28 ms in every view (t1-gout).
#
# The source does not set a shader's highest register: Apple's scheduler does (t2-x53j). The peak of
# direct_key_fill.frag is the shadow filters' reads in flight inside the light loop, and a form of the source that freed
# 6 registers of the loop's state raised the peak from 111 to 112 live registers and the shader to 116, because the
# scheduler spent the freed registers on more reads in flight. Twelve forms that keep the picture found no room
# (docs/research/t2-x53j-register-room). So the guard holds the limit and does not ask for room.
STEP = 115
STEP_COST = ("from register 116 the M1 keeps 384 threads of the shader resident in place of 448, which cost the direct "
             "pass 0.10 to 0.36 ms of the frame in every view (t2-x53j, t1-gout)")

# The guard warns when a shader has fewer registers of room than this: one more value at the peak crosses the step.
ROOM_WARNING = 2

# The files, from the repository root, that change every guarded shader's code without being in an include closure: this
# tool's own compile settings and the dev shell's MoltenVK, whose SPIRV-Cross writes the Metal source both for the count
# and for the game (native/moltenvk.nix and its patches), and the flake's lock, which pins glslc, spirv-opt and MoltenVK.
# A staged path among them, or one that starts with native/moltenvk, asks for a count of every guarded shader. Xcode's
# metal-tt lies outside the repository, so no commit can change it.
COUNT_INPUTS = ("tools/shader_registers.py", "flake.lock")
COUNT_INPUT_PREFIX = "native/moltenvk"


@dataclass(frozen=True)
class Guarded:
    """A shader of the direct pass whose registers the tool reads: the file, its pipeline's color formats, the highest
    register that it may reach, and, when its lack of room at that limit is known and accepted, the words that say so in
    the guard's warning."""
    name: str
    formats: tuple
    limit: int
    known_low_room: str = ""


# Both direct shaders draw the direct pass, so both pay the step's cost.
SHADERS = [
    Guarded("direct.frag", ("RGBA16Float", "RGBA16Float"), STEP),
    Guarded("direct_key_fill.frag", ("RGBA16Float", "RGBA16Float"), STEP,
            "the known state since t2-x53j, not a new fault: Apple's scheduler fills any register that the source "
            "frees, so no form of the source holds room"),
]


@dataclass(frozen=True)
class Verdict:
    """One result of the guard: its words and its exit code."""
    text: str
    exit_code: int

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


INCLUDE = re.compile(r'^[ \t]*#[ \t]*include[ \t]+"([^"]+)"', re.MULTILINE)


def include_closure(folder, shader):
    """The names of `shader` and of every file in `folder` that it includes, through any depth of includes. An
    include inside a preprocessor branch counts, since a change there can still reach the shader."""
    closure, pending = set(), [shader]
    while pending:
        name = pending.pop()
        if name in closure:
            continue
        closure.add(name)
        path = folder / name
        if path.is_file():
            pending += INCLUDE.findall(path.read_text())
    return closure


def guarded_shaders_touched(staged, folder, shaders):
    """The names of `shaders` (files of `folder`, the shader folder) whose include closure holds one of the `staged`
    paths, which are relative to the repository root. A staged input of the count itself asks for all of them."""
    if any(p in COUNT_INPUTS or p.startswith(COUNT_INPUT_PREFIX) for p in staged):
        return list(shaders)
    names = {PurePosixPath(p).name for p in staged if PurePosixPath(p).parent == SHADER_FOLDER}
    return [shader for shader in shaders if include_closure(folder, shader) & names]


def verdict_of(guarded, register):
    """The guard's result for the Guarded shader `guarded` at its highest register `register`: a pass with the room
    under the limit, and a warning when the room is under ROOM_WARNING, or a fail above the limit. The warning names the
    shader's known lack of room only at the register of that known state, the limit."""
    shader, limit = guarded.name, guarded.limit
    if register > limit:
        return Verdict(f"fail: {shader} is at register {register}, above its limit of {limit}: {STEP_COST}.", FAIL)
    room = limit - register
    text = f"pass: {shader} is at register {register}, the limit is {limit}, room {room} register{'s' if room != 1 else ''}"
    if room < ROOM_WARNING:
        text += (f". warning: less than {ROOM_WARNING} registers of room, so one more value at the shader's peak can "
                 f"cross the limit")
        if guarded.known_low_room and register == limit:
            text += f". This is {guarded.known_low_room}"
    return Verdict(text + ".", PASS)


def applicability(metal_tt, decoder):
    """None when the count can run, else the guard's result without a count: `metal_tt` is the path of Xcode's metal-tt
    or None, and `decoder` the applegpu folder or None. Without metal-tt the count does not apply. With metal-tt the
    count applies, so an absent decoder is an error, never a "does not apply": the guard runs inside the dev shell there,
    and a dev shell without APPLEGPU is one that was entered before the flake set it (t0-xvfx)."""
    if metal_tt is None:
        return Verdict("does not apply: Xcode's Metal toolchain (metal-tt) is absent, so the M1's registers cannot be "
                       "counted on this machine.", NOT_APPLICABLE)
    if decoder is None:
        return Verdict("error: the applegpu decoder is absent (APPLEGPU names no folder with applegpu.py), so the "
                       "registers cannot be counted although metal-tt is present. This dev shell was entered before the "
                       "flake set APPLEGPU: enter the dev shell again (nix develop) and commit again.", ERROR)
    return None


def metal_library(spirv, folder, name):
    frozen = folder / f"{name}.frozen.spv"
    source = folder / f"{name}.metal"
    library = folder / f"{name}.metallib"
    run("spirv-opt", "--set-spec-const-default-value", CONSTANTS, "--freeze-spec-const", spirv, "-o", frozen)
    run("MoltenVKShaderConverter", "-si", frozen, "-mo", source, "-mab", "-q")
    if not any(stage in source.read_text() for stage in ("vertex ", "fragment ", "kernel ")):
        raise RuntimeError(f"MoltenVKShaderConverter wrote no shader stage for {name}")
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


def decoder_folder():
    """The applegpu folder that APPLEGPU names, or None when it holds no decoder."""
    decoder = os.environ.get("APPLEGPU")
    return decoder if decoder and (Path(decoder) / "applegpu.py").exists() else None


def metal_tt_path():
    """The path of Xcode's metal-tt, or None where Xcode's Metal toolchain is absent."""
    if not Path("/usr/bin/xcrun").exists():
        return None
    try:
        return xcrun("-f", "metal-tt") or None
    except subprocess.CalledProcessError:
        return None


def counter(applegpu, shaders, out):
    """A function that counts one shader of `shaders` (a folder) into `out` and returns its highest register and its
    disassembled lines. It compiles the vertex shader once."""
    out.mkdir(parents=True, exist_ok=True)

    def compile_spirv(name):
        run("glslc", "--target-env=vulkan1.3", "-O", "-Werror", "-I", shaders, "-o", out / f"{name}.spv",
            shaders / name)

    compile_spirv("fullscreen.vert")
    vertex = metal_library(out / "fullscreen.vert.spv", out, "fullscreen.vert")

    def count(shader):
        compile_spirv(shader.name)
        fragment = metal_library(out / f"{shader.name}.spv", out, shader.name)
        archive = native_pipeline(vertex, fragment, list(shader.formats), out, shader.name)
        lines = disassemble(applegpu, fragment_code(archive))
        (out / f"{shader.name}.disasm").write_text("\n".join(lines) + "\n")
        return highest_register(lines), lines

    return count


def import_decoder(decoder):
    sys.path.insert(0, decoder)
    import applegpu
    return applegpu


def staged_shader_folder(out):
    """A copy of the shader folder as git's index holds it, under `out`: what the commit will hold."""
    index = out / "index"
    shutil.rmtree(index, ignore_errors=True)
    files = subprocess.run(["git", "ls-files", "-z", "--", str(SHADER_FOLDER)], check=True, capture_output=True,
                           text=True).stdout.split("\0")
    subprocess.run(["git", "checkout-index", f"--prefix={index}/", "--", *[f for f in files if f]], check=True)
    return index / SHADER_FOLDER


def guard(argv):
    """The guard: judges each guarded shader whose include closure holds a staged file. Returns the exit code."""
    out = Path(argv[0]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    folder = staged_shader_folder(out)
    touched = guarded_shaders_touched(argv[1:], folder, [s.name for s in SHADERS])
    if not touched:
        print("register guard: does not apply: no staged file is in the include closure of a guarded shader "
              f"({', '.join(s.name for s in SHADERS)}).")
        return NOT_APPLICABLE
    metal_tt = metal_tt_path()
    if metal_tt is not None and not os.environ.get("IN_NIX_SHELL"):
        # The count needs the dev shell's glslc, spirv-opt, MoltenVKShaderConverter and APPLEGPU.
        os.execvp("nix", ["nix", "develop", "--command", "python3", "-I", __file__, "guard", *argv])
    absent = applicability(metal_tt, decoder_folder())
    if absent is not None:
        print(f"register guard: {absent.text}", file=sys.stderr if absent.exit_code == ERROR else sys.stdout)
        return absent.exit_code
    started = time.monotonic()
    count = counter(import_decoder(decoder_folder()), folder, out)
    verdicts = [verdict_of(shader, count(shader)[0]) for shader in SHADERS if shader.name in touched]
    for verdict in verdicts:
        print(f"register guard: {verdict.text}")
    print(f"register guard: counted {len(verdicts)} shader(s) in {time.monotonic() - started:.0f} s. The disassembly "
          f"is in {out}.")
    return max(v.exit_code for v in verdicts)


def main(argv):
    if len(argv) < 2:
        sys.exit(__doc__)
    if argv[1] == "guard":
        if len(argv) < 3:
            sys.exit(__doc__)
        try:
            sys.exit(guard(argv[2:]))
        except Exception as error:  # noqa: BLE001: any failure of the count is an error of the guard, not a pass
            print(f"register guard: error: {error!r}", file=sys.stderr)
            sys.exit(ERROR)
    out = Path(argv[1]).resolve()
    shaders = Path(argv[2] if len(argv) > 2 else str(SHADER_FOLDER)).resolve()
    label = argv[3] if len(argv) > 3 else "head"
    decoder = decoder_folder()
    if decoder is None:
        sys.exit("Set APPLEGPU to a clone of https://github.com/dougallj/applegpu at 28dd80468d4a6c94fa4dac34ed488196f5fedc5f.")
    count = counter(import_decoder(decoder), shaders, out)
    for shader in SHADERS:
        register, lines = count(shader)
        print(f"{label} {shader.name} r{register} {len(lines)} instructions", flush=True)


if __name__ == "__main__":
    main(sys.argv)
