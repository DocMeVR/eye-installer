# Changelog

## 1.1.0

- Hiding parts: clicks now only select (shown in red), and one **Hide selected parts** button rebuilds the mesh once. Each click used to rebuild and save the whole mesh, which took seconds on faces with many blendshapes.
- The Scene view preview no longer rewrites blendshapes for *Blendshapes scale the eye evenly*; that happens on install. The preview updates sooner after a change.
- A progress bar is shown while hidden parts are rebuilt.

## 1.0.0

First public release.

- Guided installer: Avatar, Eyes, Look, Done.
- Two ways to build the eye: a flat eye behind the eyelids, or the avatar's own eye surface with the new shader.
- Live preview in the Scene view before installing.
- Click to hide leftover highlights and decorations, also as a separate Hide Mesh Islands window.
- Blendshapes can be rewritten so they scale the new eye evenly.
- Installs are rebuilt automatically when the model file is reimported, so the FBX can be edited in Blender.
- Named presets, English and Japanese UI.
