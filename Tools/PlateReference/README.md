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

## Camera-only comparison

Generate the plate scene first, then open it in Blender's background process.
The orbit script moves the camera through ±12° of azimuth and ±8° of tilt.
It renders the current material, rounded strike normals with variation inside
each strike, and two separate color treatments of the rounded material.
Lights stay fixed in each treatment. The added colored sources are confined
to the colored-lights treatment.

```powershell
blender -b C:/path/to/output/plate-materials.blend --python-exit-code 1 --python Tools/PlateReference/render_orbit.py -- --output C:/path/to/orbit
```

The output contains one PNG sequence per treatment. Use `--frames 3` for the
endpoints and center, or omit it for the 17-frame sweep.

Blender 4.5 applies thin-film interference to dielectrics. The thin-film
treatment mixes a separate dielectric reflection with the metallic substrate.
Use `--variant film-control --frames 3` for the matched dielectric comparison
with the film disabled.
