"""Tool code of t2-x53j. liveness.py <native pipeline archive>: the live 32-bit registers of the fragment code in an
archive that tools/shader_registers.py wrote (<out folder>/<shader>.native.metallib), with the applegpu decoder of the dev
shell (APPLEGPU). Run it in the dev shell from the repository root:

    nix develop --command python3 docs/research/t2-x53j-register-room/tools/liveness.py <out>/direct_key_fill.frag.native.metallib

It decodes every instruction with its address, builds the control flow from the jumps (jmp_exec_none and jmp_exec_any
go to their target or fall through, as the execution mask allows both), and solves the live registers backward to a
fixed point. A register's definition and uses come from its operands: the destination is the first operand, but the
seventh of texture_sample and texture_load and the fourth of device_load; jumps, stores and the execution mask's
instructions define none, except r0, the mask's counter. Half registers (r5l, r5h) count as their whole register. It
prints the most live registers and where, the 15 instructions with the most, and each register live at the peak with its
last definition before it in the code's order. An approximation: it is a reading aid for the peak, not the driver's
allocation.
"""
import importlib.util, os, re, sys
from pathlib import Path

spec = importlib.util.spec_from_file_location("sr", Path(__file__).resolve().parents[4] / "tools" / "shader_registers.py")
sr = importlib.util.module_from_spec(spec); spec.loader.exec_module(sr)
sys.path.insert(0, os.environ["APPLEGPU"])
import applegpu

code = sr.fragment_code(Path(sys.argv[1]))
ins = []  # (pc, size, text)
pc = 0
while pc < len(code):
    n = applegpu.opcode_to_number(code[pc:pc + 16])
    d = next(d for d in applegpu.instruction_descriptors if d.matches(n))
    size = d.decode_size(n)
    ins.append((pc, size, str(d.disassemble(n, pc=pc))))
    pc += size
index = {p: k for k, (p, _, _) in enumerate(ins)}

REG = re.compile(r"(?<![A-Za-z0-9])r(\d+)")


def regs(op):
    return {int(x) for x in REG.findall(op)}


NODEST = ("jmp", "stop", "wait", "pop_exec", "push_exec", "if_", "else_", "while_", "break", "call", "ret",
          "stack_store", "st_tile", "signal_pix", "wait_pix", "uniform_store", "device_store", "threadgroup_store")


def du(text):
    parts = text.split(None, 1)
    op = parts[0]
    a = [x.strip() for x in parts[1].split(",")] if len(parts) > 1 else []
    if op.startswith(("texture_sample", "texture_load")):
        return regs(a[6]), set().union(*[regs(x) for i, x in enumerate(a) if i != 6])
    if op.startswith("device_load"):
        return regs(a[3]), set().union(*[regs(x) for i, x in enumerate(a) if i != 3])
    if op.startswith(("if_", "else_", "while_", "pop_exec", "push_exec")):
        r0 = {0} if a and a[0].startswith("r0") else set()
        return r0, set().union(*[regs(x) for x in a]) if a else set()
    if op.startswith(NODEST):
        return set(), set().union(*[regs(x) for x in a]) if a else set()
    if not a:
        return set(), set()
    return regs(a[0]), set().union(*[regs(x) for x in a[1:]]) if len(a) > 1 else set()


D, U, S = [], [], []
for k, (p, size, t) in enumerate(ins):
    d, u = du(t)
    D.append(d); U.append(u)
    succ = [k + 1] if k + 1 < len(ins) and not t.startswith("stop") else []
    m = re.match(r"(jmp_exec_none|jmp_exec_any)\s+0x([0-9A-Fa-f]+)", t)
    if m:
        target = int(m.group(2), 16)
        if target in index:
            succ.append(index[target])
    S.append(succ)
live_in = [set() for _ in ins]
changed = True
while changed:
    changed = False
    for k in range(len(ins) - 1, -1, -1):
        out = set().union(*[live_in[s] for s in S[k]]) if S[k] else set()
        new = U[k] | (out - D[k])
        if new != live_in[k]:
            live_in[k] = new; changed = True
counts = [len(x) for x in live_in]
peak = max(range(len(ins)), key=lambda k: counts[k])
print("instructions", len(ins), "max live", counts[peak], "at line", peak + 1, ins[peak][2][:90])
top = sorted(range(len(ins)), key=lambda k: -counts[k])[:15]
for k in sorted(top):
    print("  line", k + 1, "live", counts[k], ins[k][2][:80])
# the live registers at the peak and their last linear definition
for r in sorted(live_in[peak]):
    for j in range(peak - 1, -1, -1):
        if r in D[j]:
            print(f"r{r}: line {j + 1}: {ins[j][2][:100]}")
            break
    else:
        print(f"r{r}: input")
