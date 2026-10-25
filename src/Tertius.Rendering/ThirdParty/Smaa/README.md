# SMAA reference files

These files come from iryoku/smaa (https://github.com/iryoku/smaa), commit `71c806a838bdd7d517df19192a20f0c61b3ca29d` of 2013-11-06, under the MIT license in `LICENSE.txt`.

| File | Source | Change |
|---|---|---|
| `SMAA.hlsl` | `SMAA.hlsl` | Line ends changed from CRLF to LF. The comment art changed from code page 437 to UTF-8. The code is unchanged. |
| `AreaTex.bin` | `Textures/AreaTex.h`, array `areaTexBytes` | The bytes of the array, in order: 160 × 560 texels, two 8-bit channels, rows of 320 bytes. SHA-256 `35065cef2a02cabcad711d6bf430239ae64e27d71c4e4fa06f29cce2c992f0d2`. |
| `SearchTex.bin` | `Textures/SearchTex.h`, array `searchTexBytes` | The bytes of the array, in order: 64 × 16 texels, one 8-bit channel, rows of 64 bytes. SHA-256 `3694eae5e9d44b8ebb4415a13f8c7b94dc08a2fc86658434d771c4610fe5744d`. |
| `LICENSE.txt` | `LICENSE.txt` | Line ends changed from CRLF to LF. |

The design is in `docs/plans/anti-aliasing.md`.

The notice for the distributed game is in `THIRD-PARTY-NOTICES.md` at the repository root. `Tertius.Demo.csproj` copies it into every build and publish output.
