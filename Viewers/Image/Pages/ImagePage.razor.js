// ImagePage.razor.js — 图片查看器 JS 行为（隔离模块）
// v2: Spring 物理引擎 + 手势跟踪 + 渐进加载 + Filmstrip

// ── Spring Physics Animation Engine ──

const _springs = new Map();
let _springId = 0;

class SpringRunner {
  constructor() {
    this.active = false;
    this.rafId = null;
  }

  start(from, to, initialVelocity, config, onUpdate, onComplete) {
    this.stop();

    const { stiffness = 250, damping = 25, mass = 1, precision = 0.5 } = config;
    let x = from;
    let v = initialVelocity || 0;
    let settledFrames = 0;
    const dt = 1 / 60;

    const tick = () => {
      const force = -stiffness * (x - to);
      const dampedForce = force - damping * v;
      const accel = dampedForce / mass;

      v += accel * dt;
      x += v * dt;

      onUpdate(x);

      if (Math.abs(x - to) < precision && Math.abs(v) < precision) {
        settledFrames++;
        if (settledFrames >= 3) {
          x = to;
          onUpdate(x);
          this.stop();
          if (onComplete) onComplete();
          return;
        }
      } else {
        settledFrames = 0;
      }

      this.rafId = requestAnimationFrame(tick);
    };

    this.active = true;
    this.rafId = requestAnimationFrame(tick);
  }

  stop() {
    if (this.rafId) {
      cancelAnimationFrame(this.rafId);
      this.rafId = null;
    }
    this.active = false;
  }
}

export function springStart(elementSelector, from, to, velocity, config) {
  return new Promise(resolve => {
    const id = ++_springId;
    const runner = new SpringRunner();
    _springs.set(id, runner);

    runner.start(from, to, velocity, config,
      (x) => {
        const el = document.querySelector(elementSelector);
        if (el) el.style.transform = `translateX(${x}px)`;
      },
      () => {
        _springs.delete(id);
        resolve(id);
      }
    );
  });
}

export function springCancel(elementSelector) {
  for (const [id, r] of _springs) {
    r.stop();
    _springs.delete(id);
  }
  const el = document.querySelector(elementSelector);
  if (el) el.style.removeProperty('transform');
}

// ── Gesture Tracker ──

let _gestureActive = false;
let _gestureStartX = 0;
let _gestureStartY = 0;
let _gestureLastX = 0;
let _gestureLastY = 0;
let _gestureLastTime = 0;
let _gestureVelocitySamples = [];
let _gestureEl = null;
let _gestureDotNetRef = null;
let _gestureSwipeThreshold = 0; // min px for swipe detect

// Drag peek: preview of adjacent images visible while dragging
let _peekPrev = null;   // <img> for the previous image (left side)
let _peekNext = null;   // <img> for the next image (right side)
let _peekViewport = null; // container element
let _pendingFilmstripReveal = false; // filmstrip click: hide real wrap, reveal pre-scaled at slide finish

function _gestureReset() {
  _gestureActive = false;
  _gestureStartX = 0;
  _gestureStartY = 0;
  _gestureLastX = 0;
  _gestureLastY = 0;
  _gestureLastTime = 0;
  _gestureVelocitySamples = [];
  if (_gestureEl) _gestureEl.classList.remove('tracking');
}

function _gestureGetVelocity() {
  if (_gestureVelocitySamples.length === 0) return 0;
  let sum = 0;
  for (const s of _gestureVelocitySamples) sum += s;
  return sum / _gestureVelocitySamples.length;
}

function _gestureCleanupPeek() {
  if (_peekViewport && _peekViewport.parentNode) {
    _peekViewport.parentNode.removeChild(_peekViewport);
  }
  _peekPrev = null;
  _peekNext = null;
  _peekViewport = null;
}

