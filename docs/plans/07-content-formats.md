# 07 Content formats: blueprints and scenes

Status: planned. It needs plan 06 for the host. Plan 08 builds packages on top of it. Plans 09 and 10 use both.

## Goal

Store blueprints and scenes in JSON files that people and agents can write by hand. A JSON Schema describes each file kind, so VS Code completes and marks errors while you type.

- A blueprint file describes one kind of entity: its components, body, render data and frame.
- A scene file describes a starting state of a world: a set of entities, their frames, their attachments and their per-instance values.
- The loader turns blueprint files into `Blueprint` records and scene files into spawned entities.

Today a blueprint exists only as a C# object (`src/Tertius.Simulation/Blueprints/Blueprints.cs`), and no scene format exists. This plan owns the file formats, the merge rule, the value grammar, the schemas and the scene spawner. Plan 08 owns packages, ids, mods and code loading. Plan 09 owns the assets that the files name.

Two terms of plan 08 appear in this plan. A content set is the result of one load: the packages, the frozen blueprint registry and the prepared scenes. A code set is the loaded package code under a content set: the component types and the caches over them. Several content sets can share one code set.

## Decisions

1. The files are JSON, and a JSON Schema describes each file kind. (User)
2. The files are JSONC: comments and trailing commas are allowed. Hand-written content needs comments, and `Utf8JsonReader` reads both. The workspace configuration that plan 08 generates opens the files in the JSONC mode of VS Code.
3. A file holds no name and no id. Its id comes from its path (plan 08). One file holds one blueprint or one scene.
4. The loader reads files with its own small reader over `Utf8JsonReader`, not with `JsonSerializer`. There are three reasons:
   - Every error must name a line and a column. `JsonDocument` and `JsonNode` have no source positions (research probe).
   - A value that a merge takes from another file must still name the file that wrote it.
   - The caches of `JsonSerializer` keep a collectible load context alive after they touch a mod type (research probe). The reader keeps no type caches.
5. The binder turns a JSON value into a C# value. The C# type of the target member decides the JSON form of the value. One description per C# type, `TypeShape`, drives the binder, the schema writer and the inspector of plan 10. So the schema and the binder accept the same value forms.
6. One merge rule covers blueprint inheritance, scene overrides and the patches of plan 08. The rule is JSON Merge Patch (RFC 7386), directed by the C# type of each position. Only at a union position does an object with a `kind` member replace the target. One rule is easier to learn and test than three.
7. A collection that content can change is an object keyed by name: components, scene entities and topics. An array holds a value, and a merge replaces it whole. So a merge can add, change or remove one item by its name.
8. Numbers use the units of the code: SI units and radians. Where degrees are easier to read, a file writes `{"deg": 30}`. A color can also be an sRGB hex string. See "Units".
9. Keys are the camelCase form of the C# member names, as `JsonNamingPolicy.CamelCase` makes them. Enum values are camelCase names too. Component names keep their C# form, because they are type names.
10. A scene is a flat set of entities keyed by scene id. A scene id is a name that is unique in one scene. Each entity names the frame that it is in and its attachment parent. In a nested file, every move of an entity to another frame is a large edit. The editor and the patches of plan 08 work on one entity at a time.
11. In a scene file, a component field of type `Entity` holds a scene id. The spawner writes the handle after it spawns every entity of the scene. So the order of the entities in the file does not matter for references.
12. The loader resolves blueprint inheritance on the JSON tree, after the patches of plan 08. It gives the registry fully resolved blueprints with no parent. A patch to a parent then reaches its children, and the body, render and frame sections merge field by field.
13. File model types in `Tertius.Content` mirror the engine types `BodyDesc`, `RenderData` and `FrameSpec`. They add the forms that files need: asset references, degree literals and file defaults. A small hand-written mapping turns them into engine types, and the schema comes from the file model types.
14. A render section can define a primitive mesh (box, sphere or plane) and a material inline. Tests, prototypes and the demo then need no asset files for simple shapes.
15. The reader keeps comments in the tree, also a comment after a value on the same line. The writer writes one canonical layout and puts each comment back at its value. The editor of plan 10 can then save a hand-written file and keep its comments.
16. Errors use the compiler format that the `$msCompile` problem matcher of VS Code reads. Agents get the same errors as JSON.
17. A value that a file sets is immutable. Every spawn of a blueprint shares the bound values. A mutable shared value lets one entity change every other entity of its blueprint, and the chunk workers then race.

## Design

### File kinds

| File | Folder in a package | Owner | Purpose |
|---|---|---|---|
| `manifest.json` | The package root | Plan 08 | Version, dependencies and authors of the package |
| `*.blueprint.json` | `blueprints/` | This plan | One entity template |
| `*.scene.json` | `scenes/` | This plan | A starting state of a world |
| `*.patch.json` | `patches/` | Plan 08 | A change to a blueprint or scene of another package |
| `*.import.json` | `assets/` | Plan 09 | Import configuration and provenance of source assets |

Plan 08 gives the full rules for ids and references. In short:

- `ships/corvette` is a path in the package of the file that writes it.
- `tertius.base:ships/corvette` names the package too.
- An asset reference keeps the file extension. It can name a sub-asset after `#`: `ships/corvette.blend#Hull`. A sub-asset is a named thing inside an asset file, such as a piece or a material. The text after `#` is the fragment. Plan 09 defines the fragments of each asset kind.
- A name or reference always resolves in the package of the file that wrote it. This rule also holds after a merge mixes values from several files.

### Value grammar

The binder reads a value in the form that the C# type of the target member asks for.

| C# member type | JSON form | Example |
|---|---|---|
| `bool` | `true` or `false` | `true` |
| An integer type | A whole number in the range of the type | `12` |
| `float`, `double` | A number, or a degree literal | `0.25`, `{"deg": 30}` |
| `string` | A string | `"hello"` |
| An enum | The camelCase name of a value | `"dynamic"` |
| A flags enum | An array of camelCase names | `["visible", "castsShadow"]` |
| `Vector2`, `Vector3`, `Vector4` | An array of 2, 3 or 4 numbers. A `Vector3` or `Vector4` can also be an sRGB hex color. | `[0, 1, 0]`, `"#ff8a3d"` |
| `Double3` | An array of 3 numbers, read as doubles | `[1.495978707e11, 0, 0]` |
| `Quaternion`, `DQuaternion` | A rotation (see "Rotations and poses") | `{"axis": [0, 1, 0], "angle": {"deg": 90}}` |
| `Pose`, `DPose` | A pose, or a node pose (see "Rotations and poses") | `{"position": [0, 1, 0]}`, `{"node": "ships/corvette.blend#AirlockInner"}` |
| `FrameBounds` | A radius for a sphere, or 3 radii for an ellipsoid | `50`, `[30, 22, 60]` |
| `Entity` | A scene id. Only scene files can set it. | `"airlock-outer"` |
| `FrameHandle` | The scene id of a frame entity. Only scene files can set it. | `"corvette"` |
| `Topic` | A topic name | `"input"` |
| `BlueprintId` | A blueprint reference | `"weapons/bolt"` |
| `MeshId`, `ModelId`, `MaterialId`, `TextureId` | An asset reference. A mesh or material can also be inline. | `"ships/corvette.blend#Hull"` |
| `ShapeDesc` | A shape object | `{"kind": "sphere", "radius": 1}` |
| `T?` | The form of `T`. A missing key leaves the initial value of the member, which can be null. | |
| `IReadOnlyList<T>`, `ImmutableArray<T>` | An array of values in the form of `T` | `[[0, 0, 1], [0, 0, -1]]` |
| A struct with a public parameterless constructor | An object. Its keys are the camelCase names of its settable members. | `{"min": 1, "max": 4}` |
| A class with a public parameterless constructor whose settable members are all `init` properties | An object, as for a struct | `{"min": 1, "max": 4}` |

