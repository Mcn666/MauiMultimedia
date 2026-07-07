# maui-blazor-webview-es2019

让 **.NET 9 / .NET 10 的 MAUI Blazor Hybrid** 应用能在**旧版 Android / iOS WebView** 上正常运行。

## 问题

.NET 9 起，官方 `_framework/blazor.webview.js` 大量使用 ES2020 语法（`?.` / `??` / `??=`）。
旧设备 WebView 解析即报 `Uncaught SyntaxError: Unexpected token .`：

- Android：系统 WebView < Chrome 80（约 Android 10 及更早；部分国产 ROM 长期不更新）
- iOS：WKWebView / Safari < 16.4

这是**解析期错误，polyfill 救不了**。微软官方立场是不修复（建议用户自己升级 WebView），
因此社区需要自行兼容。详见 `dotnet/aspnetcore#53699` 与 `dotnet/maui#27327`。

## 方案

构建期用 **esbuild** 把官方 `blazor.webview.js` 仅做语法降级（`transform` → `es2018`），
并前置一段极小运行时垫片（`Object.hasOwn` / `Promise.withResolvers` / `Array.at` 等）。

**相比社区的两种主流做法，本方案更省维护：**

| 方案 | 是否自动跟随 .NET 更新 |
|---|---|
| Eilon 的 `blazor.webview.es2019.js`（静态 gist） | ❌ 手动，不跟踪补丁 |
| Bit.BlazorES2019（NuGet 包） | ❌ 等作者发版 |
| **本工具（构建期从官方包重新生成）** | ✅ 每次构建自动跟随 |

## 在任何 MAUI Blazor 项目里使用

### 最简单：双击 / 拖放（Windows .bat）

把整个 `maui-blazor-webview-es2019/` 目录放进你的项目仓库，或保留在任意共享位置。然后：

- **双击** `install.bat` → 自动接入到它的**父目录**（典型用法：把工具包放进 `某项目/maui-blazor-webview-es2019/` 后，进该目录双击 `install.bat` 即可）；
- 或把**目标项目文件夹直接拖到 `install.bat` 上** → 自动接入被拖的项目。

运行后会提示「是否立即构建」，输入 `Y` 即自动 `dotnet build`；输入别的则只完成文件接入。

> `.bat` 只是个薄包装器：内部自动探测 node（优先 PATH，其次 常见安装位置、nvm），再把目标项目路径传给 `install.mjs`（核心逻辑全在 `install.mjs`）。若仍找不到 node 才会报错。

### 命令行（跨平台）

把整个 `maui-blazor-webview-es2019/` 目录放进你的项目仓库（或保留在任意共享位置），然后：

```bash
cd 你的项目根目录
node /path/to/maui-blazor-webview-es2019/install.mjs
dotnet build
```

`install.mjs` 会自动完成：

1. 找到主 `.csproj`（评分优先：含 `<UseMaui>` / `Microsoft.Maui` / `BlazorWebView`）
2. 复制工具包到 `你的项目/maui-blazor-webview-es2019/`
3. 在主 `.csproj` 末尾插入 `<Import>`（**相对路径按主项目位置自动算好**，无需手算）
4. 把 host 页里的官方 `<script src="_framework/blazor.webview.js">` 替换为兼容脚本
5. `npm install`（仅首次）→ 立即生成 `wwwroot/scripts/blazor.webview.improved.js`

你只需要再 `dotnet build` 即可。每次 `.NET` 升级后重建，产物自动从新版本官方文件重新生成。

**参数：**

- `node install.mjs [项目根目录]` —— 不传则默认当前目录
- `node install.mjs --no-build` —— 仅做文件接入，跳过 `npm install` / 转译（无网络时先接好，之后再 build）

已接入过的项目再跑 `install.mjs` 是安全的：工具包覆盖更新、`.csproj` 的 `<Import>` 与 host 页脚本若已配置则自动跳过。

## 手动方式（可选）

若你想自己控制每一步：

1. 复制 `maui-blazor-webview-es2019/` 进项目。
2. 主 `.csproj` 末尾加 `<Import Project="..\maui-blazor-webview-es2019\transpile-blazor.targets" />`（路径按实际存放位置调整）。
3. `cd maui-blazor-webview-es2019 && npm install`（仅首次）。
4. host 页把官方脚本换成 `<script src="scripts/blazor.webview.improved.js" autostart="false"></script>`。
5. 构建。