function _gestureCreatePeek() {
  _gestureCleanupPeek();

  const slide = document.querySelector('.img-slide');
  if (!slide) return;

  // Fetch adjacent URIs + zoom from C#.
  // NOTE: GetPeekZoom is called AFTER GetPeekUri (chained via .then) because
  // GetPeekUri decodes the image into DecodeCache — GetPeekZoom reads the
  // cached dimensions for the zoom calculation. Parallel calls would race:
  // GetPeekZoom would see no cache entry and return the default zoom (1.0),
  // making both prev and next peeks use the same wrong scale.
  function getAdj(dir) {
    if (!_gestureDotNetRef) return Promise.resolve({ uri: null, zoom: 1 });
    return _gestureDotNetRef.invokeMethodAsync('GetPeekUri', dir)
      .catch(() => null)
      .then(function (uri) {
        if (!uri) return { uri: null, zoom: 1 };
        return _gestureDotNetRef.invokeMethodAsync('GetPeekZoom', dir)
          .catch(function () { return 1; })
          .then(function (zoom) { return { uri: uri, zoom: zoom }; });
      });
  }

  const prevPromise = getAdj(-1);
  const nextPromise = getAdj(1);

  Promise.all([prevPromise, nextPromise]).then(([prev, next]) => {
    if (!prev.uri && !next.uri) return;

    const wrap = slide.querySelector('.img-wrap');
    if (!wrap) return;

    const container = document.createElement('div');
    container.style.cssText = 'position:absolute;top:0;right:0;bottom:0;left:0;' +
      'pointer-events:none;display:flex;align-items:center;justify-content:center;';

    function makePeek(uri, zoom, leftValue) {
      if (!uri) return null;
      // Match the real image rendering: intrinsic-size <img> wrapped in a
      // scale(zoom) inner, not object-fit:contain at viewport size —
      // otherwise a 1:1 image would appear full-viewport during peek and
      // snap to actual size on transition completion (= "缩放抖动").
      // leftValue: '-100%' for prev (left side), '100%' for next (right side).
      var card = document.createElement('div');
      card.style.cssText = 'position:absolute;top:0;bottom:0;left:' + leftValue + ';width:100%;' +
        'display:flex;align-items:center;justify-content:center;';
      var inner = document.createElement('div');
      inner.style.cssText = 'display:flex;align-items:center;justify-content:center;';
      inner.style.transform = 'scale(' + zoom + ')';
      var img = document.createElement('img');
      img.src = uri;
      img.draggable = false;
      img.style.cssText = 'max-width:none;max-height:none;border-radius:2px;';
      inner.appendChild(img);
      card.appendChild(inner);
      container.appendChild(card);
      return card;
    }

    if (prev.uri) _peekPrev = makePeek(prev.uri, prev.zoom, '-100%');
    if (next.uri) _peekNext = makePeek(next.uri, next.zoom, '100%');

    // Insert peek BEFORE .img-wrap so it sits behind it in stacking order
    slide.insertBefore(container, wrap);
    _peekViewport = container;
  });
}

