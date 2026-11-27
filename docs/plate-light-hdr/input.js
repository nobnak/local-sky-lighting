import { Quaternion, Vector3 } from 'three/webgpu';

const limit = 24;
const pinchStep = 1.25;
const radians = Math.PI / 180;
const right = new Vector3(1, 0, 0);
const up = new Vector3(0, 1, 0);
const forward = new Vector3(0, 0, 1);
const clamp = angle => Math.max(-limit, Math.min(limit, angle));

export function bindInputs(canvas, setTilt, changeLight) {
  const motion = document.querySelector('#motion');
  const sensor = window.isSecureContext ? window.DeviceOrientationEvent : undefined;
  const needsPermission = typeof sensor?.requestPermission === 'function';
  const touches = new Map();
  const pose = new Quaternion();
  const rotation = new Quaternion();
  const neutral = new Vector3();
  const normal = new Vector3();
  let screenAngle, timer, listening = false, active = false;
  let drag, distance = 0, x = 0, y = 0;

  function steer(horizontal, vertical) {
    x = clamp(horizontal);
    y = clamp(vertical);
    setTilt(x, y);
  }

  function showSensor(state, text) {
    motion.dataset.state = state;
    motion.textContent = text;
    motion.setAttribute('aria-pressed', String(active));
  }

  function reset() {
    if (active) neutral.copy(forward).applyQuaternion(pose);
    steer(0, 0);
  }

  function onPose(event) {
    if (!Number.isFinite(event.beta) || !Number.isFinite(event.gamma)) return;
    const alpha = Number.isFinite(event.alpha) ? event.alpha : 0;
    const angle = window.screen.orientation?.angle ?? window.orientation ?? 0;
    pose.setFromAxisAngle(forward, alpha * radians)
      .multiply(rotation.setFromAxisAngle(right, event.beta * radians))
      .multiply(rotation.setFromAxisAngle(up, event.gamma * radians))
      .multiply(rotation.setFromAxisAngle(forward, -angle * radians));
    if (!active || screenAngle !== angle) {
      active = true;
      screenAngle = angle;
      drag = null;
      clearTimeout(timer);
      showSensor('running', 'センサー動作中');
      reset();
      return;
    }
    normal.copy(neutral).applyQuaternion(rotation.copy(pose).invert());
    steer(Math.atan2(normal.x, normal.z) / radians, Math.atan2(normal.y, normal.z) / radians);
  }

  function start(offerPermission = false) {
    if (!sensor) {
      motion.disabled = true;
      showSensor('idle', 'センサー非対応');
      return;
    }
    listening = true;
    window.addEventListener('deviceorientation', onPose);
    clearTimeout(timer);
    showSensor('waiting', offerPermission ? 'センサーを許可' : 'センサー待機中');
    timer = setTimeout(() => showSensor('idle', offerPermission ? 'センサーを許可' : 'センサー未検出'), 2500);
  }

  function stop(label) {
    window.removeEventListener('deviceorientation', onPose);
    clearTimeout(timer);
    listening = active = false;
    showSensor('idle', label);
    reset();
  }

  motion.addEventListener('click', async () => {
    if (active) {
      stop('センサーを使う');
      return;
    }
    if (!needsPermission) {
      if (!listening) start();
      return;
    }
    motion.disabled = true;
    try {
      if (await sensor.requestPermission() !== 'granted') stop('センサー未許可');
      else if (!active) start();
    } catch (_) {
      stop('センサー未許可');
    } finally {
      motion.disabled = false;
    }
  });
  document.querySelector('#reset').addEventListener('click', reset);
  start(needsPermission);

  function touchDistance() {
    if (touches.size !== 2) return 0;
    const [a, b] = touches.values();
    return Math.hypot(a.clientX - b.clientX, a.clientY - b.clientY);
  }

  canvas.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    canvas.setPointerCapture(event.pointerId);
    if (event.pointerType === 'touch') {
      touches.set(event.pointerId, event);
      if (touches.size > 1) {
        drag = null;
        distance = touchDistance();
        return;
      }
    }
    if (!active && event.isPrimary) drag = { id: event.pointerId, x, y, event };
  });
  canvas.addEventListener('pointermove', event => {
    if (touches.has(event.pointerId)) {
      touches.set(event.pointerId, event);
      if (touches.size > 1) {
        const next = touchDistance();
        if (next && (!distance || next <= distance / pinchStep || next >= distance * pinchStep)) {
          if (distance) changeLight(next < distance ? 1 : -1);
          distance = next;
        }
        return;
      }
    }
    if (drag?.id !== event.pointerId) return;
    const sensitivity = 2 * limit / Math.min(innerWidth, innerHeight);
    steer(drag.x + sensitivity * (event.clientX - drag.event.clientX),
      drag.y + sensitivity * (drag.event.clientY - event.clientY));
  });
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) {
    canvas.addEventListener(type, event => {
      if (touches.delete(event.pointerId)) distance = touchDistance();
      if (drag?.id === event.pointerId) drag = null;
    });
  }
  canvas.addEventListener('keydown', event => {
    if (event.key === '+' || event.key === '=' || event.key === '-') {
      event.preventDefault();
      changeLight(event.key === '-' ? 1 : -1);
      return;
    }
    if (active) return;
    const delta = { ArrowLeft: [-2, 0], ArrowRight: [2, 0], ArrowUp: [0, 2], ArrowDown: [0, -2] }[event.key];
    if (!delta) return;
    event.preventDefault();
    steer(x + delta[0], y + delta[1]);
  });
}
