// VideoPage.razor.js —— 视频查看器 JS 行为（隔离模块）
//
// 播放路径分流（以"文件真实魔数"为准，不信扩展名）：
//   · 真 FLV / MPEG-TS —— 浏览器原生不支持，用 mpegts.js 前端软解复用后经 MSE 喂给 <video>。
//                           FLV 仅支持 H.264/HEVC + AAC/MP3；老式 VP6/H.263/Nellymoser 编码
//                           MSE 无法解，会报解码错误。
//   · 实为 MP4/MOV(.flv 伪装) —— 直接原生 <video>，Chromium 原生支持。
//   · 原生容器(MP4/WebM/Ogg…) —— 直接 <video src>。
//   · AVI / MKV / 真 MPEG-PS 等 —— 浏览器无法解码，给出"请转码"提示，不黑屏。
//
// mpegts.js 为 UMD 包，挂在 window.mpegts。以绝对 URL 动态注入，避免
// BlazorWebView 路由变化导致相对路径基准漂移、脚本加载不到的问题。

let _player = null;          // 当前 mpegts.js 播放器实例
let _scriptLoading = null;   // mpegts.js 脚本加载 Promise（只加载一次）

// 与 Model3D 项目一致：RCL 静态资源统一用绝对路径 /_content/{Assembly}/...，
// 不受 per-viewer BlazorWebView 宿主页 / 路由基准漂移影响。
const VIDEO_SCRIPTS_BASE = '/_content/MauiMultimedia.Viewers.Video/';

// 抓取文件前 n 字节（Range 请求，不下载整文件），供魔数嗅探。
async function fetchHead(url, n = 64) {
    try {
        const resp = await fetch(url, {
            method: 'GET',
            headers: { Range: 'bytes=0-' + (n - 1) }
        });
        const ab = await resp.arrayBuffer();
        return new Uint8Array(ab.slice(0, n));
    } catch (e) {
        return null;
    }
}

// 按真实魔数判断容器类型（不信扩展名）。
function sniffMagic(head) {
    if (!head || head.length < 12) return 'unknown';
    // FLV: 'F''L''V'
    if (head[0] === 0x46 && head[1] === 0x4C && head[2] === 0x56) return 'flv';
    // MPEG-TS: 同步字节 0x47——标准 188 字节包在偏移 0，M2TS 192 字节包（4 字节
    // TP_extra_header + 188 字节 TS 包）同步字节在偏移 4
    if (head[0] === 0x47 || head[4] === 0x47) return 'ts';
    // MP4 / MOV / 3GP / M4V: 'ftyp' 出现在偏移 4（前 4 字节是 box size）
    if (head[4] === 0x66 && head[5] === 0x74 && head[6] === 0x79 && head[7] === 0x70) return 'mp4';
    // AVI: 'RIFF'....'AVI '
    if (head[0] === 0x52 && head[1] === 0x49 && head[2] === 0x46 && head[3] === 0x46) return 'avi';
    // ASF/WMV: 30 26 B2 75 8E 66 CF 11 A6 D9 00 AA 00 62 CE 6C
    if (head[0] === 0x30 && head[1] === 0x26 && head[2] === 0xB2 && head[3] === 0x75) return 'wmv';
    // EBML 家族：MKV / WebM 同用 0x1A 0x45 0xDF 0xA3 头，用 DocType 区分——
    // WebM 浏览器原生支持（走原生 <video>），MKV 不支持
    if (head[0] === 0x1A && head[1] === 0x45 && head[2] === 0xDF && head[3] === 0xA3) {
        const ascii = String.fromCharCode(...head.slice(0, 64));
        return ascii.includes('webm') ? 'webm' : 'mkv';
    }
    // 真 MPEG program stream: 00 00 01 BA（pack start）或 00 00 01 B3（sequence header）
    if (head[0] === 0x00 && head[1] === 0x00 && head[2] === 0x01 &&
        (head[3] === 0xBA || head[3] === 0xB3)) return 'mpegps';
    return 'unknown';
}

function loadMpegts() {
    if (window.mpegts) return Promise.resolve(window.mpegts);
    if (_scriptLoading) return _scriptLoading;
    _scriptLoading = new Promise((resolve, reject) => {
        const src = VIDEO_SCRIPTS_BASE + 'mpegts.js';
        const existing = document.querySelector(`script[src="${src}"]`);
        if (existing && window.mpegts) { resolve(window.mpegts); return; }
        const s = document.createElement('script');
        s.src = src;
        s.onload = () => {
            if (window.mpegts && window.mpegts.createPlayer && window.mpegts.Events) {
                resolve(window.mpegts);
            } else {
                _scriptLoading = null;
                reject(new Error('mpegts.js 已加载但接口不完整'));
            }
        };
        s.onerror = () => {
            _scriptLoading = null;
            reject(new Error('mpegts.js 加载失败，URL=' + src));
        };
        document.head.appendChild(s);
    });
    return _scriptLoading;
}