export function initGestureTracker(dotNetRef, elementSelector, swipeThreshold) {
  _gestureDotNetRef = dotNetRef;
  _gestureSwipeThreshold = swipeThreshold || 80;

  const el = document.querySelector(elementSelector);
  if (!el) return;
  _gestureEl = el;

  // Multi-touch bookkeeping for pinch-zoom. Closed over so it persists across
  // the pointerdown/move/up/cancel listeners below.
  const pointers = new Map();   // pointerId -> {x, y}
  let pinchActive = false;
  let pinchStartDist = 0;

  function distance(a, b) {
    const dx = a.x - b.x, dy = a.y - b.y;
    return Math.sqrt(dx * dx + dy * dy);
  }
  function midpoint(a, b) {
    return { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
  }

  el.addEventListener('pointerdown', (e) => {
    pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

    // ── Enter pinch mode when a second finger lands ──
    if (pointers.size >= 2) {
      const pts = [...pointers.values()];
      pinchActive = true;
      _gestureActive = false;          // pause any in-progress swipe/drag
      el.classList.remove('tracking');
      clearSlideTransform();           // undo a fit-mode swipe translate
      _gestureCleanupPeek();
      pinchStartDist = distance(pts[0], pts[1]);
      const mid = midpoint(pts[0], pts[1]);
      if (_gestureDotNetRef) {
        _gestureDotNetRef.invokeMethodAsync('BeginPinch', mid.x, mid.y);
      }
      return;
    }

    if (_gestureActive) return;
    // Let nav buttons handle their own clicks — don't capture
    if (e.target.closest('.nav-btn')) return;
    _gestureActive = true;
    _gestureStartX = e.clientX;
    _gestureStartY = e.clientY;
    _gestureLastX = e.clientX;
    _gestureLastY = e.clientY;
    _gestureLastTime = performance.now();
    _gestureVelocitySamples = [];
    el.setPointerCapture(e.pointerId);
    el.classList.add('tracking');

    // Create peek previews of adjacent images in ALL modes (not just fit).
    // For 1:1 images that fit the viewport, the peek shows through the slide
    // just like fit mode — no guide line, no damping.
    _gestureCreatePeek();
  });

  el.addEventListener('pointermove', (e) => {
    if (pointers.has(e.pointerId)) pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });

    // ── Pinch: scale by the ratio of current / start two-finger distance ──
    if (pinchActive && pointers.size >= 2) {
      const pts = [...pointers.values()];
      const dist = distance(pts[0], pts[1]);
      const mid = midpoint(pts[0], pts[1]);
      if (pinchStartDist > 0 && _gestureDotNetRef) {
        _gestureDotNetRef.invokeMethodAsync('OnPinchMove', dist / pinchStartDist, mid.x, mid.y);
      }
      return;
    }

    if (!_gestureActive) return;
    const now = performance.now();
    const dt = now - _gestureLastTime;
    const dx = e.clientX - _gestureLastX;
    _gestureLastX = e.clientX;
    _gestureLastY = e.clientY;
    _gestureLastTime = now;

    if (dt > 0) {
      _gestureVelocitySamples.push(dx / dt);
      if (_gestureVelocitySamples.length > 5) _gestureVelocitySamples.shift();
    }

    const offsetX = e.clientX - _gestureStartX;

    // Fit mode: slide .img-slide to preview navigation with peek visible.
    // Free mode: C# OnPointerMove handles the transform (damped overscroll
    // or full slide depending on whether the image can actually be panned),
    // so JS does NOT set it here — avoids jitter from competing transforms.
    var isFitMode = el.classList.contains('fit');
    if (isFitMode) {
      const imgSlide = document.querySelector('.img-slide');
      if (imgSlide) {
        imgSlide.style.transform = `translateX(${offsetX}px)`;
        imgSlide.style.transition = 'none';
      }
    }

    // Notify Blazor of drag progress
    if (_gestureDotNetRef) {
      _gestureDotNetRef.invokeMethodAsync('OnGestureDrag', offsetX);
    }
  });

  function endPointer(e) {
    pointers.delete(e.pointerId);
    try { el.releasePointerCapture(e.pointerId); } catch (_) {}

    // ── Still pinching: only end when fewer than 2 fingers remain ──
    if (pinchActive) {
      if (pointers.size < 2) {
        pinchActive = false;
        // Leave a lone remaining finger idle (no swipe) so we don't accidentally
        // navigate after a zoom. Single-finger pan resumes on the next press.
        if (_gestureDotNetRef) _gestureDotNetRef.invokeMethodAsync('EndPinch');
      }
      return;
    }

    if (!_gestureActive) return;
    const offsetX = e.clientX - _gestureStartX;
    const velocity = _gestureGetVelocity();
    _gestureReset();

    if (_gestureDotNetRef) {
      _gestureDotNetRef.invokeMethodAsync('OnGestureRelease', offsetX, velocity);
    }
  }

  el.addEventListener('pointerup', endPointer);
  el.addEventListener('pointercancel', endPointer);
}

export function disposeGestureTracker() {
  _gestureReset();
  _gestureCleanupPeek();
  _gestureDotNetRef = null;
  _gestureEl = null;
}

// ── Window Resize Handler ──

let _resizeDotNetRef = null;
var _resizeObserver = null;  // var for Android 10 compatibility

export function initResizeHandler(dotNetRef) {
  _resizeDotNetRef = dotNetRef;
  var vp = document.querySelector('.image-viewport');
  if (!vp) return;
  // ResizeObserver fires synchronously on observe() with the initial size.
  // Skip that first callback — the page's own OnAfterRenderAsync handles
  // the initial filmstrip positioning after measuring the viewport.
  try {
    var first = true;
    var timer = null;
    _resizeObserver = new ResizeObserver(function () {
      if (first) { first = false; return; }
      // Debounce: ResizeObserver fires once per animation frame while the user
      // is dragging the window edge, which would otherwise flood OnWindowResize
      // and spawn overlapping filmstrip re-window + scroll passes (occasional
      // thumbnail churn on shrink). Fire only after the size has settled.
      if (timer) clearTimeout(timer);
      timer = setTimeout(function () {
        if (_resizeDotNetRef) {
          _resizeDotNetRef.invokeMethodAsync('OnWindowResize');
        }
      }, 120);
    });
    _resizeObserver.observe(vp);
  } catch (_) { /* ResizeObserver not available — skip */ }
}

export function disposeResizeHandler() {
  _resizeDotNetRef = null;
  if (_resizeObserver) {
    try { _resizeObserver.disconnect(); } catch (_) {}
    _resizeObserver = null;
  }
}

// ── Frame wait (deferred paint gate) ──
// Returns a Promise that resolves after the next rAF + paint, giving the
// browser a frame to composite the final state before C# modifies more DOM.
export function waitFrame() {
  return new Promise(resolve => requestAnimationFrame(resolve));
}

// ── Image Slide Transform ──

