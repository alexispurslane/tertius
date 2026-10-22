# jscpd 5, the copy-paste detector, built from its crate on crates.io. nixpkgs does not have it.
# tools/duplicates.sh runs it on the C# code.
{ lib, rustPlatform, fetchCrate }:

rustPlatform.buildRustPackage rec {
  pname = "jscpd";
  version = "5.4.2";

  src = fetchCrate {
    inherit pname version;
    hash = "sha256-TUz8x8G2Bsv6NGtdr3zMrT68JwPWsRUaqJWBCwe56fE=";
  };

  cargoHash = "sha256-iZNRhBdPO7fju7CaLu1q0Ppm6K0b80uwHTkR5vKRDFs=";

  # The crate's tests read fixture folders that the published crate leaves out.
  doCheck = false;

  meta = {
    description = "Copy-paste detector for source code";
    homepage = "https://github.com/kucherenko/jscpd";
    license = lib.licenses.mit;
    mainProgram = "jscpd";
  };
}