These rules apply to every value:

- A settable member is a public instance field that is not read-only or constant. A public property with a public `set` or `init` accessor and no index parameters is also settable. This is the rule of `Blueprints` today. The new `ComponentMembers` class in Simulation becomes the one place that holds it.
- A member of a type that the table does not list cannot be set from a file. A file that sets it gets error CT3005, and the schema leaves the member out.
- The reader keeps the text of every number. The binder parses that text once, as the member type, with the invariant culture. So a `float` never rounds twice through a `double`.
- An integer outside the range of the member type is error CT3003. A number with a fraction for an integer member is error CT3002.
- `[Range]` from `System.ComponentModel.DataAnnotations` on a member sets a minimum and a maximum. The binder enforces them and the schema states them. A range also catches many degree values written into radian fields.
- A `TextureId` member has a texture slot: the kind of texture that it needs. `[TextureSlot(...)]` on the member sets it, and the default is `TextureSlot.Any`. Plan 09 makes sure that the image fits the slot.
- Positional records without a parameterless constructor, dictionaries and Euler angles are left out of this plan.

The binder reads each value once per blueprint, and every spawn of the blueprint gets the same value object. So a file can set only immutable values:

- `string`, every scalar, and the engine types of the table. The engine never changes a `ShapeDesc` or another engine value after it makes it.
- `IReadOnlyList<T>` and `ImmutableArray<T>`, with an element type that is immutable by these rules. The binder makes an `ImmutableArray<T>` for both, so no component can cast the list back to an array.
- A struct whose settable members have immutable types. An assignment copies a struct, so its settable fields are safe.
- A class whose settable members are all `init` properties of immutable types, such as a record with `init` members.

A `T[]`, a `List<T>`, and a class with a `set` accessor or a public field are error CT3005. The message names the immutable type to use instead.

### Units

| Quantity | Unit in files | Notes |
|---|---|---|
| Length, position, radius, light range | m | Positions in a frame are doubles. Offsets are floats. |
| Angle | rad, or a degree literal | This includes the field of view, cone half-angles and Kepler angles. |
| Angular rate | rad/s | A degree literal turns deg/s into rad/s. |
| Time, epoch, start time | s | An epoch is absolute simulation time. |
| Velocity | m/s | In the axes of the frame of the entity |
| Angular velocity | rad/s | In the axes of the frame of the entity |
| Mass | kg | |
| Force | N | For component fields |
| Acceleration, uniform gravity | m/s² | |
| Gravitational parameter μ | m³/s² | |
| Damping | 1/s | Jolt's unit |
| Color | Linear RGB, 0 to 1 per channel | Emissive values can be above 1. A hex string is sRGB, and the binder converts it to linear. |
| Light intensity | 1 is 683 cd for point and spot lights | Irradiance at distance d is color × intensity / d². An intensity of 1 is a Blender point light of 4π W, about 12.6 W (plan 09). |
| Scale | A factor | It changes rendering only. Collision does not scale. |

The degree literal `{"deg": x}` is a number literal. The binder writes x × π / 180 in the member type. It is valid where a `float` or `double` is expected, and nowhere else. Files keep radians for three reasons:

- A plain number in a file always has the unit of the code. No unit bug can come from a guessed conversion.
- glTF also stores radians.
- Published data, such as orbital elements or the light cones of Blender, often uses degrees. The literal keeps such a value readable and exactly as published.

The sRGB literal is `"#rrggbb"` or `"#rrggbbaa"`. The binder converts each color channel with the sRGB transfer function and keeps alpha linear. It is valid only for `Vector3` and `Vector4` members.

### Rotations and poses

A rotation has one of two forms:

- `[x, y, z, w]` is a quaternion. The binder normalizes it. If its length differs from 1 by more than 1e-3, the binder reports error CT3004.
- `{"axis": [x, y, z], "angle": a}` is a rotation by the angle a about the axis. The binder normalizes the axis. The angle is a number in radians, or a degree literal.

Euler angles are left out, because they need an order rule. The axis and angle form covers rotations that people write by hand.

A pose is `{"position": [x, y, z], "rotation": r}`, and both keys are optional. A missing position is zero, and a missing rotation is the identity. `default(Quaternion)` is all zeros and not a valid rotation, so the binder never uses it. A `DPose` reads its position as doubles.

A node pose is `{"node": <asset reference>}`. Its value is the pose of a node of a model, relative to the origin of the piece that holds the node (plan 09). A door then takes its place from the model, and no file copies the numbers. A node pose has no other keys.

The axes are the axes of the engine: right-handed, +Y up, and -Z forward for cameras, spot lights and models.

### Blueprint files

A blueprint file has these top-level keys.

| Key | Form | Inherited | Meaning |
|---|---|---|---|
| `$schema` | A string | No | The path of the schema. The loader ignores the value. |
| `parent` | A blueprint reference | No | The blueprint that this one starts from |
| `abstract` | A boolean, `false` by default | No | An abstract blueprint can only be a parent. A spawn of it is error CT4007. |
| `components` | An object: component name to field values | Yes | The components of the entity |
| `body` | A body section, or `null` | Yes | The physics body. `null` removes an inherited body. |
| `render` | A render section, or `null` | Yes | Model, mesh, material, flags and light |
| `asFrame` | A frame section, or `null` | Yes | Makes the entity a frame |
| `topics` | An object: topic name to `true` or `false` | Yes | The topics that the entity subscribes to at spawn. `false` removes an inherited topic. |

Components run in `Priority` order. The order in the file only breaks ties of `Priority`. A merge keeps the order of the parent and appends new components at the end.

The first example is an abstract parent, `content/tertius.demo/blueprints/ships/ship-base.blueprint.json`. Its id is `tertius.demo:ships/ship-base`.

```jsonc
{
  "$schema": "../../.schemas/blueprint.schema.json",
  // Every ship is a Kepler frame with no gravity of its own.
  // A child blueprint adds the hull, the bubble and the model.
  "abstract": true,
  "components": {
    "Health": { "max": 1000 }
  },
  "asFrame": {
    "driver": "kepler",
    "gravity": { "kind": "none" }
  }
}
```

The child, `content/tertius.demo/blueprints/ships/corvette.blueprint.json`, has the id `tertius.demo:ships/corvette`.

```jsonc
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "parent": "ships/ship-base",
  "components": {
    // Merges with the parent's Health: max becomes 1200.
    "Health": { "max": 1200 },
    "Thruster": {
      "maxThrust": 2.5e5,                      // N
      "nozzles": [[-3, 0, 30], [3, 0, 30]]     // m, in ship axes
    }
  },
  "body": {
    // A Kepler hull moves, so this shape takes the convex markers of the piece Hull.
    "shape": { "kind": "asset", "asset": "ships/corvette.blend#Hull" },
    // The interior is static geometry in the ship's own frame. It takes the triangle mesh markers.
    "interiorShape": { "kind": "asset", "asset": "ships/corvette.blend#Hull" },
    "mass": 3.2e5                              // kg
  },
  "render": {
    "model": "ships/corvette.blend#Hull"       // every part and light of the piece Hull
  },
  "asFrame": {
    "bounds": [30, 22, 60]                     // m, ellipsoid radii in ship axes
  }
}
```

In this example:

- `Health` resolves to `tertius.base:Health`, because `tertius.demo` depends on `tertius.base`. `Thruster` comes from the DLL of `tertius.demo`.
- The resolved corvette has `Health` with `max` 1200 at the position of the parent's `Health`, then `Thruster`.
- The frame section of the corvette merges with the parent's: driver `kepler`, no gravity, and the ellipsoid bubble. The orbit comes from the scene.
- `ships/corvette.blend#Hull` is the piece `Hull` of the Blender file `assets/ships/corvette.blend`. A piece is a node of a model that renders as one unit (plan 09).
- One piece gives two shapes. The body is a moving use, so it gets the convex collision markers of the piece. The interior is a static use, so it gets the triangle mesh markers. Plan 09 defines the markers.

