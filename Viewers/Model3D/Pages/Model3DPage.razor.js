// Model3DPage.razor.js —— 3D 查看器的 THREE.js 初始化
//
// 设计要点（修复“切换文件丢失模型”的根因）：
//   - initViewer() 只执行一次：创建 WebGLRenderer / Scene / 相机 / 光照 / OrbitControls / 动画循环。
//   - loadModel() 在每次切换文件时调用：清空旧模型（dispose 几何与材质）、加载新模型进【同一个】场景。
//   - 这样无论切换多少次，都只有 1 个渲染器、1 个动画循环，不会因重复 initThree 导致多套
//     动画循环抢同一块 canvas、旧场景兜底残留而“丢失部分模型”。

const MODEL3D_SCRIPTS_BASE = '/_content/MauiMultimedia.Viewers.Model3D/scripts/';
let _scriptsPromise = null;

// 单例查看器状态
let _viewer = null;            // { renderer, scene, camera, controls, canvas, modelRoot, mixer, clock, ext }
let _textureData = {};         // 当前模型的贴图 data URI 映射（运行时可被 loadModel 替换）
let _texOverrideInstalled = false;
let _rafId = 0;
let _loadResolve = null;       // loadModel 返回的 Promise 的 resolve/reject，供 onModelLoaded 调用
let _loadReject = null;

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
            ['GLTFLoader.js', false],
            ['fflate.min.js', false],
            ['STLLoader.js', false],
            ['OBJLoader.js', false],
            ['MTLLoader.js', false],
            ['FBXLoader.js', false],
            ['TGALoader.js', false],
            ['mmdparser.js', false],
            ['MMDLoader.js', false],
            ['OrbitControls.js', false],
        ];
        const failed = [];
        for (const [file, mod] of files) {
            try { await loadScript(MODEL3D_SCRIPTS_BASE + file, mod); }
            catch { failed.push(file); }
        }
        const three = typeof THREE !== 'undefined';
        return { Ok: failed.length === 0, Failed: failed, Three: three };
    })();
    return _scriptsPromise;
}

// 贴图拦截：把 THREE.TextureLoader / ImageLoader 的 URL 替换成本地解码出的 data URI。
// 只安装一次（避免重复包裹导致递归），运行时读取可变的 _textureData。
// 额外职责：贴图图片解码完成后抽样检测是否带透明通道(alpha<250 的像素)，
// 若是则给引用它的材质开启 transparent + alphaTest，避免"黑色=透明"的 DDS 被当成不透明黑渲染。
function installTextureOverride() {
    if (_texOverrideInstalled || typeof THREE === 'undefined') return;
    const swap = function (origLoad) {
        return function (url, onLoad, onProgress, onError) {
            const fileName = url.split('/').pop().split('?')[0].split('#')[0];
            let realUrl = url;
            if (_textureData[fileName]) {
                this.setPath('');
                realUrl = _textureData[fileName];
            }
            const wrappedOnLoad = function (tex, ...args) {
                try {
                    const hasAlpha = detectTextureAlpha(tex, fileName);
                    if (hasAlpha) applyAlphaToMaterial(tex);
                } catch (e) { /* 检测失败不影响渲染 */ }
                if (onLoad) onLoad(tex, ...args);
            };
            return origLoad.call(this, realUrl, wrappedOnLoad, onProgress, onError);
        };
    };
    THREE.TextureLoader.prototype.load = swap(THREE.TextureLoader.prototype.load);
    if (THREE.ImageLoader && THREE.ImageLoader.prototype.load) {
        THREE.ImageLoader.prototype.load = swap(THREE.ImageLoader.prototype.load);
    }
    _texOverrideInstalled = true;
}

// 抽样检测贴图是否带透明通道。在贴图 image 解码完成后调用(tex 为 Texture)。
// 结果记入 tex.userData.hasAlpha 并返回布尔。透明区域在 DDS 里常表现为"黑底 alpha=0"，
// 因此靠 alpha 通道判断，而不是靠 RGB 黑色（黑≠一定透明，不能把黑色当透明切掉）。
function detectTextureAlpha(tex, fileName) {
    const img = (tex && tex.isTexture) ? tex.image : tex;
    if (!img || !img.width || !img.height) return false;
    const sw = Math.min(img.width, 64), sh = Math.min(img.height, 64);
    let hasAlpha = false;
    try {
        const c = document.createElement('canvas');
        c.width = sw; c.height = sh;
        const ctx = c.getContext('2d');
        ctx.drawImage(img, 0, 0, sw, sh);
        const data = ctx.getImageData(0, 0, sw, sh).data;
        for (let i = 3; i < data.length; i += 4) {
            if (data[i] < 250) { hasAlpha = true; break; }
        }
    } catch (e) { return false; }
    if (tex && tex.isTexture) {
        tex.userData = tex.userData || {};
        tex.userData.hasAlpha = hasAlpha;
        tex.userData.modelTexName = fileName;
    }
    return hasAlpha;
}

