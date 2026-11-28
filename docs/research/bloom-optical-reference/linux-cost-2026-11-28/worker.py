import subprocess,json,hashlib,difflib,os,time,urllib.request
from pathlib import Path
p=Path('preflight'); p.mkdir()
def run(args,log,cwd=None):
 with open(log,'w') as f:
  subprocess.run(args,cwd=cwd,stdout=f,stderr=subprocess.STDOUT,check=True)
run(['nvidia-smi','--query-gpu=name,uuid,driver_version,pci.bus_id','--format=csv'],p/'device.csv')
# The sidecar approves this hash-identical staging mirror for the pinned NOAA terrain input.
(p/'stage.json').write_text(json.dumps(dict(stage='approved_terrain_mirror_fetch', started_unix=time.time())))
entry=json.loads(Path('before/content/tertius.demo/assets/blue-danube/earth/sources.json').read_text())['files']['elevation.dods']
assert entry['bytes']==9370338 and entry['sha256']=='sha256:a14135cf20fe2d0a7a18af72a0999483735c1f7e47cf3dbdc24d819beda61b04'
started=time.monotonic()
try:
 with urllib.request.urlopen(entry['stagingMirror'],timeout=35) as reply:
  data=reply.read(entry['bytes']+1)
 digest='sha256:'+hashlib.sha256(data).hexdigest()
 assert len(data)==entry['bytes'] and digest==entry['sha256']
 staging=Path('source-staging').resolve();staging.mkdir();(staging/'elevation.dods').write_bytes(data)
 (p/'approved-mirror.json').write_text(json.dumps(dict(url=entry['stagingMirror'],bytes=len(data),sha256=digest,seconds=time.monotonic()-started,verified=True)))
except Exception as error:
 (p/'approved-mirror.json').write_text(json.dumps(dict(url=entry['stagingMirror'],error=str(error),seconds=time.monotonic()-started,verified=False)))
 raise
# Canonical asset provisioning, once. Both trees consume this same cooked content root.
asset_start=time.monotonic()
(p/'stage.json').write_text(json.dumps(dict(stage='canonical_asset_setup', started_unix=time.time())))
try:
 run(['bash','tools/blue-danube-assets/setup.sh','--staging',str(staging)],p/'asset-setup.log','before')
finally:
 (p/'asset-setup-duration.json').write_text(json.dumps(dict(seconds=time.monotonic()-asset_start)))
