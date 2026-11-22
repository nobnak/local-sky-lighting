mergeInto(LibraryManager.library, {
    $LocalSkyMotion: null,

    StartLocalSkyMotion__deps: ['$LocalSkyMotion'],
    StartLocalSkyMotion: function () {
        if (LocalSkyMotion) return;
        const panel = document.createElement('div');
        panel.style.cssText = 'position:fixed;z-index:20;left:50%;bottom:max(14px,env(safe-area-inset-bottom));transform:translateX(-50%);width:max-content;max-width:96vw;text-align:center;color:#fff;font:14px system-ui';
        const button = document.createElement('button');
        button.textContent = 'Enable motion';
        button.style.cssText = 'background:#e6d9bb;color:#272118;padding:10px 16px;border:0;border-radius:6px;font:600 14px system-ui;cursor:pointer';
        const align = button.cloneNode();
        align.textContent = 'Align north';
        align.hidden = true;
        align.style.marginLeft = '8px';
        const hint = document.createElement('div');
        hint.style.cssText = 'margin-bottom:6px;text-shadow:0 1px 4px #000,0 1px 2px #000';
        panel.append(hint, button, align);
        document.body.appendChild(panel);

        const state = LocalSkyMotion = { panel, sample: null, absoluteTime: -Infinity, align: 0, timer: 0 };
        state.onOrientation = function (event) {
            if (![event.alpha, event.beta, event.gamma].every(Number.isFinite)) return;
            const now = performance.now();
            const absolute = event.type === 'deviceorientationabsolute' || event.absolute;
            if (absolute) state.absoluteTime = now;
            else if (now - state.absoluteTime < 500) return;
            const heading = event.webkitCompassHeading;
            const accuracy = event.webkitCompassAccuracy;
            const compass = Number.isFinite(heading) && heading >= 0
                && (!Number.isFinite(accuracy) || accuracy >= 0) ? heading : -1;
            if (state.sample && state.sample[3] !== (absolute ? 1 : 0)) state.calibrated = false;
            state.sample = [event.alpha, event.beta, event.gamma, absolute ? 1 : 0, compass];
            const message = state.calibrated ? 'True north set manually.'
                : 'Lay flat, point the top edge north, then tap Align north.';
            const text = window.innerWidth > window.innerHeight ? 'Use the phone in portrait.' : message;
            if (hint.textContent !== text) hint.textContent = text;
            align.hidden = false;
            clearTimeout(state.timer);
        };
        button.onclick = async function () {
            try {
                if (!window.DeviceOrientationEvent) throw new Error('Motion unavailable');
                if (typeof DeviceOrientationEvent.requestPermission === 'function') {
                    const permission = await DeviceOrientationEvent.requestPermission(true);
                    if (permission !== 'granted') throw new Error('Motion permission declined');
                }
                if (LocalSkyMotion !== state) return;
                window.addEventListener('deviceorientation', state.onOrientation);
                window.addEventListener('deviceorientationabsolute', state.onOrientation);
                button.hidden = true;
                hint.textContent = 'Waiting for motion data…';
                state.timer = setTimeout(function () {
                    if (!state.sample) hint.textContent = 'No motion data. North / South previews still work.';
                }, 3000);
            } catch (error) {
                hint.textContent = error.message + '. North / South previews still work.';
            }
        };
        align.onclick = function () {
            if (!state.sample || window.innerWidth > window.innerHeight) return;
            if (Math.abs(state.sample[1]) > 25 || Math.abs(state.sample[2]) > 25) {
                hint.textContent = 'Lay the phone flat to align north.';
                return;
            }
            state.align++;
            state.calibrated = true;
            hint.textContent = 'True north set manually.';
        };
    },

    StopLocalSkyMotion__deps: ['$LocalSkyMotion'],
    StopLocalSkyMotion: function () {
        if (!LocalSkyMotion) return;
        window.removeEventListener('deviceorientation', LocalSkyMotion.onOrientation);
        window.removeEventListener('deviceorientationabsolute', LocalSkyMotion.onOrientation);
        clearTimeout(LocalSkyMotion.timer);
        LocalSkyMotion.panel.remove();
        LocalSkyMotion = null;
    },

    ReadLocalSkyMotion__deps: ['$LocalSkyMotion'],
    ReadLocalSkyMotion: function (buffer) {
        const state = LocalSkyMotion;
        if (!state || !state.sample || window.innerWidth > window.innerHeight) return 0;
        HEAPF32.set(state.sample, buffer >> 2);
        HEAPF32[(buffer >> 2) + 5] = state.align;
        return 1;
    }
});