// 贴图确认带透明后，遍历当前场景里引用它的材质，开启透明/硬切，消除"黑底不透明"。
function applyAlphaToMaterial(tex) {
    if (!_viewer || !_viewer.scene) return;
    _viewer.scene.traverse(function (o) {
        if (!o.isMesh || !o.material) return;
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach(function (m) {
            const usesAlphaTex = (m.map === tex) || (m.emissiveMap === tex) ||
                                 (m.specularMap === tex) || (m.lightMap === tex);
            if (usesAlphaTex) {
                m.transparent = true;
                if (m.alphaTest === 0) m.alphaTest = 0.5; // 硬切透明，避免半透明排序发黑/闪烁
                m.needsUpdate = true;
            }
        });
    });
}

// 模型加入场景前，对"已经解码完成且带透明"的贴图材质开启透明（覆盖贴图早于模型加载完成的情况）。
function enableAlphaForTexturedMaterials(root) {
    if (!root || !root.traverse) return;
    root.traverse(function (o) {
        if (!o.isMesh || !o.material) return;
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach(function (m) {
            const colorMaps = [m.map, m.emissiveMap, m.specularMap, m.lightMap];
            for (const t of colorMaps) {
                if (t && t.isTexture && t.userData && t.userData.hasAlpha) {
                    m.transparent = true;
                    if (m.alphaTest === 0) m.alphaTest = 0.5;
                    m.needsUpdate = true;
                    break;
                }
            }
        });
    });
}

// 递归释放几何与材质，避免切换模型后 GPU 内存泄漏
function disposeObject(obj) {
    if (!obj) return;
    obj.traverse(function (o) {
        if (o.geometry) o.geometry.dispose();
        if (o.material) {
            const mats = Array.isArray(o.material) ? o.material : [o.material];
            mats.forEach(function (m) {
                if (!m) return;
                for (const key in m) {
                    if (m[key] && m[key].isTexture) m[key].dispose();
                }
                m.dispose();
            });
        }
    });
}

// 修正贴图输入色彩空间（解决"偏白化"的核心一步）。
// three r128 默认贴图按 LinearEncoding 采样，但模型颜色贴图(baseColor/map/specular/emissive 等)
// 实为 sRGB 编码的文件。若不把它们标记为 sRGB，renderer.outputEncoding(sRGB) 会把线性值直接当线性
// 重新编码输出 → 整体过曝、发白。此处统一把"颜色类贴图"的 encoding 设为 sRGB，
// 非颜色贴图(normal/rough/metal/bump/ao 等)保持 Linear 不动。GLTFLoader 已自带正确设置，这里做兜底。
function fixTextureEncodings(root) {
    if (!root || !root.traverse) return;
    const colorProps = ['map', 'emissiveMap', 'specularMap', 'lightMap', 'aoMap'];
    if (THREE.sRGBEncoding === undefined) return;
    root.traverse(function (o) {
        if (!o.isMesh || !o.material) return;
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach(function (m) {
            if (!m) return;
            colorProps.forEach(function (prop) {
                const tex = m[prop];
                if (tex && tex.isTexture && tex.encoding !== THREE.sRGBEncoding) {
                    tex.encoding = THREE.sRGBEncoding;
                    tex.needsUpdate = true;
                }
            });
            m.needsUpdate = true;
        });
    });
}

// 在“无光照（unlit）”模式与正常模式间切换模型材质。
// unlit 时把每个网格材质替换为 MeshBasicMaterial（仅保留基础色/贴图，不受光照影响），
// 用于“关灯”预览——便于检查模型本来的颜色与贴图，而不被明暗与卡通梯度干扰。
// 原材质暂存在 mesh.userData._origMats，恢复时换回并释放 basic 材质。
function setUnlit(root, unlit) {
    if (!root || !root.traverse) return;
    root.traverse(function (o) {
        if (!o.isMesh) return;
        const src = o.material;
        if (unlit) {
            if (o.userData && o.userData._origMats) return; // 已是 unlit
            const toBasic = function (orig) {
                const b = new THREE.MeshBasicMaterial();
                if (orig.color && orig.color.isColor) b.color.copy(orig.color);
                if (orig.map) b.map = orig.map;
                b.transparent = orig.transparent;
                b.alphaTest = orig.alphaTest;
                b.side = orig.side;
                return b;
            };
            if (Array.isArray(src)) {
                o.userData = o.userData || {};
                o.userData._origMats = src;
                o.material = src.map(toBasic);
            } else if (src) {
                o.userData = o.userData || {};
                o.userData._origMats = src;
                o.material = toBasic(src);
            }
        } else {
            if (o.userData && o.userData._origMats) {
                if (Array.isArray(o.material)) o.material.forEach(function (b) { if (b && b.dispose) b.dispose(); });
                else if (o.material && o.material.dispose) o.material.dispose();
                o.material = o.userData._origMats;
                o.userData._origMats = null;
            }
        }
    });
}

