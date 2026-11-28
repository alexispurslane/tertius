throw 'INERT: duration policy and paid submission remain unapproved. This is a review copy only.'
& 'python3' '-I' 'tools/runpod/gpu_job.py' 'submit' '--agent' 'issue4-exposure' '--tree' 'before=b002351eb48d33f008f8333e53e61345de3260cd' '--tree' 'after=588b6d2747560c73aac542fb25ce80eec1ba8db7' '--tree' 'harness=6f14335c629b25e36899699006c268026d33ce7e' '--run' 'python3 -I -c ''import hashlib,pathlib,runpy
p=pathlib.Path(''"''"''harness/tools/camera-cut-pristine/prepare.py''"''"'')
if hashlib.sha256(p.read_bytes()).hexdigest()!=''"''"''b34b921ce27f674de3c15fa206a10e0deda59ec218abafdd1af3e9bee29e3d21''"''"'':
 raise ValueError("Preparation script hash differs")
runpy.run_path(str(p),run_name="__main__")
'' --manifest-sha256 445a4fb94e70d6326ec0fd8bf7d0a39aace36b5794c593bf225f0137f89bea57' '--timeout' '1920' '--out' 'INERT-parent-must-select-result-folder' '--output' 'linux-out/return/*'
