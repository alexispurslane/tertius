#!/usr/bin/env python3
"""Tool code. The register count of shader_registers.py from disassembled lines, and its refusals.

    nix develop --command python3 -I tools/test_shader_registers.py

The standard library only. The file loads shader_registers.py by its path, because python3 -I leaves the script's
folder out of the import path. No test compiles a shader or needs the Metal toolchain or the decoder.
"""

import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("shader_registers", Path(__file__).with_name("shader_registers.py"))
shader_registers = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shader_registers)


class FakeDescriptor:
    """An instruction of the fake decoder: words below 0x80 decode as two-byte instructions that name register rN."""

    def matches(self, number):
        return number < 0x80

    def decode_size(self, number):
        return 2

    def disassemble(self, number, pc):
        return f"fadd32 r{number}, r0, 1.0"


class FakeDecoder:
    instruction_descriptors = [FakeDescriptor()]

    @staticmethod
    def opcode_to_number(code):
        return code[0]


class TheHighestRegister(unittest.TestCase):
    def test_is_the_largest_register_index_that_any_instruction_names(self):
        lines = ["fmadd32.sat r3, r115, 0.125, -1.0", "mov_imm r7, 1042983595, 0b0", "jmp_exec_none 0x1a"]
        self.assertEqual(shader_registers.highest_register(lines), 115)

    def test_counts_a_half_register_by_its_index(self):
        self.assertEqual(shader_registers.highest_register(["fadd16 r116l, r2h, r3l"]), 116)

    def test_counts_both_registers_of_a_pair_and_leaves_out_uniform_registers(self):
        lines = ["uniform_store u120, r4", "device_load 0, i32, xy, r9_r10, u2_u3, 0, signed"]
        self.assertEqual(shader_registers.highest_register(lines), 10)

    def test_is_minus_one_for_code_that_names_no_register(self):
        self.assertEqual(shader_registers.highest_register(["stop", "jmp_exec_none 0x40"]), -1)


class TheDisassembly(unittest.TestCase):
    def test_gives_one_line_per_instruction(self):
        lines = shader_registers.disassemble(FakeDecoder, bytes([5, 0, 7, 0]))
        self.assertEqual(lines, ["fadd32 r5, r0, 1.0", "fadd32 r7, r0, 1.0"])

    def test_refuses_code_with_a_word_that_the_decoder_does_not_know(self):
        with self.assertRaisesRegex(RuntimeError, "offset 0x2"):
            shader_registers.disassemble(FakeDecoder, bytes([5, 0, 0x90, 0]))


def shader_folder(files):
    """A temporary folder that holds `files`, a map of file names to their text."""
    folder = tempfile.TemporaryDirectory()
    for name, text in files.items():
        (Path(folder.name) / name).write_text(text)
    return folder


INCLUDES = {
    "lit.frag": '#version 460\n#include "surface.glsl"\nvoid main() {}\n',
    "surface.glsl": '#ifndef SURFACE\n#define SURFACE\n#include "frame.glsl"\n#endif\n',
    "frame.glsl": '#ifndef FRAME\n#define FRAME\n  #  include "surface.glsl"\n#endif\n',
    "sky.glsl": "float sky;\n",
}


class TheIncludeClosure(unittest.TestCase):
    def test_holds_the_shader_and_every_file_that_it_includes_through_any_depth(self):
        with shader_folder(INCLUDES) as folder:
            closure = shader_registers.include_closure(Path(folder), "lit.frag")
        self.assertEqual(closure, {"lit.frag", "surface.glsl", "frame.glsl"})

    def test_leaves_out_a_file_that_no_include_reaches(self):
        with shader_folder(INCLUDES) as folder:
            self.assertNotIn("sky.glsl", shader_registers.include_closure(Path(folder), "lit.frag"))