function destroyPlayer() {
    if (_player) {
        try { _player.destroy(); } catch (e) { /* ignore */ }
        _player = null;
    }
}

// 真正走 mpegts 软解（仅用于真 FLV / TS）
async function playWithMpegts(v, url, type) {
    let mpegts;
    try {
        mpegts = await loadMpegts();
    } catch (e) {
        return 'error:软解库加载失败 - ' + e.message;
    }

    if (!mpegts.isSupported()) {
        return 'error:MSE 不受支持，当前环境无法软解播放该格式';
    }

    // 静音 mpegts 的 warn 级日志。主要是 seek 后的 "Dropping audio frame ... overlap"
    // 丢帧提示（FLV seek 已知伪影，不影响播放，留着只会刷屏）。error 级仍保留，
    // 以便暴露真正的故障（如编码不支持 / 解复用异常）。
    try {
        if (mpegts.LoggingControl && typeof mpegts.LoggingControl.enableWarn === 'function')
            mpegts.LoggingControl.enableWarn(false);
    } catch (e) { /* ignore */ }

    // 清掉原生 src，避免与 MSE 冲突
    v.removeAttribute('src');
    v.load();

    try {
        // VOD（非直播）配置：
        //  · enableStashBuffer:false —— 关掉帧暂存缓冲。seek 后若 stash 里残留 seek 前的
        //    旧帧，会被 MP4Remuxer 以"过期 DTS"判定为重叠而丢弃（即日志里的丢帧杂音）。
        //    关掉后帧直送 MSE，向后拖拽不再触发该伪影。
        //  · seekType:'range' —— 复用 FileServer 的 HTTP Range 支持做精准定位。
        //  · fixAudioTimestampGap:true —— 保留默认，正常间隔下自动补帧避免 A/V 漂移。
        //  · autoCleanupSourceBuffer:true —— VOD 大文件默认不清理 SourceBuffer，会堆到上限
        //    触发 "MSE SourceBuffer is full" 背压、占满 WebView2 内存；开启后仅保留播放点
        //    之后约 180s 缓冲，超出裁剪——消除背压、限制内存；拖拽超出已保留范围时由
        //    seekType:'range' 经 HTTP Range 重新拉取（本地 FileServer 瞬时完成）。
        _player = mpegts.createPlayer(
            { type: type, isLive: false, url: url },
            {
                enableWorker: false,
                lazyLoad: false,
                enableStashBuffer: false,
                seekType: 'range',
                fixAudioTimestampGap: true,
                autoCleanupSourceBuffer: true,
                autoCleanupMaxBackwardDuration: 180,
                autoCleanupMinBackwardDuration: 120
            }
        );
    } catch (e) {
        return 'error:创建播放器失败 - ' + e.message;
    }

    // 用 Promise 桥接：成功解复用出媒体信息 → ok；报错 → error
    return await new Promise((resolve) => {
        let settled = false;
        const done = (r) => { if (!settled) { settled = true; resolve(r); } };

        _player.on(mpegts.Events.ERROR, (t, d, info) => {
            const extra = info ? ' (' + JSON.stringify(info) + ')' : '';
            console.error('[VideoPage] mpegts ERROR:', t, d, info);
            done('error:解码错误 ' + t + '/' + d + extra);
        });
        _player.on(mpegts.Events.MEDIA_INFO, () => {
            done('ok');
        });

        _player.attachMediaElement(v);
        _player.load();
        // 注意：此处**不再**做超时假成功，任何失败都会显式返回 error。
    });
}