export function setSlideTransform(transform) {
  const el = document.querySelector('.img-slide');
  if (el) { el.style.transform = transform; el.style.transition = 'none'; }
}

// Applied during pointer-move when zoomed in and the pan has reached its
// horizontal boundary. Shows a damped elastic stretch (overscroll preview)
// on .img-slide. px=0 animates it smoothly back to neutral.
export function setSlideOverscroll(px) {
  const el = document.querySelector('.img-slide');
  if (!el) return;
  px = Math.round(px);
  if (px === 0) {
    if (el.style.transform === '' || el.style.transform === 'none') return;
    // Smooth snap-back: animate transform back to 0 with a CSS transition
    // then clean up inline styles once the animation completes.
    el.style.transition = 'transform 200ms cubic-bezier(0.4, 0, 0.2, 1)';
    el.style.transform = 'translateX(0px)';
    setTimeout(function () {
      if (el) {
        el.style.removeProperty('transform');
        el.style.removeProperty('transition');
      }
    }, 220);
  } else {
    el.style.transform = 'translateX(' + px + 'px)';
    el.style.transition = 'none';
  }
}

export function clearSlideTransform() {
  const el = document.querySelector('.img-slide');
  if (el) { el.style.removeProperty('transform'); el.style.removeProperty('transition'); }
}

export function setSlideTransition(duration) {
  const el = document.querySelector('.img-slide');
  if (el) el.style.transition = `transform ${duration}ms cubic-bezier(0.4, 0, 0.2, 1)`;
}

// ── Overscroll Guide — vertical line, damped position ──

var _guideEl = null;

export function showOverscrollGuide(overscroll, threshold) {
  var vp = document.querySelector('.image-viewport');
  if (!vp) return;
  var r = vp.getBoundingClientRect();
  var vpL = r.left;
  var vpR = r.right;

  if (!_guideEl) {
    _guideEl = document.createElement('div');
    _guideEl.style.cssText = 'position:fixed;top:0;left:0;right:0;height:0;pointer-events:none;z-index:9999';
    document.body.appendChild(_guideEl);

    var ln = document.createElement('div');
    ln.style.cssText = 'position:fixed;top:0;bottom:0;width:2px;pointer-events:none;transition:background .15s';
    _guideEl.appendChild(ln);

    var lb = document.createElement('span');
    lb.style.cssText = 'position:fixed;font:10px/1 monospace;' +
      'color:#fff;background:rgba(0,0,0,.7);padding:2px 4px;border-radius:2px;white-space:nowrap;pointer-events:none;';
    _guideEl.appendChild(lb);
  }

  var absOx = Math.abs(overscroll);
  var isRight = overscroll < 0; // drag LEFT -> line on RIGHT side
  threshold = threshold || 80;
  var damp = 0.3;
  var dampedThresh = Math.round(threshold * damp);

  var line = _guideEl.children[0];
  var label = _guideEl.children[1];
  var topPx = r.top;
  var botPx = r.bottom;

  line.style.top = topPx + 'px';
  line.style.bottom = 'auto';
  line.style.height = (botPx - topPx) + 'px';
  label.style.top = (topPx + 8) + 'px';

  if (isRight) {
    // Drag LEFT -> line on the RIGHT side
    line.style.left = (vpR - dampedThresh) + 'px';
    label.style.left = (vpR - dampedThresh + 4) + 'px';
  } else {
    // Drag RIGHT -> line on the LEFT side
    line.style.left = (vpL + dampedThresh) + 'px';
    label.style.left = (vpL + dampedThresh + 4) + 'px';
  }

  label.textContent = absOx >= threshold
    ? '✓ ' + Math.round(threshold) + 'px'
    : Math.round(absOx) + ' / ' + Math.round(threshold) + 'px';
  line.style.background = absOx >= threshold ? '#4CAF50' : '#ff9800';
  line.style.boxShadow = absOx >= threshold ? '0 0 6px rgba(76,175,80,.6)' : 'none';

  _guideEl.style.opacity = absOx > 0 ? '1' : '0';
}

export function hideOverscrollGuide() {
  if (_guideEl) {
    if (_guideEl.parentNode) _guideEl.parentNode.removeChild(_guideEl);
    _guideEl = null;
  }
}

