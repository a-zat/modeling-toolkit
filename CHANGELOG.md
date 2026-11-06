# Changelog

The changes of each release of the Modeling Toolkit. Versions follow [semantic versioning](https://semver.org); before
1.0, a minor version may change anything, the native format included, which newer versions keep reading.

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
