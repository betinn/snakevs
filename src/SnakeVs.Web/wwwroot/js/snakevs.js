// Snake VS web host: frame loop, canvas sizing and the small browser services the game asks for.
// Loaded before the KNI scripts so the AudioContext hook is in place when the engine creates its context.
(function () {
    'use strict';

    const MAX_PIXEL_RATIO = 3;
    const SAVE_KEY = 'snakevs.save';

    // Browsers start audio suspended until the first user gesture: remember every context and resume it then.
    const audioContexts = [];
    const NativeAudioContext = window.AudioContext || window.webkitAudioContext;
    if (NativeAudioContext) {
        window.AudioContext = class extends NativeAudioContext {
            constructor(options) {
                super(options);
                audioContexts.push(this);
            }
        };
    }

    function resumeAudio() {
        for (const ac of audioContexts) {
            if (ac.state !== 'running') ac.resume().catch(() => { });
        }
    }

    function suspendAudio() {
        for (const ac of audioContexts) {
            if (ac.state === 'running') ac.suspend().catch(() => { });
        }
    }

    // Back button / back gesture: one extra history entry is kept on top while playing, so "back" reaches the
    // game (pause, previous screen) instead of leaving the site. The game itself decides when to really leave.
    let historyArmed = !!(history.state && history.state.snakevs); // a reload keeps the entry pushed earlier
    let rearmHistory = false;
    let backRequested = false;
    let backFromHistoryThisFrame = false;

    function armHistory() {
        if (historyArmed) return;
        history.pushState({ snakevs: true }, '');
        historyArmed = true;
    }

    window.addEventListener('popstate', () => {
        if (!historyArmed) return;
        historyArmed = false;
        rearmHistory = true;
        // With the name dialog open, "back" only closes the dialog.
        if (closeNameDialog) closeNameDialog(null);
        else backRequested = true;
    });

    // Name dialog for the online ranking (HTML, so phones get their native keyboard). Only one at a time.
    // Must accept the same characters as Leaderboard.NormalizeName in SnakeVs.Core.
    const NAME_REJECT = /[^A-Z0-9ÁÀÂÃÉÊÍÓÔÕÚÜÇÑ .\-_]/g;
    let closeNameDialog = null;

    function askName(initial, title, points, label, saveText, cancelText, maxLength) {
        if (closeNameDialog) closeNameDialog(null);
        const dialog = document.getElementById('name-dialog');
        const input = document.getElementById('name-input');
        const save = document.getElementById('name-save');
        const cancel = document.getElementById('name-cancel');
        document.getElementById('name-title').textContent = title;
        document.getElementById('name-points').textContent = points;
        document.getElementById('name-label').textContent = label;
        save.textContent = saveText;
        cancel.textContent = cancelText;
        input.maxLength = maxLength;
        input.value = (initial || '').toUpperCase().replace(NAME_REJECT, '').slice(0, maxLength);

        const refresh = () => {
            const clean = input.value.toUpperCase().replace(NAME_REJECT, '').replace(/^ +/, '').replace(/ {2,}/g, ' ');
            if (clean !== input.value) input.value = clean;
            save.disabled = clean.trim().length === 0;
        };

        return new Promise((resolve) => {
            const onSubmit = (e) => {
                e.preventDefault();
                if (!save.disabled) closeNameDialog(input.value.trim());
                else input.focus();
            };
            const onCancel = () => closeNameDialog(null);
            const onKey = (e) => {
                e.stopPropagation(); // typing must not reach the game
                if (e.key === 'Escape') closeNameDialog(null);
            };

            closeNameDialog = (value) => {
                closeNameDialog = null;
                dialog.hidden = true;
                dialog.removeEventListener('submit', onSubmit);
                cancel.removeEventListener('click', onCancel);
                input.removeEventListener('input', refresh);
                dialog.removeEventListener('keydown', onKey);
                dialog.removeEventListener('keyup', onKey);
                input.blur();
                canvas.focus();
                resolve(value);
            };

            dialog.addEventListener('submit', onSubmit);
            cancel.addEventListener('click', onCancel);
            input.addEventListener('input', refresh);
            dialog.addEventListener('keydown', onKey);
            dialog.addEventListener('keyup', onKey);
            refresh();
            dialog.hidden = false;
            input.focus();
            input.select();
        });
    }

    function onUserGesture() {
        resumeAudio();
        armHistory();
    }

    let canvas = null;
    let safeProbe = null;
    let insets = [0, 0, 0, 0];

    // Every pointer (mouse, touch, pen) is queued as (phase, id, x, y) for the next frame: phase 0 down, 1 move,
    // 2 up (see PointerPhase). Polling once per frame would lose a quick tap that starts and ends between frames.
    const pointerQueue = [];
    const pointersDown = new Set();

    function queuePointer(phase, e) {
        pointerQueue.push(phase, e.pointerId, Math.round(e.clientX), Math.round(e.clientY));
    }

    function onPointerDown(e) {
        if (e.pointerType === 'mouse' && e.button !== 0) return;
        pointersDown.add(e.pointerId);
        try {
            canvas.setPointerCapture(e.pointerId);
        } catch {
            // Capture is a nicety (keeps tracking outside the canvas).
        }

        queuePointer(0, e);
    }

    function onPointerMove(e) {
        if (pointersDown.has(e.pointerId)) queuePointer(1, e);
    }

    function onPointerUp(e) {
        if (!pointersDown.delete(e.pointerId)) return;
        queuePointer(2, e);
    }

    function pixelRatio() {
        return Math.min(window.devicePixelRatio || 1, MAX_PIXEL_RATIO);
    }

    // Draw at the device resolution so text and shapes stay sharp on high density screens.
    // The engine resets the canvas to CSS size on resize; this runs before every frame and wins.
    function fitCanvas() {
        const ratio = pixelRatio();
        const w = Math.max(1, Math.round(window.innerWidth * ratio));
        const h = Math.max(1, Math.round(window.innerHeight * ratio));
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
            measureInsets();
        }
    }

    // Notch / rounded corner insets (CSS env(safe-area-inset-*)) in back buffer pixels.
    function measureInsets() {
        const s = getComputedStyle(safeProbe);
        const r = canvas.width / Math.max(1, window.innerWidth);
        insets = [s.paddingLeft, s.paddingTop, s.paddingRight, s.paddingBottom]
            .map((v) => Math.round((parseFloat(v) || 0) * r));
    }

    let game = null;

    function frame() {
        fitCanvas();
        game.invokeMethod('Tick');
        if (rearmHistory) {
            rearmHistory = false;
            armHistory();
        }

        window.requestAnimationFrame(frame);
    }

    function suspend() {
        suspendAudio();
        if (game) game.invokeMethod('Suspend');
    }

    window.snakevs = {
        start(dotnetRef) {
            game = dotnetRef;
            canvas = document.getElementById('theCanvas');

            safeProbe = document.createElement('div');
            safeProbe.className = 'safe-probe';
            document.body.appendChild(safeProbe);

            canvas.addEventListener('pointerdown', onPointerDown);
            canvas.addEventListener('pointermove', onPointerMove);
            canvas.addEventListener('pointerup', onPointerUp);
            canvas.addEventListener('pointercancel', onPointerUp);

            // Touches are handled above as pointer events. Stop the raw touch events at the canvas: no page
            // scrolling/zooming, no emulated mouse events, and the engine's own touch panel never sees them.
            const block = (e) => e.preventDefault();
            const swallow = (e) => {
                e.preventDefault();
                e.stopPropagation();
            };
            for (const type of ['touchstart', 'touchmove', 'touchend', 'touchcancel']) {
                canvas.addEventListener(type, swallow, { passive: false });
                // Taps on the name dialog stay there too (the engine listens on window).
                document.getElementById('name-dialog').addEventListener(type, (e) => e.stopPropagation());
            }

            canvas.addEventListener('contextmenu', block);
            window.addEventListener('keydown', (e) => {
                // Arrows and space must steer the snake, not scroll the page.
                if ([' ', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.key)) e.preventDefault();
            });
            window.addEventListener('wheel', block, { passive: false });

            for (const type of ['pointerdown', 'keydown', 'touchend']) {
                window.addEventListener(type, onUserGesture, { capture: true });
            }

            document.addEventListener('visibilitychange', () => {
                if (document.visibilityState === 'hidden') suspend();
                else resumeAudio();
            });
            window.addEventListener('pagehide', suspend);

            fitCanvas();
            canvas.focus();
            window.requestAnimationFrame(() => {
                frame();
                document.getElementById('loading')?.remove();
            });
        },

        // Everything the game reads every frame, in one call:
        // [canvas width, canvas height, pixel ratio x1000, safe inset left, top, right, bottom, back pressed,
        //  then the queued pointer events as (phase, id, x, y)].
        frameState() {
            const ratio = canvas.width / Math.max(1, window.innerWidth);
            const back = backRequested;
            backRequested = false;
            backFromHistoryThisFrame = back;
            const state = [canvas.width, canvas.height, Math.round(ratio * 1000), ...insets, back ? 1 : 0, ...pointerQueue];
            pointerQueue.length = 0;
            return state;
        },

        // Ranking API base URL: meta "snakevs-api" (empty = this site, "off" = no online ranking).
        apiBase() {
            const meta = document.querySelector('meta[name="snakevs-api"]');
            let value = meta ? meta.content.trim() : '';
            // Local development only: http://localhost:5080/?api=http://localhost:5090/
            if (['localhost', '127.0.0.1'].includes(location.hostname)) {
                value = new URLSearchParams(location.search).get('api') || value;
            }
            if (value.toLowerCase() === 'off') return null;
            const url = new URL(value || '.', document.baseURI).href;
            return url.endsWith('/') ? url : url + '/';
        },

        askName,

        // Local development only: http://localhost:5080/?stage=7&auto=1 opens the journey at that stage
        // (auto: the CPU plays). Answers [stage, auto], zeros when not asked for.
        demo() {
            if (!['localhost', '127.0.0.1'].includes(location.hostname)) return [0, 0];
            const query = new URLSearchParams(location.search);
            const stage = parseInt(query.get('stage') || '0', 10);
            return [stage > 0 ? stage : 0, query.get('auto') ? 1 : 0];
        },

        language() {
            return (navigator.languages && navigator.languages[0]) || navigator.language || 'en';
        },

        exit() {
            // Only a browser back press leaves the page (Esc on the menu does nothing). Its trap entry is already
            // gone, so going back once more leaves the site; nothing happens when the game is the tab's first page.
            if (!backFromHistoryThisFrame) return;
            rearmHistory = false;
            history.back();
        },

        vibrate(ms) {
            try {
                if (navigator.vibrate) navigator.vibrate(ms);
            } catch {
                // Not supported or blocked: haptics are optional.
            }
        },

        storageRead() {
            try {
                return window.localStorage.getItem(SAVE_KEY);
            } catch {
                return null;
            }
        },

        storageWrite(text) {
            try {
                window.localStorage.setItem(SAVE_KEY, text);
            } catch {
                // Private mode or quota exceeded.
            }
        },
    };
})();
