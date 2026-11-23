# SCRATCH t0-t2oq: tools/shader_registers.py's count for a compute shader: a compute pipeline for applegpu_g13s, and the
# machine code of its kernel. python3 count_compute.py <out> <folder> <shader> <label>
import sys, json, importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location("sr", Path(__file__).resolve().parents[4] / "tools" / "shader_registers.py")
sr = importlib.util.module_from_spec(spec); spec.loader.exec_module(sr)
out, shaders, name, label = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], sys.argv[4]
out.mkdir(parents=True, exist_ok=True)
sr.run("glslc", "--target-env=vulkan1.3", "-O", "-Werror", "-I", shaders, "-o", out / f"{name}.spv", shaders / name)
library = sr.metal_library(out / f"{name}.spv", out, name)
config = {"libraries": {"paths": [{"label": "Compute", "path": str(library)}]},
          "pipelines": {"compute_pipelines": [{"compute_function": "alias:Compute#main0"}]}}
config_path = out / f"{name}.mtlp-json"
config_path.write_text(json.dumps(config, indent=2) + "\n")
archive = out / f"{name}.native.metallib"
sr.run(sr.xcrun("-f", "metal-tt"), config_path, "-arch", "applegpu_g13s", "-platform_version", "macos", "26.0", "27.0",
       "-sysroot", sr.xcrun("--show-sdk-path"), "-o", archive)
data = archive.read_bytes()
sections, _ = sr.macho_sections_and_symbols(data)
print("sections:", [(s["segment"], s["name"]) for s in sections])
kernel = next(s for s in sections if s["segment"] == "__TEXT" and s["name"] in ("__compute", "__kernel"))
image = data[kernel["offset"]:kernel["offset"] + kernel["length"]]
inner, symbols = sr.macho_sections_and_symbols(image)
print("symbols:", [s["name"] for s in symbols][:10])
text = next(s for s in inner if s["name"] == "__text")
main = next(s for s in symbols if s["name"] == "_agc.main")
code = image[text["offset"] + main["address"]:text["offset"] + text["length"]]
lines = sr.disassemble(sr.import_decoder(sr.decoder_folder()), code)
(out / f"{name}.disasm").write_text("\n".join(lines) + "\n")
print(label, name, f"r{sr.highest_register(lines)}", len(lines), "instructions")
