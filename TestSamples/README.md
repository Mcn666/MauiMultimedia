# TestSamples — 支持格式测试样本

本目录包含 MauiMultimedia 所有查看器支持格式的测试样本，用于验证查看器的实际解码/渲染能力。

## 目录结构与覆盖情况

### 📄 Text 查看器 — 22/22 ✅
| 格式 | 文件 |
|------|------|
| .txt | `Text/sample.txt` |
| .log | `Text/sample.log` |
| .md | `Text/sample.md` |
| .csv | `Text/sample.csv` |
| .xml | `Text/sample.xml` |
| .json | `Text/sample.json` |
| .yaml / .yml | `Text/sample.yaml` / `Text/sample.yml` |
| .html / .htm | `Text/sample.html` / `Text/sample.htm` |
| .css | `Text/sample.css` |
| .js | `Text/sample.js` |
| .py | `Text/sample.py` |
| .cs | `Text/sample.cs` |
| .sh | `Text/sample.sh` |
| .bat | `Text/sample.bat` |
| .ps1 | `Text/sample.ps1` |
| .ini / .cfg / .conf / .env | `Text/sample.ini` 等 |
| .gitignore | `Text/.gitignore` |

### 🌐 Html 查看器 — 4/4 ✅
| 格式 | 文件 |
|------|------|
| .html | `Html/sample.html` |
| .htm | `Html/sample.htm` |
| .mht | `Html/sample.mht` |
| .mhtml | `Html/sample.mhtml` |

### 🖼️ Image 查看器 — 11/11 ✅（TIFF 已移除，样本保留为负样本）
| 格式 | 文件 | 说明 |
|------|------|------|
| .jpg / .jpeg / .jfif | `Image/sample.jpg` 等 | JFIF 本质是 JPEG |
| .png | `Image/sample.png` | |
| .gif | `Image/sample.gif` | **动画 GIF**（12 帧循环） |
| .bmp | `Image/sample.bmp` | |
| .webp | `Image/sample.webp` | |
| .ico | `Image/sample.ico` | 多尺寸（16/32/48/64） |
| .svg | `Image/sample.svg` | 矢量渐变+图形，可解析宽高 |
| .avif | `Image/sample.avif` | 浏览器直出渲染（SkiaSharp 不解码，网格无缩略图） |
| .dds | `Image/sample.dds` | 手写未压缩 BGRA 头 |
| .tiff / .tif | `Image/sample.tiff` / `.tif` | ⚠️ **负样本**：SkiaSharp 无 TIFF 编解码器、浏览器不渲染，已从支持列表移除 |

### 🎬 Video 查看器 — 15/15 ✅（含真实设备测试结论 2026-07-31）
| 格式 | 文件 | 编码 | Android 实测 | Windows 实测 |
|------|------|------|:--------:|:--------:|
| .mp4 | `Video/sample.mp4` | H.264 | ✅ | ✅ |
| .m4v | `Video/sample.m4v` | H.264 | ✅ | ✅ |
| .3gp | `Video/sample.3gp` | H.264/3GP | ✅ | ✅ |
| .mov | `Video/sample.mov` | H.264/QuickTime | ✅ | ✅ |
| .webm | `Video/sample.webm` | VP8 | ✅（修复误判后） | ✅ |
| .mkv | `Video/sample.mkv` | H.264/Matroska | ❌ 提示转码 | ❌ 提示转码 |
| .avi | `Video/sample.avi` | MPEG-4 Part 2 | ❌ 提示转码 | ❌ 提示转码 |
| .mpg / .mpeg | `Video/sample.mpg` 等 | MPEG-2 PS | ❌ 提示转码 | ❌ 提示转码 |
| .ts | `Video/sample.ts` | **H.264** | ✅ mpegts.js | ✅ mpegts.js |
| .mts / .m2ts | `Video/sample.mts` 等 | H.264 + M2TS | ✅ mpegts.js | ✅ mpegts.js |
| .flv | `Video/sample.flv` | **H.264** | ✅ mpegts.js | ✅ mpegts.js |
| .ogv | `Video/sample.ogv` | Theora | ❌ 无法解码（有提示） | ❌ 无法解码（WebView2 禁用 Theora，有提示） |
| .wmv | `Video/sample.wmv` | WMV2 | ❌ 提示转码 | ❌ 提示转码 |