// ── 3D Cylinder Transition (v3 — optimized for 60fps) ──
// We animate CLONES layered above the real .img-wrap. When the animation ends
// we DO touch the real wrap, but ONLY to pin it to EXACTLY what Blazor will
// render next: `scale(targetScale)` where targetScale == displayZoom (the same
// value the toolbar shows). Blazor's diff then sees no change, so the rendered
// zoom stays identical to displayZoom — no desync, no 200% flash. (.img-wrap
// always carries scale(displayZoom) now; fit mode no longer uses a bare CSS
// transform, which is what used to let the rendered zoom and toolbar diverge.)
//
// v3 optimizations:
//   A) Pre-decode target image via Image.decode() before any DOM work, so the
//      browser has the decoded bitmap ready when backImg.src is set later —
//      eliminates the 20-50ms main-thread decode that caused frame drops on
//      large JPEGs.
//   B) translateZ(0) on front/back clones guarantees GPU compositor layers,
//      avoiding per-frame layer creation/disposal.
//   C) will-change:transform on slide (already in CSS) + contain:paint on
//      .cyl-animating isolates compositing to just the slide subtree.
//   D) Batch all style changes before the rAF, minimizing forced layout.

export function cylinderTransition(imageUrl, direction, targetScale = 1, targetFit = true, panX = 0, panY = 0, outgoingScale = 1) {
  return new Promise((resolve) => {
    const slide = document.querySelector('.img-slide');
    const wrap = slide && slide.querySelector('.img-wrap');
    const img = slide && slide.querySelector('.img-display');
    if (!slide || !wrap || !img) { resolve(); return; }

    const isNext = direction === 'next';
    const duration = 480;
    const outAngle = isNext ? -90 : 90;
    const inAngle = isNext ? 90 : -90;

    // IMPORTANT: hide the real wrap IMMEDIATELY, before any Blazor render
    // flush can update it with ApplyZoomFor's new displayZoom.
    slide.style.perspective = '2800px';
    slide.style.perspectiveOrigin = 'center center';
    slide.classList.add('cyl-animating');

    // ── Front card: shown immediately, starts rotating right away ──
    // Stays at outgoingScale throughout so mid-flip projections match the
    // back card's (outgoingScale×cosθ×w = targetScale×cosθ×w = viewport×cosθ).
    const front = wrap.cloneNode(true);
    front.classList.add('cyl-clone');
    front.style.position = 'absolute';
    front.style.top = '0';
    front.style.right = '0';
    front.style.bottom = '0';
    front.style.left = '0';
    front.style.margin = '0';
    front.style.width = '100%';
    front.style.height = '100%';
    front.style.transformStyle = 'preserve-3d';
    front.style.backfaceVisibility = 'hidden';
    front.style.willChange = 'transform';
    front.style.transform = `translate(${panX}px,${panY}px) scale(${outgoingScale}) rotateY(0deg) translateZ(0)`;
    front.style.transition = `transform ${duration}ms ease-in-out`;
    slide.appendChild(front);
    // Force layout
    void front.offsetHeight;

    // Record when front starts rotating (for back card timing compensation)
    const frontStartTime = performance.now();

    // Start front rotation immediately
    requestAnimationFrame(() => {
      front.style.transform = `translate(0px,0px) scale(${outgoingScale}) rotateY(${outAngle}deg) translateZ(0)`;
    });

    // ── Back card: preload target image off-screen, then show ──
    // Front rotates out immediately; the back card only appears after the
    // image is decoded. Its animation duration is dynamically shortened so
    // both cards finish at the same time (t = 480ms), avoiding a gap where
    // one card completes before the other.
    const back = document.createElement('div');
    back.style.cssText = 'position:absolute;top:0;right:0;bottom:0;left:0;display:flex;' +
      'align-items:center;justify-content:center;backface-visibility:hidden;' +
      'transform-style:preserve-3d;';
    back.style.willChange = 'transform';

    const backInner = document.createElement('div');
    backInner.style.cssText = 'display:flex;align-items:center;' +
      'justify-content:center;transform-style:preserve-3d;';
    backInner.style.transform = `scale(${targetScale})`;

    const backImg = document.createElement('img');
    backImg.draggable = false;
    backImg.style.cssText = 'max-width:none;max-height:none;border-radius:2px;';
    backInner.appendChild(backImg);
    back.appendChild(backInner);

    function appendBack() {
      if (back.parentNode) return; // already appended
      const elapsed = performance.now() - frontStartTime;
      const backDuration = Math.max(200, duration - elapsed);
      back.style.transform = `rotateY(${inAngle}deg) translateZ(0)`;
      back.style.transition = `transform ${backDuration}ms ease-in-out`;
      slide.appendChild(back);
      void back.offsetHeight;
      requestAnimationFrame(() => {
        back.style.transform = 'rotateY(0deg) translateZ(0)';
      });
      // Also listen for back's transitionend to call finish
      back.addEventListener('transitionend', finish);
    }

    // Preload target image off-screen; when decoded, show back card.
    // The browser fetches + decodes the image in the background without
    // blocking the (already-running) front card flip animation.
    backImg.src = imageUrl;
    const preloader = new Image();
    preloader.onload = appendBack;
    preloader.onerror = appendBack;
    preloader.src = imageUrl;
    // Fallback: show back card at most 200ms after front starts, even if
    // the image hasn't fully loaded yet (avoids a completely empty flip).
    const fallbackTimer = setTimeout(appendBack, 200);

    // ── Completion: wait for front + back transitionend ──

    let backEndCount = 0;
    let done = false;
    const finish = () => {
      if (done) return;
      // Front's transition always fires at 480ms. Back's fires at its
      // dynamic backDuration. Wait for BOTH before cleaning up.
      if (back.parentNode) {
        backEndCount++;
        if (backEndCount < 2) return;   // need front + back
      }
      done = true;
      clearTimeout(fallbackTimer);
      front.removeEventListener('transitionend', finish);
      back.removeEventListener('transitionend', finish);

      img.src = imageUrl;
      wrap.style.transform = `translate(0px,0px) scale(${targetScale})`;

      if (front.parentNode) front.parentNode.removeChild(front);
      if (back.parentNode) back.parentNode.removeChild(back);
      slide.classList.remove('cyl-animating');
      slide.style.perspective = '';
      slide.style.perspectiveOrigin = '';

      resolve();
    };
    front.addEventListener('transitionend', finish);
    // back transitionend listener added in appendBack()
    setTimeout(finish, duration + 150);
  });
}

