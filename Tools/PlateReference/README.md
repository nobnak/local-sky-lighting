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

Open `Assets/PlateLightStudy/PlateLightStudy.unity` to preview the fixed camera,
bowl and rim meshes, and the editable `Amber bowl` material. Its surface map
stores the normal slope in red and green and the polish mask in blue. The
scratch direction map is estimated from the local surface structure and
controls the orientation of the anisotropic lobe over the sharper mirror lobe.
The shader integrates the mirror and anisotropic reflection lobes over
the rectangular emitters in the original light rig. A shared projection
mask controls where these reflections appear and how bright they are.
The view is fixed at 24 degrees from the plate normal to approximate the
opening shot. Tilting the phone moves the mask and rotates the light rig.
Turning the phone horizontally rotates the light rig around the plate normal
in the opposite direction, keeping the plate and view fixed. Both motions use
the phone pose captured at startup or by the reset button as their reference.
Pinch chooses one of three nominal projection diameters. During interactive
viewing, the mask tapers to zero at twice the nominal radius, keeping the
original full-strength core. Drag and arrow keys provide a manual tilt fallback.

Append `&phase=4`, `&phase=9`, or `&phase=14` to hold the compact, broad,
or dimmed lighting used in the comparison. `&phase=cycle` sweeps through
those states. These timings are a look study based on the reference video.

To regenerate the broken grooves, run `refine_strike_texture.py` with the
original packed EXR, the scratch-direction PNG, and the destination EXR.
The script uses OpenEXR, NumPy, SciPy, and Pillow and keeps the polish mask.

The `Plate Post-processing` profile applies Bloom, shadows and highlights
grading, and ACES tone mapping for the SDR canvas. Append `&bloom=0` to the
viewer URL to compare the same lighting and material with Bloom disabled.

Export the mesh, surface map and light rig from the original Blender scene,
then build the Unity Web viewer:

```powershell
blender -b C:/path/to/reference-study/reference-plate.blend --python-exit-code 1 --python Tools/PlateReference/export_unity_plate.py -- Assets/PlateLightStudy/Realtime
Unity.exe -batchmode -quit -projectPath C:/path/to/local-sky-lighting -buildTarget WebGL -executeMethod LocalSkyLighting.PlateLightStudyBuild.BuildWeb
```

GitHub Pages serves the viewer from `docs/plate-light-study/`. Sensor access
uses HTTPS and browsers that require a gesture show a permission button.

### Native HDR output

Use **Local Sky Lighting → Build Struck Plate for Windows** to build the
current plate scene with **Use HDR Display Output** enabled. The initial
output mode depends on the display. The lower-left button switches
output when the display, graphics backend, and driver support runtime HDR
switching. The same button is available in the Game view in Play mode when
the Editor backend supports HDR output. On Windows, DirectX 11 supports HDR
output in the Player, while DirectX 12 also supports it in the Editor.

In the Windows Player, left-drag the view to aim the light.
The plate and camera stay fixed, and the aim persists when you release.

While the plate camera is outputting HDR, its runtime profile bypasses the
SDR tone curve and retains Bloom and color grading. Returning to SDR restores
the saved tone mapping. The Unity Web viewer uses SDR display output.

### Three.js HDR phone-tilt comparison

GitHub Pages also serves `docs/plate-light-hdr/`. It preserves the earlier
5×5 grid of Blender renders with three emitter sizes. The current Unity viewer
uses one white spotlight and varies the beam angle. The HDR/SDR button compares
the same linear frames, scene exposure and shadow curve. Only HDR mode extends
the high end of the highlight curve above SDR reference white. The page uses
WebGPU's extended-range canvas when available and shows SDR on other displays,
with a WebGL 2 fallback for browsers that cannot initialize WebGPU. HDR display
requires a compatible browser, operating-system mode and display. Append
`?hdr-test=1` to show the actual graphics backend, the browser's dynamic-range
media query and the canvas configuration. If the browser reports standard
dynamic range but accepts an extended float canvas, the page starts in SDR
and lets you try the HDR canvas manually. An accepted canvas configuration
does not guarantee that the display presents the extra brightness.

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

### Fixed-light camera move, 0:36–0:47

The [synchronized comparison](https://nobnak.github.io/local-sky-lighting/plate-orbit-comparison/)
uses the official YouTube player as the clock. Each Unity render starts at
0:36 and runs at 24 fps. The page switches between the previous path, a
camera orbit around the plate normal, and the same orbit with sharper
reflections. All variants hold their rig and exposure fixed during capture.
The orbit is a visual approximation of the close-up framing. Camera
transforms and sample times are written to `poses.csv` in each output directory.

```powershell
Unity.exe -batchmode -quit -force-d3d11 -projectPath C:/path/to/local-sky-lighting -executeMethod LocalSkyLighting.PlateOrbitCapture.Render -plateOrbitOutput C:/path/to/orbit
Unity.exe -batchmode -quit -force-d3d11 -projectPath C:/path/to/local-sky-lighting -executeMethod LocalSkyLighting.PlateOrbitCapture.Render -plateOrbitOutput C:/path/to/tuned -plateOrbitTuned
blender.exe -b --python-exit-code 1 --python Tools/PlateReference/encode_orbit.py -- C:/path/to/orbit
blender.exe -b --python-exit-code 1 --python Tools/PlateReference/encode_orbit.py -- C:/path/to/tuned
```

### Overlapping strike comparison

The Unity capture renders three materials from the saved plate scene. A uses
the current material. B uses the same maps with continuous metal reflection
on the bowl. C uses the same response as B with successively stamped tool
faces. Each stamp replaces the slope, finish, and scratch direction inside
its footprint. Slopes are derived from each face before stamping so the
height discontinuity between separate strikes does not introduce a ridge.
The bake checks tool coverage over the bowl interior.

The light pass keeps the camera fixed and sweeps the light rig. The orbit
pass fixes the neutral light rig and orbits the camera around the plate's
normal. Both passes use the same exposure and orthographic framing for all
three materials. The capture uses the saved camera post-processing setting.
The comparison materials are created for the capture and discarded on exit.

```powershell
python Tools/PlateReference/prepare_overlap_surface.py C:/path/to/maps
Unity.exe -batchmode -quit -force-d3d11 -projectPath C:/path/to/local-sky-lighting -buildTarget Win64 -executeMethod LocalSkyLighting.PlateSurfaceComparison.Render -strikeComparisonMaps C:/path/to/maps -strikeComparisonOutput C:/path/to/capture
python Tools/PlateReference/encode_surface_comparison.py C:/path/to/capture
```

The Python steps require NumPy and Pillow. The encoder uses FFmpeg with
libx264 and combines synchronized views into two 24 fps MP4s. Add
`-strikeComparisonPreview` to the Unity command to render five samples per
pass, and pass `--stills-only` to assemble preview sheets.
