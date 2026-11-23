# Changelog

The changes of each release of the Modeling Toolkit. Versions follow [semantic versioning](https://semver.org); before
1.0, a minor version may change anything, the native format included, which newer versions keep reading.

## 0.3.0 — 2026-11-23

Components that follow each other, layers, and a walk mode with gravity and places to sit.

### Components, anchors and points of view

- **Anchors:** named places in a component, such as the end of a table where a leaf goes or the seat of a chair,
  listed in its properties, added by a click in the 3D view, moved and removed there.
- **Attachments:** a component follows an anchor of another at a fixed offset, so chairs go with the leaf of a table
  that extends and a lamp stays on its shelf; the properties choose the anchor it follows.
- **Points of view:** an anchor marked as a place to look from, with the height and direction of the eyes; the
  properties look from it.
- Several entities selected in the outline are made one component; loose meshes and shapes are made a component from
  the outline. Deleting a component that holds other things asks for confirmation.

### Layers

- An entity is on one layer at most, or on that of the nearest component above it. A hidden layer leaves out of the 3D
  view what is on it with what it places; materials keep their colours.
- The outline shows the structure or the layers, each layer with its entities and the model space on none last, with
  an eye to show or hide each layer, renaming, a new layer and deleting.
- The properties put the selection on a layer, or on a new one; `mtk edit layer=` does it from the command line and
  `mtk tree` shows the layer of each entity.

### Editing

- With the select tool, a drag on the selection moves it from the point pressed, snapped, to where it is let go,
  snapped to what is there; a press without a drag is still a click.
- What a tool moves with the pointer is looked through, so that the point to move to snaps to the vertices and edges
  behind it. A corner snapped to on a mesh shows all the edges that meet there, a point on an edge the whole edge.
- A component turned moves along its own axes too, besides those of the block being edited, and the handles of its box
  slide it along its own axes.
- The handles of the face pointed at stay shown while the pointer is near its sides, so that a corner can be taken.
- A model imported or pasted follows the pointer by its origin when it stands on it, else by the middle of the bottom
  of its box.
- Editing a block shows only that block, in its place; a click outside it does nothing and a double click leaves it.
- The outline selects several nodes to move out, move into a block or delete together, and the model is renamed from
  the outline or its properties.

### Walk mode and interaction

- Gravity follows the ground and climbs steps up to 400 mm.
- A click on a chair takes its point of view: seated, Q and E turn, and Escape or a click on nothing stands up where the
  walk was.
- A click opens a hidden part, such as a leaf of a table, or takes a point of view only inside the component that holds
  it. Optional dots mark what can be used, in every display mode; the interaction is called Interact.
- The **Movement** section of the properties belongs to components; the switch for all the movements of a component,
  in its header, covers only those within the selection.

### The 3D view

- Edges behind a face are no longer drawn through it at mid distances: the faces are drawn deeper by their slope rather
  than the edges closer to the camera.

### Files and command line

- A glTF file saved again keeps its structure and names: the scene names the model, its nodes are the entities of the
  model space, the names of nodes that hold only geometry are kept, a mesh used in several places is one node, and a
  component of a single mesh stays a component.
- Anchors, attachments, points of view and layers are saved in the native format, now at version 7, which still reads
  the files of 0.2.0, and in the extras of glTF files.
- `mtk edit` adds anchors and points of view, attaches and detaches components, puts entities on layers, names
  entities of the same name with #2, #3... and places imports at points of the model; `mtk tree` shows anchors and what
  each component follows.

### Fixes

- A right click on the project in the outline no longer leaves the window unresponsive.
- The wheel no longer steps in the walk mode.

### Known issues

- The application is not signed by a developer identity: once downloaded, macOS refuses to open it until the
  quarantine mark is removed (see the README of the package), and Windows SmartScreen asks for a confirmation.

## 0.2.0 — 2026-11-19

From a viewer to an editor: models can now be drawn, edited and walked through.

### Editing

- Drawing tools with an inference engine that snaps to vertices, middles of edges, centres of circles and arcs, faces,
  the ground and the axes, with typed measures: **Line**, **Rectangle**, **Circle** and **Arc**.
- Shaping tools: **Push/Pull** of faces, **Offset**, **Move**, **Rotate** and **Scale**, with copy, snaps to the global
  axes and a protractor; **Paint** with the material chosen, and **Erase**.
- **Tape measure** and guides, window selection, and cut, copy and paste.
- Handles on the face of the selection under the pointer: corners move it anywhere, the middle of the
  face along the axes, an arrow turns it. A Shift click selects the innermost component, and a right click opens a
  menu of what can be done there.
- Undo and redo of every edit, the project marked with an asterisk when it has unsaved changes, and a prompt to save
  when closing, opening or starting a new project.
- The **outline** of the project: blocks and their references, entering a block with a double click, renaming, a new
  block, Duplicate, moving a reference out of its block or into the project by dragging, and visibility by an eye.
- **Import** of a model into the project as a component, placed with the pointer.
- **Simplify**: cleans up heavy imported meshes with presets, a preview with metrics and an Apply that can be undone;
  the same from the command line with `mtk simplify`.
- Materials on faces, a picker with a library by category and custom colours, textures mapped in millimetres, and the
  native format at version 3. `mtk edit` and `mtk tree` edit and list a model from the command line.

### The 3D view

- The camera turns as on a turntable around the point under the pointer; W, A, S, D glide it over the ground and Q and
  E turn it.
- **Walk mode**: click where to stand and walk through the model at the height of the eyes, with collisions that slide
  along walls, step over thresholds and let doors that open through, and the speed, eye height and field of view as
  settings. The inspector closes as the walk begins.
- **Rendered** display mode: models under a sky with a sun placed for the place and time of day, clouds and a ground,
  and the display modes as a segmented control.
- Edges of meshes drawn as widened quads, and a CAD render style with thick outlines.

### Animations

- Animations of glTF files are read and played: the **Movement** section of the properties of a component lists what
  moves, each named after the component, with a switch and a slider, and a click in the rendered view or the walk, or
  the **Interact** tool, opens and closes doors and drawers.

### Files and application

- Models are exported as `.glb` with their hierarchy, shared meshes, materials and textures.
- glTF files with nodes scaled to nothing at rest, as those of tables that extend, are read.
- Settings in a view with their categories on the left, a new icon of the application, shown in the Dock of macOS also
  when it runs as a plain executable, and the message of the status bar copied with a click.

### Known issues

- The application is not signed by a developer identity: once downloaded, macOS refuses to open it until the
  quarantine mark is removed (see the README of the package), and Windows SmartScreen asks for a confirmation.
- Nothing can be drawn over the 3D view, which is a native view of the operating system (#25).
- Only the base material model of glTF is read: normal, occlusion and emissive textures, alpha modes, double-sided
  materials, texture samplers and transforms, and material extensions are not (#7).
- Walking does not fall or climb on its own (#97), and the movements of components cannot be defined yet (#88).

## 0.1.0 — 2026-11-06

The first release: a viewer of 3D models with exact geometry, a command line tool and the native format they share.

### Import and the native format

- glTF files (`.gltf`, `.glb`) are read with OpenCASCADE, converted to millimetres and Z up, with the hierarchy of
  their nodes, meshes shared by several nodes kept once, materials (base colour, opacity, metallic and roughness) and
  base colour textures. Meshes compressed with Draco and textures in WebP are read too. Files that require an
  extension that would leave out geometry are refused; the others are read with a warning for what is left out.
- The native format `.mtk`, a zip archive with a versioned JSON manifest, keeps blocks and their references, meshes,
  B-rep shapes, materials and textures. Saving never leaves a half-written file, and files that are truncated,
  corrupted, incomplete or of an unknown version are refused as invalid.

### Exact geometry

- Boxes, cylinders, spheres, cones and tori, placed anywhere, and their union, difference and intersection, with the
  faces that the operations split merged again.
- Shapes are turned into triangles for display, with the edges of the shapes, in the background and in parallel.

### Desktop application (macOS and Windows)

- Opens and saves models, in the background, with their progress in the status bar and a button to cancel.
- 3D view drawn by OpenCASCADE: orbit, pan and zoom with the mouse or the trackpad, a cube of the standard views, a
  grid on the ground that follows the size of the model, perspective or orthographic projection, and three display
  modes: shaded, shaded with edges and wireframe. Meshes are drawn with their sharp edges, shapes with their own.
- Model tree and properties: clicking a part of the model selects it in the tree, which fades the rest.
- Materials panel, light and dark themes, and a command palette (`⌘K`, `Ctrl+K`).

### Command line tool `mtk`

- `info`, `convert`, `stats`, `make` (the primitives) and `combine` (the boolean operations).

### Known issues

- The application is not signed by a developer identity: once downloaded, macOS refuses to open it until the
  quarantine mark is removed (see the README of the package), and Windows SmartScreen asks for a confirmation.
- Nothing can be drawn over the 3D view, which is a native view of the operating system (#25).
- On dark materials the tint of the mesh under the pointer is hard to see (#30).
- Only the base material model of glTF is read: normal, occlusion and emissive textures, alpha modes, double-sided
  materials, texture samplers and transforms, and material extensions are not (#7).
- Models cannot be edited yet: editing is the subject of the next milestones.
