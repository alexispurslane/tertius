# The applegpu decoder of Dougall Johnson (https://github.com/dougallj/applegpu): Python that disassembles the machine
# code of Apple's GPUs. tools/shader_registers.py reads the highest register of the direct shaders with it, and the
# pre-commit hook's register guard runs that tool (tools/README.md, "The registers of the direct shaders"). The dev
# shell names this folder in APPLEGPU, on the Mac only, because only the Mac has the Metal toolchain that the count
# also needs. The revision is the one that the first counts used (docs/research/light-pairs/pair/notes.md).
{ fetchFromGitHub }:

fetchFromGitHub {
  owner = "dougallj";
  repo = "applegpu";
  rev = "28dd80468d4a6c94fa4dac34ed488196f5fedc5f";
  hash = "sha256-68A2gnQYXxQuEWNeSQeHNIyDfMltepobKWYIS63z8nI=";
}
