# Tool code (t1-gout, step 3). stills.py: the survey's still cases on two builds, to find the stills that the remedy
# changes and to show each change beside main.
#
#   stills.py prepare <out>: for each still case of tools/renderer-survey/cases.json whose frame lies within its script's
#     first MAX_FRAMES frames, writes <out>/scripts/<script>: the script cut after the step that makes the last frame that
#     any of its cases names. A frame counts from 0 over the captures and film frames, as the survey counts them. It writes
#     <out>/cases.txt, one line "name script frame" per kept case, and prints the cases that it leaves out.
#   stills.py frames <out> <run folder> <script>: prints, for each kept case of <script>, "name path" of its frame in the
#     run's output folder (a capture keeps its name; film frame k of film f is f-<k>.png).
#   stills.py compare <out> <main folder> <fix folder> <sheet folder>: per case, the display-linear luminance of both
#     frames, the count of pixels whose luminance differs by more than 1 % of the frame's 99th percentile, and the
#     largest difference; for each changed case, a sheet of main beside the fix at 1:1 around the box of 192 x 192 pixels
#     whose sum of differences is largest, and the difference at a gain of 8. Writes <out>/compare.txt.
import json, os, sys
MAX_FRAMES = 600
R = os.path.abspath(os.path.join(os.path.dirname(__file__), '../../../..'))


def frames_of(step):
    return 1 if step.get('do') == 'capture' else step.get('frames', 0) if step.get('do') == 'film' else 0


def locate(steps, frame):
    """The index of the step that makes frame `frame`, and the frame's file name."""
    k = 0
    for i, st in enumerate(steps):
        n = frames_of(st)
        if frame < k + n:
            return i, (st['name'] if st['do'] == 'capture' else f"{st['name']}-{frame - k:06d}")
        k += n
    raise ValueError(frame)


def prepare(out):
    cases = json.load(open(f'{R}/tools/renderer-survey/cases.json'))['stills']
    os.makedirs(f'{out}/scripts', exist_ok=True)
    keep, last = [], {}
    for c in cases:
        if 'script' not in c:
            print('left out (no script):', c['name'])
            continue
        s = json.load(open(f"{R}/scripts/{c['script']}"))
        if c['frame'] >= MAX_FRAMES:
            print('left out (frame past', MAX_FRAMES, '):', c['name'], c['frame'])
            continue
        i, name = locate(s['steps'], c['frame'])
        keep.append((c['name'], c['script'], c['frame'], name))
        last[c['script']] = max(last.get(c['script'], 0), i)
    for script, i in last.items():
        s = json.load(open(f'{R}/scripts/{script}'))
        s['steps'] = s['steps'][:i + 1]
        st = s['steps'][-1]
        if st.get('do') == 'film':
            needed = max(f for _, sc, f, _ in keep if sc == script) - sum(frames_of(x) for x in s['steps'][:-1])
            st['frames'] = needed + 1
        json.dump(s, open(f'{out}/scripts/{script}', 'w'), indent=1)
    with open(f'{out}/cases.txt', 'w') as stream:
        for name, script, frame, file in keep:
            stream.write(f'{name} {script} {frame} {file}\n')
    print(len(keep), 'cases in', len(last), 'scripts')


def cases_of(out):
    return [line.split() for line in open(f'{out}/cases.txt')]


def luminance(path):
    import numpy as np
    from PIL import Image
    a = np.asarray(Image.open(path).convert('RGB')).astype(float) / 255
    return a, np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4) @ [0.2126, 0.7152, 0.0722]


def compare(out, main, fix, sheets):
    import numpy as np
    from PIL import Image, ImageDraw
    os.makedirs(sheets, exist_ok=True)
    lines = []
    for name, script, frame, file in cases_of(out):
        a = f'{main}/{script[:-5]}/{file}.png'
        b = f'{fix}/{script[:-5]}/{file}.png'
        if not (os.path.exists(a) and os.path.exists(b)):
            lines.append(f'{name:24} missing')
            continue
        ra, ya = luminance(a)
        rb, yb = luminance(b)
        d = yb - ya
        p99 = max(float(np.percentile(ya, 99)), 1e-4)
        changed = int((np.abs(d) > 0.01 * p99).sum())
        lines.append(f'{name:24} changed {changed:7d} px  most {np.abs(d).max() / p99:6.3f} of p99  '
                     f'mean {d.mean() / p99:+.5f} of p99')
        if changed == 0:
            continue
        S = 192
        k = np.abs(d).cumsum(0).cumsum(1)
        k = np.pad(k, ((1, 0), (1, 0)))
        sums = k[S:, S:] - k[:-S, S:] - k[S:, :-S] + k[:-S, :-S]
        y0, x0 = np.unravel_index(np.argmax(sums), sums.shape)
        crop = lambda img: Image.fromarray((img[y0:y0 + S, x0:x0 + S] * 255).astype(np.uint8))
        g = np.clip(0.5 + 8 * d / p99, 0, 1)
        sheet = Image.new('RGB', (3 * S + 20, S + 40), 'white')
        sheet.paste(crop(ra), (0, 30)); sheet.paste(crop(rb), (S + 10, 30))
        sheet.paste(Image.fromarray((g[y0:y0 + S, x0:x0 + S] * 255).astype(np.uint8)).convert('RGB'), (2 * S + 20, 30))
        dr = ImageDraw.Draw(sheet)
        dr.text((4, 4), f'{name} at ({x0}, {y0}): main | fix | difference x8 (grey 0)', fill='black')
        sheet.save(f'{sheets}/{name}.png')
    with open(f'{out}/compare.txt', 'w') as stream:
        stream.write('\n'.join(lines) + '\n')
    print('\n'.join(lines))


if __name__ == '__main__':
    if sys.argv[1] == 'prepare':
        prepare(sys.argv[2])
    elif sys.argv[1] == 'compare':
        compare(*sys.argv[2:6])