// ── Slide Transition (gesture swipe) ──
// The pre-positioned peek image slides into view as .img-slide translates
// to full viewport width. No clone, no fade — the peek is already in the
// DOM at left:-100%/100%, and the viewport's overflow:hidden clips it
// naturally as the slide moves.

export function slideTransition(imageUrl, direction, viewportWidth, targetScale = 1) {
  return new Promise(resolve => {
    const slide = document.querySelector('.img-slide');
    const wrap = slide && slide.querySelector('.img-wrap');
    const img = slide && slide.querySelector('.img-display');
    if (!slide || !wrap || !img) { resolve(); return; }

    const duration = 280;
    const endX = direction < 0 ? viewportWidth : -viewportWidth;

    slide.style.transition = `transform ${duration}ms cubic-bezier(0.4, 0, 0.2, 1)`;
    slide.style.transform = `translateX(${endX}px)`;

    let done = false;
    const finish = () => {
      if (done) return;
      done = true;
      slide.removeEventListener('transitionend', finish);

      // Pin the real image and zoom BEFORE resetting the slide position,
      // so when transform snaps back to '' the .img-wrap already shows the
      // target image at the correct zoom — no frame of old image visible.
      img.src = imageUrl;
      wrap.style.transform = `translate(0px,0px) scale(${targetScale})`;
      if (_pendingFilmstripReveal) {
        // Reveal the real wrap (already pre-scaled to targetScale, hidden during
        // the slide) and drop the frozen clone + peek in the SAME frame so there
        // is no post-reset blank gap. This is what makes a far filmstrip click
        // land without the zoom snap the old single-slide path showed.
        wrap.style.visibility = '';
        _pendingFilmstripReveal = false;
        if (_peekViewport && _peekViewport.parentNode) {
          _peekViewport.parentNode.removeChild(_peekViewport);
          _peekViewport = null;
        }
      }
      slide.style.transform = '';
      slide.style.transition = '';
      slide.classList.remove('tracking');

      resolve();
    };
    slide.addEventListener('transitionend', finish);
    setTimeout(finish, duration + 100);
  });
}

export function cleanupGesturePeek() {
  _gestureCleanupPeek();
}