## 单独转译（transpile-blazor.mjs）

`install.mjs` 内部就是调用它完成语法降级。你也可以脱离 `install.mjs` 单独使用，比如 CI 里手动触发、或调试转译结果：

```bash
# 从 NuGet 缓存自动取最高版本官方 blazor.webview.js，输出到指定路径
node transpile-blazor.mjs --out wwwroot/scripts/blazor.webview.improved.js

# 指定输入源（默认自动取官方包，无需 --in）
node transpile-blazor.mjs --in /path/to/blazor.webview.js --out wwwroot/scripts/blazor.webview.improved.js
```

参数：
- `--out <path>`：**必填**，输出文件路径（省略时默认写到当前目录的 `blazor.webview.improved.js`）。
- `--in <src>`：可选，输入源 js；省略时自动在 NuGet 缓存里取 `microsoft.aspnetcore.components.webview` 版本号最高的官方文件。
- 也支持 `--out=` / `--in=` 写法。

转译用 esbuild `transform` 仅做语法降级到 `es2018`（不重新打包），并在文件头前置一段极小运行时垫片（`Object.hasOwn` / `Promise.withResolvers` / `Array.at` 等，仅当全局缺失时才定义）。

## 可选：UA 条件加载（新设备吃新特性、旧设备兜底）

若想让现代设备仍用官方最新文件（含 .NET 9/10 的静态资源优化、重连改进等），
可在 `index.html` 末尾用一小段脚本按 UA 动态选择：

```html
<script>
  function needsEs2019() {
    var m = navigator.userAgent.match(/Android\s([0-9.]*)/);
    if (m && parseFloat(m[1]) < 11) return true;           // Android < 11
    var i = navigator.userAgent.match(/OS (\d+)_/);
    if (/iPhone|iPad/.test(navigator.userAgent) && i && parseInt(i[1],10) < 16) return true; // iOS < 16
    return false;
  }
  var s = document.createElement('script');
  s.setAttribute('autostart', 'false');
  s.src = needsEs2019() ? 'scripts/blazor.webview.improved.js' : '_framework/blazor.webview.js';
  document.head.appendChild(s);
</script>
```

## CI / 无 Node 环境

- 构建目标在**找不到 node** 时会自动跳过，使用已提交的 `blazor.webview.improved.js` 兜底，不阻断构建。
- 也可用 `NodeToolPath` 属性显式指定 node 路径（如 CI 镜像里的固定路径）。
- 若转译失败且输出文件不存在，脚本以退出码 1 暴露，使构建失败，避免发布破损文件。

## 让 dotnet build 自动重新生成（而非只用兜底）

接入时 `install.bat` 已经生成 `blazor.webview.improved.js` 并写入项目（`wwwroot/scripts/`），`dotnet build` 即使找不到 node 也能用这份兜底文件（构建不会失败）。

若希望**每次 `dotnet build` 都从官方包重新生成**（这是本方案「.NET 升级自动跟随」的卖点），需要让构建进程能找到 node：

- 推荐：安装**系统级 Node.js**（https://nodejs.org/，安装时勾选 *Add to PATH*，装完重开 IDE/终端）；
- 或构建时显式指定路径（换成你机器上的 node）：
  ```bash
  dotnet build -p:NodeToolPath="C:\Users\69562\.workbuddy\binaries\node\versions\22.22.2\node.exe"
  ```

> 注意：`install.bat` 能自动找到 WorkBuddy 托管版 node，但 `dotnet build` 的 MSBuild 进程默认只认 PATH 里的 `node`，因此两者检测范围不同。要彻底省心，建议装一份系统级 Node.js。

## 验证

转译产物应满足：`node --check` 通过，且 `?.` / `??` 语法残留为 0。
终极验证请在真机/模拟器的旧 WebView（Android API 28~30 / iOS < 16.4）实测一次。

> **已验证环境**：本仓库已在 **.NET 10 / MAUI** 上实测通过——MAUI Blazor Hybrid 默认示例 四个目标框架
> （`net10.0-android` / `net10.0-ios` / `net10.0-maccatalyst` / `net10.0-windows`）均构建成功，
> 且 `blazor.webview.improved.js` 已确认打进 Android 包的 `assets/wwwroot/scripts/`。