> 设备实测发现并修复的 4 个问题：
> 1. **webm 误判为 MKV**：`sniffMagic` 把 EBML 头一律判 'mkv'，而 WebM 同属 EBML 家族但浏览器原生支持 → 用 DocType（`webm`/`matroska`）区分
> 2. **mts/m2ts 无法播放**：M2TS 是 192 字节包（4 字节 TP_extra_header + 188 字节 TS 包），同步字节 0x47 在偏移 4，`sniffMagic` 只查偏移 0 → 补充 `head[4] === 0x47`
> 3. **ts/flv 样本编码错误**：原样本用 MPEG-2/FLV1，mpegts.js 只支持 H.264 → 已用 H.264 重新生成
> 4. **wmv/ogv 静默黑屏**：原生 `<video>` 解码失败无提示 → 加 ASF 魔数识别直接提示转码 + 原生路径监听 error 事件
>
> **验证结果**：Android + Windows 双端实测，webm/ts/flv/mts/m2ts 修复后全部正常播放；avi/mkv/mpg/mpeg/wmv 正确提示转码；ogv 两端均无法解码（WebView 环境 codec 限制），有友好提示。

### 🧊 Model3D 查看器 — 4/7 ✅（3 缺失）
| 格式 | 文件 | 说明 |
|------|------|------|
| .glb | `Model3D/sample.glb` | 手写 glTF 2.0 容器（四棱锥） |
| .gltf | `Model3D/sample.gltf` + `sample.bin` | ASCII glTF |
| .obj | `Model3D/sample.obj` + `sample.mtl` | Wavefront |
| .stl | `Model3D/sample.stl` | ASCII STL |
| .fbx | — ❌ | 专有二进制格式，无免费生成器 |
| .pmx | — ❌ | MMD 专有格式，需专用工具（如 Blender 插件） |
| .vrm | — ❌ | VRoid 专有格式，需 VRoid Studio |

### 📦 Archive 查看器 — 7/7 ✅（rar 缺失 + bz2/xz/zst 已移除）
| 格式 | 文件 | 说明 |
|------|------|------|
| .zip | `Archive/sample.zip` | Deflate |
| .tar | `Archive/sample.tar` | 未压缩 |
| .gz | `Archive/sample.gz` | 单文件 gzip |
| .tgz / .tar.gz | `Archive/sample.tgz` / `.tar.gz` | tar+gzip |
| .7z | `Archive/sample.7z` | LZMA2 |
| .rar | — ❌ | **专有格式，WinRAR 付费，无法免费生成** |
| .bz2 / .xz / .zst | `Archive/sample.bz2` 等 | ⚠️ **负样本**：单文件压缩流，`ArchiveFactory.OpenArchive` 不支持（只支持 Zip/Rar/Tar/GZip/7Zip），已从支持列表移除 |

## 缺失/移除格式汇总

| 格式 | 状态 | 原因 |
|------|------|------|
| .rar | 缺失 | RAR 是专有格式，创建工具需授权（用 WinRAR 手动创建） |
| .fbx | 缺失 | Autodesk 专有格式（用 Blender 导出） |
| .pmx | 缺失 | MMD 模型格式（用 Blender + MMD 插件导出） |
| .vrm | 缺失 | VRoid 专有格式（用 VRoid Studio 导出） |
| .tiff / .tif | **已移除** | SkiaSharp 无 TIFF 编解码器、浏览器不渲染 → 声明了也打不开 |
| .bz2 / .xz / .zst | **已移除** | SharpCompress OpenArchive 不支持单文件压缩流 |

## 重新生成

样本由 `_generators/` 下的 Python 脚本生成，依赖：

```bash
pip install Pillow imageio-ffmpeg py7zr zstandard
python _generators/gen_text.py      # 文本/HTML/3D 文本
python _generators/gen_images.py    # 图片（含 GIF 动画、手写 DDS）
python _generators/gen_videos.py    # 视频（需 ffmpeg）
python _generators/gen_archives.py  # 压缩包
```

> 注：`sample.glb` 和 `sample.bin` 由一次性脚本生成，未留脚本；如需重新生成可参考下方格式说明（glTF 2.0 容器规范 + 四棱锥几何数据）。

## 验证建议

1. 把 `TestSamples` 目录复制到设备存储，从 Home 页浏览
2. 每个格式打开一次，确认解码/渲染正常
3. `sample.gif`（动画）应看到 12 帧循环，网格中为静态首帧
4. `sample.m2ts` / `sample.ts` 应走 mpegts.js 软解路径
5. `sample.dds` 应显示为 64×64 渐变图
6. `sample.glb` 应显示四棱锥
