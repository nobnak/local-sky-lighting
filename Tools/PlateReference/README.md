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

## Official-photo appearance study

Use the same plate scene as input. This script generates a procedural packed
EXR of strike slopes and polish coverage, compares the surface and lighting
changes, and saves the selected Blender scene. The official photograph is used
as a visual reference. The Blender script generates its own material maps.

```powershell
blender -b C:/path/to/output/plate-materials.blend --python-exit-code 1 --python Tools/PlateReference/render_reference.py -- --output C:/path/to/reference-study --sweep
```

The output includes Previous, shaped strokes, amber body, a comparison of
AgX and Khronos PBR Neutral, and the selected `06-final.png`. The optional
`--sweep` renders five views of the selected setup with the lighting fixed.
Use `--draft` to reduce resolution and samples.

## Strike boundary and detail study

Open the saved `reference-plate.blend` from the official-photo appearance study
and run the same script with `--surface-study`. It preserves that scene's
lighting, exposure, and framing. The three matched views show the current
surface, continuous height-derived strike normals with independent shading
masks, and the same surface with sub-strike height and polish variation.
The saved scene retains all three materials and packs their EXR data.

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/render_reference.py -- --output C:/path/to/surface-study --surface-study --sweep
```

In this mode `--sweep` renders synchronized camera-only variations for each
of the three materials. `--draft` is available for a quicker static comparison.