// ── Single Peek (filmstrip click → slide) ──
// Builds ONE preview card of `uri` at `scale`, parked one viewport away in the
// slide direction (left:-100% for prev, right:100% for next) — exactly like the
// gesture peek. slideTransition() then translates .img-slide by one viewport so
// this preview slides into view, giving a filmstrip click the SAME single-slide
// animation as a swipe gesture. Removed by cleanupGesturePeek().
//
// Returns a Promise that resolves only AFTER the peek <img> bitmap is decoded,
// so the caller (OnFilmstripClick) can await it before starting slideTransition
// and the image is paint-ready on the FIRST animation frame. This mirrors the
// time a gesture peek gets during the drag (built at pointer-down, decoded long
// before release): without it the large data:URI is still decoding during the
// 280ms slide and "pops" into view mid-slide already-scaled — which reads as
// zoom jitter. The decode is done on THIS element (not a separate preloader) so
// there is no dependency on the browser's per-URI decoded-bitmap cache.
export function createSinglePeek(uri, scale, direction) {
  _gestureCleanupPeek();
  const slide = document.querySelector('.img-slide');
  if (!slide) return Promise.resolve();
  const wrap = slide.querySelector('.img-wrap');
  if (!wrap || !uri) return Promise.resolve();

  const isNext = direction > 0;
  const container = document.createElement('div');
  container.style.cssText = 'position:absolute;top:0;right:0;bottom:0;left:0;' +
    'pointer-events:none;display:flex;align-items:center;justify-content:center;';

  const card = document.createElement('div');
  card.style.cssText = 'position:absolute;top:0;bottom:0;' +
    (isNext ? 'left:100%' : 'left:-100%') +
    ';width:100%;display:flex;align-items:center;justify-content:center;';

  const inner = document.createElement('div');
  inner.style.cssText = 'display:flex;align-items:center;justify-content:center;';
  inner.style.transform = 'scale(' + scale + ')';

  const img = document.createElement('img');
  img.draggable = false;
  img.style.cssText = 'max-width:none;max-height:none;border-radius:2px;';

  inner.appendChild(img);
  card.appendChild(inner);
  container.appendChild(card);

  // Freeze the CURRENT image as a plain clone that slides out at its OWN zoom
  // (displayZoom), and HIDE the real .img-wrap pre-scaled to targetScale so the
  // end-of-slide zoom swap is invisible. This mirrors the old multi-slide
  // reveal: there the real wrap was hidden + pre-scaled and revealed at the
  // finish frame, so a far filmstrip click (current zoom far from target zoom)
  // never shows the real wrap snapping displayZoom -> targetScale. Without it
  // the visible real wrap snaps scale at the finish frame = zoom jitter;
  // adjacent gesture swipes hide it only because their zooms are near-equal.
  // NOTE: the frozen clone uses NO .img-wrap / .img-display class so it cannot
  // collide with slideTransition's slide.querySelector('.img-wrap'/.img-display')
  // (which must keep resolving to the REAL wrap/img).
  var frozenImg = wrap.querySelector('.img-display');
  var frozen = document.createElement('div');
  frozen.className = 'filmstrip-frozen-current';
  frozen.style.cssText = 'position:absolute;top:0;left:0;right:0;bottom:0;' +
    'display:flex;align-items:center;justify-content:center;pointer-events:none;' +
    'transform:' + (wrap.style.transform || 'none') + ';';
  var frozenPic = document.createElement('img');
  frozenPic.src = frozenImg ? frozenImg.src : '';
  frozenPic.draggable = false;
  frozenPic.style.cssText = 'max-width:none;max-height:none;border-radius:2px;';
  frozen.appendChild(frozenPic);
  container.appendChild(frozen);

  wrap.style.visibility = 'hidden';
  wrap.style.transform = 'translate(0px,0px) scale(' + scale + ')';

  slide.insertBefore(container, wrap);
  _peekViewport = container;
  _pendingFilmstripReveal = true;

  return new Promise((resolve) => {
    let done = false;
    const finish = () => { if (!done) { done = true; resolve(); } };
    img.onload = finish;
    img.onerror = finish;
    // IMPORTANT: set src BEFORE decode() — decode() rejects on a blank image.
    img.src = uri;
    if (typeof img.decode === 'function') {
      img.decode().then(finish).catch(finish);
    }
    // Safety net: never block the slide longer than this if decode stalls.
    setTimeout(finish, 400);
  });
}



// ── Filmstrip ──

