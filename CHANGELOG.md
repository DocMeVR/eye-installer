# Changelog

## [1.2.0](https://github.com/DocMeVR/eye-installer/compare/1.1.0...1.2.0) (2026-10-10)


### Features

* **hide:** split Hide Mesh Islands into its own package, installed as a dependency ([77a12b0](https://github.com/DocMeVR/eye-installer/commit/77a12b02e404e1ce51e72b83082c85427c474e2c))
* **look:** angle the eyes outward or up, and roll them mirrored or per eye ([bb080c3](https://github.com/DocMeVR/eye-installer/commit/bb080c3179261fdcd371f250995d92472047b2bc))

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
