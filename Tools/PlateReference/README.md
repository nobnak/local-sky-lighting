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

## Varied strike faces

With the same `reference-plate.blend` input, use `--variety-study` to compare the
original material, the smooth-boundary detail material, and the varied strike
faces. This variant preserves the original strike tilt and flatter interiors.
Normals, polish and the body blend feather over separate widths. Short
internal tool marks break up the reflections. All three views use the input lighting and
exposure. The saved `surface-study.blend` opens with the varied material.

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/render_reference.py -- --output C:/path/to/variety-study --variety-study --sweep
```

## Single-source size study

Use the original `reference-plate.blend` as input. This compares the lower-center
gold area light at 100%, 50%, and 25% width and height, plus a render with that
fixture disabled. All sizes use the saved position, color, and total power.
Other lights, the plate material, and exposure are shared across the comparison.
Use `--sweep` to render matching camera views for the three active sizes.

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/render_light_size.py -- --output C:/path/to/light-size-study --sweep
```

Use `--isolated` with a separate output directory to disable all other fixtures
for the same comparison. The saved world remains active.

### Unity Web phone-tilt viewer

The camera shows the plate face-on. Phone orientation sets a relative light
direction from the first sensor sample, or from the pose at the recenter
button. Dragging provides a manual fallback. Unity interpolates a five-by-five
grid of frontal Blender renders over ±24° per axis. Pinch in or out to step the
lower-center gold area's width and height between 100%, 50%, and 25%. The same
steps are available with the minus and plus keys.

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/render_phone_tilt.py -- --output C:/path/to/phone-tilt
python Tools/PlateReference/pack_phone_tilt_frames.py C:/path/to/phone-tilt Assets/PlateLightStudy/Atlases
Unity.exe -batchmode -quit -projectPath C:/path/to/local-sky-lighting -buildTarget WebGL -executeMethod LocalSkyLighting.PlateLightStudyBuild.BuildWeb
```

GitHub Pages serves the viewer from `docs/plate-light-study/`. Sensor access
uses HTTPS. The page listens for orientation events on startup and offers a
permission button on browsers that require a user gesture.

### Three.js HDR phone-tilt comparison

GitHub Pages also serves `docs/plate-light-hdr/`. It uses the same fixed view,
tilt grid and three light sizes as the Unity viewer. The HDR/SDR button compares
the same linear frames, scene exposure and shadow curve. Only HDR mode extends
the high end of the highlight curve above SDR reference white. The page uses
WebGPU's extended-range canvas when available and shows SDR on other displays,
with a WebGL 2 fallback for browsers that cannot initialize WebGPU. HDR display
requires a compatible browser, operating-system mode and display.

Regenerate the atlases from the original `reference-plate.blend`:

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/render_phone_tilt.py -- --output C:/path/to/phone-tilt-linear --format exr
python -m pip install numpy OpenEXR
python Tools/PlateReference/pack_phone_tilt_exr.py C:/path/to/phone-tilt-linear docs/plate-light-hdr/Frames
```

The Blender frames are scene-linear half-float OpenEXR. The three 5×5 atlases
use lossy DWAA compression at level 20 to retain HDR range with a smaller web
payload. The page loads its pinned Three.js modules from jsDelivr and needs no
Unity Web build for this route.
