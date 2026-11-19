# Changelog

The changes of each release of the Modeling Toolkit. Versions follow [semantic versioning](https://semver.org); before
1.0, a minor version may change anything, the native format included, which newer versions keep reading.

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
