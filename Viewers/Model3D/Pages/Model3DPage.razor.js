// Model3DPage.razor.js —— 3D 查看器（非 GLB：STL/OBJ）的 THREE.js 初始化（隔离模块）
// THREE / OrbitControls / STLLoader / OBJLoader 与 <model-viewer> 自定义元素均由本模块
// 按需从 Model3D 程序集的 wwwroot（/_content/...）动态加载，不再依赖宿主 viewer.html。

const MODEL3D_SCRIPTS_BASE = '/_content/MauiMultimedia.Viewers.Model3D/scripts/';
let _scriptsPromise = null;

function loadScript(src, asModule) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${src}"]`);
        if (existing) { resolve(); return; }
        const el = document.createElement('script');
        el.src = src;
        if (asModule) el.type = 'module';
        el.onload = () => { console.log('[Model3D] 已加载', src); resolve(); };
        el.onerror = () => { console.error('[Model3D] 加载失败', src); reject(new Error('加载失败: ' + src)); };
        document.head.appendChild(el);
    });
}

// 按顺序加载 Model3D 依赖；返回同一 Promise 以便多次调用去重。
// 返回状态对象 { ok, failed[], three, modelViewer }，供 C# 端判断是否就绪。
export async function ensureScriptsLoaded() {
    if (_scriptsPromise) return _scriptsPromise;
    _scriptsPromise = (async () => {
        const files = [
            ['three.min.js', false],
            ['STLLoader.js', false],
            ['OBJLoader.js', false],
            ['OrbitControls.js', false],
            ['model-viewer.min.js', true],
        ];
        const failed = [];
        for (const [file, mod] of files) {
            try { await loadScript(MODEL3D_SCRIPTS_BASE + file, mod); }
            catch { failed.push(file); }
        }
        const three = typeof THREE !== 'undefined';
        const modelViewer = !!(window.customElements && window.customElements.get('model-viewer'));
        if (failed.length) console.error('[Model3D] 失败脚本:', failed);
        if (!three) console.error('[Model3D] THREE 未定义');
        if (!modelViewer) console.error('[Model3D] <model-viewer> 未注册');
        return { Ok: failed.length === 0, Failed: failed, Three: three, ModelViewer: modelViewer };
    })();
    return _scriptsPromise;
}

export async function initThree(canvasId, modelUrl, ext) {
    // 内部先确保依赖就位，避免 C# 端 await 未传导导致脚本未加载就执行
    const status = await ensureScriptsLoaded();
    if (!status.Ok) throw new Error('脚本加载失败: ' + status.Failed.join(', '));
    if (typeof THREE === 'undefined') throw new Error('THREE 未定义（three.min.js 未加载）');

    const canvas = document.getElementById(canvasId);
    if (!canvas) throw new Error('找不到 canvas 元素: ' + canvasId);

    const renderer = new THREE.WebGLRenderer({ canvas: canvas, antialias: true });
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);
    renderer.setClearColor(0x1a1a1a, 1);

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x1a1a1a);

    const camera = new THREE.PerspectiveCamera(45, canvas.clientWidth / canvas.clientHeight, 0.1, 1000);
    camera.position.set(5, 5, 10);

    // 灯光
    const ambient = new THREE.AmbientLight(0x404040);
    scene.add(ambient);
    const dir = new THREE.DirectionalLight(0xffffff, 1);
    dir.position.set(5, 10, 7);
    scene.add(dir);
    const dir2 = new THREE.DirectionalLight(0xffffff, 0.5);
    dir2.position.set(-5, -5, -5);
    scene.add(dir2);

    // 加载模型
    const loader = ext === '.stl' ? new THREE.STLLoader() : new THREE.OBJLoader();

    const onLoad = function (obj) {
        const mesh = obj instanceof THREE.BufferGeometry
            ? new THREE.Mesh(obj, new THREE.MeshStandardMaterial({ color: 0x88aaff, roughness: 0.4, metalness: 0.1 }))
            : obj;

        // 计算包围盒居中
        const box = new THREE.Box3().setFromObject(mesh);
        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());
        const maxDim = Math.max(size.x, size.y, size.z);
        const scale = maxDim > 0 ? 4 / maxDim : 1;
        mesh.position.sub(center.multiplyScalar(scale));
        mesh.scale.set(scale, scale, scale);
        scene.add(mesh);

        // 网格地面
        const grid = new THREE.GridHelper(8, 16, 0x444444, 0x333333);
        scene.add(grid);

        // 轨道控制
        const controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.target.set(0, 0, 0);
        controls.update();

        (function animate() {
            requestAnimationFrame(animate);
            controls.update();
            renderer.render(scene, camera);
        })();
    };

    loader.load(modelUrl, onLoad);
}
