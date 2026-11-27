# Plate reflection study

Blender 4.5 LTS / Cycles. Generates two 1280 × 764 renders with matched geometry,
camera, exposure and lighting. Both materials mix a polished reflection with a
broader reflection from the same alloy. B adds an amber dielectric coat to the
polished component. The saved scene opens with B on the Shallow bowl object.

```powershell
blender -b --python-exit-code 1 --python Tools/PlateReference/render_plate.py -- --output C:/path/to/output --look-study
```

- `--look-study` also renders four matched views of the polished baseline,
  the material changes, the lighting changes, and both changes together.
- `--movie` also renders a three-second lighting study with a fixed camera.
- `--draft` reduces the resolution and samples of the stills.

Visual reference: [Plate for the Sea of Light](https://www.teamlab.art/jp/ew/plate-seaoflight-kyoto/kyoto/).
The procedural surface and lights are a CG interpretation of the reference.
