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

  // Fetch adjacent URIs from C# (fire-and-forget — results arrive async)
  const prevPromise = _gestureDotNetRef
    ? _gestureDotNetRef.invokeMethodAsync('GetPeekUri', -1).catch(() => null)
    : Promise.resolve(null);
  const nextPromise = _gestureDotNetRef
    ? _gestureDotNetRef.invokeMethodAsync('GetPeekUri', 1).catch(() => null)
    : Promise.resolve(null);

  Promise.all([prevPromise, nextPromise]).then(([prevUri, nextUri]) => {
    // Peek elements are children of .img-slide, positioned to its left and
    // right. When the slide translates, they move with it. The viewport's
    // overflow:hidden naturally clips them — only the portion that slides
    // into the visible area (between 0 and viewportWidth) shows through.
    // This avoids any z-index or stacking-context trickery.
    if (!prevUri && !nextUri) return;

    const wrap = slide.querySelector('.img-wrap');
    if (!wrap) return;

    const container = document.createElement('div');
    container.style.cssText = 'position:absolute;top:0;right:0;bottom:0;left:0;' +
      'pointer-events:none;display:flex;align-items:center;justify-content:center;';

    if (prevUri) {
      const p = document.createElement('div');
      p.style.cssText = 'position:absolute;top:0;bottom:0;left:-100%;width:100%;' +
        'display:flex;align-items:center;justify-content:center;';
      const img = document.createElement('img');
      img.src = prevUri;
      img.draggable = false;
      img.style.cssText = 'width:100%;height:100%;object-fit:contain;border-radius:2px;';
      p.appendChild(img);
      container.appendChild(p);
      _peekPrev = p;
    }

    if (nextUri) {
      const n = document.createElement('div');
      n.style.cssText = 'position:absolute;top:0;bottom:0;left:100%;width:100%;' +
        'display:flex;align-items:center;justify-content:center;';
      const img = document.createElement('img');
      img.src = nextUri;
      img.draggable = false;
      img.style.cssText = 'width:100%;height:100%;object-fit:contain;border-radius:2px;';
      n.appendChild(img);
      container.appendChild(n);
      _peekNext = n;
    }

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

  el.addEventListener('pointerdown', (e) => {
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

    // Create peek previews of adjacent images (only in fit mode)
    if (el.classList.contains('fit')) {
      _gestureCreatePeek();
    }
  });

  el.addEventListener('pointermove', (e) => {
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

    // In fit mode: slide .img-slide to preview navigation.
    // In free mode (zoomed in): C# panX/panY handles the image, and C#
    // OnPointerMove sets .img-slide overscroll feedback if at boundary.
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

  el.addEventListener('pointerup', (e) => {
    if (!_gestureActive) return;
    const offsetX = e.clientX - _gestureStartX;
    const velocity = _gestureGetVelocity();
    _gestureReset();

    if (_gestureDotNetRef) {
      _gestureDotNetRef.invokeMethodAsync('OnGestureRelease', offsetX, velocity);
    }
  });

  el.addEventListener('pointercancel', () => {
    if (!_gestureActive) return;
    const offsetX = _gestureLastX - _gestureStartX;
    const velocity = _gestureGetVelocity();
    _gestureReset();

    if (_gestureDotNetRef) {
      _gestureDotNetRef.invokeMethodAsync('OnGestureRelease', offsetX, velocity);
    }
  });
}

export function disposeGestureTracker() {
  _gestureReset();
  _gestureCleanupPeek();
  _gestureDotNetRef = null;
  _gestureEl = null;
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

// ── Flip-Through Transition (filmstrip click) ──

export function flipThroughTransition(thumbUris, targetUri, direction, targetScale = 1, targetFit = true) {
  return new Promise(resolve => {
    const slide = document.querySelector('.img-slide');
    const imageUris = thumbUris;   // cards render these 120px thumbnails
    if (!slide || !imageUris || !imageUris.length) { resolve(); return; }

    const isNext = direction === 'next';
    const cardMs = 110;  // ms per card
    const n = imageUris.length;

    // Setup container
    slide.style.overflow = 'hidden';
    const displayImg = slide.querySelector('.img-display');

    const cards = imageUris.map((uri, i) => {
      const card = document.createElement('div');
      card.style.cssText = 'position:absolute;top:0;right:0;bottom:0;left:0;display:flex;align-items:center;justify-content:center;backface-visibility:hidden;';
      card.style.transform = `translateX(${isNext ? '120%' : '-120%'})`;
      card.style.transition = `transform ${cardMs}ms ease-out, opacity ${cardMs}ms ease-out`;
      card.style.opacity = '0.7';
      card.style.zIndex = (n - i);

      // The flip cards are decorative eye-candy, so they show the 120px
      // thumbnail (thumbUris) scaled to FILL the viewport via object-fit:contain
      // — NOT scaled by targetScale (which assumes the full natural image size
      // and would shrink a 120px thumb to a speck). Decoupling the cards from
      // targetScale also frees the animation from the full-res HTTP fetch, so
      // the flip is instant after P1's streaming change. The real, correctly-
      // zoomed image is shown on the final frame by Blazor (displayImg.src =
      // targetUri below).
      const inner = document.createElement('div');
      inner.style.cssText = 'display:flex;align-items:center;justify-content:center;width:100%;height:100%;';
      const img = document.createElement('img');
      img.src = uri;
      img.style.cssText = 'width:100%;height:100%;object-fit:contain;border-radius:2px;';
      img.draggable = false;
      inner.appendChild(img);
      card.appendChild(inner);

      slide.appendChild(card);
      return card;
    });

    // Force layout
    cards[0].offsetHeight;

    let i = 0;
    const advance = () => {
      if (i >= n) {
        // Cleanup
        if (displayImg) {
          // Hand off to the REAL (full-res) target so the final frame shows
          // the actual image at the correct zoom — not a 120px thumbnail.
          // Blazor's LoadImageAsync then re-renders .img-display with the
          // (same/similar) target URL, continuing the load seamlessly.
          if (targetUri) displayImg.src = targetUri;
          // Pin the real .img-wrap (NOT .img-stack) to EXACTLY what Blazor will
          // render next: translate(0,0) scale(displayZoom). The .img-wrap already
          // carries scale(displayZoom) via GetZoomStyle(), so pinning it here
          // matches and Blazor's diff sees no change -> no pop, no double-scale.
          // (displayImg.parentElement is .img-stack, a CHILD of .img-wrap — pinning
          // there would nest scale(displayZoom) * scale(targetScale) and make the
          // image render at displayZoom^2, diverging from the toolbar's single
          // displayZoom * _dpr. That was the filmstrip-specific "zoom mismatch".)
          const w = displayImg.closest('.img-wrap') || slide.querySelector('.img-wrap');
          if (w) w.style.transform = `translate(0px,0px) scale(${targetScale})`;
        }
        setTimeout(() => {
          cards.forEach(c => { try { slide.removeChild(c); } catch (_) {} });
          slide.style.overflow = '';
          resolve();
        }, cardMs + 30);
        return;
      }

      // Slide in current card
      cards[i].style.transform = 'translateX(0)';
      cards[i].style.opacity = '1';

      // Push out previous card
      if (i > 0) {
        cards[i - 1].style.transform = `translateX(${isNext ? '-20%' : '20%'})`;
        cards[i - 1].style.opacity = '0.4';
      }

      setTimeout(() => {
        if (i > 0) {
          cards[i - 1].style.transition = `opacity 40ms ease-in`;
          cards[i - 1].style.opacity = '0';
        }
      }, cardMs * 0.6);

      i++;
      if (i < n) setTimeout(advance, cardMs);
      else advance();
    };

    requestAnimationFrame(() => advance());
  });
}

// ── Filmstrip ──

// Scroll the (virtualized) filmstrip so `index` is centered. `el` is the
// track ElementReference passed from Blazor. The track uses leading/trailing
// spacers + a small real-item window, so strip.children[index] is NOT the
// i-th thumbnail — compute the target scrollLeft from the known stride.
// Keep these in sync with ImageFilmstrip (ItemWidth = 52, ItemGap = 4).
export function scrollFilmstripToElement(el, index, smooth = false) {
  const strip = el;
  if (!strip) return;
  const stride = 56;   // ItemWidth + ItemGap
  const itemW = 52;    // ItemWidth
  const max = strip.scrollWidth - strip.clientWidth;

    // Few items: the whole strip fits inside the track (max <= 0), so every
    // thumbnail is already visible and there is nothing to scroll. Leave the
    // strip in its natural layout — the active thumbnail is simply highlighted
    // in place. The previous behavior recentered per-index here (paddingLeft =
    // clientWidth/2 - itemW/2 - index*stride), which made the ENTIRE strip slide
    // left/right on every prev/next press and read as "jumping". Don't do that.
    // Clear any inline padding so we fall back to the CSS `padding: 0 12px`.
    if (max <= 0) {
        strip.style.paddingLeft = '';
        strip.style.paddingRight = '';
        return;
    }

  // Many items: clear any leading pad we may have added for the few-items case
  // and scroll the active thumbnail to the center of the track.
  strip.style.paddingLeft = '';
  strip.style.paddingRight = '';
  const target = Math.max(0, Math.min(index * stride - strip.clientWidth / 2 + itemW / 2, max));
  if (smooth) {
    strip.scrollTo({ left: target, behavior: 'smooth' });
  } else {
    // Assigning scrollLeft is instant in EVERY WebView, unlike
    // scrollTo({behavior:'instant'}) which some embedded WebViews silently
    // ignore and fall back to a smooth (animated) scroll — that animation is
    // exactly what used to spam viewport thumbnail loads. So we set the
    // property directly to guarantee a jump with no intermediate scroll events.
    strip.scrollLeft = target;
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
      if (!v) { resolve([0, 0, 1]); return; }
      resolve([v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1]);
    });
  });
}

export function revokeBlobUrls(urls) {
  if (!urls) return;
  for (const u of urls) {
    try { URL.revokeObjectURL(u); } catch (e) { /* noop */ }
  }
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
