# Immutable camera-cut measurement inputs

These 15 payload files preserve the exact reviewed pristine proposal from issue #4. `payload-manifest.json` records each SHA-256. The worker, C# fixture, analyzer, scripts, retained diagnostics and inert measurement config keep their original bytes. No engine source lives here.

A canonical named tree makes these files available under `harness/tools/camera-cut-pristine`. The handler excludes `docs/` during checkout, so the runtime inputs belong under `tools/`. `prepare.py` verifies the manifest hash supplied by the short command, checks every payload hash before copying any file, then runs the unchanged worker from the canonical job's work folder. The `before` and `after` trees remain siblings. Existing destination files cause refusal.

The checked-in config has both approval flags false and zero authorized submissions. Preparation reaches the worker's existing refusal before creating `linux-out` or starting measurement. A later paid approval would need a separately reviewed external authorization step. This bundle supplies no such step and changes no canonical config or endpoint.

Run the local preparation tests in the dev shell:

```powershell
nix develop --command python3 -I tools/camera-cut-pristine/test_prepare.py
```

These tests exercise the actual canonical handler and `Popen` on the host. They establish file reconstruction and the inert gate. They make no Linux performance or exact Linux execution-limit claim.