// 切换光照：on=true 正常受光渲染；on=false 无光照（unlit）预览。
// 注意：unlit 用 basic 材质，光源是否 visible 不影响 basic，但为语义一致仍同步切换灯光可见性。
export function setLights(on) {
    if (!_viewer) return;
    _viewer.lightsOn = on;
    if (_viewer.lights) {
        _viewer.lights.ambient.visible = on;
        _viewer.lights.dir.visible = on;
        _viewer.lights.dir2.visible = on;
        _viewer.lights.hemi.visible = on;
    }
    if (_viewer.modelRoot) setUnlit(_viewer.modelRoot, !on);
}

// 切换网格地面（GridHelper）可见性。
export function setGrid(on) {
    if (!_viewer || !_viewer.grid) return;
    _viewer.grid.visible = on;
}

// 按格式调整光照。PMX(MMD) 用卡通(MeshToon)材质，补光(半球/环境)稍强即整体过曝发白；
// 故 PMX 时大幅压低半球光与环境光、保留主方向光撑出明暗，避免"偏白化"；
// 其它格式(含 glTF PBR)恢复默认，保留环境感。每次 loadModel 都会按 ext 重设，切换格式互不污染。
function applyLightingForExt(ext) {
    if (!_viewer || !_viewer.lights) return;
    const L = _viewer.lights, d = L.defaults;
    if (ext === '.pmx') {
        L.hemi.intensity = 0.1;    // 补光回到接近"初版"水平但略收；真正的白化主因(自发光)已压住
        L.ambient.intensity = 0.2;
        L.dir.intensity = 0.75;    // 主光略升，保证可见度与卡通明暗
        L.dir2.intensity = 0.12;
    } else {
        L.hemi.intensity = d.hemi;
        L.ambient.intensity = d.ambient;
        L.dir.intensity = d.dir;
        L.dir2.intensity = d.dir2;
    }
}

// 创建查看器：仅一次。后续的模型切换由 loadModel 复用此管线。
export async function initViewer(canvasId) {
    if (_viewer) return; // 幂等：已初始化则直接返回

    const status = await ensureScriptsLoaded();
    if (!status.Ok) throw new Error('脚本加载失败: ' + status.Failed.join(', '));
    if (typeof THREE === 'undefined') throw new Error('THREE 未定义');

    const canvas = document.getElementById(canvasId);
    if (!canvas) throw new Error('找不到 canvas 元素: ' + canvasId);

    installTextureOverride();

    const renderer = new THREE.WebGLRenderer({ canvas: canvas, antialias: true });
    renderer.setPixelRatio(window.devicePixelRatio);
    renderer.setSize(canvas.clientWidth, canvas.clientHeight);
    renderer.setClearColor(0x1a1a1a, 1);
    // glTF/GLB 的 PBR 材质需要正确的色彩空间（r128 API），老 WebView 也支持。
    // 输出用 sRGB 编码；贴图的 "sRGB→linear 输入解码" 在 onModelLoaded 里按贴图类型设置。
    // 二者配合才能正确还原颜色，否则会"偏白化"（sRGB 输出 + 线性输入 → 过曝发白）。
    if (THREE.sRGBEncoding !== undefined) renderer.outputEncoding = THREE.sRGBEncoding;
    // 不使用 ACESFilmicToneMapping：该映射会去饱和、高光向白收缩，正是"偏白化"的主因之一。
    renderer.toneMapping = THREE.NoToneMapping;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x1a1a1a);

    const camera = new THREE.PerspectiveCamera(45, canvas.clientWidth / canvas.clientHeight, 0.1, 1000);
    camera.position.set(5, 5, 10);

    // 光照：MMD/PMX 的 MeshToonMaterial 对光照极敏感，补光(半球/环境)过强会整体过曝发白。
    // 默认强度偏保守；PMX 这类卡通材质会在 loadModel 里进一步压低补光（见 applyLightingForExt）。
    const ambient = new THREE.AmbientLight(0x666666, 0.45);
    scene.add(ambient);
    const dir = new THREE.DirectionalLight(0xffffff, 0.6);
    dir.position.set(5, 10, 7);
    scene.add(dir);
    const dir2 = new THREE.DirectionalLight(0xffffff, 0.2);
    dir2.position.set(-5, -5, -5);
    scene.add(dir2);
    // 半球光：让 glTF PBR 材质（尤其金属/粗糙表面）有环境感
    const hemi = new THREE.HemisphereLight(0xffffff, 0x444444, 0.6);
    scene.add(hemi);
    // 记下默认强度，供按格式切换光照（PMX 压低补光）时还原
    const _lightDefaults = {
        ambient: ambient.intensity, dir: dir.intensity, dir2: dir2.intensity, hemi: hemi.intensity
    };

    // 网格地面（常驻，不随模型切换重建；由工具栏“网格”按钮控制显隐）
    const grid = new THREE.GridHelper(8, 16, 0x444444, 0x333333);
    grid.position.y = 0;
    scene.add(grid);

    const controls = new THREE.OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.target.set(0, 0, 0);
    controls.update();

    _viewer = {
        renderer, scene, camera, controls, canvas,
        modelRoot: null,
        mixer: null,
        clock: new THREE.Clock(),
        ext: '',
        lightsOn: true,
        lights: { ambient, dir, dir2, hemi, defaults: _lightDefaults },
        grid: grid
    };

    (function animate() {
        if (!_viewer) return;
        _rafId = requestAnimationFrame(animate);
        if (_viewer.mixer) _viewer.mixer.update(_viewer.clock.getDelta());
        _viewer.controls.update();
        _viewer.renderer.render(_viewer.scene, _viewer.camera);
    })();
}