content=Path('before/content').resolve(); os.environ['TERTIUS_CONTENT_ROOT']=str(content)
art=content/'tertius.demo/bin/assets/blue-danube'
files=sorted([*art.rglob('model.glb'),art/'earth/material.zip'])
assert len(files)==11 and all(f.is_file() for f in files)
entries=[dict(path=str(f.relative_to(art)),bytes=f.stat().st_size,sha256=hashlib.sha256(f.read_bytes()).hexdigest()) for f in files]
manifest_text=''.join(e['sha256']+'  '+e['path']+'\n' for e in entries)
mac_hash='0bc4de65f352f451dd55aa24698e78cf01b62d4cfeabeb00450767868835132b'
asset_hash=hashlib.sha256(manifest_text.encode()).hexdigest()
(p/'asset-manifest.txt').write_text(manifest_text)
(p/'asset-manifest.json').write_text(json.dumps(dict(content_root=str(content),entries=entries,manifest_sha256=asset_hash,mac_canonical_manifest_sha256=mac_hash,matches_mac_cooked_bytes=asset_hash==mac_hash,shared_by=['before','after']),indent=2))
(p/'stage.json').write_text(json.dumps(dict(stage='assets_ready_extent_diagnostics', started_unix=time.time())))
script='scripts/temporal-upscale-timing.json'
common=['--size','1920x1080','--video','none','--exposure','6.0043726','--bloom','on','--tone-curve','aces','--screen-trace','on','--environment-map','on']
scenarios={'native':['--upscaling','off'],'mapped':['--upscaling','fsr3','--render-scale','0.6666667']}
manifest=[]
for tree in ['before','after']:
 path=Path(tree)/'src/Tertius.Rendering/VulkanRenderer.cs'; original=path.read_bytes(); text=original.decode()
 sha=subprocess.check_output(['git','rev-parse','HEAD'],cwd=tree,text=True).strip()
 assert hashlib.sha256((Path(tree)/script).read_bytes()).hexdigest()=='f881515e8d04e118ef2820e6c6068946bb70cc6601b59f3111895b34dd0633e4'
 anchor='        long number = FrameNumber;'
 assert text.count(anchor)==1
 probe=anchor+'\n        System.Console.Error.WriteLine($"ACTIVE_EXTENT {number} {pending.RenderSize.Width} {pending.RenderSize.Height} {pending.OutputSize.Width} {pending.OutputSize.Height}");'
 text=text.replace(anchor,probe)
 anchor2='        upscaling = UpscalingOf(options, headless);'
 assert text.count(anchor2)==1
 probe2=anchor2+'\n        System.Console.Error.WriteLine("RESOLVED_OPTIONS " + System.Text.Json.JsonSerializer.Serialize(new { options.Width, options.Height, options.Validation, options.FramesInFlight, options.CapturesInFlight, options.VSync, options.ShadowMapSize, options.CascadedLights, LocalShadows = options.LocalShadows.ToString(), LightPairs = options.LightPairs.ToString(), options.Impostors, options.ImpostorTiles, Exposure = options.Exposure is FixedExposure fixedExposure ? fixedExposure.ToString() : "auto", ToneCurve = options.ToneCurve.ToString(), options.OutputDither, Bloom = options.Bloom is Bloom glow ? glow.ToString() : "off", ScreenTrace = options.ScreenTrace.ToString(), options.LightVolume, options.WarmUpFrames, Subsurface = options.Subsurface.ToString(), EnvironmentMap = options.EnvironmentMap.ToString(), options.Deformation, options.PassTimestamps, Upscaling = upscaling.ToString(), Scale = upscaling.LargestScale, Headless = headless }));'
 text=text.replace(anchor2,probe2)
 diff=''.join(difflib.unified_diff(original.decode().splitlines(True),text.splitlines(True),fromfile=tree+'/original',tofile=tree+'/diagnostic'))
 (p/(tree+'.patch')).write_text(diff)
 try:
  path.write_text(text)
  run(['dotnet','build','src/Tertius.Demo','-c','Release','-nologo','-v:q'],p/(tree+'-diagnostic-build.log'),tree)
  for scenario,args in scenarios.items():
   out=p/(tree+'-'+scenario)
   run(['src/Tertius.Demo/bin/Release/net11.0/Tertius.Demo','--script',script,'--headless','--out','../'+str(out),*common,*args],p/(tree+'-'+scenario+'.log'),tree)
   lines=(p/(tree+'-'+scenario+'.log')).read_text().splitlines()
   frames=[list(map(int,l.split()[1:])) for l in lines if l.startswith('ACTIVE_EXTENT ')]
   resolved=[json.loads(l.split(' ',1)[1]) for l in lines if l.startswith('RESOLVED_OPTIONS ')]
   expected=[1920,1080] if scenario=='native' else [1280,720]
   assert frames and all(f[1:3]==expected and f[3:5]==[1920,1080] for f in frames),(tree,scenario,frames)
   assert len(resolved)==1
   manifest.append(dict(tree=tree,commit=sha,scenario=scenario,submitted_frames=frames,resolved_options=resolved[0],command=common+args,diagnostic_diff_sha256=hashlib.sha256(diff.encode()).hexdigest()))
 finally:
  path.write_bytes(original)
  assert path.read_bytes()==subprocess.check_output(['git','show','HEAD:src/Tertius.Rendering/VulkanRenderer.cs'],cwd=tree)
  assert not subprocess.check_output(['git','diff','--name-only'],cwd=tree)
  (p/(tree+'-source-restored.json')).write_text(json.dumps(dict(commit=sha,restored=True,source_sha256=hashlib.sha256(path.read_bytes()).hexdigest())))
 run(['dotnet','build','src/Tertius.Demo','-c','Release','-nologo','-v:q'],p/(tree+'-pristine-build.log'),tree)
 manifest.append(dict(tree=tree,commit=sha,source_restored=True,source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),pristine_rebuild=True))
 (p/'observations.json').write_text(json.dumps(manifest,indent=2))
# ab_run.sh rebuilds these pristine trees and supplies its own warm-up and alternating rounds.
(p/'stage.json').write_text(json.dumps(dict(stage='matched_timing_rounds', started_unix=time.time())))
for scenario,args in scenarios.items():
 env=os.environ.copy(); env['DEMO_ARGS']=' '.join(common+args)
 with open(scenario+'-ab.log','w') as f:
  subprocess.run(['bash','before/tools/runpod/ab_run.sh',script,'5','before','after'],env=env,stdout=f,stderr=subprocess.STDOUT,check=True)
 Path(scenario).mkdir(); Path('out').rename(Path(scenario)/'out')

assert all(hashlib.sha256((art/e['path']).read_bytes()).hexdigest()==e['sha256'] for e in entries)

(p/'stage.json').write_text(json.dumps(dict(stage='complete', ended_unix=time.time())))
