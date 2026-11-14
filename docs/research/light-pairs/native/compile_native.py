# Research tool only. Compile existing shader binaries offline; submit no GPU work.
from pathlib import Path
import json
import subprocess
import sys

root = Path('/private/tmp/t2-mlvy/native/shaders')
toolchain = Path('/var/run/com.apple.security.cryptexd/mnt/com.apple.MobileAsset.MetalToolchain-v27.1.266.1.k6mwDY/Metal.xctoolchain/usr/bin')
sdk = '/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk'

def run(*args):
    subprocess.run([str(x) for x in args], check=True)

def library(source, name, constants=None, cross=False):
    frozen = root / (name + '.spv')
    metal = root / (name + '.metal')
    lib = root / (name + '.metallib')
    options = ['--set-spec-const-default-value', constants] if constants else []
    run('spirv-opt', *options, '--freeze-spec-const', source, '-o', frozen)
    run('spirv-dis', frozen, '-o', root / (name + '.spvasm'))
    if cross:
        converter = '/nix/store/1qbw138456azg7xxzdkjbywxw7d14imz-spirv-cross-1.4.357.0/bin/spirv-cross'
        run(converter, frozen, '--msl', '--msl-version', '30100', '--msl-argument-buffers',
            '--msl-argument-buffer-tier', '1', '--msl-device-argument-buffer', '1',
            '--output', metal)
    else:
        run('MoltenVKShaderConverter', '-si', frozen, '-mo', metal, '-mab', '-q')
    if not any(stage in metal.read_text() for stage in ['vertex ', 'fragment ', 'kernel ']):
        raise RuntimeError('Converter emitted no shader stage for ' + name)
    run(toolchain / 'metal', '-isysroot', sdk, metal, '-o', lib)
    return lib

source_root = Path('/private/tmp/t2-mlvy')
shader_path = Path('src/Tertius.Rendering/obj/Release/net11.0/shaders')
vertex = root / 'vertex.metallib'
if not vertex.exists():
    vertex = library(source_root / 'src-maind' / shader_path / 'fullscreen.vert.spv', 'vertex')

def pipeline(name, fragment, formats, vertex=vertex, extra=None):
    config = {
        'libraries': {'paths': [
            {'label': 'Vertex', 'path': str(vertex)},
            {'label': 'Fragment', 'path': str(fragment)}]},
        'pipelines': {'render_pipelines': [{
            'vertex_function': 'alias:Vertex#main0',
            'fragment_function': 'alias:Fragment#main0',
            'color_attachments': [{'pixel_format': f} for f in formats],
            **(extra or {})
        }]}
    }
    config_path = root / (name + '.mtlp-json')
    config_path.write_text(json.dumps(config, indent=2) + '\n')
    run(toolchain / 'metal-tt', config_path, '-arch', 'applegpu_g13s',
        '-platform_version', 'macos', '26.0', '27.0', '-sysroot', sdk,
        '-o', root / (name + '.native.metallib'))
    print('COMPILED', name, flush=True)


if __name__ == '__main__':
    assert sys.argv[1] == 'applegpu_g13s'
    for variant in sys.argv[2:]:
        fragment = library(source_root / ('src-' + variant) / shader_path / 'direct.frag.spv', variant + '-native-input')
        pipeline(variant, fragment, ['RGBA16Float', 'RGBA16Float'])
