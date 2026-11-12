# The Mac's MoltenVK: the nixpkgs build with two patches (tools/README.md, "The patched MoltenVK").
# Only the Mac's dev shell uses it. The engine does not depend on it: stock MoltenVK and a Linux driver render the
# same frame.
{ moltenvk }:

moltenvk.overrideAttrs (old: {
  patches = (old.patches or [ ]) ++ [
    # Upstream's fix of MoltenVK issue 2805 (pull request 2819), which release 1.4.2 does not hold. A compute encoder
    # that several kinds of command share updates the fence of every barrier stage that it used. A nixpkgs update to a
    # release with the fix makes this patch fail to apply. Remove it then.
    ./moltenvk-execution-dependencies.patch
    # Ours: a pipeline barrier to the compute stage alone keeps an open compute encoder of dispatches. It builds on
    # the patch above.
    ./moltenvk-keep-dispatch-encoder.patch
  ];
})