// Scroll the (virtualized) filmstrip so `index` is ALWAYS centered in the
// viewport — including the very FIRST and very LAST thumbnail, which on a plain
// scroll would be pinned to the edges because there is no content beyond them.
//
// How: the C# side (ImageFilmstrip.CenterWindow) adds an equal "centering pad"
// to the lead/trailing SPACERS — i.e. empty scroll space with no thumbnails — so
// the first item can scroll until its center hits the viewport center (scrollLeft
// 0) and the last item until its center hits the center (scrollLeft max). Every
// other item then centers via the same scroll math. Whether 2 or 2000 thumbnails,
// the target ends up dead-center.
//
// IMPORTANT: this function must NEVER set paddingLeft/Right from JS. Setting it
// here (reading clientWidth, writing huge symmetric padding on a scroll container)
// created a scrollbar show/hide + layout feedback loop on desktop window resize,
// which made the virtualized window recompute endlessly and replace thumbnails.
// So we only MEASURE and SCROLL. The centering slack lives in the C# spacers.
//
// Centering uses the element's ACTUAL rendered geometry (data-index +
// getBoundingClientRect + current scrollLeft), so it is correct on desktop and
// mobile regardless of the virtualized lead-spacer width or item size.
export function scrollFilmstripToElement(el, index, smooth = false) {
  const strip = el;
  if (!strip) return;

  const target = strip.querySelector('.filmstrip-item[data-index="' + index + '"]');
  if (!target) {
    console.log('[Filmstrip] index=' + index + ' -> NO DOM element (window not covering it?)');
    return;
  }

  // Centering slack is provided by the C# lead/trailing spacers, NOT by JS
  // padding — so we only measure and scroll here.
  const max = strip.scrollWidth - strip.clientWidth;
  const trackRect = strip.getBoundingClientRect();
  const r = target.getBoundingClientRect();
  const itemW = r.width;
  // Item's left edge within the FULL scrollable content (spacers + items), by
  // adding the current scroll offset back to the on-screen position.
  const itemLeftInContent = (r.left - trackRect.left) + strip.scrollLeft;
  let dest = itemLeftInContent - strip.clientWidth / 2 + itemW / 2;
  dest = Math.max(0, Math.min(dest, max));

  console.log('[Filmstrip] index=' + index +
    ' clientWidth=' + strip.clientWidth +
    ' itemW=' + Math.round(itemW) +
    ' scrollWidth=' + strip.scrollWidth +
    ' max=' + Math.round(max) +
    ' itemLeftInContent=' + Math.round(itemLeftInContent) +
    ' dest=' + Math.round(dest) +
    ' was=' + Math.round(strip.scrollLeft));

  if (smooth) {
    strip.scrollTo({ left: dest, behavior: 'smooth' });
  } else {
    // Assigning scrollLeft is instant in EVERY WebView, unlike
    // scrollTo({behavior:'instant'}) which some embedded WebViews silently
    // ignore and fall back to a smooth (animated) scroll — that animation is
    // exactly what used to spam viewport thumbnail loads. So we set the
    // property directly to guarantee a jump with no intermediate scroll events.
    strip.scrollLeft = dest;
  }
}

// ── Progressive Image Loading (blur-in) ──

export function applyBlurTransition(imgSelector, duration) {
  const img = document.querySelector(imgSelector);
  if (!img) return;
  img.style.filter = 'blur(20px)';
  img.style.transform = 'scale(1.02)';
  img.style.transition = `filter ${duration}ms ease-out, transform ${duration}ms ease-out`;

  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      img.style.filter = 'blur(0px)';
      img.style.transform = 'scale(1)';
    });
  });

  return new Promise(resolve => {
    img.addEventListener('transitionend', () => resolve(), { once: true });
    setTimeout(() => resolve(), duration + 100);
  });
}

// ── Zoom Popup Indicator ──

let _zoomPopupTimer = null;

export function showZoomPopup(percentText) {
  let popup = document.querySelector('.zoom-popup');
  if (!popup) {
    popup = document.createElement('div');
    popup.className = 'zoom-popup';
    var vp = document.querySelector('.viewer-page');
    if (vp) vp.appendChild(popup);
  }
  popup.textContent = percentText;
  popup.classList.add('visible');

  if (_zoomPopupTimer) clearTimeout(_zoomPopupTimer);
  _zoomPopupTimer = setTimeout(() => {
    popup.classList.remove('visible');
  }, 1500);
}

// ── Utility (kept from v1) ──

export function focusViewport() {
  var iv = document.querySelector('.image-viewport');
  if (iv) iv.focus();
}

export function getViewportMetrics() {
  return new Promise(resolve => {
    requestAnimationFrame(() => {
      const v = document.querySelector('.image-viewport');
      if (!v) { resolve([0, 0, 1, 0, 0]); return; }
      const r = v.getBoundingClientRect();
      resolve([v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1, r.left, r.top]);
    });
  });
}

export function getStitchMetrics() {
  const c = document.querySelector('.v-stitch-container');
  if (!c) return [0, 0, 0];
  return [c.scrollTop, c.clientHeight, c.clientWidth];
}

export function setStitchScrollTop(px) {
  const c = document.querySelector('.v-stitch-container');
  if (c) c.scrollTop = px;
}

// Deprecated — kept for reference, replaced by spring
export function waitAnimationEnd() {
  return new Promise(r => {
    const el = document.querySelector('.img-slide');
    if (!el) { r(); return; }
    el.addEventListener('animationend', () => r(), { once: true });
  });
}
