# 10 Editor

Status: planned. It needs plan 06 for the host, plans 07 and 08 for content, and plan 09 for assets.

## Goal

Build an editor for everything in Tertius with Dear ImGui, and give it a theme that looks like a desktop application. (User)

- The editor edits the content files of plans 07 and 08: scenes, blueprints, patches and manifests.
- A 3D viewport shows the result at every scale from 1 m to 1e11 m.
- Play mode runs the game's component DLLs inside the editor, and the editor loads them again after a rebuild.
- Every editor action is also a script step. Claude agents can drive the editor without a window and read its screenshots.

The first useful editor has a viewport, a hierarchy, an inspector and play mode (step groups A and B). The asset browser, the blueprint editor, the console, the message log, the profiler, the debug switches and package management follow in group C. Plan 09 owns asset import, previews, thumbnails and asset hot reload. This plan connects them to the editor (see "Assets in the editor") and does not design them again.

## Decisions

1. The editor uses Dear ImGui with docking. Its theme makes it look like a desktop application. (User)
2. The binding is Hexa.NET.ImGui 3.1.0-experimental with Hexa.NET.ImGuizmo 3.1.0-experimental, pinned exactly. There are four reasons:
   - It wraps Dear ImGui 1.92.9b, the newest release, with docking and the texture protocol of 1.92.
   - The sources of its native libraries are known commits, so Nix can build them (decision 3). The stable Hexa 2.2.9 wraps 1.92.2b, and neither the fork branch nor the upstream tags of cimgui hold that version.
   - ImGui.NET 1.91.6.1 predates 1.92 and has no ImGuizmo binding.
   - The only ImGui integration of Silk.NET is for OpenGL.

   The cost is the label "experimental". The exact pin and the binding test of step A1 keep that risk small. (User)
3. The native libraries `cimgui` and `cimguizmo` come from small pinned Nix derivations, not from the NuGet packages. Each derivation builds the fork commit and the CMake flags that Hexa's own build workflow uses. Native dependencies come from Nix in this project. (User)
4. Both ImGui backends are C# code in Tertius. The renderer backend draws through the engine's Vulkan renderer, and every UI texture is a slot of the bindless array. A 3D view or a thumbnail is then an ordinary `TextureId` in the UI. Hexa ships its own backends as separate native libraries, and its GLFW backend can bring a second GLFW copy next to the one of Silk.NET. The C# backends avoid that question.
5. `Tertius.Rendering` gets no ImGui dependency. The editor copies the draw data of each UI frame into an engine type, `UiDrawList`. The copy is small, and a later in-game debug UI can use the same UI pass.
6. One OS window holds every panel, with docking. Panels in separate OS windows come later. The Dear ImGui wiki says that they are hard to add to a custom backend. It also says that they do not work on Wayland and are often broken on X11.
7. The theme is dark, with one blue accent. A dark window keeps the attention on space scenes, and current IDEs use the same scheme. The fonts are Inter and JetBrains Mono, and the icons are Lucide. All three are in the pinned nixpkgs, under the OFL and ISC licenses.
8. The editor edits documents and never edits a World directly. A document is the immutable tree of one content file (plan 07). The worlds of the editor only show the documents. So agents, git and the CLI always see the real source of truth.
9. Undo and redo keep a list of immutable document states. No edit has a command object or an inverse. (User: no command pattern.) Structural sharing makes each state cost only the path from the root to the changed value.
10. The edit world shows the documents in edit mode. It follows an edit in one of two ways. A change of a pose, an attach offset, a render section, an orbit or component values applies in place. Every other change rebuilds the edit world. A property test makes sure that both ways give the same world.
11. The edit world has no physics backend and never ticks. It shows the start state of the scene. With physics, bodies move away from the poses that the user edits.
12. Play mode spawns a new world, the play world, from the documents in memory. Stop disposes it. So stop restores the edit state exactly, by construction.
13. The editor runs the UI, the ticks and the recording of GPU commands on the main thread. The World API belongs to one thread, and Dear ImGui draw data lives only until the next UI frame. The simulation result does not depend on the thread that ticks.
14. Code reload follows plan 08: dispose the worlds, unload the package contexts, load again and rebuild from the documents. The editor never swaps code inside a live world.
15. The editor camera is not an entity. It is a frame handle, an eye pose in that frame and a field of view. A new engine-only call builds a render snapshot from it. The camera follows the frame rules of ARCHITECTURE.md, so it moves between frames like an object.
16. The flight speed of the camera scales with the distance to the nearest surface. The GPU measures that distance in the view, and the CPU measures it to the bounding spheres around the camera. One control then works from 1 m to 1e11 m.
17. Picking uses a pick pass that runs only for a click, cut to one pixel by a scissor. It writes the entity index and generation, so the result is a full `Entity` handle with a generation check.
18. The gizmo is ImGuizmo. It works in the axes of the frame of the selected entity, and the editor applies its delta matrix in doubles. The pinned ImGuizmo handles reversed and infinite projections, so it gets the engine's projection without the Y flip of Vulkan clip space.
19. Overlays, such as frame bubbles and orbits, are 3D lines that a new line pass of the renderer draws. The pass dims lines behind geometry with the distance target. Captures for agents include the overlays.
20. Every editor action is a method of one class, `EditorApi`. The panels and the scripts call the same methods. The headless runner uses the step format of plan 06.
21. The Dear ImGui Test Engine is left out for now. Its license is free for the user as a natural person, but it is C++ only and has no Hexa binding. Snapshot tests and `EditorApi` tests cover the editor.
22. The editor saves a document by itself 1 s after the last edit, and also on Cmd+S. Agents, git and the CLI read the files, so an unsaved change is invisible to them. Undo still works after a save. (User)
23. Stop discards the changes that the inspector made during play mode. "Apply to scene" keeps the live values of one component, as one undoable edit. Play mode then never changes a document by surprise. (User)

## Design

### Overview

```
                    +----------------------- main thread ------------------------+
 Silk.NET window -->| platform backend --> ImGui frame --> panels --> EditorApi |
 and input          |                                         |                  |
                    |                     EditorSession: documents, history,    |
                    |                     selection, play state                  |
                    |                         |                    |             |
                    |              edit world (no physics)   play world (physics)|
                    |                         |                    |             |
                    |   render snapshots (CameraView builds and the published  |
                    |   snapshot), line overlays, UiDrawList                     |
                    +---------------------------------|--------------------------+
                                                      v
                               VulkanRenderer.RenderFrame: views, then the UI pass
```

The editor is the project `tools/Tertius.Editor`, an executable. It references `Tertius.Host`, `Tertius.Content`, `Tertius.Assets`, `Tertius.Import`, `Tertius.Rendering`, `Tertius.Physics` and `Tertius.Simulation`. Its tests are in `tests/Tertius.Editor.Tests`.

```
tools/Tertius.Editor/
  Program.cs            windowed and headless entry points
  Session/              EditorSession, EditorDocuments, History, Selection, file watching
  Sync/                 WorldSync: the diff, the in-place path and the rebuild path
  Play/                 PlaySession: play, pause, step, stop and code reload
  Viewport/             EditorCamera, flight, focus, picking, gizmo, overlays
  Ui/                   platform backend, UiBridge, theme, fonts, icons, default layout
  Panels/               one class per panel
  Inspector/            one widget model per value form, InspectorCache
  Scripting/            EditorApi, ScriptRunner, LiveChannel
```

`tertius editor` starts the editor, and `tertius editor run` runs a script. The `tertius` wrapper of plan 08 forwards both to this project.

### Threads and the UI frame

A UI frame is one pass of the editor loop that draws the window once. One UI frame does these steps on the main thread, in this order:

1. Read the window events and give them to Dear ImGui. In play mode, input for the Game view goes to the play world as `InputEvent` posts.
2. Apply the asset imports that finished on the background thread (see "Assets in the editor").
3. In play mode, run the ticks that are due, with the fixed-step loop of plan 06. At most 4 ticks run per UI frame. If the simulation falls behind, play mode slows down, and the status bar shows the speed, for example `0.6x`.
4. Apply the edits of the previous UI frame to the edit world (see "From documents to the edit world").
5. Build the UI: `ImGui.NewFrame`, every panel, `ImGui.Render`.
6. Build a render snapshot for each visible 3D view, and the line overlays of each view.
7. Copy the draw data into a `UiDrawList`, and call `VulkanRenderer.RenderFrame` with the views and the UI.

The order has a reason. Edits from UI frame N reach the world before the views of UI frame N+1 draw. So the view never shows a state that the panels do not show.

The gameplay workers and the physics thread still run in parallel inside `World.Tick`. Only the call of `Tick` moves to the main thread.

After 0.5 s with no input, outside play mode, the editor waits for window events with a timeout of 250 ms. An idle editor then costs almost no CPU.

### Dear ImGui: binding and native libraries

