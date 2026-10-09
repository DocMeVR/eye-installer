# DocMe Eye Installer

Puts a procedural eye shader onto a VRChat avatar in four steps. Works with the DocMe eye shaders (sold separately) and with any other material meant for a flat eye.

## Install

1. Add the listing to VRChat Creator Companion: open **https://docmevr.github.io/eye-installer** and press **Add to VCC**, or add `https://docmevr.github.io/eye-installer/index.json` under Settings > Packages > Add Repository.
2. In your avatar project, press **Manage Project** and add **DocMe Eye Installer**.

Unity 2022.3 and VRChat SDK 3.5 or newer.

## Use

Open **Tools > DocMe > Eye Shader Installer**.

1. **Avatar**: drop your avatar in, or have it selected in the Hierarchy. The tool finds the eyes on its own.
2. **Eyes**: the found eyes are tinted in the Scene view (blue left, orange right). If a tint is wrong or missing, press *Fix* and click the iris.
3. **Look**: pick the eye material, choose how to build the eye, set the size. The result shows on the avatar while you change settings. Press **Install eyes**.
4. **Done**: select leftover highlights and hide them in one go, fine tune size and position, show the original eyes for comparison, or remove everything again.

### Two ways to build the eye

- **Flat eye in the socket**: a new flat eye is placed behind the eyelids at the depth of the old iris, and the old eye is hidden. Works on every avatar.
- **Reuse the avatar's own eye**: the original eye surface gets the new shader, so it keeps moving with blinks and expressions. Best when the old eye is already a flat sheet; a round eye is flattened for you.

### Hiding parts

Highlights or decorations from the old eye can sit on top of the new one. On the Done page press **Select parts to hide**, click them in the Scene view (they light up red; a second click unselects), then press **Hide selected parts** or Enter. Every hide rebuilds the mesh once, so select everything first. The same tool is also available on its own under **Tools > DocMe > Hide Mesh Islands** and works for any small part of a mesh.

Hidden parts are folded away by a blendshape and can be brought back at any time. If another blendshape pulls a folded part back into view, turn on *Delete hidden parts instead of folding them*.

### Blender round trip

The tool never changes your model file. It records what it did and rebuilds it when the FBX is reimported, so you can keep editing the model in Blender. Turn this off under Advanced on the Done page if you do not want that.

### Everything is reversible

**Remove the eyes** on the Done page restores the original mesh and deletes the generated files. Generated meshes live in `Assets/DocMe Generated/EyeInstaller`.

## How this was made

The code in this repository was written by AI (Claude, by Anthropic). The idea, the design, every feature, the testing on real avatars and the decisions on how it should behave are mine (DocMe). I planned it and directed the work; the AI typed it out. Bug reports and pull requests are welcome, and anything that gets merged goes through my own review.

## License

MIT, for the installer only. The DocMe eye shaders are not included and may not be resold.
