# libjoltc_double: the joltc C API over Jolt Physics, built with double-precision positions.
# This mirrors native/build-joltc.sh, but pins both sources by hash so the build is reproducible.
{
  lib,
  stdenv,
  fetchFromGitHub,
  cmake,
  ninja,
  fixDarwinDylibNames,
}:

let
  # Jolt Physics at the tag that joltc pins. joltc builds it as a subproject.
  joltPhysics = fetchFromGitHub {
    owner = "jrouwe";
    repo = "JoltPhysics";
    tag = "v5.6.0";
    hash = "sha256-igPNGwYtXjR2BXKD20/uzfn7x7JwBdbVjMQIk957+TU=";
  };
in
stdenv.mkDerivation {
  pname = "joltc-double";
  # joltc has no release tags, so the version names the Jolt release and the joltc commit date.
  version = "5.6.0-unstable-2026-07-13";

  # The joltc revision that matches JoltPhysicsSharp 2.23.0.
  src = fetchFromGitHub {
    owner = "amerkoleci";
    repo = "joltc";
    rev = "886e088675bae3a086f8318c7803f8ee962c2f2c";
    hash = "sha256-dhFvgdYCJreWoaxqUQrAR9gB9dIZn9EdFV3JF2PHFO4=";
  };

  nativeBuildInputs = [
    cmake
    ninja
  ]
  ++ lib.optionals stdenv.hostPlatform.isDarwin [ fixDarwinDylibNames ];

  # Distribution is Jolt's own optimized configuration without asserts or the profiler.
  cmakeBuildType = "Distribution";

  cmakeFlags = [
    (lib.cmakeBool "DOUBLE_PRECISION" true)
    (lib.cmakeBool "JPH_BUILD_SHARED" true)
    (lib.cmakeBool "JPH_SAMPLES" false)
    # The tests pull googletest with FetchContent, which the sandbox forbids.
    (lib.cmakeBool "JPH_TESTS" false)
    (lib.cmakeBool "JPH_USE_DX12" false)
    (lib.cmakeBool "JPH_USE_VK" false)
    (lib.cmakeBool "JPH_USE_MTL" false)
    (lib.cmakeFeature "JOLT_PHYSICS_ROOT" "${joltPhysics}")
    # Any FetchContent that still tries the network must fail at configure time, not hang.
    (lib.cmakeBool "FETCHCONTENT_FULLY_DISCONNECTED" true)
  ]
  # joltc defaults to a universal x86_64;arm64 binary. Build only the host architecture.
  ++ lib.optionals stdenv.hostPlatform.isDarwin [
    (lib.cmakeFeature "CMAKE_OSX_ARCHITECTURES" (
      if stdenv.hostPlatform.isAarch64 then "arm64" else "x86_64"
    ))
  ];

  # joltc's install rule also installs the static Jolt library and its headers.
  # Tertius needs only the shared C library and its header.
  installPhase = ''
    runHook preInstall
    install -Dm644 lib/libjoltc_double${stdenv.hostPlatform.extensions.sharedLibrary} -t $out/lib
    install -Dm644 $src/include/joltc.h -t $out/include
    runHook postInstall
  '';

  meta = {
    description = "joltc C API for Jolt Physics, built with double-precision positions";
    homepage = "https://github.com/amerkoleci/joltc";
    license = lib.licenses.mit;
    platforms = lib.platforms.darwin ++ lib.platforms.linux;
  };
}
