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


class TheCommand(unittest.TestCase):
    def test_refuses_to_run_without_the_decoder(self):
        env = {k: v for k, v in os.environ.items() if k != "APPLEGPU"}
        result = subprocess.run([sys.executable, "-I", str(Path(__file__).with_name("shader_registers.py")), "/tmp/x"],
                                capture_output=True, text=True, env=env)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("APPLEGPU", result.stderr)


if __name__ == "__main__":
    unittest.main()