// 模型加载完成后的处理（复用于所有格式）
function onModelLoaded(obj) {
    const v = _viewer;
    if (!v) return;
    try {
        const root = (obj && obj.scene) ? obj.scene : obj;
        let mesh;
        if (root instanceof THREE.BufferGeometry) {
            mesh = new THREE.Mesh(root, new THREE.MeshStandardMaterial({ color: 0x88aaff, roughness: 0.4, metalness: 0.1 }));
        } else {
            mesh = root;
        }

        // 修正颜色贴图输入色彩空间（sRGB→linear），消除"偏白化"。必须在加入场景前完成。
        fixTextureEncodings(mesh);

        // 对已解码完成的带透明贴图材质开启透明（迟加载的贴图会在其 onLoad 里再补一次）。
        enableAlphaForTexturedMaterials(mesh);

        // PMX(MMD) 材质：emissive 来自 PMX 自带 ambient，是自发光——会直接往白色叠。
        // 无论有无贴图都压低，避免叠加场景光照后整模型偏白（仅压无贴图那一支仍会漏掉有贴图材质）。
        if (v.ext === '.pmx' && mesh.traverse) {
            mesh.traverse(function (o) {
                if (!o.isMesh || !o.material) return;
                const mats = Array.isArray(o.material) ? o.material : [o.material];
                mats.forEach(function (m) {
                    if (m.emissive) m.emissive.multiplyScalar(0.2);
                });
            });
        }

        // glTF 动画（若存在则自动播放第一段）；切换模型时先清空旧 mixer
        v.mixer = null;
        if (obj && obj.animations && obj.animations.length && mesh && mesh.traverse) {
            try {
                v.mixer = new THREE.AnimationMixer(mesh);
                v.mixer.clipAction(obj.animations[0]).play();
            } catch (e) { /* 动画失败不影响静态渲染 */ }
        }

        // 自适应居中缩放
        const box = new THREE.Box3().setFromObject(mesh);
        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());
        const maxDim = Math.max(size.x, size.y, size.z);
        const s = maxDim > 0 ? 4 / maxDim : 1;
        mesh.position.sub(center.multiplyScalar(s));
        mesh.scale.set(s, s, s);

        // 关键：先把新模型加进场景，确认加入成功后再释放旧模型。
        // 这样即便后续步骤抛异常，旧模型仍留在场景里，绝不会变成"旧已删、新未加"的空场景（全黑）。
        const oldRoot = v._previousRoot;
        v.modelRoot = mesh;
        v.scene.add(mesh);
        // 若当前处于“关灯(unlit)”状态，新模型也按 unlit 渲染，保持切换前后一致
        if (!v.lightsOn) setUnlit(mesh, true);
        if (oldRoot && oldRoot !== mesh) {
            v.scene.remove(oldRoot);
            disposeObject(oldRoot);
        }
        v._previousRoot = null;

        if (_loadResolve) { const r = _loadResolve; _loadResolve = null; _loadReject = null; r(); }
    } catch (e) {
        console.error('[Model3D] onModelLoaded failed:', e);
        // 出错时保留上一个模型可见，并把错误向上抛出，避免静默黑屏
        if (_loadReject) { const rj = _loadReject; _loadResolve = null; _loadReject = null; rj(e); }
    }
}

