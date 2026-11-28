import errno,os,json,pathlib,platform,sys,shutil,hashlib
from importlib import util
P=pathlib.Path
def arg(n): return P(os.environ[n])
s=util.spec_from_file_location('handler',arg('handlerfile'))
h=util.module_from_spec(s);sys.modules['handler']=h;s.loader.exec_module(h)
work=P.cwd()/'work'; (work/'harness'/'tools').mkdir(parents=True)
shutil.copytree(arg('payload'),work/'harness/tools/camera-cut-pristine')
old=arg('oldcmd').read_bytes(); new=arg('shortcmd').read_bytes()
report=dict(system=platform.system(),machine=platform.machine(),page=os.sysconf('SC_PAGE_SIZE'),old_bytes=len(old)+1,new_bytes=len(new)+1)
assert (len(old),len(new))==(409197,418)
try:
 h.run_commands([old.decode()],work,[sys.executable,'-c','raise SystemExit(77)'],20,[],bytearray())
except OSError as e:
 assert e.errno==errno.E2BIG
 report['old_errno']=e.errno
else: raise ValueError('Old argument accepted by kernel')
log=bytearray()
rc=h.run_commands([new.decode()],work,[os.environ['shellfile'],'-c'],30,[],log)
assert rc=='failed' and b'INERT: new Issue #4 paid approval' in log,bytes(log[-1600:])
manifest=json.loads((work/'harness/tools/camera-cut-pristine/payload-manifest.json').read_bytes())
assert all(hashlib.sha256((work/k).read_bytes()).hexdigest()==v for k,v in manifest.items())
report.update(verified_files=len(manifest),new_command_reached_inert_gate=True)
arg('out').write_text(json.dumps(report))
