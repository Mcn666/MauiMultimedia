// Model3DPage.razor.js —— 3D 查看器的 THREE.js 初始化

const MODEL3D_SCRIPTS_BASE = '/_content/MauiMultimedia.Viewers.Model3D/scripts/';
let _scriptsPromise = null;

function loadScript(src, asModule) {
    return new Promise((resolve, reject) => {
        const existing = document.querySelector(`script[src="${src}"]`);
        if (existing) { resolve(); return; }
        const el = document.createElement('script');
        el.src = src;
        if (asModule) el.type = 'module';
        el.onload = () => { resolve(); };
        el.onerror = () => { reject(new Error('加载失败: ' + src)); };
        document.head.appendChild(el);
    });
}

export async function ensureScriptsLoaded() {
    if (_scriptsPromise) return _scriptsPromise;
    _scriptsPromise = (async () => {
        const files = [
            ['three.min.js', false],
            ['fflate.min.js', false],
            ['STLLoader.js', false],
            ['OBJLoader.js', false],
            ['MTLLoader.js', false],
            ['FBXLoader.js', false],
            ['TGALoader.js', false],
            ['mmdparser.js', false],
            ['MMDLoader.js', false],
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
        return { Ok: failed.length === 0, Failed: failed, Three: three, ModelViewer: modelViewer };
    })();
    return _scriptsPromise;
}

export async function initThree(canvasId, modelUrl, ext, textureDataJson) {
    const status = await ensureScriptsLoaded();
    if (!status.Ok) throw new Error('脚本加载失败: ' + status.Failed.join(', '));
    if (typeof THREE === 'undefined') throw new Error('THREE 未定义');

    // ── 贴图 data URI 映射表 ──
    var textureData = {};
    if (textureDataJson) {
        try { textureData = JSON.parse(textureDataJson); } catch(e) {}
    }
    window.__textureData = textureData;

    // ── 拦截 TextureLoader/ImageLoader ──
    if (Object.keys(textureData).length > 0) {
        var origTexLoad = THREE.TextureLoader.prototype.load;
        THREE.TextureLoader.prototype.load = function(url, onLoad, onProgress, onError) {
            var fileName = url.split('/').pop().split('?')[0].split('#')[0];
            if (textureData[fileName]) {
                this.setPath('');
                url = textureData[fileName];
            }
            return origTexLoad.call(this, url, onLoad, onProgress, onError);
        };
        if (THREE.ImageLoader && THREE.ImageLoader.prototype.load) {
            var origImgLoad = THREE.ImageLoader.prototype.load;
            THREE.ImageLoader.prototype.load = function(url, onLoad, onProgress, onError) {
                var fileName = url.split('/').pop().split('?')[0].split('#')[0];
                if (textureData[fileName]) {
                    this.setPath('');
                    url = textureData[fileName];
                }
                return origImgLoad.call(this, url, onLoad, onProgress, onError);
            };
        }
    }

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

    const ambient = new THREE.AmbientLight(0x404040);
    scene.add(ambient);
    const dir = new THREE.DirectionalLight(0xffffff, 1);
    dir.position.set(5, 10, 7);
    scene.add(dir);
    const dir2 = new THREE.DirectionalLight(0xffffff, 0.5);
    dir2.position.set(-5, -5, -5);
    scene.add(dir2);

    // ── 选择加载器 ──
    var loader;
    if (ext === '.stl') {
        loader = new THREE.STLLoader();
    } else if (ext === '.obj') {
        loader = new THREE.OBJLoader();
        // 应用 MTL 材质
        var mtlData = textureData['__mtl__'];
        if (mtlData && typeof THREE.MTLLoader !== 'undefined') {
            try {
                var mtlResourcePath = modelUrl.substring(0, modelUrl.lastIndexOf('/') + 1);
                var mtlLoader = new THREE.MTLLoader();
                mtlLoader.setResourcePath(mtlResourcePath);
                var materials = mtlLoader.parse(mtlData);
                materials.preload();
                // DDS flipY 修正
                var hasDds = Object.keys(textureData).some(function(k) {
                    return k.toLowerCase().endsWith('.dds');
                });
                if (hasDds) {
                    Object.keys(materials.materials).forEach(function(matName) {
                        var mat = materials.materials[matName];
                        ['map', 'specularMap', 'normalMap', 'bumpMap'].forEach(function(prop) {
                            if (mat[prop] && mat[prop].isTexture) mat[prop].flipY = false;
                        });
                    });
                }
                // C4D 导出的 MTL 常设 d 0（透明度=0），导致模型完全透明不可见
                Object.keys(materials.materials).forEach(function(matName) {
                    var mat = materials.materials[matName];
                    if (mat.opacity !== undefined && mat.opacity < 0.01) {
                        mat.opacity = 1;
                        mat.transparent = false;
                    }
                });
                loader.setMaterials(materials);
            } catch(e) {
                console.warn('[Model3D] MTL parse error:', e);
            }
        }
    } else if (ext === '.fbx') {
        loader = new THREE.FBXLoader();
    } else if (ext === '.pmx') {
        loader = new THREE.MMDLoader();
    } else {
        throw new Error('不支持的 3D 格式: ' + ext);
    }

    // ── 模型加载完成后处理 ──
    function onLoad(obj, extra) {
        var mesh;
        if (obj instanceof THREE.BufferGeometry) {
            mesh = new THREE.Mesh(obj, new THREE.MeshStandardMaterial({ color: 0x88aaff, roughness: 0.4, metalness: 0.1 }));
        } else {
            mesh = obj;
        }

        var box = new THREE.Box3().setFromObject(mesh);
        var center = box.getCenter(new THREE.Vector3());
        var size = box.getSize(new THREE.Vector3());
        var maxDim = Math.max(size.x, size.y, size.z);
        var s = maxDim > 0 ? 4 / maxDim : 1;
        mesh.position.sub(center.multiplyScalar(s));
        mesh.scale.set(s, s, s);
        scene.add(mesh);

        var grid = new THREE.GridHelper(8, 16, 0x444444, 0x333333);
        scene.add(grid);

        var controls = new THREE.OrbitControls(camera, renderer.domElement);
        controls.enableDamping = true;
        controls.target.set(0, 0, 0);
        controls.update();

        (function animate() {
            requestAnimationFrame(animate);
            controls.update();
            renderer.render(scene, camera);
        })();
    }

    // ── 按格式加载 ──
    function makeError(msg) {
        console.error('[Model3D] ' + msg);
    }

    if (ext === '.pmx') {
        var pmxLoader = new THREE.MMDLoader();
        var resourcePath = modelUrl.substring(0, modelUrl.lastIndexOf('/') + 1);
        pmxLoader.load = function(url, onLoadCb, onProgress, onErrCb) {
            var e = url.indexOf('blob:') === 0 ? 'pmx' : this._extractExtension(url);
            if (e !== 'pmd' && e !== 'pmx') { onErrCb(new Error('Unknown extension: .' + e)); return; }
            this[e === 'pmd' ? 'loadPMD' : 'loadPMX'](url, function(data) {
                onLoadCb(pmxLoader.meshBuilder.build(data, resourcePath, onProgress, onErrCb));
            }, onProgress, onErrCb);
        };
        pmxLoader.load(modelUrl, onLoad, undefined, function(err) {
            makeError('PMX 加载失败: ' + (err.message || err));
        });
    } else if (ext === '.fbx') {
        loader.load(modelUrl, onLoad, undefined, function(err) {
            makeError('FBX 加载失败: ' + (err.message || err));
        });
    } else {
        loader.load(modelUrl, onLoad, undefined, function(err) {
            makeError('模型加载失败: ' + (err.message || err));
        });
    }
}
