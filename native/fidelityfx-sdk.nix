# AMD's FidelityFX SDK v1.1.4 (FSR 3.1 upscaler 3.1.4), only the folders that tools/fsr3-golden/ builds from: the host
# code of the upscaler, its shared helpers and the GPU headers that the host code includes. The tool runs AMD's own host
# calculations and prints the golden values that the tests of Fsr3Upscaler hold (docs/plans/temporal-renderer.md,
# "The host"). The engine's copy of the GLSL shaders is in src/Tertius.Rendering/ThirdParty/Fsr3, whose README names
# the same revision. The dev shell gives this folder as TERTIUS_FFX_SDK.
# License: MIT (sdk/LICENSE.txt).
{ fetchFromGitHub }:

fetchFromGitHub {
  owner = "GPUOpen-LibrariesAndSDKs";
  repo = "FidelityFX-SDK";
  # The tag v1.1.4.
  rev = "c6efa6bf7f2027b3ec94f28578bb5965eabb9e55";
  sparseCheckout = [
    "sdk/include"
    "sdk/src/components/fsr3upscaler"
    "sdk/src/shared"
    "sdk/src/backends/vk/shaders/fsr3upscaler"
  ];
  hash = "sha256-mLhd2Vv1gcjQXq59inLRAhXfuEl9KYRNbBIwLrU5004=";
}