The C# side of `Thruster` shows the mapping from members to keys:

```csharp
namespace TertiusDemo;

/// <summary>Main engines. The ship pushes along -Z from its nozzles.</summary>
public sealed class Thruster : IComponent
{
    /// <summary>Total thrust at full throttle, in newtons.</summary>
    [Range(0, 1e9)] public float MaxThrust;          // key "maxThrust"

    /// <summary>Nozzle positions in ship axes, in meters. The binder gives an ImmutableArray.</summary>
    public IReadOnlyList<Vector3> Nozzles = [];      // key "nozzles"

    public int Priority => 0;
    public EventMask Wants => EventMask.Of<Tick>();
    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox) { ... }
}
```

Four smaller blueprints from the same package show topics, inline primitives, an inline material, a light and a door:

```jsonc
// blueprints/people/crew.blueprint.json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "components": { "PlayerController": { "walkSpeed": 1.6, "jumpSpeed": 3.5 } },
  "topics": { "input": true },
  "body": {
    "shape": { "kind": "capsule", "halfHeight": 0.55, "radius": 0.3 },
    "motion": "character",
    "mass": 75
  },
  "render": {
    "mesh": "people/crew.glb#Body",                // a piece with one material
    "material": "people/crew.glb#Suit",
    "viewOffset": { "position": [0, 0.75, 0] }     // the eye, used when this entity is the camera
  }
}

// blueprints/props/crate.blueprint.json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "body": { "shape": { "kind": "box", "halfExtents": [0.4, 0.4, 0.4] }, "mass": 40 },
  "render": {
    "mesh": { "kind": "box", "halfExtents": [0.4, 0.4, 0.4] },
    "material": { "baseColor": "#8a6b3f", "gloss": 0.3 }
  }
}

// blueprints/props/warning-lamp.blueprint.json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "render": {
    "mesh": { "kind": "sphere", "radius": 0.08 },
    "material": { "baseColor": "#ffb070", "emissive": [4, 2.4, 1.2] },
    "light": {
      "kind": "spot",
      "color": "#ffb070",
      "intensity": 30,
      "range": 8,                                    // m
      "innerConeAngle": { "deg": 25 },
      "outerConeAngle": { "deg": 35 },
      "offset": { "rotation": { "axis": [1, 0, 0], "angle": { "deg": -90 } } }   // points down
    }
  }
}

// blueprints/ship-parts/airlock-door.blueprint.json
{
  "$schema": "../../.schemas/blueprint.schema.json",
  "components": { "Door": { "openSeconds": 2.0 } },
  "body": {
    "shape": { "kind": "asset", "asset": "ships/corvette.blend#AirlockDoor" },
    "motion": "kinematic"
  },
  "render": { "model": "ships/corvette.blend#AirlockDoor" }
}
```

### Sections

The body section maps to `BodyDesc`.

| Key | Form | Default | Notes |
|---|---|---|---|
| `shape` | A shape | None | The resolved blueprint must have one (CT3009). |
| `motion` | `"static"`, `"kinematic"`, `"dynamic"` or `"character"` | `"dynamic"` | A character needs a capsule. |
| `layer` | `"static"` or `"moving"` | `"moving"` | The engine sets the frame hull and ghost layers itself, so files cannot. |
| `mass` | kg | 1 | |
| `friction` | 0 to 1 | 0.5 | |
| `restitution` | 0 to 1 | 0.1 | |
| `linearDamping` | 1/s | 0.05 | |
| `angularDamping` | 1/s | 0.05 | |
| `interiorShape` | A shape | None | The static shape inside the frame of a frame owner |

A shape is an object with a `kind`.

| `kind` | Keys | Engine type |
|---|---|---|
| `box` | `halfExtents` (m) | `BoxShapeDesc` |
| `sphere` | `radius` (m) | `SphereShapeDesc` |
| `capsule` | `halfHeight`, `radius` (m), along +Y | `CapsuleShapeDesc` |
| `convexHull` | `points`: an array of positions, at least 4 | `ConvexHullShapeDesc` |
| `mesh` | `vertices`, `indices`. Only static bodies and frame interiors can use it. | `MeshShapeDesc` |
| `compound` | `children`: an array of `{"shape": ..., "pose": ...}` | `CompoundShapeDesc` |
| `asset` | `asset`: a reference to a piece of a model | The `ShapeDesc` that plan 09 builds from the piece for the use |

The use of an asset shape is static or moving. A static use is an `interiorShape`, or the shape of a body with motion `static`. Every other body is a moving use. The binder passes the use to the resolver, so one piece can give convex hulls for a body and a triangle mesh for an interior.

The physics `ShapeCache` keys shapes on the object reference, not on the value. So the loader builds one `ShapeDesc` object per resolved blueprint, and every spawn of that blueprint shares it. The asset resolver returns one shared object per piece and use. A scene entity that changes its body section gets its own object.

The render section maps to `RenderData`. The mapping starts from `new RenderData()`, never from `default`, which has a zero scale.

| Key | Form | Default | Notes |
|---|---|---|---|
| `model` | A reference to a piece of a model, or to a whole model file | None | The entity draws every part and light of the piece. A section with `model` has no `mesh` and no `material` (CT3011). Plan 09 adds the binding, with `ModelId`. |
| `mesh` | An asset reference, or a primitive | None | Primitives: `{"kind": "box", "halfExtents"}`, `{"kind": "sphere", "radius", "segments", "rings"}`, `{"kind": "plane", "halfSize", "uvRepeat"}` |
| `material` | An asset reference, or an inline material | The default material of the renderer | |
| `scale` | 3 factors | `[1, 1, 1]` | |
| `boundingRadius` | m | 0 | 0 means that the renderer derives it. |
| `flags` | An array of `"visible"`, `"castsShadow"`, `"transparent"`, `"noImpostor"`, `"celestial"` | `["visible", "castsShadow"]` | |
| `viewOffset` | A pose | Identity | The eye of the camera entity |
| `light` | A light section | No light | |

The default flags of a file differ from `new RenderData()`, which has no flags. A file with a render section draws its model or mesh unless the file says otherwise.

An inline material maps to `MaterialDesc`. Each texture key passes its slot to the resolver.

| Key | Form | Default | Slot |
|---|---|---|---|
| `baseColor` | Linear RGBA | `[1, 1, 1, 1]` | |
| `specularIntensity` | 0 to 1 | 0.5 | |
| `gloss` | 0 to 1 | 0.5 | |
| `emissive` | Linear RGB | `[0, 0, 0]` | |
| `albedo` | A texture reference | None | `Albedo` |
| `normal` | A texture reference | None | `Normal` |
| `specularGloss` | A texture reference | None | `SpecularGloss` |
| `emissiveMap` | A texture reference | None | `Emissive`. Emitted radiance is `emissive` times the texture color. Plan 09 adds the binding, with `MaterialDesc.EmissiveMap`. |

The loader creates one material for each distinct inline value and one mesh for each distinct primitive.

The light section maps to `LightData` with `Enabled` set to true. A spot light points along -Z of its offset.

| Key | Form | Default |
|---|---|---|
| `kind` | `"point"` or `"spot"` | `"point"` |
| `color` | Linear RGB | `[1, 1, 1]` |
| `intensity` | 1 is 683 cd | 1 |
| `range` | m | 10 |
| `innerConeAngle` | rad | 0.3 |
| `outerConeAngle` | rad | 0.5 |
| `offset` | A pose | Identity |