// 切换文件时调用：清空旧模型、加载新模型进同一场景。返回 Promise，加载完成/失败时才 settle。
export async function loadModel(modelUrl, ext, textureDataJson) {
    if (!_viewer) throw new Error('viewer 未初始化（请先调用 initViewer）');

    try { _textureData = textureDataJson ? JSON.parse(textureDataJson) : {}; }
    catch (e) { _textureData = {}; }
    window.__textureData = _textureData;
    _viewer.ext = ext;
    // 按格式调光：PMX 压低补光防"偏白化"，其它格式恢复默认
    applyLightingForExt(ext);

    // 暂存当前正在显示的模型；new 模型在 onModelLoaded 成功加入场景后才释放它，
    // 确保任何加载/解析异常都不会让场景变成空的（黑屏）。
    _viewer._previousRoot = _viewer.modelRoot;

    // ── 选择加载器 ──
    let loader;
    if (ext === '.stl') {
        loader = new THREE.STLLoader();
    } else if (ext === '.obj') {
        loader = new THREE.OBJLoader();
        // 应用 MTL 材质
        const mtlData = _textureData['__mtl__'];
        if (mtlData && typeof THREE.MTLLoader !== 'undefined') {
            try {
                const mtlResourcePath = modelUrl.substring(0, modelUrl.lastIndexOf('/') + 1);
                const mtlLoader = new THREE.MTLLoader();
                mtlLoader.setResourcePath(mtlResourcePath);
                const materials = mtlLoader.parse(mtlData);
                materials.preload();
                // DDS flipY 修正
                const hasDds = Object.keys(_textureData).some(function (k) {
                    return k.toLowerCase().endsWith('.dds');
                });
                if (hasDds) {
                    Object.keys(materials.materials).forEach(function (matName) {
                        const mat = materials.materials[matName];
                        ['map', 'specularMap', 'normalMap', 'bumpMap'].forEach(function (prop) {
                            if (mat[prop] && mat[prop].isTexture) mat[prop].flipY = false;
                        });
                    });
                }
                // C4D 导出的 MTL 常设 d 0（透明度=0），导致模型完全透明不可见
                Object.keys(materials.materials).forEach(function (matName) {
                    const mat = materials.materials[matName];
                    if (mat.opacity !== undefined && mat.opacity < 0.01) {
                        mat.opacity = 1;
                        mat.transparent = false;
                    }
                });
                loader.setMaterials(materials);
            } catch (e) {
                console.warn('[Model3D] MTL parse error:', e);
            }
        }
    } else if (ext === '.fbx') {
        loader = new THREE.FBXLoader();
    } else if (ext === '.pmx') {
        loader = new THREE.MMDLoader();
    } else if (ext === '.glb' || ext === '.gltf' || ext === '.vrm') {
        loader = new THREE.GLTFLoader();
    } else {
        throw new Error('不支持的 3D 格式: ' + ext);
    }

    return new Promise(function (resolve, reject) {
        _loadResolve = resolve;
        _loadReject = reject;
        function onErr(err) {
            const msg = '模型加载失败: ' + ((err && err.message) || err);
            console.error('[Model3D] ' + msg);
            const rj = _loadReject;
            _loadResolve = null;
            _loadReject = null;
            if (rj) rj(new Error(msg));
        }

        if (ext === '.pmx') {
            // MMDLoader 需要按扩展名分流 loadPMD/loadPMX
            const pmxLoader = new THREE.MMDLoader();
            const resourcePath = modelUrl.substring(0, modelUrl.lastIndexOf('/') + 1);
            pmxLoader.load = function (url, onLoadCb, onProgress, onErrCb) {
                const e = url.indexOf('blob:') === 0 ? 'pmx' : this._extractExtension(url);
                if (e !== 'pmd' && e !== 'pmx') { onErrCb(new Error('Unknown extension: .' + e)); return; }
                this[e === 'pmd' ? 'loadPMD' : 'loadPMX'](url, function (data) {
                    onLoadCb(pmxLoader.meshBuilder.build(data, resourcePath, onProgress, onErrCb));
                }, onProgress, onErrCb);
            };
            pmxLoader.load(modelUrl, onModelLoaded, undefined, onErr);
        } else {
            loader.load(modelUrl, onModelLoaded, undefined, onErr);
        }
    });
}
