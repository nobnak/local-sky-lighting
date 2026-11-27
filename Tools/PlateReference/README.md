# Plate reflection study

Blender 4.5 LTS / Cycles. Generates two 1280 × 764 renders with matched dish
geometry, camera, lights, exposure and base material. The second render adds
a tinted dielectric coat. The optional three-second lighting study uses the
coated version and animates three lights against a fixed camera.

```powershell
blender -b --python-exit-code 1 --python Tools/PlateReference/render_plate.py -- --output C:/path/to/output --movie
```

The stills and the Blender scene are generated in the output directory. Add
`--draft` for lower-resolution stills. The scene retains both materials so
they can be switched on the Shallow bowl object.

Visual reference: [Plate for the Sea of Light](https://www.teamlab.art/jp/ew/plate-seaoflight-kyoto/kyoto/).
The procedural surface and lights are a CG interpretation of the photograph.