The `kind` of a light section is an enum, not the case of a union. So a merge changes it like any other value (see "The merge rule").

The frame section `asFrame` maps to `FrameSpec`.

| Key | Form | Default | Notes |
|---|---|---|---|
| `bounds` | A radius, or 3 radii (m) | None | The bubble. The resolved frame section must have one (CT3009). |
| `driver` | `"static"`, `"kepler"` or `"physics"` | `"static"` | |
| `gravity` | `{"kind": "none"}`, `{"kind": "uniform", "acceleration": [x, y, z], "region": {"min": [...], "max": [...]}}` or `{"kind": "point", "mu": μ}` | None | `region` is optional. |
| `orbit` | An orbit | None | A Kepler frame must have one, and other frames must not (CT7007). |

An orbit has one of two kinds:

- `{"kind": "elements", ...}` holds the elements of `KeplerOrbit`: `semiMajorAxis` (m), `eccentricity`, `inclination`, `longitudeOfAscendingNode`, `argumentOfPeriapsis`, `meanAnomalyAtEpoch` (rad), `epoch` (s), `parentMu` (m³/s²), `axialTilt` (a rotation), `spinRate` (rad/s) and `spinPhaseAtEpoch` (rad).
- `{"kind": "circular", "radius": r, "phase": p, "spinRate": s, "parentMu": μ}` calls `KeplerOrbit.Circular`.

If `parentMu` is absent, the scene preparer takes μ from the point gravity of the parent frame. If the parent frame has no point gravity, that is error CT7006. So μ lives in one place. The orbit conventions are the engine's. The reference plane is the XZ plane of the parent, and +Y is the orbit normal at zero inclination. The rotation order is Ω about +Y, then i about +X, then ω about +Y.

A blueprint can leave out the orbit, as the corvette does, because the scene gives it. Three rules keep an incomplete orbit out of a running world:

- The scene preparer makes sure that every Kepler frame has an orbit with a semi-major axis and a parent μ above 0 (CT7007).
- A `BlueprintId` field that names a Kepler blueprint without a complete orbit is error CT3012. A component can then never spawn a frame that stays at the origin of its parent.
- `World.Spawn` rejects such a `FrameSpec` with an ordinary argument guard (see "Engine changes for this plan").

### Scene files

A scene file has these top-level keys.

| Key | Form | Default | Engine target |
|---|---|---|---|
| `$schema` | A string | None | Ignored |
| `startTime` | s | 0 | `WorldOptions.StartTime`. The host reads it before it creates the `World`. |
| `camera` | A scene id | None | `World.Camera` |
| `verticalFov` | rad | π/3 | `World.VerticalFov` |
| `sun` | `{"frame": id, "color": [r, g, b], "intensity": i}` | None | `SunFrame`, `SunColor`, `SunIntensity` |
| `ambient` | `{"skyColor", "groundColor", "intensity"}` | The engine default | `World.Ambient` |
| `entities` | An object: scene id to entity entry | None | The scene must have it (CT3009). |

An entity entry has these keys.

| Key | Form | Meaning |
|---|---|---|
| `blueprint` | A blueprint reference | The blueprint to start from. Without it, the entity starts empty. |
| `frame` | A scene id | The frame entity that this entity is in |
| `pose` | A pose with double positions, or a node pose | The pose in the frame. For a frame entity, the pose in its parent frame. |
| `velocity` | m/s | Initial linear velocity, in frame axes |
| `angularVelocity` | rad/s | Initial angular velocity, in frame axes |
| `attachTo` | A scene id | The attachment parent. The entity is in the frame of its parent. |
| `attachOffset` | A pose with float positions | The offset from the attachment parent |
| `components`, `body`, `render`, `asFrame`, `topics` | As in blueprint files | Merged over the values of the blueprint |

A scene id matches `^[a-z0-9][a-z0-9_.-]*$` and has at most 64 characters.

Before it spawns anything, the preparer enforces these rules. Each broken rule is an error at the position of the entry.

- An entity with no `frame` and no `attachTo` must have a frame section, and it becomes a root frame. A root frame has no pose, no velocity and no body (CT7004, CT7005).
- An attached entity has no `frame`, `pose`, `velocity`, body or frame section (CT7004).
- A Kepler frame has no `pose` and no `velocity`, because its orbit sets both (CT7004).
- `frame` names a frame entity of the same scene (CT7002, CT7003).
- The `frame` and `attachTo` references form no cycle (CT7001).
- Every Kepler frame has a complete orbit, and no other frame has an orbit (CT7007).
- `camera` names an entity, and `sun.frame` names a frame entity (CT7008).

The spawner spawns every entity with `World.Spawn(SpawnDesc)`, root frames too. A root frame gets a `SpawnDesc` with `AsFrame` set and no `Frame`, so it keeps its components, subscriptions and render data. `World.CreateRootFrame` takes no components, so the spawner does not use it.

The spawner spawns a frame before its contents, and an attachment parent before its children. Among the entities that are ready at the same time, the order in the file decides. This is Kahn's algorithm with a queue ordered by file position. The World needs the handle of the frame and of the parent at spawn time. The tie-break by file order gives the same handles for the same file in a fresh world.

A component field of type `Entity` or `FrameHandle` holds a scene id in a scene file. In a blueprint file, such a field is error CT3008, because no entity exists yet. The spawner keeps a list of the component, the member and the scene id for each such field. After the last spawn, it writes each handle through a setter that it compiled at load time. No tick runs during this time, so no component ever sees a default handle.

This example scene is `content/tertius.demo/scenes/start.scene.json`. It has a star, a planet frame, a ship frame in orbit with entities inside, node poses, an attachment and `Entity` fields.

```jsonc
{
  "$schema": "../.schemas/scene.schema.json",
  "startTime": 0,                                  // s
  "camera": "pilot",
  "verticalFov": { "deg": 70 },
  "sun": { "frame": "sol", "color": [1.0, 0.96, 0.9], "intensity": 1 },
  "ambient": { "skyColor": [0.02, 0.02, 0.03], "groundColor": [0.01, 0.01, 0.01], "intensity": 1 },
  "entities": {
    // The root frame. It has no "frame" key and no pose.
    // stars/sol has point gravity with mu 1.32712440018e20 m^3/s^2 and a 1e13 m bubble.
    "sol": { "blueprint": "stars/sol" },

    // planets/terra: a Kepler driver, point gravity 3.986004418e14 m^3/s^2, a 9e8 m bubble, a 6.371e6 m sphere.
    "terra": {
      "blueprint": "planets/terra",
      "frame": "sol",
      "asFrame": {
        "orbit": {
          "kind": "elements",
          "semiMajorAxis": 1.495978707e11,         // m
          "eccentricity": 0.0167,
          "inclination": 0,
          "longitudeOfAscendingNode": 0,
          "argumentOfPeriapsis": { "deg": 102.9 },
          "meanAnomalyAtEpoch": { "deg": 100.5 },
          "epoch": 0,                              // s
          "axialTilt": { "axis": [1, 0, 0], "angle": { "deg": 23.44 } },
          "spinRate": 7.2921159e-5,                // rad/s
          "spinPhaseAtEpoch": 0
          // No parentMu: the preparer takes it from the point gravity of "sol".
        }
      }
    },

    // A circular orbit 400 km above the surface of terra.
    "corvette": {
      "blueprint": "ships/corvette",
      "frame": "terra",
      "asFrame": { "orbit": { "kind": "circular", "radius": 6.771e6, "phase": { "deg": 40 } } }
    },

    // Interior entities are in the frame "corvette", so their poses are in ship axes.
    "pilot": {
      "blueprint": "people/crew",
      "frame": "corvette",
      "pose": {
        "position": [0, 0.9, -18],
        "rotation": { "axis": [0, 1, 0], "angle": { "deg": 180 } }
      }
    },
    "crate-1": { "blueprint": "props/crate", "frame": "corvette", "pose": { "position": [2.5, 0.4, -6] } },

    // The doors take their poses from nodes of the ship model, so no file copies the numbers.
    "airlock-inner": {
      "blueprint": "ship-parts/airlock-door",
      "frame": "corvette",
      "pose": { "node": "ships/corvette.blend#AirlockInner" }
    },
    "airlock-outer": {
      "blueprint": "ship-parts/airlock-door",
      "frame": "corvette",
      "pose": { "node": "ships/corvette.blend#AirlockDoor" },
      "components": { "Door": { "openSeconds": 3.0 } }    // an override for this door only
    },

    // Entity fields hold scene ids. The spawner writes the handles after every spawn.
    "airlock": {
      "blueprint": "ship-parts/airlock",
      "frame": "corvette",
      "pose": { "position": [0, 1.1, 14] },
      "components": {
        "Airlock": { "innerDoor": "airlock-inner", "outerDoor": "airlock-outer", "cycleSeconds": 8 }
      }
    },

    // An attachment: the lamp follows the outer door and takes its frame.
    "outer-lamp": {
      "blueprint": "props/warning-lamp",
      "attachTo": "airlock-outer",
      "attachOffset": { "position": [0, 1.4, 0.1] },
      "render": { "light": { "color": "#ff8a3d" } }       // merges: only the color changes
    }
  }
}
```

