// Model3DPage.razor.js —— 3D 查看器（非 GLB：STL/OBJ）的 THREE.js 初始化（隔离模块）
// 注意：THREE 为全局对象（由宿主 index.html 引入），本模块仅消费它。
export function initThree(canvasId, modelUrl, ext) {
    const canvas = document.getElementById(canvasId);
    if (!canvas || typeof THREE === 'undefined') return;

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
