# films.py: the motion results of main (out-m-mot) and the fix (out-f-mot) side by side.
import json, os, glob
R='/private/tmp/t7-3e22/x2/runs/'
C='/private/tmp/t7-3e22/x2/cloud/'
names=sorted({os.path.basename(p)[:-5] for p in glob.glob(R+'out-?-mot/results/motion/*.json')+glob.glob(C+'j[DM]-*/out-x/results/motion/*.json')})
def load(t,n):
    p=f'{R}out-{t}-mot/results/motion/{n}.json'
    if os.path.exists(p): return json.load(open(p))
    c=glob.glob(f"{C}j[DM]-*/out-{'x' if t=='f' else 'm'}/results/motion/{n}.json")
    return dict(json.load(open(c[0])), cloud=True) if c else None
print(f"{'film':26s} {'flicker m/f':>17s} {'grain m/f':>17s} {'blotch m/f':>17s} {'level m/f':>17s}  failed m | f")
for n in names:
    m,f=load('m',n),load('f',n)
    if not m or not f: print(n,'incomplete'); continue
    def pair(k):
        a,b=m[k],f[k]
        if a is None or b is None: return f"{str(a):>8s}{str(b):>8s} "
        return f"{a:8.4f}{b:8.4f}" + ('*' if (abs(b)>abs(a)*1.10+1e-4) else ' ')
    print(f"{n:26s} {pair('flicker')} {pair('grainFlicker')} {pair('blotch')} {pair('level')}  {','.join(m['failed']) or '-'} | {','.join(f['failed']) or '-'}{'  (cloud)' if f.get('cloud') else ''}")