| Item | Choice | Facts |
|---|---|---|
| Managed binding | Hexa.NET.ImGui 3.1.0-experimental | Wraps Dear ImGui 1.92.9b. Has `net10.0` libraries. Depends on HexaGen.Runtime 1.1.22. Built from Hexa commit `ffc1263` (NuGet nuspec). |
| Gizmo binding | Hexa.NET.ImGuizmo 3.1.0-experimental | Wraps ImGuizmo 1.92.5 WIP at commit `dc25afb` (NuGet nuspec). |
| Native `cimgui` | `native/cimgui.nix` | Hexa builds it from `JunaMeinhold/cimgui`, branch `docking_inter`, with `-DIMGUI_WCHAR32=ON -DIMGUI_FREETYPE=ON -DCIMGUI_VARGS0=1` and FreeType (Hexa's `cmake.yml`). The pin is commit `576040c`, whose submodule holds Dear ImGui at `b48d1af` (1.92.9b). The fork is MIT. |
| Native `cimguizmo` | `native/cimguizmo.nix` | Hexa builds it from `JunaMeinhold/cimguizmo`, branch `master`, with the same flags. The pin is commit `7a7dc6b`. The fork is MIT. |
| FreeType | nixpkgs `freetype` 2.14.3 | Dear ImGui uses it to draw glyphs. |
| Fonts | nixpkgs `inter` 4.1, `jetbrains-mono` 2.304, `lucide` 0.563.0 | `lucide` installs `share/fonts/truetype/Lucide.ttf`. |

The last commits of both forks are from 2026-07-31, the day that Hexa published 3.1.0. Hexa does not record which commits it built, so step A1 compares the Nix build with the NuGet library:

- The exported symbols must be equal, as `nm` lists them.
- A small C program in the derivation writes the sizes of the main structs to `cimgui-sizes.json`. The structs are `ImGuiIO`, `ImGuiStyle`, `ImDrawCmd`, `ImDrawVert`, `ImDrawList`, `ImTextureData` and `ImFontConfig`. A test compares the sizes with the Hexa structs.

If either comparison fails, step A1 tries the fork commits of the days around the release. If none of them matches, the user chooses between the stable Hexa 2.2.9 with its prebuilt libraries and a patch to the derivation.

The dev shell sets `TERTIUS_CIMGUI_DIR`, `TERTIUS_CIMGUIZMO_DIR` and `TERTIUS_EDITOR_FONTS_DIR`. A project that sets `<UsesImGui>true</UsesImGui>` gets the libraries and fonts copied next to its output, as `UsesJolt` does for Jolt. The package references set `ExcludeAssets="native"`, so the NuGet libraries never reach the output.

### The UI backend

The platform side is `ImGuiPlatform` in the editor. It reads Silk.NET input and gives it to `ImGuiIO`:

- Keys, text characters, mouse position, buttons and wheel, and window focus.
- The clipboard, through the Silk.NET keyboard.
- The mouse cursor shape that Dear ImGui asks for.
- `DisplaySize` in window units, and `DisplayFramebufferScale` as the framebuffer size divided by the window size.
- `ConfigMacOSXBehaviors` on macOS, so Cmd works as Ctrl does on Linux.

The renderer side is `UiBridge` in the editor plus the UI pass in the renderer.

- `UiBridge` copies `ImDrawData` into a `UiDrawList`: vertices, 16-bit indices and draw commands with a clip rectangle, a texture and offsets. `ImDrawVert` and `UiVertex` have the same 20-byte layout, so each list is one block copy.
- The backend sets `RendererHasVtxOffset` and `RendererHasTextures`. `UiBridge` answers the texture requests of Dear ImGui with three renderer calls:

| Request | Renderer call |
|---|---|
| `WantCreate` | `CreateTexture` with one mip level, then `SetTexID` with the bindless slot |
| `WantUpdates` | `UpdateTexture` for each changed rectangle |
| `WantDestroy` | `ReleaseTexture`, after the GPU frames in flight: the frames that the GPU still draws |

- The atlas format is RGBA32, because the renderer takes RGBA8 textures.
- The UI pass draws after every view, into the window output. It has one pipeline: alpha blending, no culling, no depth, a scissor per command. Push constants hold the scale, the translation and the bindless slot of the texture.
- The UI pass has its own sampler: linear filtering and clamp to edge. The material sampler repeats, which is wrong for UI images.

Color rule: every UI texture is a UNORM image that holds sRGB-encoded bytes. The UI pass copies colors with no conversion into a UNORM output. Dear ImGui then blends in sRGB space, which is the space that its colors and anti-aliasing expect.

- The editor creates its renderer with `RendererOptions.UnormOutput`. The tone mapper already writes sRGB-encoded values into UNORM outputs (`EncodeSrgbInShader`).
- A view image is UNORM with sRGB-encoded bytes, written by the same tone mapper.
- Thumbnails from PNG files are created with `Srgb = false`, so the GPU does not decode them.
- If the surface offers no UNORM format, the UI shader encodes its output instead. Step A2 finds out which case MoltenVK gives.

### The theme

Sizes are in points. On macOS, a point is one window unit, and Retina screens draw it with 2 pixels. Dear ImGui 1.92 draws glyphs at the density of `DisplayFramebufferScale`, so text stays sharp with no extra work.

Fonts:

| Use | Font | Size |
|---|---|---|
| UI text | Inter Regular | 15 |
| Panel titles and section headers | Inter SemiBold | 15 |
| Console, message log, profiler tables, JSON views | JetBrains Mono Regular | 14 |
| Icons, merged into both Inter fonts | Lucide | 16, with `GlyphOffset` (0, 2) and `GlyphMinAdvanceX` 16 |

- The icons merge with `MergeMode`, so a label such as `$"{Icons.Box} crate-1"` mixes text and icon in one call.
- A small generator reads the glyph names and code points of the pinned `Lucide.ttf` and writes `Icons.g.cs`. The constants then always match the font.
- Belief: the Inter 4.1 release has static TTF files under `extras/ttf`, and the nixpkgs package removes that folder. In that case, `native/editor-fonts.nix` takes `Inter-Regular.ttf` and `Inter-SemiBold.ttf` from the same pinned release archive.

The palette:

| Role | ImGui colors | Value |
|---|---|---|
| Deepest background | `MenuBarBg`, `TitleBg`, `TitleBgActive`, `TitleBgCollapsed`, `DockingEmptyBg` | `#17181B` |
| Window background | `WindowBg`, `ChildBg` (alpha 0) | `#1E1F22` |
| Popups | `PopupBg` | `#26282C` |
| Input fields | `FrameBg`, `FrameBgHovered`, `FrameBgActive` | `#26282C`, `#2F3136`, `#393C42` |
| Buttons | `Button`, `ButtonHovered`, `ButtonActive` | `#2F3136`, `#393C42`, `#44484F` |
| Selected rows | `Header`, `HeaderHovered`, `HeaderActive` | `#2C4A7A`, `#335690`, `#3B62A6` |
| Borders and separators | `Border`, `Separator`, `TableBorderStrong` | `#34363B` |
| Light table lines | `TableBorderLight`, `TreeLines` | `#2A2C30` |
| Text | `Text`, `TextDisabled` | `#DFE1E5`, `#868A91` |
| Accent | `CheckMark`, `SliderGrab`, `TabSelectedOverline`, `DragDropTarget`, `NavCursor`, `PlotHistogram` | `#4A86F7` |
| Accent, active | `SliderGrabActive`, `SeparatorActive` | `#6EA2FA` |
| Accent, weak | `TextSelectedBg` (alpha 0.35), `DockingPreview` (alpha 0.55), `SeparatorHovered` (alpha 0.6), `ResizeGripHovered` (alpha 0.4), `DragDropTargetBg` (alpha 0.15) | `#4A86F7` |
| Accent, dark | `CheckboxSelectedBg` | `#2C4A7A` |
| Unsaved document dot | `UnsavedMarker` | `#DFE1E5` |
| Tabs | `Tab`, `TabHovered`, `TabSelected`, `TabDimmed`, `TabDimmedSelected`, `TabDimmedSelectedOverline` | `#1E1F22`, `#2F3136`, `#26282C`, `#1A1B1E`, `#222327`, `#5A5E66` |
| Scrollbars | `ScrollbarBg` (alpha 0), `ScrollbarGrab`, `ScrollbarGrabHovered`, `ScrollbarGrabActive` | `#393C42`, `#4A4E56`, `#5A5F68` |
| Table rows | `TableHeaderBg`, `TableRowBgAlt` | `#23252A`, white at alpha 0.025 |
| Links and plot lines | `TextLink`, `PlotLines` | `#7AA7FA`, `#A0A6B0` |
| Modal dimming | `ModalWindowDimBg` | black at alpha 0.55 |

The editor also uses colors that Dear ImGui has no slot for:

| Role | Value |
|---|---|
| Error | `#F2555A` |
| Warning | `#E5B13A` |
| Success | `#4FB06D` |
| Gizmo axes X, Y, Z | `#E5484D`, `#5BB25E`, `#2F7FF0` |
| Selection in the viewport | `#F5A524` |
| Frame bubbles, by depth in the frame tree | `#7AA7FA`, `#B58CF0`, `#4FC1B6`, `#E58FB4`, `#C9B458`, then repeat |
| A value that the scene overrides | `#4A86F7` marker dot |
| A value that a patch sets | `#B58CF0` marker dot |

The viewport selection is orange, not blue. Orange stands out in dark blue space scenes, and it differs from the blue Z axis.

Shapes and spacing:

| Style value | Value |
|---|---|
| `WindowPadding` | (10, 10) |
| `FramePadding` | (8, 5) |
| `ItemSpacing`, `ItemInnerSpacing` | (8, 6), (6, 4) |
| `CellPadding` | (6, 4) |
| `IndentSpacing` | 16 |
| `ScrollbarSize`, `GrabMinSize` | 12, 10 |
| `WindowRounding`, `PopupRounding` | 6, 6 |
| `ChildRounding`, `FrameRounding`, `GrabRounding` | 4, 4, 4 |
| `TabRounding`, `ScrollbarRounding` | 5, 6 |
| `WindowBorderSize`, `PopupBorderSize`, `ChildBorderSize` | 1, 1, 1 |
| `FrameBorderSize`, `TabBorderSize` | 0, 0 |
| `TabBarBorderSize`, `TabBarOverlineSize` | 1, 2 |
| `WindowTitleAlign` | (0, 0.5) |
| `WindowMenuButtonPosition` | None |
| `DisabledAlpha` | 0.45 |

The hierarchy uses `ImGuiTreeNodeFlags_DrawLinesToNodes`, which draws the lines of the tree. The inspector draws its component sections with neutral header colors. So only selected rows carry the accent.

High-DPI rule:

- On macOS, window units are points. The UI scale s is 1, and the framebuffer scale gives the density.
- On Linux and Windows, window units are pixels. The UI scale s is the content scale of the monitor, such as 1.5.
- The user can set a UI scale factor u from 0.75 to 2 in the editor configuration. The default is 1.
- The editor builds a new default style, applies the theme, and then calls `ScaleAllSizes(s × u)` and sets `FontScaleDpi = s × u`. `ScaleAllSizes` multiplies the current values, so the editor never applies it twice to one style.
- After the window moves to a monitor with another content scale, the editor applies the theme again.

The code shape:

```csharp
namespace Tertius.Editor.Ui;

internal static class EditorTheme
{
    public const float UiFontSize = 15f;

    /// <summary>Writes the theme into a fresh default style. Call it again after the scale changes.</summary>
    public static void Apply(ImGuiStylePtr style, float scale)
    {
        style.FontSizeBase = UiFontSize;
        style.WindowPadding = new Vector2(10, 10);
        style.FramePadding = new Vector2(8, 5);
        style.ItemSpacing = new Vector2(8, 6);
        style.WindowRounding = 6;
        style.FrameRounding = 4;
        ...
        Set(style, ImGuiCol.WindowBg, 0x1E1F22);
        Set(style, ImGuiCol.Text, 0xDFE1E5);
        Set(style, ImGuiCol.CheckMark, 0x4A86F7);
        Set(style, ImGuiCol.TextSelectedBg, 0x4A86F7, alpha: 0.35f);
        ...
        style.ScaleAllSizes(scale);
        style.FontScaleDpi = scale;
    }

    private static void Set(ImGuiStylePtr style, ImGuiCol slot, uint rgb, float alpha = 1f) =>
        style.Colors[(int)slot] = new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
}
```

where

- `style` = the Dear ImGui style that the editor rebuilds from the defaults before each call.
- `scale` = s × u, the UI scale of the monitor times the factor from the editor configuration.
- `Set` = writes one color slot from a 24-bit sRGB hex value. Dear ImGui colors are sRGB-encoded, so the bytes go in unchanged.

A theme test window, `Theme preview`, shows every widget kind with the theme. Its snapshot test makes palette changes reviewable.

### Default layout and panels

```
+------------------------------------------------------------------------------+
| File  Edit  View  Play  Packages  Help        [move rotate scale] [> || >| X] |
+--------------+-------------------------------------------+-------------------+
| Hierarchy    | Scene | Game | <blueprint tabs>          | Inspector         |
| History      |                                           | Debug             |
|              |                                           | Packages          |
|   20 %       |                 3D view                   |   24 %            |
|              |                                           |                   |
+--------------+-------------------------------------------+                   |
| Assets | Console | Messages | Profiler           28 %  |                   |
+--------------------------------------------------------+-------------------+
| edit | tick 0 | sol > terra > corvette | 2.4 ms | 0 errors | 3 warnings     |
+------------------------------------------------------------------------------+
```

`DockBuilder` makes this layout on the first start and on View > Reset layout. `DockBuilder` is internal API of Dear ImGui, which is one more reason to pin the version. The status bar uses `BeginViewportSideBar`. The layout file is `.tertius/editor/layout.ini`, and the editor configuration is `.tertius/editor/editor.json`. Git ignores the `.tertius/` folder.

| Panel | What it shows | Edit mode | Play mode |
|---|---|---|---|
| Scene | The world from the editor camera, with overlays and the gizmo | The edit world | The play world |
| Game | The world from the scene's camera entity | The start state | The play world, with interpolation and game input |
| Hierarchy | Frames, entities and attachments as one tree | From the scene document and the patches | From the live world |
| Inspector | The selected entity, or the selected value of a blueprint | Edits the documents | Shows live values |
| Assets | Packages, folders and assets, with thumbnails | Drag source, import | Drag source |
| Blueprint tabs | One blueprint document per tab, with a preview | Edits the blueprint | Read-only |
| Console | Log lines, world errors, content diagnostics and build output | Yes | Yes |
| Messages | Routed events and their recipients | Empty | The last 5 s of events |
| Profiler | Editor, tick, renderer and GPU pass times | Editor and GPU | Also tick phases |
| Debug | Renderer and physics switches and debug displays | Yes | Yes |
| Packages | Packages, load order, mods, build and reload | Yes | Read-only |
| History | The undo list, with labels | Yes | Read-only |

Keyboard shortcuts use Cmd on macOS and Ctrl on Linux and Windows:

| Keys | Action |
|---|---|
| Cmd+Z, Cmd+Shift+Z | Undo, redo |
| Cmd+S, Cmd+Alt+S | Save the active document, save all |
| Cmd+P, Cmd+Shift+P, Cmd+. | Play or stop, pause, one tick |
| Q, W, E, R | Select, move, rotate, scale tool |
| F | Fly to the selection |
| Cmd+D, Delete | Duplicate, delete |
| Right mouse button and W, A, S, D, Q, E | Fly the camera |
| Alt and the left mouse button | Orbit about the selection |
| Mouse wheel during flight | Change the speed factor |
| Shift+F1 | Give the mouse back from the game to the editor |

### Documents, edits and undo

Terms:

- Document: the immutable content tree of one file, as `ContentNode` of plan 07.
- Document set: the documents of every open file, keyed by file path.
- History: the list of past document sets, with a current index.
- Gesture: one continuous user action, such as one drag of a slider.
- Edit: a function from one document set to a new document set.

```csharp
namespace Tertius.Editor;

/// <summary>The open documents, keyed by path relative to the repository root. Immutable.</summary>
public sealed record EditorDocuments(ImmutableSortedDictionary<string, ObjectNode> Roots);

/// <summary>Selected entities by scene id in edit mode. Play mode selects entity handles instead.</summary>
public sealed record Selection(ImmutableArray<string> SceneIds, string? Primary);

public sealed record HistoryEntry(EditorDocuments Documents, Selection Selection, string Label, long RetainedBytes);

/// <summary>Past states of the documents. Undo and redo move an index. No entry holds an operation.</summary>
public sealed class History
{
    public IReadOnlyList<HistoryEntry> Entries { get; }
    public int Current { get; }

    /// <summary>Drops the redo entries and adds the entry. Inside an open gesture, replaces the gesture's entry instead.</summary>
    public void Push(HistoryEntry entry);
    public void BeginGesture(string key);
    public void EndGesture();
    public bool Undo();
    public bool Redo();

    /// <summary>The current entry is in range, gestures do not nest, and the byte count matches the entries.</summary>
    public void AssertInvariants();
}
```

An edit is an ordinary function that `EditorApi` builds and runs once. History keeps the result, never the function. So no operation needs an undo of its own, and a new kind of edit gets undo for free. This is the difference from the command pattern.

`ContentEdit` is a new static class in `Tertius.Content`, next to `MergePatch`. It holds pure tree functions: `Find`, `Set`, `Remove`, `Insert` and `Rename` by JSON Pointer. Each function copies only the nodes on the path to the change. Every other subtree of the result is the same object as in the input. Nodes store no JSON Pointer (plan 07), so a `Rename` or an `Insert` changes no node below it. A node that the editor makes has no source span.

A gesture merges into one history entry. The inspector calls `BeginGesture` at `IsItemActivated` and `EndGesture` at `IsItemDeactivatedAfterEdit`. Each UI frame of the drag replaces the entry of the gesture. The world follows every UI frame, and undo goes back to the state before the drag.

Selection changes create no history entry. Each entry stores the selection at the time of its edit, and undo restores it with the documents.

The cost of one edit, as estimates from the record sizes of plan 07 that the benchmarks of step B1 replace:

| Scene size | New nodes per edit of one pose | Retained bytes per entry | Time per edit and diff |
|---|---|---|---|
| 200 entities | About 12 | About 2.5 KB | Under 0.02 ms |
| 10,000 entities | About 12 | About 81 KB | Under 0.2 ms |

The `entities` object of a scene holds its members in an `ImmutableArray`. An edit of one entity copies that array, which is 8 bytes per entity. That copy is most of the cost in large scenes. History has a cap of 256 MB of retained bytes and 10,000 entries, and it drops the oldest entries first. If large scenes make edits slow, `ObjectNode` can change to an `ImmutableList`, which copies O(log n) nodes per edit. That change touches only plan 07's member collection type.

Saving follows these rules. The editor saves by itself 1 s after the last edit, and also on Cmd+S (User). The rules hold for both kinds of save.

- A document is dirty while its root is not the same object as the root that the editor last saved or loaded. Undo back to the saved state makes it clean again.
- Save writes the canonical layout of plan 07 with `ContentWriter`, comments included. It writes a temporary file and renames it.
- Before each save, the editor compares the bytes on disk with the bytes that it last loaded or saved. If they differ, it does not write, and it shows the banner of an external change instead. So a save that comes after the write of an agent, and before the watcher event, never overwrites that write.
- The editor watches every open file. If a clean document changes on disk, the editor loads it again and adds a history entry, "External change to scenes/start.scene.json". If a dirty document changes on disk, a banner offers "Keep mine" and "Load from disk". Agents often change files while the editor runs, so this case is normal and not an error.

### From documents to the edit world

The edit world shows the active scene as the content set prepares it: the scene document of the editor, with the patches of other packages applied. `WorldSync` keeps it equal to a fresh spawn of that prepared scene. Every load of the editor sets `ContentRequest.Overrides` to the open documents, so unsaved edits count.

After each UI frame with edits, `WorldSync` compares the old and the new document set. Subtrees that are the same object are equal, so the diff visits only the changed paths. Then it picks a path for each changed entity:

| Change | Path | How |
|---|---|---|
| `pose` | In place | `World.SetPose` |
| `attachOffset` | In place | `World.SetAttachOffset` |
| `render` | In place | `World.SetRender` |
| `asFrame.orbit` only | In place | `World.SetOrbit`, which applies the orbit at the start time |
| Component field values, same component names | In place | `World.ReplaceComponent` with a new instance from the recipe |
| `velocity`, `angularVelocity`, `topics`, `body` | Prepared entity only | The edit world does not tick, so it shows none of these. The collision overlay of edit mode reads the body from the prepared entity. |
| Scene keys `camera`, `verticalFov`, `sun`, `ambient` | In place | The World properties of the same names |
| Anything else: `blueprint`, `frame`, `attachTo`, other `asFrame` keys, added or removed components, added or removed entities, `startTime` | Rebuild | Prepare the scene, create a new edit world, spawn |
| Any blueprint or patch document | Rebuild | `ContentLoader.ReloadData` with the open documents, then as above |

Each in-place change binds one entity entry again with the preparer of one entity entry (step B4). That preparer applies the patches of the content set to the entry first, as phase 7 of plan 08 does. A blueprint edit rebuilds the edit world after its gesture ends, not on every UI frame. A reload of the data takes tens of milliseconds.

Errors leave the editor usable:

- A broken entity entry is left out of the edit world, with the entities in its frame and its attachments. The hierarchy marks it with an error icon, and the console lists the diagnostics.
- If `ReloadData` fails, the editor keeps the previous content set and edit world, and it shows the diagnostics.
- If the code fails to load, no content set exists. The hierarchy and the raw inspector still work, because they need only the documents.

`EditorSession.AssertInvariants` states the invariants of this orchestration:

- The current document set is the document set of the current history entry.
- Every live entity of the edit world maps to one prepared entity of the active scene.
- Every prepared entity that the preparer did not leave out maps to one live entity.
- Every selected scene id names an entity of the active scene: an entry of its document, or an entity that a patch adds.
- No world uses a content set that is disposed.
- A play world exists exactly in the play states `Playing` and `Paused`.

### The hierarchy

In edit mode, the hierarchy comes from the scene document and from the patches of the content set:

- A root frame is a top node. Inside a frame come its entities, and a frame entity holds its own contents. Attachments sit under their parents.
- Children keep document order, which is also the tie-break of the spawn order (plan 07).
- A drag onto a frame entity sets `frame`. A drag with Alt onto an entity sets `attachTo`. The editor converts the pose, or makes an attach offset, so that the entity keeps its place in space at the start time.
- A drag between two rows changes the member order of `entities`. This changes the spawn order and so the entity handles in play mode. The result is still deterministic.
- Delete removes the entry, its attachments and the contents of a frame entity. A popup lists every entry that goes with it. `Entity` fields that named a removed entry become errors in the console. The editor never clears them silently.
- An entity that a patch adds, such as `shield-hum` of plan 08, is a read-only row with the patch color. Its tooltip names the patch, such as `alice.shields:start-hum`. Without a content set, the hierarchy shows only the scene document.
- New entries get a scene id from the last segment of the blueprint id and a number, such as `crate-2`.
- Rename changes a scene id and every reference to it in the same scene: `frame`, `attachTo`, `camera`, `sun.frame`, and component fields of type `Entity` or `FrameHandle`. `TypeShape` gives the member types. Patches in other packages can name the old id, and the console warns about each one.
- Selection uses `BeginMultiSelect` of Dear ImGui: click, Shift and Cmd. A list clipper draws only the visible rows, so 10,000 entities stay fast.

In play mode, the hierarchy comes from the live play world, once per tick. Entities from the scene show their scene ids. Entities that components spawned show `#index.generation` and their blueprint id.

### The inspector

The inspector shows the selected entity as the merge of its blueprint and its scene entry, with patches. It uses `ContentSet.Explain(scene, sceneId)` of plan 08 for the source of each value:

- A value of the scene entry has a blue marker dot. The context menu offers "Revert to blueprint value", which removes the key from the entry.
- A value from the blueprint shows its source in a tooltip, such as `tertius.demo:ships/corvette`.
- A value that a patch sets has the patch marker, and its widget is read-only. A patch applies to the scene file before the merge, so an edit of the scene entry has no effect at load. The tooltip names the patch, and "Open patch" opens the patch document at that value. `EditorApi.Set` on such a value writes the entry and returns a warning that names the patch.
- An edit writes into the scene entry. "Edit in blueprint" opens the blueprint document at that value.

One widget model exists per value form of `TypeShape` (plan 07). A widget model turns the current value and the widget input into a new `ContentNode`, with no Dear ImGui calls. So the tests cover it with no UI.

| Value form | Widget |
|---|---|
| `bool` | `Checkbox` |
| Integer | Drag field in the range of the type, or a slider for a `[Range]` with two ends |
| `float`, `double` | Drag field with a speed of 0.5 % of the value (at least 0.001), or a slider for a `[Range]` with two ends |
| `string` | Text field |
| Enum | Combo of the camelCase names |
| Flags enum | One checkbox per flag, in a popup |
| Vector | Two to four drag fields. A color picker for engine color keys and for members with `[Color]`. |
| Rotation | Three angles in degrees: yaw about Y, pitch about X and roll about Z, as R = Ry(yaw) · Rx(pitch) · Rz(roll) |
| Pose | Position and rotation |
| Blueprint, asset or topic reference | Text field with a search popup and a drag target |
| `Entity`, `FrameHandle` | Combo of scene ids, and a button that picks in the Scene view |
| List | Table rows with add, remove and move |
| Record | A tree node with the members |
| Union with `kind` | A combo for the kind, then the members of that case |
| Nullable | A checkbox "set", then the inner widget |

Rules for written values:

- A rotation about one principal axis, within 1e-6, is written as `{"axis": ..., "angle": {"deg": ...}}`. Any other rotation is written as `[x, y, z, w]`. People can then read most rotations in the file.
- The rotation of the three angles is R = Ry(yaw) · Rx(pitch) · Rz(roll) for column vectors: roll first, then pitch, then yaw, about the fixed axes of the parent. This is the order of `Quaternion.CreateFromYawPitchRoll` (probe). A test locks it in.
- During a drag of an angle, the inspector keeps the three angles in its UI state. They do not jump between equal rotations with other angles.
- A typed number keeps its text. A dragged number is written with the shortest text that reads back as the same value.
- "Add component" lists only the names that the file can use: its own package, then direct dependencies (plan 08). A search field filters the list.
- The doc comment of a member is its tooltip.

`[Color]` is a new attribute in the mod API, in `Tertius.Simulation`. It marks a `Vector3` or `Vector4` member as a color for the inspector.

The raw inspector edits any content file as a JSON tree: keys, values, add and remove. It serves manifests, patches, files of an unknown kind, and every file after a failed code load.

`InspectorCache` holds compiled accessors and widget models per component type. Play mode reads live values through these accessors, not through reflection on each UI frame. The cache belongs to the code set, and the editor drops it before `CodeSet.Unload` (plan 08, table of holders).

### Play mode

| State | Worlds | Inspector | Ticks |
|---|---|---|---|
| Edit | The edit world | Edits documents | None |
| Playing | The edit world, hidden, and the play world | Live values | At the tick rate |
| Paused | Both | Live values | Only from "one tick" |

- Play prepares the active scene from the documents in memory, saved or not, with the current content set. It spawns the scene into a new world with a new physics backend. The editor camera stays in the Scene view, and the Game view shows the scene's camera entity.
- Pause stops the ticks. The views draw the last tick with interpolation alpha 1.
- One tick runs `World.Tick` once while play mode is paused.
- Stop disposes the play world and its physics backend. Play never touches the documents or the edit world, so the edit state is the same as before play.
- Changes in the inspector during play write into the live components between ticks. A banner says that stop discards them. "Apply to scene" copies the live values of one component into the scene entry, as one undoable edit (group C).
- Click the Game view to send keyboard and mouse input to the game. Press Shift+F1 to give the input back to the editor.

Code reload:

1. A watcher sees a changed DLL in the `bin/` folder of a package, and waits 500 ms for more changes.
2. If play mode runs, stop it, and say so in the console.
3. Dispose the edit world and every content set. Drop `InspectorCache` and the message trace.
4. Call `CodeSet.Unload`. If a package context stays alive, show warning CT5010 and a banner that asks for a restart. The editor still continues.
5. Load the content again, and rebuild the edit world from the documents. The documents keep every unsaved change.

The Build button runs `dotnet build` for the code project of the selected package as a child process. The console shows its output, and each compiler error opens its file and line. The DLL watcher then starts the reload.

### Assets in the editor

The editor connects the asset pipeline of plan 09 like this:

- One `AssetStore` lives for the whole editor process. The editor creates it at start, with the renderer and with `CheckFreshness` on. Every load and every `ReloadData` uses it, so unchanged assets keep their ids.
- Before play spawns the play world, it calls `IPhysicsBackend.Prepare` on the new physics backend for each shape of `AssetStore.Shapes`. The sync point then never builds a large shape. The edit world has no physics, so it needs no shapes.
- `AssetWatcher` watches the `assets/` folder of every source package of the load set. It imports on its background thread and raises `Imported` on that thread.
- The handler of `Imported` only puts the report into a queue. In step 2 of each UI frame, the main thread takes the reports and calls `AssetStore.Reload`. `Reload` calls `IRenderer.Replace*`, and the renderer records its commands on the main thread (decision 13).
- A drop of a file onto the window runs `add` of plan 09 on a background thread, and its report goes through the same queue.
- The console shows the diagnostics of every import, and the asset browser marks the assets that failed.
- A `.blend` source needs the assets shell of plan 09. In the default shell, a changed `.blend` file gives AS6001 in the console, and the last good output stays.

After `Reload`, the editor acts on the flags of `AssetReload`:

| Flag | Edit world | Play world |
|---|---|---|
| None | Nothing to do: meshes, textures, materials and models change in place with the same ids. | The same |
| `CollisionChanged` | Rebuild, so the collision overlay shows the new shapes | It keeps its old shapes until stop. The next play prepares the new ones. |
| `StructureChanged` | `ContentLoader.ReloadData`, then a rebuild | It keeps the old content set until stop. Then the editor disposes that set. |

`AssetStore` holds no mod types, so a code reload leaves it alone.

### Views in the renderer

A view is an offscreen target that the renderer draws one scene into. Its tone-mapped image is a UI texture.

- Each view owns its G-buffer, depth and HDR images. They take 38 bytes per pixel, as in `RenderTargets` today, plus 4 bytes per pixel for the output image.
- A 1920 × 1080 view therefore takes about 87 MB. A 2400 × 1600 view, a panel of 1200 × 800 points on a Retina screen, takes about 161 MB.
- A view configuration value sets a resolution factor of 50 %, 75 % or 100 %. The default is 100 %.
- A view resizes after its panel keeps one size for 3 UI frames. Until then, the old image stretches. A resize waits for the device to be idle, so it must not run on every UI frame of a drag.
- The window output of the editor draws only the UI. So the renderer allocates its main G-buffer only for a scene drawn straight to the output, as in the launcher.
- One shadow map serves every view, because the views record one after another.
- At most one view per UI frame uses impostors. The impostor manager keys its state on the entity and the part, so two cameras fight over the captures. The primary view is the Scene view in edit mode and the Game view in play mode. Other views draw full geometry.
- The Scene and Game views share one dock node as tabs. So by default only one 3D view draws per UI frame.

`World.BuildRenderSnapshot(RenderSnapshot, in CameraView)` builds a snapshot from the editor camera. It reads the same front tables as the normal build. It does not read or write the interpolation memory of the snapshot builder, so the Game view keeps its interpolation in play mode. The Scene view draws the latest tick with no interpolation. On a 120 Hz screen, the Scene view therefore moves in steps of 60 Hz during play.

### The editor camera

```csharp
/// <summary>The editor camera: a frame, an eye pose in that frame and a vertical field of view.</summary>
public readonly record struct CameraView(FrameHandle Frame, DPose Eye, float VerticalFov);
```

`EditorCamera` keeps a `CameraView` and moves it:

- The camera belongs to the deepest frame whose bubble holds the eye. It leaves a frame only after the eye is outside the bubble by 2 % of the bubble radius. These are the hand-off rules of ARCHITECTURE.md, with a radius of 0 for the eye.
- A move to another frame converts the eye pose with `FrameTree.Relative`, in doubles.
- The camera moves with its frame. A camera inside an orbiting ship stays in the ship during play.
- "Lock to frame" turns off the frame changes, for work near the edge of a bubble.
- The status bar shows the frame path of the camera, such as `sol > terra > corvette`. A click on a path entry flies to that frame.

The render origin is the eye, because the snapshot builder writes every instance relative to the camera in doubles. The overlays use the same math. So no separate floating origin exists.

Flight speed:

```csharp
double nearest = Math.Min(gpuNearest ?? double.PositiveInfinity, cpuNearest);
double speed = speedFactor * Math.Clamp(nearest, 0.05, 1e12);   // m/s
```

where

- `gpuNearest` = the smallest eye distance in the probe block, in meters. The probe block is 16 × 16 pixels at the center of the view, read from the distance target. For a block of only sky, the value is null. It arrives 2 UI frames late, which is fine for speed.
- `cpuNearest` = the smallest distance from the eye to the surface of a bounding sphere in the snapshot, in meters. A sphere that holds the eye does not count, so the GPU value covers the inside of a ship.
- `speedFactor` = the user's factor, 1 by default, changed by the mouse wheel during flight by 1.25 per step. Shift multiplies it by 4.
- `speed` = the camera speed in meters per second.

A camera 1 m from a wall then moves at about 1 m/s. A camera 1e7 m above a planet moves at about 1e7 m/s.

Other camera controls:

- Orbit mode turns the camera about a pivot: the selection, or the point under the mouse from the distance target.
- The orbit distance changes by a factor of 1.1 per wheel step, so it works at every scale.
- "Fly to selection" moves the camera into the frame of the selected entity, at 2.5 bounding radii. The distance changes along a log scale over 0.5 s, so a jump from 1 m to 1e11 m looks smooth.

### Picking

- A click in the Scene view asks for a pick at that pixel.
- In that UI frame, a pick pass draws the visible instances of each depth range again, from far to near. A scissor cuts it to one pixel. It writes the entity index and the generation into a 1 × 1 R32G32_UINT image.
- The pass draws impostor billboards too. The result arrives after the GPU frames in flight.
- The editor makes sure that the handle is alive with `World.IsAlive`. The selection then maps the handle to a scene id through the `SceneInstance` of plan 07.
- Transparent instances are not in the G-buffer. If the pass finds nothing, the CPU tests the click ray against the bounding spheres of the transparent instances.
- `RenderInstance` gets an `EntityGeneration` field for this. The pick shader reads it from a per-instance buffer that the batcher fills only in UI frames with a pick.

Box selection projects the entity positions on the CPU. It needs no GPU work.

### Gizmos

The gizmo is ImGuizmo. It draws into the draw list of the Scene view window, over the view image.

- View matrix: the camera rotation, expressed in the axes of the frame of the selected entity. The camera is at the origin.
- Model matrix: the entity pose relative to the camera, in float.
- Projection: the infinite reversed-Z projection of the view, without the Y flip of Vulkan clip space. ImGuizmo maps screen Y upward. The pinned ImGuizmo takes its mouse ray from the near plane and handles a far plane at infinity (`ComputeCameraRay`, after its issue #423).
- World mode uses the axes of the entity's frame. Local mode uses the axes of the entity.
- The editor reads the delta matrix of ImGuizmo and applies it to the document pose in doubles. Errors therefore do not add up over a long drag.
- An attached entity moves its `attachOffset`, in the axes of its parent.
- A Kepler frame has no translation gizmo, because its orbit sets its pose. The inspector edits the orbit.
- The scale tool changes the render scale only. For an entity with a body, the toolbar warns that collision does not scale.
- Snap steps: 0.1 m, 15° and 0.1. A toolbar menu changes them.
- The view cube of ImGuizmo (`ViewManipulate`) sits in the top right corner of the Scene view.

### Overlays

The editor builds a `LineList` for each view. Positions are floats relative to the render origin, computed in doubles. The line pass draws them after tone mapping. A line behind geometry, by the distance target, draws at 30 % alpha.

| Overlay | Lines |
|---|---|
| Frame bubble | 3 ellipses in the principal planes of the frame, 64 segments each. A sphere also gets its outline as seen from the camera. |
| Kepler orbit | 128 points, equal steps of the eccentric anomaly, in the parent frame |
| Frame axes | A small triad at each frame origin |
| Selection | The outline circle of the bounding sphere (group B). A real outline pass replaces it in group C. |
| Collision shapes | Box, sphere and capsule from their parameters. Meshes from the triangles of their `MeshShapeDesc`. Hulls from a convex hull of their points, which the editor computes in C# at the first draw (group C). |
| Light ranges | A circle at the range of each light of the selection |
| Grid | The XZ plane of the camera's frame, with a spacing of 10 to the power of the floor of log10 of the camera height |

The colors of bubbles and orbits follow the depth in the frame tree (see the palette).

### Group C panels

Asset browser:

- A tree of packages and folders, and a grid of thumbnails. The thumbnails are the `thumbnail.png` files of plan 09, 256 × 256 pixels each.
- Thumbnails load as UI textures, with a cache of 256 textures: 256 KB each, 64 MB in all. Released slots of the bindless array go back to the renderer. Content textures and UI textures share the 4,093 user slots of the array.
- Filters by kind: models, textures, blueprints and scenes. A search field matches ids.
- A drag of a blueprint into the Scene view or the hierarchy adds an entity entry with that blueprint. A drag of a model asset adds an entry with the render section that plan 09 defines for models.
- A drag of a file from the operating system into the window imports it into the selected package folder, through the import of plan 09.
- The asset inspector shows the import configuration and the record of the source: URL, author and license (plan 09).
- Rename uses `tertius content rename` of plan 08.

Blueprint editor:

- Each blueprint document opens as a tab next to the Scene view.
- The left side shows the parent chain, with links.
- The center is a small view of a preview world that holds only the blueprint, with a default sun.
- The right side is the inspector for the blueprint document, with the source of each resolved value from `ContentSet.Explain`.
- "Create blueprint from entity" moves the overrides of a scene entry into a new blueprint file and points the entry at it.

Console:

- Lines from `World.Log`, `World.Errors`, content diagnostics, build output and the editor.
- Filters by severity and by text.
- A diagnostic opens its file at its line in the external editor. The command is in the editor configuration, with `{file}`, `{line}` and `{column}` placeholders, and `code --goto {file}:{line}:{column}` is the default.
- A diagnostic whose pointer is inside a scene entity also selects that entity.

Message log:

- `World.EventTrace` records each routed event at sync step 8: the tick, the type, the sender, the target, the recipients, the count of recipients that the event mask dropped, and the payload bytes.
- The trace is on only while the Messages panel is open. With the trace off, the router pays one branch per tick.
- A ring buffer keeps the last 300 ticks (5 s), about 26 MB at 1,000 events per tick.
- The trace never keeps the `Extra` object of an event. It can be an instance of a mod type, and such a reference keeps a package context alive. The trace keeps its type name.
- Columns: tick, type, sender, target, recipients and payload. The payload shows its fields, read from the bytes with the payload type of `EventTypeRegistry`.
- A filter shows only the inbox of the selected entity, in delivery order. This order is the same in every run (ARCHITECTURE.md, Messages), so it helps with questions of determinism.

Profiler:

- `World.LastTickTimings`: the time of each tick phase. The phases are the parallel gameplay work, the physics step and the sync steps of ARCHITECTURE.md. One timestamp at each phase boundary costs under 1 µs per tick, so the timings are always on.
- GPU times per pass and per view from timestamp queries, read after the GPU frames in flight. `RendererOptions.GpuTimings` turns them on, and the editor sets it. On this machine, vulkaninfo reports `timestampComputeAndGraphics = true`, `timestampPeriod = 1` and 64 valid bits.
- Editor CPU times: UI build, draw data copy, overlays, snapshot builds and world sync.
- A bar per UI frame for the last 240 UI frames with `ImGui.PlotHistogram`, and a table of averages, minimums and maximums. ImPlot is not needed for this.

Debug panel:

- Renderer, per view: impostors, shadows, exposure, and a display mode. The modes are lit, albedo, normals, specular and gloss, emissive, distance, depth range, and impostor tint. Each mode is a switch of the tone map shader, computed from the G-buffer.
- Overlays: bubbles, orbits, frame axes, grid, light ranges and collision shapes.
- Physics: the collision shapes with a color per motion type (static grey, kinematic blue, dynamic green, character yellow).

Packages panel:

- Every package of the content roots and mod roots, with id, version, title, origin, load index and state: loaded, disabled, skipped or broken.
- The dependencies of the selected package as a tree.
- Switches that turn mods on and off. The editor configuration keeps them as `ContentRequest.Disabled`.
- Buttons for a new package, Build, Reload, the content check of `tertius content validate`, and `explain` of the selected id.

### Agent hooks: the editor script API

`EditorApi` has one public method per editor action. Panels call it, the script runner calls it, and the live channel calls it. Arguments and results are records that convert to JSON.

A script uses the step format of plan 06 and adds the editor steps:

```jsonc
{
  "window": { "width": 1600, "height": 1000, "scale": 1 },
  "content": { "game": "tertius.demo", "contentRoots": ["content"] },
  "steps": [
    { "do": "open", "scene": "tertius.demo:start" },
    { "do": "select", "ids": ["crate-1"] },
    { "do": "set", "entity": "crate-1", "pointer": "/pose/position", "value": [2.5, 0.4, -4] },
    { "do": "camera", "focus": "crate-1" },
    { "do": "capture", "target": "window", "file": "crate-moved.png" },
    { "do": "capture", "target": "panel:Inspector", "file": "crate-inspector.png" },
    { "do": "play" },
    { "do": "step", "ticks": 60 },
    { "do": "dump", "what": "entity", "entity": "crate-1", "file": "crate-after-1s.json" },
    { "do": "stop" },
    { "do": "expect", "pointer": "/entities/crate-1/pose/position", "equals": [2.5, 0.4, -4] }
  ]
}
```

| Step | Effect |
|---|---|
| `open` | Opens a scene or a blueprint document |
| `select` | Sets the selection by scene ids |
| `set`, `remove` | Writes or removes a value by JSON Pointer in an entity entry or a document |
| `add-entity`, `add-component`, `delete`, `rename` | The hierarchy and inspector actions |
| `undo`, `redo`, `save` | History and saving |
| `camera` | Sets the camera frame and eye pose, or flies to an entity |
| `click` | A click at a point of a view, for picking |
| `play`, `pause`, `step`, `stop` | Play mode |
| `reload` | Reloads data or code |
| `frames` | Runs the given number of UI frames |
| `capture` | Writes a PNG of the window, of one panel, or of one view |
| `dump` | Writes JSON: an entity entry, a document, the selection, the history, the diagnostics, profiler numbers, or the world state dump of plan 06 |
| `expect` | Compares a document value or a dump value with an expected value |

- The command is `tertius editor run --script <file> [--headless] [--out <folder>] [--json]`.
- With `--json`, each step prints one JSON line: the step number, `ok`, the written files, the diagnostics and the time.
- The exit codes are the codes of every `tertius` subcommand, in the table of plan 08. A failed `expect` gives code 6.
- `dump` and every `EditorApi` result write component values and payloads through the value writer of plan 07, which uses `TypeShape`. They never give a mod type to `JsonSerializer`, because its caches keep a package context alive (plan 08).
- A panel capture crops the window image to the rectangle of the panel.

Rules that make a scripted run give the same images every time:

- The window size and the UI scale come from the script.
- The layout file is off, so the default layout always applies.
- Every UI frame has a delta time of 1/60 s.
- Text cursors do not blink, and the mouse rests outside every window unless a step moves it.
- The views use the deterministic switch of plan 03.
- Every number that comes from a clock, such as UI frame times in the status bar, shows as `-`.

The live channel (group C) lets an agent drive the open editor while the user watches. `tertius editor --listen` opens a Unix domain socket at `.tertius/editor/editor.sock` with file mode 0600. It takes the same step objects as JSON lines and answers with the same results. It is off by default, and it never listens on a network port.

### Engine changes

Every engine-only member gets `[EngineOnly]` from plan 08. Each change goes in test-first.

| Module | Change | Used for | Step |
|---|---|---|---|
| Rendering | `TextureData.Mips` (default true), `UpdateTexture`, `ReleaseTexture` with slot reuse after the GPU frames in flight | UI textures, thumbnails | A2 |
| Rendering | `UiDrawList`, the UI pass, `RendererOptions.UnormOutput` | The UI | A2 |
| Rendering | `RenderView`, `CreateView`, `RenderFrame`, the main G-buffer only on demand | Scene, Game and preview views | A4 |
| Simulation | `CameraView`, `World.BuildRenderSnapshot(RenderSnapshot, in CameraView)` | The editor camera | A4 |
| Simulation | `ColorAttribute` in the mod API | The inspector | B3 |
| Simulation | `World.SetPose`, `SetAttachOffset`, `SetRender`, `SetOrbit`, `ReplaceComponent`, outside a tick only | The in-place path | B4 |
| Core | `RenderInstance.EntityGeneration` | Picking returns a full handle | B5 |
| Rendering | The line pass, the pick pass, the distance probe | Overlays, picking, flight speed | B5 |
| Simulation | `World.GetAttachParent`, `World.GetBlueprint`, `World.GetBody`, `Blueprints.NameOf(BlueprintId)` | The play hierarchy, collision overlays | B6 |
| Simulation | `World.LastTickTimings` | The profiler | B6 |
| Simulation | `World.EventTrace` | The message log | C4 |
| Rendering | `RendererOptions.GpuTimings`, pass times in `RenderStats` | The profiler | C5 |
| Rendering | Per-view impostor and shadow switches, display modes, the selection outline pass | The debug panel | C6 |

### Needs from plans 07, 08 and 09

Each need has one step that owns it. Some are small additions to `Tertius.Content` that only the editor uses, so steps of this plan write them.

| Plan | Need | Owner |
|---|---|---|
| 07 | `ContentEdit` in `Tertius.Content`: pure tree edits by JSON Pointer that share unchanged subtrees | This plan, step B1 |
| 07 | A partial scene: the preparer leaves out a broken entry with its contents and attachments, and reports diagnostics. | This plan, step B2 |
| 07 | The value writer: a typed value to a `ContentNode`, the inverse of the binder. A round-trip test binds what it writes. | This plan, step B3 |
| 07 | A preparer for one entity entry against a prepared scene, with the patches of the content set. Public reads of `PreparedEntity`: pose, render data, body, recipes, frame and parent ids. | This plan, step B4 |
| 08 | `ContentRequest.Overrides`: trees in memory, by file path, that `Load` and `ReloadData` use instead of the files on disk | This plan, step B2 |
| 08 | `ContentLoader.FindPackages`: the package index and its diagnostics, also for a load that fails | Plan 08, step 3 |
| 08 | `ContentSet.Explain(scene, sceneId)`: one entity entry merged over its blueprint, with patches and sources | Plan 08, step 9 |
| 09 | `thumbnail.png` for each asset | Plan 09, step 16 |
| 09 | The importer as a library call, `AssetWatcher`, and `AssetStore.Reload` behind the same ids | Plan 09, steps 13 and 18 |
| 07, 09 | `render.model` for a dropped model asset | Plan 07, and plan 09 step 14 |

### Code

New editor types, beyond those in the sections above:

```csharp
namespace Tertius.Editor;

public enum PlayState { Edit, Playing, Paused }

/// <summary>
/// The state of one editor: the documents, the history, the selection, the content set, the edit world and play mode.
/// Every member runs on the main thread.
/// </summary>
public sealed class EditorSession : IDisposable
{
    public EditorDocuments Documents { get; }
    public Selection Selection { get; }
    public History History { get; }
    public PlayState Play { get; }
    public ContentSet? Content { get; }
    public World? EditWorld { get; }
    public World? PlayWorld { get; }
    public IReadOnlyList<ContentDiagnostic> Diagnostics { get; }

    /// <summary>Runs the edit, pushes the result to the history and marks the world for the next sync.</summary>
    public void Edit(string label, Func<EditorDocuments, EditorDocuments> edit);

    /// <summary>Asserts the invariants listed in "From documents to the edit world". Tests and the debug switch call it.</summary>
    public void AssertInvariants();
    public void Dispose();
}

/// <summary>Every editor action. Panels, scripts and the live channel call these methods and nothing else.</summary>
public sealed class EditorApi
{
    public EditorApi(EditorSession session, IEditorViews views);

    public StepResult Open(string id);
    public StepResult Select(IReadOnlyList<string> sceneIds);
    public StepResult Set(string entity, string pointer, ContentNode value);   // scripts read the value with ContentReader
    public StepResult Remove(string entity, string pointer);
    public StepResult AddEntity(string sceneId, string? blueprint, string? frame, DPose pose);
    public StepResult AddComponent(string entity, string component);
    public StepResult Delete(IReadOnlyList<string> sceneIds);
    public StepResult Rename(string oldId, string newId);
    public void BeginGesture(string key);                                       // panels only: drags merge into one entry
    public void EndGesture();
    public StepResult Undo();
    public StepResult Redo();
    public StepResult Save(string? path = null);
    public StepResult Camera(CameraRequest request);
    public StepResult Click(string view, int x, int y);
    public StepResult StartPlay();
    public StepResult Pause();
    public StepResult Step(int ticks);
    public StepResult StopPlay();
    public StepResult Reload(ReloadKind kind);
    public StepResult Capture(string target, string file);
    public StepResult Dump(DumpRequest request);
}

public sealed record StepResult(bool Ok, IReadOnlyList<string> Files, IReadOnlyList<ContentDiagnostic> Diagnostics, string? Message = null);
```

New renderer types:

```csharp
namespace Tertius.Rendering;

/// <summary>An offscreen target for one scene. Its output is a UI texture with sRGB-encoded UNORM pixels.</summary>
public sealed class RenderView : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public TextureId Texture { get; }
    public ViewOptions Options { get; set; }
    public RenderStats LastStats { get; }

    /// <summary>The result of the last finished pick, or null. A pick finishes after the GPU frames in flight.</summary>
    public PickResult? TakePick();

    /// <summary>The smallest eye distance in the probe block of the last finished UI frame, in meters. Null for sky only.</summary>
    public float? NearestDistance { get; }

    public void Resize(int width, int height);
    public void Dispose();
}

public sealed record ViewOptions(bool Impostors = false, bool Shadows = true, DisplayMode Display = DisplayMode.Lit, float Exposure = 1f);

public enum DisplayMode { Lit, Albedo, Normals, SpecularGloss, Emissive, Distance, DepthRange, ImpostorTint }

/// <summary>A picked object. Entity.Null means that the pixel showed no object.</summary>
public readonly record struct PickResult(Entity Entity, ushort Part);

/// <summary>Lines relative to the render origin, in meters, with an sRGB color in 0xAABBGGRR order.</summary>
public sealed class LineList
{
    public void Add(Vector3 from, Vector3 to, uint color);
    public void Clear();
}

/// <summary>One view to draw in a frame.</summary>
public readonly record struct ViewDraw(RenderView View, RenderSnapshot Snapshot, float Alpha, LineList? Overlay = null, (int X, int Y)? Pick = null);

/// <summary>The UI of one UI frame. Same layout as ImDrawVert: 20 bytes.</summary>
public struct UiVertex { public Vector2 Position; public Vector2 TexCoord; public uint Color; }
public readonly record struct UiCommand(Vector4 ClipRect, TextureId Texture, int IndexOffset, int IndexCount, int VertexOffset);
public sealed class UiDrawList
{
    public Vector2 DisplaySize { get; set; }
    public Vector2 FramebufferScale { get; set; }
    public List<UiVertex> Vertices { get; } = new();
    public List<ushort> Indices { get; } = new();
    public List<UiCommand> Commands { get; } = new();
}

public sealed class FrameRequest
{
    public List<ViewDraw> Views { get; } = new();
    public UiDrawList? Ui { get; set; }
}

// Additions to VulkanRenderer:
//   public RenderView CreateView(int width, int height, ViewOptions options);
//   public void RenderFrame(FrameRequest request);       // the views in list order, then the UI pass
//   public void UpdateTexture(TextureId texture, int x, int y, int width, int height, ReadOnlySpan<byte> rgba);
//   public void ReleaseTexture(TextureId texture);       // the slot returns after the GPU frames in flight
```

A released slot can hold a new texture later, so an old `TextureId` can name a new texture. In Debug builds, the texture store keeps a generation per slot. A draw with a released id then breaks a contract.

New simulation members:

```csharp
namespace Tertius.Simulation;

public enum TickPhase { Gameplay, Physics, PhysicsResults, IntentSwap, Free, MakeLive, Kepler, Route, SnapshotSwap, RenderSnapshot }

/// <summary>The duration of each phase of the last tick. One Stopwatch timestamp at each phase boundary.</summary>
public readonly struct TickTimings
{
    public double Milliseconds(TickPhase phase);
    public double TotalMilliseconds { get; }
}

// Additions to World, all [EngineOnly], all outside a tick only:
//   public void BuildRenderSnapshot(RenderSnapshot target, in CameraView camera);
//   public void SetPose(Entity entity, in DPose pose);
//   public void SetAttachOffset(Entity entity, in Pose offset);
//   public void SetRender(Entity entity, in RenderData render);
//   public void SetOrbit(FrameHandle frame, in KeplerOrbit orbit);
//   public void ReplaceComponent(Entity entity, IComponent old, IComponent replacement);   // keeps the position in the list
//   public Entity GetAttachParent(Entity entity);
//   public BlueprintId GetBlueprint(Entity entity);
//   public BodyDesc? GetBody(Entity entity);
//   public TickTimings LastTickTimings { get; }
//   public EventTrace? EventTrace { get; set; }
```

### Performance budgets

These are targets, not measurements, on the Apple M1 Max of this machine. Step C9 replaces them with numbers.

| Work | Target | Notes |
|---|---|---|
| UI build, from `NewFrame` to `Render`, default layout, demo scene | Under 1.5 ms CPU | |
| Draw data copy into `UiDrawList` | Under 0.1 ms per 50,000 vertices | Block copies |
| UI pass on the GPU at 2560 × 1600 pixels | Under 0.3 ms | |
| Hierarchy with 10,000 entities, all open | Under 0.5 ms | Visible rows only |
| Overlays for 100 reference frames with bubbles and orbits | Under 0.3 ms CPU | About 40,000 line vertices |
| `CameraView` snapshot build, 10,000 entities | Under 1 ms | |
| In-place sync of one changed entity | Under 50 µs | |
| One edit and diff, 10,000-entity scene | Under 0.2 ms | |
| Rebuild of the demo scene | Under 20 ms | |
| Managed allocations per idle UI frame | Under 16 KB | `GC.GetAllocatedBytesForCurrentThread` |
| Editor overhead per UI frame, without views and ticks | Under 3 ms CPU | The sum of the editor rows of the profiler |
| GPU memory per 1920 × 1080 view | About 87 MB | Computed from the formats |
| History | 256 MB cap | |
| Thumbnails | 64 MB cap | |

### ARCHITECTURE.md changes

1. Toolchain: add a row for the editor UI. Dear ImGui 1.92.9b with docking, through Hexa.NET.ImGui, with `cimgui` from `native/cimgui.nix`.
2. Projects and dependency rules: add `tools/Tertius.Editor`, which references Host, Content, Assets, Import, Rendering, Physics and Simulation.
3. Threads and the tick: the editor runs the UI, the ticks, asset reloads and the recording of GPU commands on the main thread.
4. Rendering: views, the UI pass and its color rule, the line pass, the pick pass, GPU timings, and texture updates and releases with slot reuse.
5. Rendering, the render snapshot: a `CameraView` build leaves the interpolation memory alone. `RenderInstance` has `EntityGeneration`.
6. Entities: the engine-only setters outside a tick, tick timings, and the event trace, which never keeps `Extra` objects.
7. Rules for references: add `InspectorCache` and the event trace to the holders of mod types.

### Facts and beliefs

Verified for this plan:

- The NuGet metadata of Hexa.NET.ImGui 3.1.0-experimental: Dear ImGui 1.92.9b, `net10.0` libraries, HexaGen.Runtime 1.1.22 and Hexa commit `ffc1263`.
- The NuGet metadata of Hexa.NET.ImGuizmo 3.1.0-experimental: ImGuizmo 1.92.5 WIP at commit `dc25afb`.
- Hexa 2.2.9 wraps Dear ImGui 1.92.2b. The fork branch `docking_inter` and the upstream cimgui tags have no 1.92.2b version. Both go from 1.92.1 to 1.92.3.
- Hexa's `cmake.yml`: the cimgui fork, the branch `docking_inter`, the CMake flags and FreeType.
- The fork commits `576040c` (cimgui, Dear ImGui submodule at `b48d1af`) and `7a7dc6b` (cimguizmo), both from 2026-07-31.
- The pinned nixpkgs: `freetype` 2.14.3, `inter` 4.1, `jetbrains-mono` 2.304 and `lucide` 0.563.0 with `Lucide.ttf`.
- The 1.92.9b headers at `b48d1af` have these items of the plan:
  - The DockBuilder API, `BeginViewportSideBar` and the tree lines.
  - `RendererHasTextures`, `RendererHasVtxOffset` and the 16-bit `ImDrawIdx`.
  - `ScaleAllSizes`, `FontScaleDpi`, `MergeMode`, `GlyphMinAdvanceX` and `BeginMultiSelect`.
  - Every `ImGuiCol` name of the palette.
- ImGuizmo at `dc25afb` takes its mouse ray from the near plane and accepts a far plane at infinity.
- vulkaninfo on this machine: timestamps in graphics queues, wide lines and non-solid fill modes.
- The renderer and simulation code that the sections above quote.

Beliefs that a step settles:

| Belief | Step |
|---|---|
| The Nix builds of the pinned fork commits have the same exports and struct sizes as the NuGet libraries of 3.1.0-experimental. | A1 |
| MoltenVK offers a UNORM BGRA8 surface format with the sRGB color space. | A2 |
| Silk.NET 2.23 gives clipboard text, cursor shapes, file drops, the monitor content scale and waiting for events. | A3 |
| The Inter 4.1 archive has static TTF files under `extras/ttf`. | A3 |
| ImGuizmo works with the engine's projection after the removal of the Y flip. | B5 |
| Timestamps on Apple GPUs are exact at the edges of render passes. | C5 |

## Steps

Each step writes its tests first and sees them fail for the right reason. The engine builds and every test passes after each step. Steps of group A and B give the first useful editor. An agent that adds or approves a reference image opens three images with the Read tool: the reference, the actual image and the heat map. Its report describes what it saw.

### Group A: foundation

1. A1, native libraries. Write `native/cimgui.nix`, `native/cimguizmo.nix` and the font derivation, the flake entries and the `UsesImGui` copy. First write the test that creates a Dear ImGui context, turns on docking and runs one empty UI frame. The test also compares the struct sizes with `cimgui-sizes.json`. Done: the test passes in the dev shell, and the symbol lists of the Nix and NuGet libraries are equal.
2. A2, the UI pass. First write CPU tests for `UiDrawList` (indices in range, clip rectangles, offsets) and for slot reuse after the GPU frames in flight. Write a snapshot test that draws a fixed `UiDrawList` headlessly: colored rectangles, a clipped triangle and a texture with an updated rectangle. Then add `UpdateTexture`, `ReleaseTexture`, `TextureData.Mips`, `UnormOutput` and the UI pass. Done: the snapshot is approved after a review.
3. A3, the editor shell. First write CPU tests for the scale rule, the theme values in the style, the key map and the tree of the default layout. Then build the window, the platform backend, `UiBridge`, the fonts, the icons, the theme, the menus, the toolbar, the status bar and empty panels. Done: `tertius editor` opens in the dev shell, and the theme preview window has an approved snapshot at scale 1 and at scale 2.
4. A4, views. First write a Simulation test: a `CameraView` build equals a build with a camera entity at the same eye pose, and it leaves the interpolation memory unchanged. Write renderer tests for the life of a view and the resize after 3 stable UI frames. Then add `RenderView`, `RenderFrame`, the main G-buffer on demand and `BuildRenderSnapshot`. Done: the Scene panel shows the world of plan 06 from a free camera.
5. A5, the headless runner. First write tests for the script parser, the exit codes and the capture of one panel. Then add headless mode, `frames`, `capture` and `expect`. Done: the snapshot tests of A3 and A4 run through scripts.

### Group B: the first useful editor

6. B1, documents and history. Add `ContentEdit` to `Tertius.Content`. First write tests:
   - `ContentEdit` keeps every untouched subtree as the same object, also below a `Rename` or an `Insert`.
   - Push, undo, redo and the loss of the redo entries after a push.
   - A gesture gives one entry, and the cap drops the oldest entries.
   - Dirty and clean after save and after undo to the saved state.
   - An external change to a clean document and to a dirty document.
   - A save does not write after the file changed on disk, and it shows the banner.

   Done: benchmarks of edits on scenes of 200 and 10,000 entities report times and retained bytes.
7. B2, the session and the rebuild path. Add `ContentRequest.Overrides` and partial scenes to `Tertius.Content`, each with its own tests first. Then write these tests:
   - The edit world of the demo scene equals a `SceneSpawner` spawn of the prepared scene, with patches.
   - The order rules of the hierarchy hold.
   - An entity that a patch adds is a read-only row.
   - The selection survives a rebuild.
   - A broken entry is left out with its contents.
   - An unsaved document reaches the content set through `Overrides`.

   Then create the `AssetStore` of the editor, and add opening and saving of scenes, the hierarchy panel and selection. Done: a save with no edits gives the same bytes as `tertius content format`.
8. B3, the inspector. Add the value writer to `Tertius.Content`, with its round-trip tests first. First write one test per widget model and per rule for written values, the rotation order included. Test the value sources and revert, patched values that are read-only, the list of components that a file can add, and the raw inspector. Add `ColorAttribute`. Done: every field of the demo crate and corvette changes through `EditorApi`, and the documents bind with no errors.
9. B4, the in-place path. Add the preparer of one entity entry to `Tertius.Content`, with its tests first: it gives the same entity as a full preparation, patches included. Write Simulation tests for each new setter: outside a tick only, a stale handle throws, and the world invariants hold. Then write the property test: random edit sequences on the demo scene give the same edit world in place as after a rebuild. Done: a drag of 600 UI frames on a scene of 10,000 entities stays within the sync budget.
10. B5, the viewport. First write tests:
    - The frame changes of the camera, with the margin.
    - The speed function and the log-scale path of "fly to selection".
    - Orbit points equal the positions of `KeplerOrbit`.
    - A gizmo delta in frame axes round-trips in doubles.
    - A headless pick of a known pixel gives the expected entity.
    - A snapshot shows lines dimmed behind geometry.

    Then add `EditorCamera`, flight, the line pass, the overlays, the pick pass, the distance probe and the gizmo. Done: a scripted flight from the pilot seat in the corvette to the sun and back gives captures with no jitter.
11. B6, play mode and code reload. First write tests:
    - The play state machine.
    - After stop, the documents and the edit world are the same objects as before play.
    - The labels of the play hierarchy, live inspector values and tick timings.
    - Play calls `Prepare` on its new physics backend for every shape of the store, and the first tick builds no shape.
    - The unload lock-in test. Load a fixture package with code, and select an entity with a package component. Play one tick and dump the entity with `dump`. Then stop and reload the code. A `WeakReference` to the old context must die.
    - Determinism: play in the editor and the scripted run of plan 06 give equal state dumps after 600 ticks.

    Done: the demo plays in the editor, and a code rebuild reloads without a restart.
12. B7, the script API for group B. First write one test per step kind. Then add the remaining steps and the snapshot tests of the group B panels: the demo layout, the inspector of the corvette, the open hierarchy and the Scene view with overlays. Done: a script plays the vertical slice of plan 06 in the editor and writes the same state dumps.

### Group C: the rest

13. C1, the console. Test the filters and the parser of compiler-format lines first. Add the Build button and the external editor command.
14. C2, the asset browser and asset hot reload. First test the thumbnail cache limits and the entries that drops create. Test that `Reload` runs only on the main thread, from the queue. Test the action for each flag of `AssetReload` in edit mode and in play mode. Then add the panel, drags, file drops through plan 09, and `AssetWatcher`.
15. C3, the blueprint editor. Test the parent chain, the value sources and "Create blueprint from entity" first. Add the tabs and the preview world.
16. C4, the message log. Test `EventTrace` in Simulation first: records, the drop count, the ring buffer, the cost with the trace off, and no `Extra` objects kept. Add the panel and the inbox filter.
17. C5, the profiler. Test the timing collection and the averages first. Add GPU timestamps, and compare the sum of the pass times with the frame time on this machine.
18. C6, the debug panel. Write snapshot tests for each display mode and for the selection outline first. Write CPU tests for the hull faces of the collision overlay. Add the switches and the physics overlays.
19. C7, the packages panel. Test the states of discovered packages and the mod switches first. Add the panel.
20. C8, the live channel and "Apply to scene". Test the socket protocol with a fake session and the apply edit first. Add a test helper that records item rectangles in test mode and clicks them through `ImGuiIO`.
21. C9, budgets and records. Run every benchmark and report the numbers against the budgets. Move the decisions of this plan into ARCHITECTURE.md, and mark the plan done.

## Testing

- CPU tests for the document model, history, gestures, the diff and both sync paths.
- CPU tests for the camera, flight, the gizmo math, picking and overlays.
- CPU tests for the theme, the scale rule and the script runner.
- The property test of step B4, which compares the in-place path with a rebuild after random edit sequences.
- Snapshot tests through the headless runner and the FLIP setup of plan 03: the empty layout, the theme preview, the demo layout, the inspector, the hierarchy, the Scene view with overlays, the display modes and the selection outline. Panels that show clock values get CPU tests of their models instead of snapshots.
- The unload lock-in test of step B6.
- The determinism test of step B6: the editor and the launcher give equal state dumps.
- `EditorSession.AssertInvariants` and `History.AssertInvariants` after every step of the editor tests.
- Lock-in tests for the decisions:
  - Undo keeps states and not operations.
  - Stop leaves the documents untouched.
  - A blueprint edit rebuilds after its gesture ends.
  - The pick result includes the generation.
  - Only one view per UI frame uses impostors.
  - The trace keeps no `Extra` objects.
  - A save never overwrites a file that changed on disk.
  - The rotation of the inspector is R = Ry(yaw) · Rx(pitch) · Rz(roll).
  - Dumps and results write mod values only through the value writer.
  - Asset reloads run only on the main thread.
- Benchmarks for every row of the budgets.

## Decided by the user

The user accepted every recommendation of this section on 2026-10-10.

1. The binding is Hexa.NET.ImGui 3.1.0-experimental with Dear ImGui 1.92.9b. Nix builds `cimgui` and `cimguizmo` from known fork commits, as the rule for native dependencies asks. The other option was the stable Hexa 2.2.9 with prebuilt libraries from NuGet.
2. The editor saves a document by itself 1 s after the last edit, and also on Cmd+S. The other option was saving only on Cmd+S.
3. Stop discards the changes that the inspector made during play mode, and "Apply to scene" keeps the values of one component on request. The other option was to write every such change into the scene document.

## Out of scope and later

- Panels in separate OS windows (multi-viewports).
- A light theme. The palette is one table, so a second table can add it.
- ImPlot graphs, node editors and visual scripting.
- The Dear ImGui Test Engine.
- Prefabs: the scene instancing that plan 07 lists as later work.
- A time control that moves Kepler frames in edit mode without ticks.
- Editing of several selected entities at once in the inspector.
- Unit attributes for component fields, such as meters or newtons.
- Native file dialogs and the native macOS menu bar.
- Deletion of asset files from the browser.
- Editor extensions inside packages, such as custom inspectors from mods.
- Replacement of component code inside a live world, which plan 08 rules out.
- An in-game debug UI that uses the same UI pass.
- A move to a stable Hexa.NET.ImGui 3.x release, after one exists.
