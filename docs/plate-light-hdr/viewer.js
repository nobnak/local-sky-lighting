import * as THREE from 'three/webgpu';
import { dot, float, max, min, mix, smoothstep, texture, uniform, uv, vec2, vec3 } from 'three/tsl';
import { EXRLoader } from 'three/addons/loaders/EXRLoader.js';
import { ExtendedSRGBColorSpace, ExtendedSRGBColorSpaceImpl } from 'three/addons/math/ColorSpaces.js';
import { bindInputs } from './input.js';

const status = document.querySelector('#status');
const size = document.querySelector('#light-size');
const mode = document.querySelector('#mode');
const diagnostics = document.querySelector('#hdr-diagnostics');
const hdrTest = new URLSearchParams(location.search).get('hdr-test') === '1';
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
let canvasConfig;
const gpuAttempts = [];
let requestedLight = 0;
let displayedLight = 0;

async function createRenderer(canvas) {
  if (navigator.gpu) {
    THREE.ColorManagement.define({ [ExtendedSRGBColorSpace]: ExtendedSRGBColorSpaceImpl });
    for (const featureLevel of ['core', 'compatibility']) {
      let device, gpu;
      let stage = 'adapter';
      try {
        const adapter = await navigator.gpu.requestAdapter({ featureLevel });
        if (!adapter) {
          gpuAttempts.push(`${featureLevel}: adapterなし`);
          continue;
        }
        stage = 'device';
        const requiredFeatures = adapter.features.has('core-features-and-limits') ? ['core-features-and-limits'] : [];
        device = await adapter.requestDevice({ requiredFeatures });
        stage = 'renderer';
        gpu = new THREE.WebGPURenderer({ canvas: canvas.cloneNode(), device, alpha: false,
          outputType: THREE.HalfFloatType });
        gpu.outputColorSpace = ExtendedSRGBColorSpace;
        await gpu.init();
        if (gpu.backend.isWebGPUBackend) {
          gpuAttempts.push(`${featureLevel}: OK`);
          return gpu;
        }
        gpuAttempts.push(`${featureLevel}: renderer不可`);
      } catch (error) {
        gpuAttempts.push(`${featureLevel}: ${stage}不可`);
        console.warn(`WebGPU ${featureLevel} ${stage} initialization failed.`, error);
      }
      await gpu?.dispose();
      device?.destroy();
    }
  }
  const fallback = new THREE.WebGPURenderer({ canvas, alpha: false,
    forceWebGL: true, outputBufferType: THREE.UnsignedByteType });
  await fallback.init();
  return fallback;
}

function keepControlsInView() {
  const viewport = window.visualViewport;
  if (!viewport) return;
  function update() {
    const bottom = viewport.scale === 1 ? Math.max(0,
      document.documentElement.clientHeight - viewport.height - viewport.offsetTop) : 0;
    document.documentElement.style.setProperty('--viewport-obstruction', `${Math.ceil(bottom)}px`);
  }
  viewport.addEventListener('resize', update);
  viewport.addEventListener('scroll', update);
  window.addEventListener('resize', update);
  update();
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
  const webGpu = renderer.backend.isWebGPUBackend;
  const extended = canvasConfig?.format === 'rgba16float' && canvasConfig?.toneMapping?.mode === 'extended';
  const available = webGpu && hdrDisplay.matches && (!canvasConfig || extended);
  const trial = hdrTest && !hdrDisplay.matches && extended;
  hdrMix.value = (available || trial) && enableHdr ? 1 : 0;
  mode.textContent = hdrMix.value ? (trial ? 'HDR試験' : 'HDR') : 'SDR';
  mode.disabled = !available && !trial;
  mode.title = available || trial ? 'HDRとSDRを切り替える' : 'この表示環境ではSDR表示';
  mode.setAttribute('aria-pressed', String(Boolean(hdrMix.value)));
  if (hdrTest) {
    const backend = webGpu ? 'WebGPU' : navigator.gpu ? 'WebGL 2（WebGPU初期化不可）' : 'WebGL 2（WebGPU APIなし）';
    const canvas = canvasConfig ? `${canvasConfig.format} / ${canvasConfig.toneMapping?.mode ?? 'standard'}`
      : webGpu ? '取得不可' : 'WebGPU未使用';
    diagnostics.textContent = `描画: ${backend}\nブラウザのHDR判定: ${hdrDisplay.matches ? 'high' : 'standard'}\nキャンバス: ${canvas}`;
    if (gpuAttempts.length) diagnostics.textContent += `\nWebGPU: ${gpuAttempts.join(' / ')}`;
    diagnostics.hidden = false;
  }
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
  resize();
  renderer.render(scene, camera);
  if (renderer.backend.isWebGPUBackend) canvasConfig = canvas.getContext('webgpu')?.getConfiguration?.();
  setMode(hdrDisplay.matches);
  new ResizeObserver(resize).observe(canvas);
  hdrDisplay.addEventListener('change', () => setMode(hdrDisplay.matches));
  mode.addEventListener('click', () => setMode(!hdrMix.value));
  bindInputs(canvas, setTilt, changeLight);
  document.querySelector('#controls').disabled = false;
  status.hidden = true;
  // Keep compressed bytes available without retaining three decoded GPU textures.
  Promise.allSettled([loadBytes(1), loadBytes(2)]);
}

keepControlsInView();
start().catch(error => {
  status.textContent = '表示を開始できませんでした。';
  console.error(error);
});
