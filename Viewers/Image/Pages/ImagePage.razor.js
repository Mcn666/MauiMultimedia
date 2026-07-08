// ImagePage.razor.js —— 图片查看器 JS 行为（隔离模块）
// 原内联在 ImagePage.razor.cs 的 eval 字符串统一收敛到这里，获得语法高亮与参数化调用。

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
        try { URL.revokeObjectURL(u); } catch (e) { }
    }
}

export function scrollStitchIntoView(index) {
    const c = document.querySelector('.v-stitch-container');
    if (!c) return;
    const imgs = c.querySelectorAll('img');
    if (imgs.length > index) imgs[index].scrollIntoView({ block: 'center' });
}

export function waitAnimationEnd() {
    return new Promise(r => {
        const el = document.querySelector('.img-slide');
        if (!el) { r(); return; }
        el.addEventListener('animationend', () => r(), { once: true });
    });
}

export function setSlideTransform(transform) {
    const el = document.querySelector('.img-slide');
    if (el) el.style.transform = transform;
}

export function clearSlideTransform() {
    const el = document.querySelector('.img-slide');
    if (el) el.style.removeProperty('transform');
}