class TheStagedFiles(unittest.TestCase):
    def test_ask_for_a_count_of_the_shader_whose_closure_holds_one_of_them(self):
        with shader_folder(INCLUDES) as folder:
            touched = shader_registers.guarded_shaders_touched(
                ["README.md", "src/Tertius.Rendering/Shaders/frame.glsl"], Path(folder), ["lit.frag"])
        self.assertEqual(touched, ["lit.frag"])

    def test_outside_every_closure_ask_for_no_count(self):
        staged = ["src/Tertius.Rendering/Shaders/sky.glsl", "tools/frame.glsl", "src/Tertius.Rendering/Pass.cs"]
        with shader_folder(INCLUDES) as folder:
            self.assertEqual(shader_registers.guarded_shaders_touched(staged, Path(folder), ["lit.frag"]), [])


class TheVerdict(unittest.TestCase):
    def test_passes_a_register_two_under_the_limit_with_its_room_and_no_warning(self):
        verdict = shader_registers.verdict_of("direct_key_fill.frag", 113)
        self.assertEqual(verdict.exit_code, shader_registers.PASS)
        self.assertIn("pass: direct_key_fill.frag is at register 113, the limit is 115, room 2 registers", verdict.text)
        self.assertNotIn("warning", verdict.text)

    def test_passes_a_register_with_less_than_two_of_room_with_a_warning(self):
        for register, room in ((114, 1), (115, 0)):
            verdict = shader_registers.verdict_of("direct_key_fill.frag", register)
            self.assertEqual(verdict.exit_code, shader_registers.PASS)
            self.assertIn(f"room {room} register", verdict.text)
            self.assertIn("warning", verdict.text)

    def test_fails_a_register_above_the_limit_and_names_what_it_costs(self):
        verdict = shader_registers.verdict_of("direct_key_fill.frag", 116)
        self.assertEqual(verdict.exit_code, shader_registers.FAIL)
        self.assertIn("fail: direct_key_fill.frag is at register 116, above its limit of 115", verdict.text)
        self.assertIn("384", verdict.text)

    def test_does_not_apply_without_the_metal_toolchain_and_never_reads_as_a_pass(self):
        verdict = shader_registers.applicability(metal_tt=None, decoder="/opt/applegpu")
        self.assertEqual(verdict.exit_code, shader_registers.NOT_APPLICABLE)
        self.assertTrue(verdict.text.startswith("does not apply:"))
        self.assertIn("metal-tt", verdict.text)
        self.assertNotIn("pass", verdict.text)

    def test_does_not_apply_without_the_decoder(self):
        verdict = shader_registers.applicability(metal_tt="/usr/bin/metal-tt", decoder=None)
        self.assertEqual(verdict.exit_code, shader_registers.NOT_APPLICABLE)
        self.assertIn("APPLEGPU", verdict.text)

    def test_with_both_tools_lets_the_count_decide(self):
        self.assertIsNone(shader_registers.applicability(metal_tt="/usr/bin/metal-tt", decoder="/opt/applegpu"))

    def test_has_one_exit_code_for_each_result(self):
        codes = {shader_registers.PASS, shader_registers.FAIL, shader_registers.ERROR, shader_registers.NOT_APPLICABLE}
        self.assertEqual(len(codes), 4)
        self.assertEqual(shader_registers.PASS, 0)


class TheCommand(unittest.TestCase):
    def test_guard_compiles_nothing_when_no_staged_file_is_in_a_guarded_closure(self):
        with tempfile.TemporaryDirectory() as out:
            result = subprocess.run([sys.executable, "-I", str(Path(__file__).with_name("shader_registers.py")),
                                     "guard", out, "README.md", "tools/README.md"],
                                    capture_output=True, text=True, cwd=Path(__file__).parent.parent)
            self.assertEqual(result.returncode, shader_registers.NOT_APPLICABLE, result.stderr)
            self.assertIn("does not apply: no staged file is in the include closure of a guarded shader", result.stdout)
            self.assertFalse(any(Path(out).glob("*.spv")))

    def test_refuses_to_run_without_the_decoder(self):
        env = {k: v for k, v in os.environ.items() if k != "APPLEGPU"}
        result = subprocess.run([sys.executable, "-I", str(Path(__file__).with_name("shader_registers.py")), "/tmp/x"],
                                capture_output=True, text=True, env=env)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("APPLEGPU", result.stderr)


if __name__ == "__main__":
    unittest.main()
