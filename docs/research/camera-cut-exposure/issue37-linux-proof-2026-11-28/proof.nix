let
 py = builtins.storePath /nix/store/9zyabiswyisqky2gd1d3z1ig5rm7dd94-python3-3.14.10;
 sh = builtins.storePath /nix/store/brnhnyb8lq3sfkdsdq10i5w85b120imz-bash-interactive-5.3p20;
in derivation {
 name = "tertius-issue37-linux-launch-proof"; system = "aarch64-linux";
 builder = "${py}/bin/python3"; args = [ "-I" ./proof.py ];
 handlerfile = ./handler.py; oldcmd = ./old; shortcmd = ./short; payload = ./payload;
 shellfile = "${sh}/bin/bash"; PATH = "${py}/bin:${sh}/bin";
}