The spawn order of this scene is `sol`, `terra`, `corvette`, `pilot`, `crate-1`, `airlock-inner`, `airlock-outer`, `airlock`, `outer-lamp`. The lamp waits for its parent, and every other entity keeps its file position.

### The merge rule

`Merge(target, patch)` gives a new tree. The merge knows the C# type of each position, and it applies the first rule that fits:

1. If the type of the position is a union and the patch is an object with a `kind` member, the result is the patch. A union is a type whose value is one of several cases, such as a shape, a gravity field or an orbit. Its `kind` member names the case, and its keys depend on the case.
2. If the type of the position is a rotation or a pose, the result is the patch. The keys of these values belong together, and a node pose must not mix with `position`.
3. If the patch and the target are both objects, the result starts with the members of the target, in target order. A patch member with the value `null` removes the member. Any other patch member becomes `Merge(old value, patch value)`. New members come after the old ones, in patch order.
4. In every other case, the result is the patch. An array, a string, a number or a boolean replaces the target.

A union patch with no `kind` merges by rule 3, so it keeps the case of the target. Any other member named `kind` is an ordinary member, because its position is not a union. Three examples are the `kind` of a light section, a component field `Kind`, and a scene entity with the scene id `kind`.

The merge finds the type of a position in two ways:

- The file model types give the types of the keys of blueprint, scene, patch and import files.
- Phase 6 of plan 08 qualifies every component name before any merge. So each member of `components` names its component type, and that type gives the types of its fields.

At a position of unknown type, such as an unknown key or an unknown component, the merge uses plain RFC 7386. The binder then reports the unknown key or name.

| Use | Target | Patch |
|---|---|---|
| Inheritance | The resolved parent, or an empty object for a blueprint with no parent | The inherited keys of the child |
| Scene override | The inherited keys of the resolved blueprint, or an empty object for an entry with no blueprint | The inheritable keys of the entity entry |
| Patch file (plan 08) | The target file, before inheritance | The `merge` object of the patch |

Before a merge, the loader turns each component key into its qualified name. So `Health` in one file and `tertius.base:Health` in another file are one key.

Every file merges onto a resolved tree or onto an empty object. So a `null` never reaches the binder: it removes a member, as in RFC 7386, or it removes nothing. A file cannot set a member to `null`. A removed member is absent from the resolved tree, so the component keeps the initial value of its C# member.

Each value in a merged tree keeps the source span where it was written, and an error in a merged value names that file. Each object also records the keys that a merge removed, each with the source span of its `null`. Plan 08 uses both to blame the package of a patch for an error that the patch caused.

### Mapping to engine types

| Place in the file | File model type | Engine type |
|---|---|---|
| Blueprint file | `BlueprintFile` | `Blueprint`, registered with `Parent: null` |
| `body` | `BodySection` | `BodyDesc` |
| A shape | `ShapeSection` and its kinds | `ShapeDesc` and its subtypes |
| `render` | `RenderSection` | `RenderData` |
| `render.light` | `LightSection` | `LightData` |
| An inline material | `MaterialSection` | `MaterialDesc` |
| `asFrame` | `FrameSection` | `FrameSpec` |
| An orbit | `OrbitSection` and its kinds | `KeplerOrbit` |
| An entity entry | `EntityEntry` | `SpawnDesc` |

### Asset references and the resolver

The binder turns every asset reference into an engine resource through `IAssetResolver` (see "Code"). Plan 09 implements it. This plan defines what the binder passes:

| Field or key | Resolver member |
|---|---|
| `render.model`, `ModelId` fields | `Model` |
| `render.mesh` with a reference, `MeshId` fields | `Mesh` |
| `render.mesh` with a primitive | `Primitive` |
| `render.material` with a reference, `MaterialId` fields | `Material` |
| `render.material` with an inline material | `InlineMaterial`, after `Texture` for each texture key |
| Texture keys of an inline material | `Texture` with the slot of the key |
| `TextureId` fields | `Texture` with the slot of `[TextureSlot]`, or `Any` |
| `{"kind": "asset"}` shapes | `Collision` with the use of the shape |
| Node poses | `NodePose` |

The binder calls the resolver in a fixed order: package load order, then file path, then document order. A resolver hands out renderer ids in the order of the requests, so equal content gets equal ids in every run. Phase 9 of plan 08 can bind in parallel, but it collects the references first and resolves them in this order on one thread.

### Schemas

- There is one schema per file kind: blueprint, scene, patch, import and manifest. Plan 08 owns the patch and manifest schemas, and plan 09 owns the import schema.
- The schemas use JSON Schema draft-07. VS Code fully supports drafts 4 to 7 and only partly supports later drafts (VS Code documentation).
- The schema writer makes the schemas from the `TypeShape` of the file model types and of every component type that a package can see. Plan 08 writes them per package into `<package>/.schemas/`, which git ignores.
- In the blueprint schema, `components` has one property per visible component name, short and qualified. `propertyNames` lists both forms, and `additionalProperties` is false. VS Code then completes component names and field names.
- Descriptions come from the XML documentation comments of the types. The mod SDK of plan 08 turns on the documentation file.
- Each file holds the relative path of its schema in `$schema`. `tertius content format` and `tertius content new` (plan 08) write that path, so people and agents do not count `../` by hand.

The blueprint, scene and patch schemas describe partial documents. A child blueprint leaves out the keys of its parent, and a scene entry or a patch changes only some keys. So these schemas follow three rules:

- They have no `required` keywords. The binder reports a missing required key (CT3009) on the resolved tree only, after every merge.
- Every member of `components`, and every inheritable section, also accepts `null`, because a `null` removes the value.
- A union accepts an object without `kind`, because such a patch merges into the case of the target.

The loader does not use the schemas at load time. The binder applies the same value forms, because both come from `TypeShape`. A conformance test runs `check-jsonschema` and the binder over the same good and bad files. `check-jsonschema` 0.38.0 reads only strict JSON, and a file with a comment fails to parse (probe). So the test first writes each file through the strict mode of `ContentWriter`, which leaves out the comments. The test compares the value-form errors of both tools by JSON Pointer. It leaves out CT3009 and the reference errors, which only the loader can find.

### Reader, writer and errors

The reader works as follows:

- It reads UTF-8, with or without a byte order mark.
- It accepts comments and trailing commas.
- It reports a duplicate key as error CT2002, because `Utf8JsonReader` accepts duplicate keys (probe).
- It gives each value a source span: its file, line, column and JSON Pointer at the time of the read.
- It counts lines and columns from 1. It counts the column in UTF-16 code units, as VS Code does.
- It keeps the text of each number, so that the writer writes an unchanged number exactly as it was.
- It keeps each comment with its markers, such as `// kg`.
- A comment that starts on the line where a value ends belongs to that value as its trailing comment, also after the comma. Every other comment belongs to the next member or array item. A comment after the last member of an object stays at the end of the object.
- It stops at a depth of 64, the default of `Utf8JsonReader`.

A node stores no current JSON Pointer. An edit of plan 10 can rename a key or insert an array item, and every pointer below that place changes. So code that walks a tree computes the current pointer on the way down. The source span keeps the pointer at read time, which names the place in the file on disk.

The writer works as follows:

- It uses two spaces of indent, LF line ends and one final newline.
- It writes members in tree order, with their comments.
- It writes each trailing comment on the line where its value ends, after the comma.
- A value that holds no comment and fits in 100 columns at its indent stays on one line. Every other object and array writes one member or item per line. The root object always writes one member per line.
- It does not keep the original whitespace. So the first save of a hand-written file can change its layout, but never its values or the place of a comment.
- Formatting a file twice gives the same bytes.
- In strict mode, it leaves out every comment and writes plain JSON for tools that cannot read JSONC.

Trees are immutable, and an edit shares the unchanged subtrees. The editor of plan 10 can then keep its undo history as a list of document roots, with no command objects.

An error reads like this:

```
content/tertius.demo/scenes/start.scene.json(41,23): error CT4001: Unknown blueprint 'props/crat'. Did you mean 'props/crate'?
```

The JSON form has the keys `code`, `severity`, `package`, `file`, `pointer`, `line`, `column`, `message` and `hint`. The file, line, column and pointer name the place where the failing value was written. A node that the editor made has no source span. Its diagnostic names the document that holds it and the current pointer.

The binder collects every error. After an error in a value, it skips that value and continues. An unknown component name hides the errors of its own fields, so one typo gives one error.

This plan owns the codes CT2xxx (JSON syntax), CT3xxx (values) and CT7xxx (scenes). Plan 08 owns the other ranges. The loader of this plan also reports four codes that plan 08 defines: CT4001, CT4006, CT4007 and CT4008.

| Code | Meaning |
|---|---|
| CT2001 | The JSON is not well formed. |
| CT2002 | An object has a key twice. |
| CT2003 | The file is not valid UTF-8. |
| CT2004 | The nesting is deeper than 64. |
| CT3001 | Unknown key |
| CT3002 | The JSON value has the wrong form for the member type. |
| CT3003 | The number is outside the range of the type or of `[Range]`. |
| CT3004 | The quaternion is not normalized. |
| CT3005 | The member type cannot be set from a file, or it is not immutable. |
| CT3006 | Unknown enum name |
| CT3007 | Two members of one C# type have the same key. |
| CT3008 | An `Entity` or `FrameHandle` value outside a scene file |
| CT3009 | A required key is missing from the resolved tree. |
| CT3010 | Unknown `kind` |
| CT3011 | A render section has `model` together with `mesh` or `material`. |
| CT3012 | A `BlueprintId` value names a Kepler frame blueprint without a complete orbit. |
| CT7001 | The frames or attachments of a scene form a cycle. |
| CT7002 | `frame` names an entity that is not a frame. |
| CT7003 | Unknown scene id |
| CT7004 | The entry has a key that this kind of entity cannot have. |
| CT7005 | The entry has no frame, no attachment parent and no frame section. |
| CT7006 | A Kepler orbit has no `parentMu`, and its parent frame has no point gravity. |
| CT7007 | A Kepler frame has no complete orbit, or a frame with another driver has an orbit. |
| CT7008 | `camera` or `sun.frame` names a wrong entity. |

### Code

The new module is `src/Tertius.Content`. It references Core and Simulation only. These are its public types for this plan:

```csharp
namespace Tertius.Content;

/// <summary>
/// Where a value was read: the file, the line and column (from 1, the column in UTF-16 code units), and the JSON
/// Pointer of the value in that file at the time of the read.
/// </summary>
public readonly record struct SourceSpan(string File, int Line, int Column, string Pointer);

/// <summary>
/// One value of a content file. Trees are immutable, and edits share unchanged subtrees. A node stores no current
/// JSON Pointer, because an edit above it can change that pointer. Code that walks a tree computes it.
/// </summary>
public abstract record ContentNode(SourceSpan? Span)   // null for a node that the editor made
{
    /// <summary>The comments before this value, each with its markers, such as "// kg".</summary>
    public ImmutableArray<string> Comments { get; init; } = [];

    /// <summary>A comment that starts on the line where this value ends, after the value and its comma.</summary>
    public string? TrailingComment { get; init; }
}
public sealed record ContentMember(string Key, SourceSpan? KeySpan, ContentNode Value);
public sealed record ObjectNode(SourceSpan? Span, ImmutableArray<ContentMember> Members) : ContentNode(Span)
{
    /// <summary>The comments after the last member.</summary>
    public ImmutableArray<string> EndComments { get; init; } = [];

    /// <summary>The keys that a merge removed. Each value is the null node that removed the key, with its span.</summary>
    public ImmutableArray<ContentMember> Removed { get; init; } = [];
}
public sealed record ArrayNode(SourceSpan? Span, ImmutableArray<ContentNode> Items) : ContentNode(Span)
{
    public ImmutableArray<string> EndComments { get; init; } = [];
}
public sealed record StringNode(SourceSpan? Span, string Value) : ContentNode(Span);
public sealed record NumberNode(SourceSpan? Span, string Text) : ContentNode(Span);
public sealed record BoolNode(SourceSpan? Span, bool Value) : ContentNode(Span);
public sealed record NullNode(SourceSpan? Span) : ContentNode(Span);

public static class ContentReader
{
    /// <summary>Parses one file. Returns null and adds errors if the file is not well-formed JSONC.</summary>
    public static ContentNode? Read(string file, ReadOnlySpan<byte> utf8, DiagnosticBag diagnostics);
}

public static class ContentWriter
{
    /// <summary>Writes the canonical layout, with comments. In strict mode, it writes plain JSON with no comments.</summary>
    public static void Write(ContentNode node, IBufferWriter<byte> output, bool strictJson = false);
}

public static class MergePatch
{
    /// <summary>
    /// RFC 7386, directed by the C# type of each position. Only at a union position does a patch with a "kind" member
    /// replace the target. A pose or a rotation replaces whole. A position of unknown type merges by plain RFC 7386.
    /// </summary>
    public static ContentNode Apply(ContentNode target, ContentNode patch, TypeShape shape, Func<string, TypeShape?> componentShape);
}

public enum Severity { Error, Warning, Info }

public sealed record ContentDiagnostic(
    string Code, Severity Severity, string Message,
    string? File, int? Line, int? Column, string? Pointer, string? Package, string? Hint = null)
{
    /// <summary>The compiler form: file(line,column): error CODE: message.</summary>
    public override string ToString() => ...;
}

/// <summary>Collects diagnostics from many threads. Sorted gives a fixed order for the same inputs.</summary>
public sealed class DiagnosticBag
{
    public void Add(ContentDiagnostic diagnostic);
    public bool HasErrors { get; }

    /// <summary>
    /// Sorted by package load order, file path, line, column, JSON Pointer and code. A missing line or column counts
    /// as 0. Plans 08, 09 and 10 use this one order.
    /// </summary>
    public IReadOnlyList<ContentDiagnostic> Sorted();
}

/// <summary>A package id, such as tertius.base. Plan 08 defines the grammar, and step 1 of this plan adds the type.</summary>
public readonly record struct PackageId { ... }

/// <summary>A reference to an asset or to a sub-asset, such as tertius.demo:ships/corvette.blend#Hull.</summary>
public readonly record struct AssetRef(PackageId Package, string Path, string? Fragment);

/// <summary>A component type and its names. Plan 08 discovers them in package DLLs.</summary>
public sealed record ComponentInfo(string Name, PackageId Package, Type Type)
{
    public string QualifiedName => $"{Package}:{Name}";
}

/// <summary>What one content file can see: its own package and the packages it depends on. Plan 08 implements it.</summary>
public interface IContentScope
{
    PackageId Package { get; }
    bool TryComponent(string name, out ComponentInfo component, out string error);
    bool TryBlueprint(string reference, out string qualifiedId, out string error);
    bool TryAsset(string reference, out AssetRef asset, out string error);
}

/// <summary>A value or the reason that there is none.</summary>
public readonly record struct Resolved<T>(T? Value, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>Primitive meshes that a render section can define inline. Value equality makes them cache keys.</summary>
public abstract record PrimitiveMesh;
public sealed record BoxMesh(Vector3 HalfExtents) : PrimitiveMesh;
public sealed record SphereMesh(float Radius, int Segments = 48, int Rings = 24) : PrimitiveMesh;
public sealed record PlaneMesh(float HalfSize, float UvRepeat = 1) : PrimitiveMesh;

/// <summary>Moving: a body that moves, which needs convex shapes. Static: an interior or a static body.</summary>
public enum CollisionUse { Moving, Static }

/// <summary>
/// Turns asset references into engine resources. Plan 09 implements it for asset files. Each method returns the
/// same result for the same input, so equal references share one resource and one ShapeDesc object.
/// Until plan 09 lands, PrimitiveAssets implements it: it creates primitives and inline materials through an
/// IRenderer, or returns None ids without one, and it reports each asset file reference as error CT4008.
/// </summary>
public interface IAssetResolver
{
    /// <summary>ContentLoader calls it after phase 2 with the load set in load order, before any other member. Plan 08 adds it.</summary>
    void BeginLoad(IReadOnlyList<PackageInfo> packages, DiagnosticBag diagnostics);

    Resolved<MeshId> Mesh(AssetRef asset);
    Resolved<ModelId> Model(AssetRef asset);                          // plan 09 adds it, together with ModelId
    Resolved<MaterialId> Material(AssetRef asset);
    Resolved<TextureId> Texture(AssetRef asset, TextureSlot slot);
    Resolved<ShapeDesc> Collision(AssetRef asset, CollisionUse use);
    Resolved<Pose> NodePose(AssetRef node);
    MeshId Primitive(PrimitiveMesh mesh);
    MaterialId InlineMaterial(MaterialDesc material);
}

/// <summary>How values of one C# type look in content files. Built once per type by reflection, at load time.</summary>
public sealed class TypeShape
{
    public Type Type { get; }
    public ValueForm Form { get; }                       // Scalar, Enum, Flags, Vector, Rotation, Pose, Reference, List, Record, Union, Nullable, Map, Components
    public IReadOnlyList<MemberShape> Members { get; }   // records only, in declaration order
    public TypeShape? Element { get; }                   // lists, maps and nullables only
    public IReadOnlyList<UnionCase> Cases { get; }       // unions only: the kind name and the case shape
}
public sealed record MemberShape(string Key, MemberInfo Member, TypeShape Shape, double? Minimum, double? Maximum, string? Description);
public sealed record UnionCase(string Kind, TypeShape Shape);

/// <summary>
/// The shapes of every type that one code set uses. The code set of plan 08 owns it and drops it at unload. Every
/// content set over that code borrows it. Until plan 08, the test loader owns one.
/// </summary>
public sealed class TypeShapes
{
    public TypeShape Of(Type type);
}

/// <summary>One entity of a prepared scene: its spawn data, its component recipes and its handle fix-ups.</summary>
public sealed class PreparedEntity
{
    public string Id { get; }
}

/// <summary>A scene with every reference resolved and every component recipe compiled.</summary>
public sealed class PreparedScene
{
    public string Id { get; }
    public double StartTime { get; }
    public IReadOnlyList<PreparedEntity> Entities { get; }   // in spawn order
}

/// <summary>The entities of one spawned scene, by scene id.</summary>
public sealed class SceneInstance
{
    public IReadOnlyDictionary<string, Entity> Entities { get; }
    public Entity this[string id] { get; }
}

public static class SceneSpawner
{
    /// <summary>
    /// Spawns the scene into an empty world that was created with the scene's start time, and sets the camera,
    /// sun and ambient light. Call it outside a tick. It runs no reflection and reads no JSON.
    /// </summary>
    public static SceneInstance Spawn(World world, PreparedScene scene);
}
```

`PackageInfo` comes from plan 08 with `BeginLoad`. `TextureSlot` is new in Core, because mods name it in an attribute:

```csharp
namespace Tertius;

/// <summary>What a texture is for. The resolver of plan 09 makes sure that the role of the image fits the slot.</summary>
public enum TextureSlot { Albedo, Normal, SpecularGloss, Emissive, Ui, Data, Any }

namespace Tertius.Simulation;

/// <summary>Sets the slot of a TextureId member. Without it, the slot is Any.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class TextureSlotAttribute(TextureSlot slot) : Attribute
{
    public TextureSlot Slot { get; } = slot;
}
```

A probe compiled `[TextureSlot(TextureSlot.Ui)]` on .NET 11. C# picks the attribute class, because the enum does not derive from `Attribute`.

A host uses the module like this:

```csharp
PreparedScene scene = content.Scene("tertius.demo:start");
var world = new World(new WorldOptions { StartTime = scene.StartTime, Blueprints = content.Blueprints }, physics);
SceneInstance instance = SceneSpawner.Spawn(world, scene);
Entity pilot = instance["pilot"];   // the scripted runs of plan 06 find entities this way
```

where

- `content` = the loaded content set of plan 08.
- `content.Blueprints` = the frozen blueprint registry of that set, which every world of the set shares.
- `physics` = the physics backend, or null for a world without physics.
- `instance` = the map from scene ids to the handles of the spawned entities.
- `pilot` = the handle of the entity with the scene id `pilot`.

Plan 10 makes four additions to this module in its own steps. They are `ContentEdit` (step B1), partial scenes (step B2), the value writer (step B3) and the preparer of one entity entry (step B4).

### Engine changes for this plan

Each change goes into Simulation or Core test-first, and the existing tests stay green.

1. `ComponentMembers`: a public static class with the settable-member rule, moved out of `Blueprints`. The binder, the schema writer and the registry use the same rule.
2. `ComponentFactories`: the factory cache becomes an instance, and `Blueprints` takes one in its constructor. One instance belongs to one code set, and every content set over that code shares it. A static cache keeps mod code loaded (research probe).
3. `ComponentRecipe`: a component type that passed the rules, its field values and its compiled factory, with `Create()`. The scene spawner uses recipes for entities with overrides.
4. `Blueprint.Topics` (a list of `Topic`) and `Blueprint.Abstract`. `CreateSpawnDesc` sets `Subscriptions` and rejects an abstract blueprint.
5. `BlueprintId`: an index into one registry plus the tag of that registry, with no public constructor. A registry tag is a number that is unique to one registry. `Blueprints.Declare(IReadOnlyList<string>)` gives every blueprint id its `BlueprintId` before any blueprint registers. The loader declares the ids in ordinal order, so a field can name a blueprint that registers later, or its own blueprint. `World.Spawn(BlueprintId, ...)` and `Self.Spawn(BlueprintId, ...)` use it. A component field of this type resolves at load time, so a typo fails at load and not in the middle of a game. A spawn with the tag of another registry throws, like a stale entity handle. `Blueprints.IdOf(string)` gives the id of a declared blueprint to engine code and tests.
6. `Blueprints.Freeze()`: after it, `Register` throws. A content set freezes its registry, so worlds can share it safely.
7. `WorldOptions.Blueprints`: the registry to share. Null keeps today's behavior, a fresh empty registry.
8. `World.Spawn` rejects a Kepler `FrameSpec` whose orbit has a `SemiMajorAxis` or a `ParentMu` that is not above 0. This is an ordinary argument guard in every build. `StateAt` of a default `KeplerOrbit` returns zero, so without the guard such a frame stays at the origin of its parent with no error.
9. `TextureSlot` in Core and `TextureSlotAttribute` in Simulation, as shown in "Code".

