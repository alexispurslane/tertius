# Measurement records

`measurements.json` contains every completed raw run collected by `table.py`, including all batch samples, span-clock summaries, source hashes, gate counts, and kept labels. `timings.md` is its direct timing table. `attempts.md` adds the decision for every stage/view/variant; `decisions.json` holds those explanations. Kept/all means kept timing rounds, not engine adoption. Stage names identify separate alternating batches, not independent implementations.

`provenance.json` names the exact checkout/base, input roots, tool hashes, interrupted/unavailable attempts, and retained scratch paths. `fixed-frame-model.json` holds the baseline subgroup counts and the stated calibration estimate. `approximation-bias.txt` and `approximation-flip.json` are the independent image costs of the cap options, not main-line gate results. `red-green.txt` preserves the observed test failures and subsequent passes for new harness behavior.

The bulk images and per-ray output buffers stay outside Git. They are retained under `/private/tmp/ter-dzxf.vv9W25`. The original captures remain read-only in the profiling spike's `captures/sftw/top` folder. Offline `order-oracle` and `order-length` stages use derived, permuted records with image symlinks. Their gate checks each ray against its own permuted original outcome; their timings exclude ordering/scatter and cannot be adopted as net frame wins.

Reproduce the raw timing table, in the Nix shell:

```sh
python3 tools/walk-hill-climb/table.py /private/tmp/walk-measurements.json /private/tmp/ter-dzxf.vv9W25/{climb1,climb2,climb3,climb4,climb5,climb6,verified1,final1,final2,order-oracle,order-length,cap62-unroll,cap62-split-red,cap62-split-green}
```

The report gives the platform and correctness limits. No Linux-driver timing, compiled-library timing, global-optimum proof, or whole-renderer gain is implied by these records.
