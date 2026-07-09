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

export function initGestureTracker(dotNetRef, elementSelector, swipeThreshold) {
  _gestureDotNetRef = dotNetRef;
  _gestureSwipeThreshold = swipeThreshold || 80;

  const el = document.querySelector(elementSelector);
  if (!el) return;
  _gestureEl = el;

  el.addEventListener('pointerdown', (e) => {
    if (_gestureActive) return;
    _gestureActive = true;
    _gestureStartX = e.clientX;
    _gestureStartY = e.clientY;
    _gestureLastX = e.clientX;
    _gestureLastY = e.clientY;
    _gestureLastTime = performance.now();
    _gestureVelocitySamples = [];
    el.setPointerCapture(e.pointerId);
    el.classList.add('tracking');
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
    const imgSlide = document.querySelector('.img-slide');
    if (imgSlide) {
      imgSlide.style.transform = `translateX(${offsetX}px)`;
      imgSlide.style.transition = 'none';
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
  _gestureDotNetRef = null;
  _gestureEl = null;
}

// ── Image Slide Transform ──

export function setSlideTransform(transform) {
  const el = document.querySelector('.img-slide');
  if (el) { el.style.transform = transform; el.style.transition = 'none'; }
}

export function clearSlideTransform() {
  const el = document.querySelector('.img-slide');
  if (el) { el.style.removeProperty('transform'); el.style.removeProperty('transition'); }
}

export function setSlideTransition(duration) {
  const el = document.querySelector('.img-slide');
  if (el) el.style.transition = `transform ${duration}ms cubic-bezier(0.4, 0, 0.2, 1)`;
}

// ── 3D Cylinder Transition ──
// We animate CLONES layered above the real .img-wrap. When the animation ends
// we DO touch the real wrap, but ONLY to pin it to EXACTLY what Blazor will
// render next: `scale(targetScale)` where targetScale == displayZoom (the same
// value the toolbar shows). Blazor's diff then sees no change, so the rendered
// zoom stays identical to displayZoom — no desync, no 200% flash. (.img-wrap
// always carries scale(displayZoom) now; fit mode no longer uses a bare CSS
// transform, which is what used to let the rendered zoom and toolbar diverge.)

export function cylinderTransition(imageUrl, direction, targetScale = 1, targetFit = true) {
  return new Promise(resolve => {
    const slide = document.querySelector('.img-slide');
    const wrap = slide?.querySelector('.img-wrap');
    const img = slide?.querySelector('.img-display');
    if (!slide || !wrap || !img) { resolve(); return; }

    const isNext = direction === 'next';
    // Slightly longer than before so large images don't whip past the
    // highest-distortion part of the flip (near rotateY 90deg) too fast.
    const duration = 480;
    const outAngle = isNext ? -90 : 90;
    const inAngle = isNext ? 90 : -90;

    // Larger perspective softens the near/far distortion of a 3D rotateY flip.
    // At 1200px a wide image's left/right edges swing a large Z distance, so
    // the perspective foreshortening becomes extreme (the "deep / whips past"
    // effect on big images). 2800px keeps a subtle 3D feel without the
    // exaggerated depth. Tune up further if very wide panoramas still distort.
    slide.style.perspective = '2800px';
    slide.style.perspectiveOrigin = 'center center';
    // Hide the real (Blazor-managed) wrap for the animation's duration so only
    // the clones are visible. Done via a class on the slide — Blazor does not
    // manage the slide's classes, so this survives Blazor's mid-animation
    // re-renders (which would otherwise wipe a direct visibility tweak).
    slide.classList.add('cyl-animating');

    // Front clone = current image, carrying the wrap's existing zoom transform.
    const front = wrap.cloneNode(true);
    front.classList.add('cyl-clone');
    front.style.position = 'absolute';
    front.style.inset = '0';
    front.style.margin = '0';
    front.style.width = '100%';
    front.style.height = '100%';
    front.style.transformStyle = 'preserve-3d';
    front.style.backfaceVisibility = 'hidden';
    front.style.willChange = 'transform';
    const base = front.style.transform || '';
    // Set the initial transform (with rotateY(0deg)) BEFORE attaching the
    // transition, so the scale(..) -> scale(..) rotateY(0) step doesn't burn
    // the whole duration as a no-op and delay the actual flip.
    front.style.transform = (base ? base + ' ' : '') + 'rotateY(0deg)';
    front.style.transition = `transform ${duration}ms ease-in-out`;

    // Back card = incoming image. It MUST render at EXACTLY the same on-screen
    // size as the real .img-wrap will after the transition — i.e. the image's
    // intrinsic pixel size scaled by targetScale (== displayZoom, the single
    // source of truth). The old code used object-fit:contain on the back <img>,
    // which sizes the image to the slide CONTAINER (fit-to-viewport) and IGNORES
    // the real zoom — so in 1:1 mode (or on HiDPI where displayZoom = 1/_dpr)
    // the incoming image was shown far larger during the flip, then snapped to
    // the correct scale the instant the real wrap was revealed. That snap is the
    // jitter seen ONLY when animations are on. Fix: scale an intrinsic-size <img>
    // by targetScale inside the rotating card, matching .img-wrap exactly.
    const back = document.createElement('div');
    back.style.cssText = 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center;backface-visibility:hidden;transform-style:preserve-3d;';
    back.style.transform = `rotateY(${inAngle}deg)`;
    back.style.transition = `transform ${duration}ms ease-in-out`;
    back.style.willChange = 'transform';

    const backInner = document.createElement('div');
    backInner.style.cssText = 'display:flex;align-items:center;justify-content:center;transform-style:preserve-3d;';
    backInner.style.transform = `scale(${targetScale})`;

    const backImg = document.createElement('img');
    backImg.src = imageUrl;
    backImg.draggable = false;
    backImg.style.cssText = 'max-width:none;max-height:none;border-radius:2px;';  // intrinsic size; scaling done by backInner
    backInner.appendChild(backImg);
    back.appendChild(backInner);

    slide.appendChild(front);
    slide.appendChild(back);

    // Force layout
    back.offsetHeight;

    requestAnimationFrame(() => {
      front.style.transform = (base ? base + ' ' : '') + `rotateY(${outAngle}deg)`;
      back.style.transform = 'rotateY(0deg)';

      let done = false;
      const finish = () => {
        if (done) return;
        done = true;
        front.removeEventListener('transitionend', finish);
        back.removeEventListener('transitionend', finish);

        // Swap the real <img> source to the new image AND pin .img-wrap's
        // transform to the new image's correct zoom before it is un-hidden.
        // Without this, the new <img> would momentarily inherit the *outgoing*
        // image's zoom (e.g. fit -> 1:1 shows scale(1.0) = 200% for one frame).
        // We set it to EXACTLY what Blazor will render next, so Blazor's diff
        // sees no change and the value stays correct — no desync, no pop.
        // .img-wrap ALWAYS carries scale(displayZoom) now (fit no longer uses a
        // bare CSS transform), so pin it unconditionally to the incoming zoom.
        img.src = imageUrl;
        // Pin the real .img-wrap to EXACTLY what Blazor will render next:
        // translate(0,0) scale(displayZoom). Matching the GetZoomStyle() output
        // string lets Blazor's diff see no change, so it won't re-set and there
        // is no pop. (pan is 0 after ResetView on navigation.)
        wrap.style.transform = `translate(0px,0px) scale(${targetScale})`;

        if (front.parentNode) front.parentNode.removeChild(front);
        if (back.parentNode) back.parentNode.removeChild(back);
        slide.classList.remove('cyl-animating');
        slide.style.perspective = '';
        slide.style.perspectiveOrigin = '';

        resolve();
      };
      front.addEventListener('transitionend', finish);
      back.addEventListener('transitionend', finish);
      setTimeout(finish, duration + 150);
    });
  });
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
      card.style.cssText = 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center;backface-visibility:hidden;';
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

  // Few items: the whole strip fits inside the track, so there is nothing to
  // scroll and the thumbnails would sit left-aligned (scrollLeft is clamped to
  // 0). To keep behavior consistent with the many-items case — where the active
  // thumbnail is scrolled to the center — pad the leading edge so the active
  // thumbnail lands in the middle of the track. We use an inline style (not a
  // Blazor-managed attribute) so it survives the background thumbnail
  // re-renders. If `index` is past the centerable range the pad clamps to 0
  // and the small strip simply left-aligns, which is fine.
  if (max <= 0) {
    const pad = Math.max(0, strip.clientWidth / 2 - itemW / 2 - index * stride);
    strip.style.paddingLeft = pad + 'px';
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
    document.querySelector('.viewer-page')?.appendChild(popup);
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
  document.querySelector('.image-viewport')?.focus();
}

export function getViewportMetrics() {
  const v = document.querySelector('.image-viewport');
  if (!v) return [0, 0, 1];
  return [v.offsetWidth, v.offsetHeight, window.devicePixelRatio || 1];
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
