"""Inspect offline M1 binaries with the pinned applegpu decoder. No GPU work."""
from pathlib import Path
from collections import Counter
import json
import re
import struct
import sys

root = Path('/private/tmp/t2-mlvy/native')
sys.path.insert(0, '/private/tmp/pair-tile/applegpu')
import applegpu


def sections_and_symbols(data):
    assert struct.unpack_from('<I', data)[0] == 0xfeedfacf
    sections = []
    symbols = []
    offset = 32
    for _ in range(struct.unpack_from('<I', data, 16)[0]):
        command, size = struct.unpack_from('<II', data, offset)
        if command == 25:
            count = struct.unpack_from('<I', data, offset + 64)[0]
            for i in range(count):
                position = offset + 72 + 80 * i
                name = data[position:position + 16].split(b'\0')[0].decode()
                segment = data[position + 16:position + 32].split(b'\0')[0].decode()
                address, length, file_offset = struct.unpack_from('<QQI', data, position + 32)
                sections.append(dict(name=name, segment=segment, address=address,
                                     length=length, offset=file_offset))
        if command == 2:
            table, count, strings, _ = struct.unpack_from('<IIII', data, offset + 8)
            for i in range(count):
                string_offset, _, section, _, address = struct.unpack_from('<IBBHQ', data, table + 16 * i)
                name = data[strings + string_offset:].split(b'\0')[0].decode()
                symbols.append(dict(name=name, section=section, address=address))
        offset += size
    return sections, symbols


def inspect(path):
    data = path.read_bytes()
    sections, symbols = sections_and_symbols(data)
    section = next(s for s in sections if s['name'] == '__text')
    main = next(s for s in symbols if s['name'] == '_agc.main')
    code = data[section['offset'] + main['address']:section['offset'] + section['length']]
    path.with_suffix('.code.bin').write_bytes(code)
    output = []
    counts = Counter()
    registers = set()
    failed = []
    offset = 0
    while offset < len(code):
        number = applegpu.opcode_to_number(code[offset:offset + 16])
        descriptor = next((d for d in applegpu.instruction_descriptors if d.matches(number)), None)
        length = 2
        if descriptor:
            length = descriptor.decode_size(number)
            mnemonic = descriptor.decode_mnem(number)
            instruction = str(descriptor.disassemble(number, pc=offset + main['address']))
            counts[mnemonic] += 1
            registers.update(int(x) for x in re.findall(r'(?<![A-Za-z0-9])r(\d+)', instruction))
        else:
            failed.append(offset)
            instruction = '<disassembly failed>'
        output.append(f'{offset + main["address"]:06x}: {code[offset:offset+length].hex():24} {instruction}')
        offset += length
    path.with_suffix('.disasm').write_text('\n'.join(output) + '\n')
    return dict(variant=path.name.removesuffix('.fragment.o'),
                fragment_object_bytes=len(data), text_section_bytes=section['length'],
                main_start_bytes=main['address'], main_bytes=len(code),
                decoded_instructions=sum(counts.values()), undecoded_words=len(failed),
                failed_offsets=failed[:40], max_register_index=max(registers, default=-1),
                distinct_register_indices=len(registers), mnemonic_counts=dict(counts))


only = set(sys.argv[1:])
for archive in sorted((root / 'shaders').glob('*.native.metallib')):
    if only and archive.name.removesuffix('.native.metallib') not in only:
        continue
    fragment_path = archive.with_name(archive.name.replace('.native.metallib', '.fragment.o'))
    if fragment_path.exists() and fragment_path.stat().st_mtime >= archive.stat().st_mtime:
        continue
    data = archive.read_bytes()
    sections, _ = sections_and_symbols(data)
    fragment_section = next(s for s in sections if s['name'] == '__fragment' and s['segment'] == '__TEXT')
    start = fragment_section['offset']
    fragment_path.write_bytes(data[start:start + fragment_section['length']])

results = json.loads((root / 'native-inspection.json').read_text()) if only else []
for path in sorted((root / 'shaders').glob('*.fragment.o')):
    name = path.name.removesuffix('.fragment.o')
    if only and name not in only:
        continue
    results = [item for item in results if item['variant'] != name]
    results.append(inspect(path))
results.sort(key=lambda item: item['variant'])
(root / 'native-inspection.json').write_text(json.dumps(results, indent=2) + '\n')
for result in results:
    print(json.dumps({k: v for k, v in result.items() if k != 'mnemonic_counts'}))
