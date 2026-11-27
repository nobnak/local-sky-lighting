import * as THREE from 'three/webgpu';
import { dot, float, max, min, mix, smoothstep, texture, uniform, uv, vec2, vec3 } from 'three/tsl';
import { EXRLoader } from 'three/addons/loaders/EXRLoader.js';
import { ExtendedSRGBColorSpace, ExtendedSRGBColorSpaceImpl } from 'three/addons/math/ColorSpaces.js';
import { bindInputs } from './input.js';

const status = document.querySelector('#status');
const size = document.querySelector('#light-size');
const mode = document.querySelector('#mode');
const hdrDisplay = matchMedia('(dynamic-range: high)');
const lightSizes = [100, 50, 25];
const frames = new Map();
const loader = new EXRLoader();
const hdrMix = uniform(0);
const grid = uniform(new THREE.Vector2(2, 2));
const scene = new THREE.Scene();
const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 2);
camera.position.z = 1;

let renderer, atlasNode, renderRequest, sizeTimer;
let requestedLight = 0;
let displayedLight = 0;

async function createRenderer(canvas) {
  if (navigator.gpu) {
    const gpu = new THREE.WebGPURenderer({ canvas, alpha: false, outputType: THREE.HalfFloatType });
    THREE.ColorManagement.define({ [ExtendedSRGBColorSpace]: ExtendedSRGBColorSpaceImpl });
    gpu.outputColorSpace = ExtendedSRGBColorSpace;
    try {
      await gpu.init();
      if (gpu.backend.isWebGPUBackend) return gpu;
    } catch (error) {
      console.warn('WebGPU initialization failed, trying WebGL 2.', error);
    }
    await gpu.dispose();
  }
  const fallback = new THREE.WebGPURenderer({ canvas: canvas.cloneNode(), alpha: false,
    forceWebGL: true, outputBufferType: THREE.UnsignedByteType });
  await fallback.init();
  return fallback;
}

function loadBytes(index) {
  if (!frames.has(index)) {
    const request = fetch(`Frames/${lightSizes[index]}.exr`).then(response => {
      if (!response.ok) throw new Error(`EXR request failed: ${response.status}`);
      return response.arrayBuffer();
    }).catch(error => {
      frames.delete(index);
      throw error;
    });
    frames.set(index, request);
  }
  return frames.get(index);
}

function decodeAtlas(bytes) {
  const data = loader.parse(bytes);
  const atlas = new THREE.DataTexture(data.data, data.width, data.height, data.format, data.type);
  atlas.colorSpace = THREE.LinearSRGBColorSpace;
  atlas.magFilter = atlas.minFilter = THREE.LinearFilter;
  atlas.needsUpdate = true;
  return atlas;
}

function createPlate(atlas) {
  atlasNode = texture(atlas);
  const cell = min(grid.floor(), vec2(3));
  const fraction = grid.sub(cell);
  const inset = vec2(.5 / (atlas.image.width / 5), .5 / (atlas.image.height / 5));
  const frameUv = mix(inset, vec2(1).sub(inset), uv());
  // EXRLoader provides the texture bottom-up; Blender's grid starts at its top row.
  const sample = (x, y) => atlasNode.sample(vec2(cell.x.add(x), float(4).sub(cell.y).sub(y))
    .add(frameUv).div(5)).rgb;
  const rgb = max(vec3(0), mix(
    mix(sample(0, 0), sample(1, 0), fraction.x),
    mix(sample(0, 1), sample(1, 1), fraction.x), fraction.y));

  // Both modes share the same midtone curve. HDR only extends strong highlights.
  const luma = dot(rgb, vec3(.2126, .7152, .0722));
  const tint = rgb.div(max(luma, .00001));
  const contrast = luma.pow(1.3);
  const base = contrast.div(contrast.add(1.45));
  const saturation = float(1).sub(smoothstep(.15, .9, base));
  const sdr = min(vec3(1), mix(vec3(1), tint, saturation).mul(base));
  const bright = max(0, luma.sub(.8));
  const headroom = bright.div(bright.add(3)).pow(2).mul(2.2).mul(hdrMix);
  const material = new THREE.MeshBasicNodeMaterial();
  material.colorNode = sdr.add(mix(vec3(1), tint, .25).mul(headroom));
  const plate = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), material);
  scene.add(plate);
  return plate;
}

function draw() {
  if (renderRequest !== undefined) return;
  renderRequest = requestAnimationFrame(() => {
    renderRequest = undefined;
    renderer.render(scene, camera);
  });
}

function setTilt(x, y) {
  grid.value.set(2 + x / 12, 2 + y / 12);
  draw();
}

function setMode(enableHdr) {
  const available = renderer.backend.isWebGPUBackend && hdrDisplay.matches;
  hdrMix.value = available && enableHdr ? 1 : 0;
  mode.textContent = hdrMix.value ? 'HDR' : 'SDR';
  mode.disabled = !available;
  mode.title = available ? 'HDRとSDRを切り替える' : 'この表示環境ではSDR表示';
  mode.setAttribute('aria-pressed', String(Boolean(hdrMix.value)));
  draw();
}

async function changeLight(step) {
  const next = Math.max(0, Math.min(lightSizes.length - 1, requestedLight + step));
  if (next === requestedLight) return;
  requestedLight = next;
  try {
    const bytes = await loadBytes(next);
    if (next !== requestedLight || next === displayedLight) return;
    const previous = atlasNode.value;
    atlasNode.value = decodeAtlas(bytes);
    previous.dispose();
    displayedLight = next;
    size.textContent = `光源サイズ ${lightSizes[next]}%`;
    draw();
  } catch (error) {
    if (next !== requestedLight) return;
    requestedLight = displayedLight;
    size.textContent = '光源を読み込めませんでした';
    console.error(error);
  }
  size.hidden = false;
  clearTimeout(sizeTimer);
  sizeTimer = setTimeout(() => { size.hidden = true; }, 1000);
}

async function start() {
  const placeholder = document.querySelector('#plate');
  const [result, bytes] = await Promise.all([createRenderer(placeholder), loadBytes(0)]);
  renderer = result;
  const canvas = renderer.domElement;
  if (canvas !== placeholder) placeholder.replaceWith(canvas);
  const atlas = decodeAtlas(bytes);
  const aspect = atlas.image.width / atlas.image.height;
  const plate = createPlate(atlas);
  function resize() {
    const width = canvas.clientWidth, height = canvas.clientHeight;
    const frameWidth = Math.min(width, height * aspect);
    plate.scale.set(frameWidth / width, frameWidth / aspect / height, 1);
    renderer.setPixelRatio(Math.min(devicePixelRatio || 1, 2));
    renderer.setSize(width, height, false);
    draw();
  }
  setMode(true);
  resize();
  renderer.render(scene, camera);
  new ResizeObserver(resize).observe(canvas);
  hdrDisplay.addEventListener('change', () => setMode(true));
  mode.addEventListener('click', () => setMode(!hdrMix.value));
  bindInputs(canvas, setTilt, changeLight);
  document.querySelector('#controls').disabled = false;
  status.hidden = true;
  // Keep compressed bytes available without retaining three decoded GPU textures.
  Promise.allSettled([loadBytes(1), loadBytes(2)]);
}

start().catch(error => {
  status.textContent = '表示を開始できませんでした。';
  console.error(error);
});
