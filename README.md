# MauiMultimedia

> 基于 **.NET 10 MAUI + Blazor Hybrid** 的跨平台本地多媒体浏览器与文件管理器。

MauiMultimedia 将「文件浏览器」与「多种格式查看器」整合为一个跨平台应用。所有查看器在应用内嵌的 WebView 中渲染，并通过反射自动发现、零手工注册——新增一种格式支持只需新建一个 Viewer 项目并引用 Core，无需改动宿主。

## 功能特性

- **文件浏览器**：快速访问 / 存储位置侧边栏、地址栏、排序栏、网格 / 列表双视图；纵向滚动锁定在内容区，三块区域互不重叠。
- **多格式查看器**：图片、视频、3D 模型、网页、文本、压缩包等（详见下方「支持的格式」）。
- **快照生成**：任意文件可通过 `ISnapshotProvider` 生成预览缩略图。
- **主题**：内置亮色 / 暗色主题，跟随系统或手动切换。

## 支持的格式

| 查看器模块 | 负责类型 | 扩展名 |
| --- | --- | --- |
| `Image` | 图片 | `.jpg .jpeg .jfif .png .gif .bmp .webp .ico .svg .avif .dds` |
| `Video` | 视频（WebView 渲染） | `.mp4 .webm .mkv .mov .avi .wmv .flv .m4v .3gp .ogv .mpg .mpeg .ts .mts .m2ts` |
| `Video.Native` | 视频（平台原生控件） | 同 `Video` 主流容器 |
| `Text` | 文本 / 代码 | `.txt .log .md .csv .xml .json .yaml .yml .html .htm .css .js .py .cs .sh .bat .ps1 .ini .cfg .conf .env .gitignore` |
| `Html` | 网页 | `.html .htm .mht .mhtml` |
| `Model3D` | 3D 模型 | `.glb .gltf .stl .obj .fbx .pmx .vrm`（`fbx` 等经转换后加载） |
| `Archive` | 压缩包 | `.zip .tar .gz .tgz .tar.gz .rar .7z` |
| `Shared` | 共享基础设施（非查看器） | — |

> 各查看器的扩展名由 `Viewers/*/XxxConstants.cs`（如 `ImageConstants.cs`、`ArchiveConstants.cs`）集中维护，Provider / Viewer / Page 三处共用同一列表，新增格式只需改一个文件。

### 视频播放能力（WebView 实际行为）

| 能力 | 格式 | 机制 |
| --- | --- | --- |
| ✅ 原生播放 | `.mp4 .webm .mov .m4v .3gp` | WebView 内建解码器 |
| ✅ 软解播放 | `.ts .mts .m2ts .flv` | `mpegts.js`（MSE）软解，H.264 编码 |
| ⚠️ 提示转码 | `.mkv .avi .wmv .mpg .mpeg .ogv` | 浏览器不支持该容器/编码，显示转码提示而非黑屏 |

> 路由（扩展名 → 哪个查看器）由各查看器的 `IFileViewer.CanHandle` 拥有（内部基于 `XxxConstants.Exts`）；MIME（扩展名 → HTTP Content-Type）由 `Core/MimeTypes.cs` 标准表统一提供。

## 架构

三层结构，依赖方向干净无环：

```
Core  (纯 net10.0 类库：契约 / 模型 / 工具，无平台代码)
  ▲
  │ 引用
Shell (MAUI + Blazor Hybrid Exe 宿主：文件浏览器 UI + FileServer)
  ▲
  │ 反射加载
Viewers (特性模块：Image / Video / Model3D / Html / Text / Archive …)
```

- **Core**：纯 `net10.0` 类库，不含任何平台代码（无 `#if ANDROID` 等）。定义所有共享契约（`IFileViewer` / `IItemPresenter` / `ISnapshotProvider` / `IFileServerService` 等）。
- **Shell**：MAUI + Blazor Hybrid 可执行宿主，承载文件浏览器 UI（Home 页面）、别名、浏览状态，并通过内置的 `FileServerService` 向 WebView 查看器提供文件流（视频 / 3D 模型 / 大图等）。
- **Viewers**：各自独立的特性模块，引用 Core、实现其契约，**不引用 Shell**。

**自动化注册**：每个 Viewer 项目在编译期由 csproj 生成 `viewer_assemblies.txt` 资源；Shell 启动时通过 `ViewerAutoRegistration` 反射强制加载这些程序集，发现其中的 `IFileViewer` / `IItemPresenter` / `ISnapshotProvider` 实现——**新增查看器无需改动 Shell 注册代码**。

## 平台支持

| 平台 | 最低版本 |
| --- | --- |
| Android | API 24 (Android 7.0) |
| iOS | 15.0 |
| MacCatalyst | 15.0 |
| Windows | 10.0.17763.0 |

## 构建与运行

### 前置条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 对应平台的 MAUI 工作负载：
  - 全平台：`dotnet workload install maui`
  - Windows 需使用 Visual Studio 2022+（含 MAUI 工作负载）
- 平台特定依赖：Android SDK、Xcode（iOS / macOS）、Windows App SDK。

### 构建示例

```bash
# 还原解决方案
dotnet restore MauiMultimedia.slnx

# 构建 Android
dotnet build Shell/MauiMultimedia.Shell.csproj -f net10.0-android

# 构建 Windows
dotnet build Shell/MauiMultimedia.Shell.csproj -f net10.0-windows10.0.19041.0

# 运行（需对应平台环境）
dotnet run --project Shell/MauiMultimedia.Shell.csproj -f net10.0-android
```

> 也可以在 Visual Studio / VS Code 中打开 `MauiMultimedia.slnx` 进行构建与调试。

## 项目结构

```
MauiMultimedia/
├── Core/                 # 共享契约与工具（纯 net10.0）
├── Shell/                # MAUI + Blazor Hybrid 宿主、文件浏览器、FileServer
├── Viewers/
│   ├── Shared/           # Viewer 共享基础设施
│   ├── Image/            # 图片查看器
│   ├── Video/            # 视频查看器（WebView 渲染）
│   ├── Video.Native/     # 视频查看器（平台原生控件）
│   ├── Model3D/          # 3D 模型查看器（three.js / MMDLoader）
│   ├── Html/             # 网页查看器
│   ├── Text/             # 文本 / 代码查看器
│   └── Archive/          # 压缩包查看器
├── Tests/                # 单元测试
├── MauiMultimedia.slnx   # 解决方案
├── LICENSE               # MIT 许可证
└── README.md
```

## 第三方依赖

- **.NET MAUI / Blazor Hybrid** — 应用框架与混合渲染。
- **three.js**（r13x, MIT）及 **MMDLoader** — `Model3D` 查看器的 WebGL 渲染与 PMX / MMD 支持。
- 其它 .NET 社区库（如 CommunityToolkit 等），许可证随各 NuGet 包声明。

## 许可证

本项目以 [MIT 许可证](./LICENSE) 发布。详见 [LICENSE](./LICENSE) 文件。

---

Copyright © 2026 MauiMultimedia Contributors