// mediaType: 'native' | 'flv' | 'mpegts'（由 C# 按扩展名初判，JS 再用魔数校正）
export async function setVideoSource(elementId, url, mediaType) {
    const v = document.getElementById(elementId);
    if (!v) return 'element-not-found';

    // 切换任何新源前，先销毁上一个软解播放器，释放 MSE buffer
    destroyPlayer();

    // 统一按真实魔数嗅探（不信扩展名）：既能识别 .flv 实为 MP4 的伪装，
    // 也能拦截 AVI/MKV/真 MPEG-PS 这类浏览器根本无法解码的容器。
    const head = await fetchHead(url);
    const magic = sniffMagic(head);

    // 浏览器无法解码的容器：明确提示转码，避免静默黑屏
    if (magic === 'avi' || magic === 'mkv' || magic === 'mpegps' || magic === 'wmv') {
        const name = magic === 'avi' ? 'AVI' : magic === 'mkv' ? 'MKV'
            : magic === 'mpegps' ? 'MPEG-PS' : 'WMV/ASF';
        return 'error:该视频容器格式（' + name + '）当前浏览器不支持播放，请转码为 H.264 编码的 MP4 后再查看。';
    }

    // FLV / MPEG-TS 分支：先按真实魔数校正，避免被扩展名误导
    // （很多下载器把 MP4 命名为 .flv，mpegts 探针会因无 FLV 魔数而拒绝）
    if (mediaType === 'flv' || mediaType === 'mpegts') {
        if (magic === 'flv') {
            return await playWithMpegts(v, url, 'flv');
        }
        if (magic === 'ts') {
            return await playWithMpegts(v, url, 'mpegts');
        }
        // 实为 MP4/MOV（扩展名误导）或嗅探不出问题：交给原生 <video>
        try { v.src = url; v.load(); return 'ok'; }
        catch (e) { return 'error:' + e.message; }
    }

    // 原生路径（非 flv/mpegts mediaType，例如 mp4/webm/ogg/mov/3gp/m4v 扩展名）。
    // 同步 set src 不会报错，解码失败发生在异步 error 事件——监听它返回友好提示，
    // 否则 wmv/ogv 等无法解码的编码会静默黑屏。
    try {
        v.src = url;
        v.load();
        if (v.readyState >= 1) return 'ok';
        return await new Promise((resolve) => {
            let settled = false;
            const done = (r) => { if (!settled) { settled = true; cleanup(); resolve(r); } };
            const onErr = () => done('error:该视频无法解码，可能编码不受当前浏览器支持，请转码为 H.264 编码的 MP4 后再查看。');
            const onMeta = () => done('ok');
            const cleanup = () => {
                v.removeEventListener('error', onErr);
                v.removeEventListener('loadedmetadata', onMeta);
            };
            v.addEventListener('error', onErr);
            v.addEventListener('loadedmetadata', onMeta);
            // 超时兜底：5 秒无信号按成功处理（避免大文件/慢加载卡死提示）
            setTimeout(() => done('ok'), 5000);
        });
    } catch (e) {
        return 'error:' + e.message;
    }
}

export function stopVideo(elementId) {
    destroyPlayer();
    const v = document.getElementById(elementId);
    if (!v) return;
    v.pause();
    v.removeAttribute('src');
    v.load();
}

// ── 自定义控件条驱动 ──
// 统一操作同一个 <video> 元素：无论原生 <video src> 还是 mpegts.js 经 MSE 喂入，
// 最终都落到该元素，因此音量/进度/倍速/全屏在两条路径下行为一致。

export function playPause(elementId) {
    const v = document.getElementById(elementId);
    if (!v) return;
    if (v.paused) v.play().catch(() => { /* 自动播放策略可能拒绝，忽略 */ });
    else v.pause();
}

export function setVolume(elementId, vol) {
    const v = document.getElementById(elementId);
    if (!v) return;
    v.volume = Math.max(0, Math.min(1, vol));
    if (v.volume > 0) v.muted = false;
}

export function setMuted(elementId, muted) {
    const v = document.getElementById(elementId);
    if (v) v.muted = !!muted;
}

export function setCurrentTime(elementId, t) {
    const v = document.getElementById(elementId);
    if (v) v.currentTime = t;
}

export function setPlaybackRate(elementId, r) {
    const v = document.getElementById(elementId);
    if (v) v.playbackRate = r;
}

export function setLoop(elementId, loop) {
    const v = document.getElementById(elementId);
    if (v) v.loop = !!loop;
}