The string overloads `World.Spawn(string, ...)` and `Self.Spawn(string, ...)` stay for tests and tools. Plan 08 marks the `Self` overload `[EngineOnly]`, so mod code spawns only through `BlueprintId` fields that the loader checked.

```csharp
namespace Tertius.Simulation;

public static class ComponentMembers
{
    public static bool TryFind(Type type, string name, out MemberInfo member, out Type memberType);
    public static IReadOnlyList<MemberInfo> Settable(Type type);   // in declaration order
}

/// <summary>
/// Compiled component factories. One instance per code set. Every content set over that code shares it, and
/// dropping it drops every reference to the component types.
/// </summary>
public sealed class ComponentFactories
{
    /// <summary>Validates the type and the fields, converts the values, and compiles a factory once per type and field-name set.</summary>
    public ComponentRecipe Recipe(Type type, IReadOnlyDictionary<string, object?> fields);
}

public sealed class ComponentRecipe
{
    public Type Type { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }
    public IComponent Create();
}

public readonly record struct BlueprintId
{
    public bool IsNone { get; }
}
```

### Performance budgets

These are targets, not measurements. The benchmarks of step 12 replace them with numbers.

| Work | Target | Notes |
|---|---|---|
| Read and bind 1,000 blueprint files of 2 KB each | Under 100 ms of CPU time | The benchmark reads on one thread. Phase 5 of plan 08 reads in parallel, so the wall time of a load is shorter. |
| Prepare a scene of 10,000 entities | Under 100 ms | |
| Spawn a prepared scene of 10,000 entities | Under 30 ms | No reflection and no JSON. One delegate call per component. |
| Compile one factory | About 60 µs | Measured in the research probe |

Loading never runs during a tick.

### ARCHITECTURE.md changes from this plan

- The Blueprints section: blueprints come from files, inheritance uses the merge rule, the registry is frozen and shared per content set, and `BlueprintId` exists. Bound field values are immutable and shared by every spawn.
- A new section, "Content files": the units, the degree and sRGB literals, the value grammar, the merge rule and the scene spawn order.
- Projects: `src/Tertius.Content` depends on Core and Simulation.
- Frames: `World.Spawn` rejects an incomplete Kepler orbit.

## Steps

Each step writes its tests first and sees them fail for the right reason. The engine builds and every test passes after each step.

1. Write `PackageId` and `AssetRef`, with the grammar of plan 08. Write one test per grammar rule.
2. Write the reader. Test positions, UTF-16 columns, comments, trailing comments after a comma, trailing commas, duplicate keys, syntax errors, a byte order mark and the depth limit. Done: the examples of this plan read with no errors.
3. Write the writer. Test that read, write and read give an equal tree. Test that comments survive and that numbers keep their text. Test that a second format gives the same bytes, and that strict mode writes plain JSON. Done: a round trip of every example of this plan keeps each comment at its key, and each trailing comment on the line of its value.
4. Write the merge. Test the examples in Appendix A of RFC 7386, the union rule and the pose rule. Test that a light `kind`, a component field `Kind` and a scene id `kind` merge as ordinary members. Test the member order, the source spans of merged values and the record of removals.
5. Move the member rule into `ComponentMembers`. The `Blueprints` tests stay green.
6. Write `TypeShape` and the binder. Write one test per row of the value grammar and one test per CT3xxx code. Test that `T[]`, `List<T>` and a class with a `set` accessor give CT3005.
7. Make the Simulation and Core changes 2 to 9, one at a time, each with its tests first.
8. Write the file model types and their mapping to engine types. Test each example section of this plan against hand-built `BodyDesc`, `RenderData` and `FrameSpec` values. Use a fake `IAssetResolver` that records its calls. The keys `render.model` and `emissiveMap` bind from step 14 of plan 09, so their tests come with that step.
9. Load blueprints with a fake `IContentScope` that holds one package from a test folder. Test inheritance, `abstract`, topics, shared `ShapeDesc` objects, and inline primitives and materials. Test the order of the resolver calls. Done: the example blueprints without `render.model` register and spawn.
10. Prepare and spawn scenes. Test the spawn order, root frames with components and topics, the `parentMu` default and attachments. Test `Entity` and `FrameHandle` fields, the scene-wide values and every CT7xxx code. Done: the example scene spawns, and one tick runs.
11. Write the schema writer. Approve the generated schemas of the example package after a review. Add `check-jsonschema` to the dev shell and write the conformance test with the strict writer.
12. Write benchmarks for the budgets and report the numbers.

Plan 08 then replaces the fake scope with real packages.

## Testing

- Unit tests for each piece, as the steps list them.
- A corpus of files in `tests/Tertius.Content.Tests/Corpus/good/` and `bad/`. Each bad file has an `.expected.json` file with the expected code, line and column of each error. A new wording of a message does not break a test, but a new code or position does.
- The schema conformance test with `check-jsonschema`.
- Determinism: one scene spawned into two fresh worlds gives the same handles and poses. A parallel read of the corpus gives the same trees and diagnostics as a serial read.
- Lock-in tests for the decisions:
  - The degree literal, the default render flags and the `parentMu` default.
  - The union rule of the merge: a member named `kind` outside a union merges field by field.
  - Arrays that replace, poses that replace, and the spawn order tie-break.
  - Two spawns of one blueprint share one `ImmutableArray`, and a mutable member type gives CT3005.
  - A trailing comment stays on the line of its value after a save.
  - A `null` in a blueprint with no parent leaves the C# initial value of the member.
  - A `BlueprintId` field can name its own blueprint.
- Contracts, for the state that must stay coherent. `PreparedScene.AssertInvariants` states that the spawn order puts frames before contents and parents before attachments, and that every handle fix-up names an entity of the scene.

## Open questions for the user

1. Angles in files.
   - Option A: radians, as in the code, with the `{"deg": x}` literal for readability.
   - Option B: degrees as plain numbers for every engine angle key, and a `[Degrees]` attribute for component fields that want degrees.
   - Recommendation: A. A plain number always has the unit of the code, so no file can have a hidden unit.
2. The layout of a scene.
   - Option A: a flat set of entities keyed by scene id, each with `frame` and `attachTo`.
   - Option B: entities nested inside their frames.
   - Recommendation: A. Moves, patches and editor changes then touch one entry, and a git diff stays small.

## Out of scope and later

- Scene instancing: an entry that spawns another scene as a unit, which is a prefab. A sketch: the entry has `"scene"` instead of `"blueprint"`, and the ids of the inner scene get the entry id as a prefix. The editor of plan 10 can add it for prefabs.
- A save of a running world. A scene describes only a starting state.
- A spawn of a scene into a world that already has entities.
- A binary cooked format for faster loads. Measure the JSON load first.
- Dictionary members, positional records and Euler angles.
- Completion of asset ids and blueprint ids in the schema.
- Localized text.
- Keeping the original whitespace of a hand-written file.
- A per-spawn copy of mutable field values. Immutable values cost nothing per spawn.
