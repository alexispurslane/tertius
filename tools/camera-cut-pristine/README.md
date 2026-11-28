# Immutable camera-cut measurement inputs

These 15 payload files preserve the exact reviewed pristine proposal from issue #4. `payload-manifest.json` records each SHA-256. The worker, C# fixture, analyzer, scripts, retained diagnostics and inert measurement config keep their original bytes. No engine source lives here.

A canonical named tree makes these files available under `harness/tools/camera-cut-pristine`. The handler excludes `docs/` during checkout, so runtime inputs belong under `tools/`. The pinned `prepare.py` verifies the manifest and every payload hash before copying any file into the canonical job's work folder. Existing destination files cause refusal.

The immutable harness remains `6f14335c629b25e36899699006c268026d33ce7e`. The later `external_launch.py` is embedded as short source in the review command. It checks an external approval record, invokes the pinned preparation, and supplies the unchanged worker with a separate runtime config. Only three approval fields can differ. The checked-in config and emitted approval remain false, with zero authorized submissions.

The [Linux proof and one-attempt proposal](../../docs/research/camera-cut-exposure/issue37-linux-proof-2026-11-28/README.md) preserve exact commands, hashes, runtime differences, source pins and limits. Linux aarch64 with 4 KiB pages rejects the original oversized argument and passes the harmless final-command regression. These results supply no GPU performance evidence.

Run the normal regressions and reproduce the existing loopback-builder proof in the dev shell:

```powershell
nix develop --command python3 -I tools/test_camera_cut_launch.py
nix develop --command python3 -I tools/camera-cut-pristine/linux_proof.py --out '/tmp/issue37-repeat-proof'
```

`proof_gate.py` is test apparatus. It stops the real worker before measurement starts. It is never part of the emitted production command or the immutable 15-file payload.