// 绑定 <video> 事件 → 回传 C#（dotNetRef.invokeMethodAsync）。
// 用 _vpBound 守卫避免重复绑定（切换视频源时元素复用，不应二次监听）。
export function initControls(elementId, dotNetRef) {
    const v = document.getElementById(elementId);
    if (!v || v._vpBound) return;
    v._vpBound = true;

    const cb = (name, arg) => {
        try { dotNetRef.invokeMethodAsync(name, arg); } catch (e) { /* ignore */ }
    };

    v.addEventListener('play', () => cb('OnPlayingChanged', true));
    v.addEventListener('pause', () => cb('OnPlayingChanged', false));
    v.addEventListener('timeupdate', () => cb('OnTimeUpdate', v.currentTime));
    v.addEventListener('durationchange', () => cb('OnDurationChanged', isFinite(v.duration) ? v.duration : 0));
    v.addEventListener('loadedmetadata', () => cb('OnDurationChanged', isFinite(v.duration) ? v.duration : 0));
    v.addEventListener('volumechange', () => cb('OnVolumeChanged', v.volume, v.muted));
    v.addEventListener('ended', () => cb('OnEnded', null));

    // ── 手势：点按=播放/暂停（立即）；横向滑动=拖动进度（scrub） ──
    // 点按与滑动靠「位移量」区分（pointerup 时判定），无需等待第二次点击，
    // 因此单击播放/暂停可立即响应，彻底去掉此前单/双击区分所需的 250ms 延迟。
    const inUiArea = (el) => el && el.closest &&
        (el.closest('.vp-controls') || el.closest('.vp-center-btn'));
    // 监听挂在整个播放器视口（.video-container）上，而非 <video> 本身——
    // 视频在容器里是信箱式居中的黑边矩形，若只挂 <video>，黑边与画面外视口区域
    // 没有监听，点击/滑动就会失效。scrub 距离仍按视频自身尺寸计算。
    const stage = v.closest('.video-container') || v.parentElement || v;
    let pId = -1, pX = 0, pY = 0, pDown = false, pSwipe = false, pSwipeStarted = false,
        scrubFrom = 0, rafId = 0, rafTarget = 0;
    const SWIPE_MIN = 8;        // px，超过即判定为滑动而非点按
    const SEEK_SPAN = 60;       // 整屏宽度对应的时间跨度(秒)：滑动仅做微调，大幅跳转请用控件条进度条

    stage.addEventListener('pointerdown', (e) => {
        if (inUiArea(e.target)) return;             // 控件条 / 中央按钮自行处理
        pDown = true; pSwipe = false; pSwipeStarted = false;
        pId = e.pointerId; pX = e.clientX; pY = e.clientY;
        scrubFrom = v.currentTime;
        try { stage.setPointerCapture(pId); } catch (_) { /* 不支持捕获时滑动离场即结束 */ }
    });

    stage.addEventListener('pointermove', (e) => {
        if (!pDown || e.pointerId !== pId) return;
        const dx = e.clientX - pX, dy = e.clientY - pY;
        if (!pSwipe) {
            // 进入滑动判定：横向位移足够大且明显大于纵向（避免竖向误触）
            if (Math.abs(dx) > SWIPE_MIN && Math.abs(dx) > Math.abs(dy) * 1.4) pSwipe = true;
            else return;
        }
        e.preventDefault();                         // 阻止默认手势（文本选择 / 滚动）
        if (!pSwipeStarted) { pSwipeStarted = true; cb('OnScrubStart', 0); }
        const rect = v.getBoundingClientRect();
        const dur = isFinite(v.duration) ? v.duration : 0;
        const delta = (dx / rect.width) * SEEK_SPAN;  // 滑动仅做微调：整屏宽 ≈ 固定时间窗，与视频总时长无关
        rafTarget = Math.max(0, Math.min(dur, scrubFrom + delta));
        if (!rafId) {
            rafId = requestAnimationFrame(() => {
                rafId = 0;
                if (isFinite(rafTarget)) v.currentTime = rafTarget;   // 直播式 scrub
                cb('OnScrub', rafTarget);
            });
        }
    }, { passive: false });

    function endPointer(e) {
        if (!pDown || (pId >= 0 && e.pointerId !== pId)) return;
        pDown = false;
        try { stage.releasePointerCapture(pId); } catch (_) { /* ignore */ }
        pId = -1; pSwipeStarted = false;
        if (rafId) { cancelAnimationFrame(rafId); rafId = 0; }
        if (pSwipe) {
            cb('OnScrubEnd', isFinite(v.currentTime) ? v.currentTime : 0);
        } else {
            cb('OnVideoClick', null);               // 点按：立即播放/暂停（无延迟）
        }
    }
    stage.addEventListener('pointerup', endPointer);
    stage.addEventListener('pointercancel', endPointer);

    // 推送初始状态
    cb('OnPlayingChanged', !v.paused);
    cb('OnVolumeChanged', v.volume, v.muted);
    if (isFinite(v.duration) && v.duration > 0) cb('OnDurationChanged', v.duration);
}
