// VideoPage.razor.js —— 视频查看器 JS 行为（隔离模块）
export function setVideoSource(elementId, url) {
    const v = document.getElementById(elementId);
    if (!v) return 'element-not-found';
    try {
        v.src = url;
        v.load();
        return 'ok';
    } catch (e) {
        return 'error:' + e.message;
    }
}

export function stopVideo(elementId) {
    const v = document.getElementById(elementId);
    if (!v) return;
    v.pause();
    v.removeAttribute('src');
    v.load();
}

export function setupAutoNext(elementId) {
    const v = document.getElementById(elementId);
    if (!v) return 'no-element';
    v.addEventListener('ended', function () {
        const nextBtn = document.querySelector('button[title="下一个"]');
        if (nextBtn && !nextBtn.disabled) nextBtn.click();
    });
    return 'ok';
}
