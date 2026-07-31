# -*- coding: utf-8 -*-
"""Generate text-based sample files for the Text viewer."""
import os

BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Text"
os.makedirs(BASE, exist_ok=True)

def w(name, content):
    path = os.path.join(BASE, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"  {name}")

print("== Text samples ==")

w("sample.txt", """MauiMultimedia 文本查看器测试样本

这是一段用于验证文本查看器显示功能的中文内容。
包含：
- 中文 UTF-8 编码
- 多行文本
- 特殊字符：@#$%^&*()_+-=[]{}|;:'",.<>?/\\
- Tab 缩进：\t示例

The quick brown fox jumps over the lazy dog.
Line 1
Line 2
Line 3
""")

w("sample.log", """2026-07-30 10:00:01 [INFO] 应用启动
2026-07-30 10:00:02 [INFO] 文件系统服务初始化完成
2026-07-30 10:00:05 [DEBUG] 加载查看器插件: Image, Video, Text
2026-07-30 10:00:08 [WARN] 磁盘空间不足 15%
2026-07-30 10:00:12 [ERROR] 打开文件失败: /tmp/missing.txt (No such file)
2026-07-30 10:00:15 [INFO] 用户切换主题为 dark
2026-07-30 10:01:02 [INFO] 导出报告完成
""")

w("sample.md", """# 测试样本 Markdown

## 二级标题

- 列表项 1
- 列表项 2
  - 嵌套项

**加粗文本** 和 *斜体文本* 以及 `行内代码`

```csharp
public void Hello() { Console.WriteLine("Hello"); }
```

> 引用块：这是一个引用

[链接示例](https://example.com)

---

| 列1 | 列2 |
|-----|-----|
| A   | B   |
""")

w("sample.csv", """姓名,年龄,城市,职业
张三,28,北京,工程师
李四,32,上海,设计师
王五,25,深圳,产品经理
赵六,41,杭州,数据分析师
""")

w("sample.xml", """<?xml version="1.0" encoding="UTF-8"?>
<project name="MauiMultimedia" version="1.0">
  <description>多媒体文件浏览器测试样本</description>
  <viewers>
    <viewer id="image" display="图片查看器" enabled="true"/>
    <viewer id="video" display="视频播放器" enabled="true"/>
    <viewer id="text" display="文本查看器" enabled="true"/>
  </viewers>
  <config>
    <theme>dark</theme>
    <cacheSize>384</cacheSize>
  </config>
</project>
""")

w("sample.json", """{
  "app": "MauiMultimedia",
  "version": "1.0.0",
  "viewers": ["Image", "Video", "Text", "Html", "Model3D", "Archive"],
  "settings": {
    "theme": "dark",
    "showHiddenFiles": false,
    "cacheBytes": 402653184
  },
  "items": [
    { "name": "sample.png", "size": 12345, "type": "image" },
    { "name": "sample.mp4", "size": 2345678, "type": "video" }
  ]
}
""")

w("sample.yaml", """app: MauiMultimedia
version: 1.0.0
viewers:
  - Image
  - Video
  - Text
settings:
  theme: dark
  showHiddenFiles: false
  cacheBytes: 402653184
items:
  - name: sample.png
    size: 12345
    type: image
""")

w("sample.yml", """# YAML 别名测试样本
defaults: &defaults
  adapter: postgres
  host: localhost

development:
  <<: *defaults
  database: dev_db

test:
  <<: *defaults
  database: test_db
""")

w("sample.html", """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<title>HTML 测试样本</title>
</head>
<body>
<h1>标题 1</h1>
<p>这是一段 <b>加粗</b> 文本，包含 <a href="https://example.com">链接</a>。</p>
<ul>
  <li>列表项 A</li>
  <li>列表项 B</li>
</ul>
</body>
</html>
""")

w("sample.htm", """<html>
<head><title>HTM 测试样本</title></head>
<body>
<h2>传统 HTM 扩展名</h2>
<p>Old-school HTML file.</p>
</body>
</html>
""")

w("sample.css", """/** CSS 测试样本 */
:root {
  --bg-primary: #1e1e1e;
  --text-primary: #d4d4d4;
  --accent: #378add;
}

.viewer-page {
  display: flex;
  flex-direction: column;
  height: 100vh;
  background: var(--bg-primary);
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
}

.btn:hover { background: var(--accent); }
""")

w("sample.js", """// JavaScript 测试样本
const app = {
  name: 'MauiMultimedia',
  version: '1.0.0',
  init() {
    console.log(`Starting ${this.name} v${this.version}`);
    document.querySelectorAll('.viewer-page').forEach(page => {
      page.addEventListener('click', this.handleClick);
    });
  },
  handleClick(e) {
    const target = e.currentTarget;
    console.log('Clicked:', target.id);
  }
};

app.init();
""")

w("sample.py", """# Python 测试样本
import os
from pathlib import Path


def scan_files(root: str, extensions: set[str]) -> list[Path]:
    \"\"\"递归扫描目录，按扩展名过滤文件。\"\"\"
    results = []
    for dirpath, _, filenames in os.walk(root):
        for name in filenames:
            if Path(name).suffix.lower() in extensions:
                results.append(Path(dirpath) / name)
    return results


if __name__ == "__main__":
    exts = {".txt", ".md", ".json"}
    files = scan_files(".", exts)
    print(f"Found {len(files)} matching files")
""")

w("sample.cs", """// C# 测试样本
using System;
using System.Collections.Generic;
using System.Linq;

namespace MauiMultimedia.Samples
{
    public class SampleService
    {
        private readonly Dictionary<string, int> _counts = new();

        public void Register(string key)
        {
            _counts[key] = _counts.GetValueOrDefault(key) + 1;
        }

        public IEnumerable<string> TopKeys(int n) =>
            _counts.OrderByDescending(kv => kv.Value).Take(n).Select(kv => kv.Key);

        public static string Hello(string name) => $"Hello, {name}!";
    }
}
""")

w("sample.sh", """#!/bin/bash
# Shell 测试样本
set -euo pipefail

APP_NAME="MauiMultimedia"
VERSION="1.0.0"

echo "Starting $APP_NAME v$VERSION"

for file in "$@"; do
    if [ -f "$file" ]; then
        echo "Processing: $file"
    else
        echo "Skipping missing file: $file" >&2
    fi
done

exit 0
""")

w("sample.bat", """@echo off
rem Batch 测试样本
set APP_NAME=MauiMultimedia
set VERSION=1.0.0

echo Starting %APP_NAME% v%VERSION%

for %%f in (%*) do (
    if exist "%%f" (
        echo Processing: %%f
    ) else (
        echo Skipping missing file: %%f 1>&2
    )
)

exit /b 0
""")

w("sample.ps1", """# PowerShell 测试样本
$AppName = 'MauiMultimedia'
$Version = '1.0.0'

Write-Host "Starting $AppName v$Version"

Get-ChildItem -File | Where-Object {
    $_.Extension -in '.txt', '.md', '.json'
} | ForEach-Object {
    Write-Host "Processing: $($_.FullName)"
}
""")

w("sample.ini", """; INI 测试样本
[app]
name=MauiMultimedia
version=1.0.0
theme=dark

[network]
timeout=30
retries=3

[storage]
cache_dir=/data/local/cache
max_bytes=402653184
""")

w("sample.cfg", """# CFG 测试样本
app_name = MauiMultimedia
version = 1.0.0

[display]
width = 1080
height = 1920
density = 2.75

[performance]
max_concurrent_decodes = 2
cache_budget_mb = 384
""")

w("sample.conf", """# Apache-style conf 测试样本
ServerRoot "/etc/mauimm"

Listen 8080

<VirtualHost *:8080>
    ServerName localhost
    DocumentRoot "/var/www/mauimm"
    DirectoryIndex index.html
</VirtualHost>

KeepAlive On
MaxKeepAliveRequests 100
""")

w("sample.env", """# 环境变量测试样本
APP_NAME=MauiMultimedia
APP_VERSION=1.0.0
LOG_LEVEL=debug
STORAGE_ROOT=/data/media/0
CACHE_BYTES=402653184
VIEWER_ENABLED_IMAGE=true
VIEWER_ENABLED_VIDEO=true
VIEWER_ENABLED_TEXT=true
""")

w(".gitignore", """# gitignore 测试样本
# 构建产物
bin/
obj/
dist/
build/

# 日志与缓存
*.log
*.tmp
.cache/
__pycache__/

# IDE
.vscode/
.idea/
*.user

# 敏感文件
*.key
*.pem
.env.local
""")

print("Text samples done.\n")

# ── Html 查看器样本（额外两种） ──
print("== Html samples ==")
HTML_BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Html"
os.makedirs(HTML_BASE, exist_ok=True)

w_html = lambda n, c: (open(os.path.join(HTML_BASE, n), "w", encoding="utf-8").write(c), print(f"  {n}"))

w_html("sample.html", """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>HTML 网页查看器测试</title>
<style>
body { font-family: sans-serif; margin: 2rem; }
h1 { color: #378add; }
</style>
</head>
<body>
<h1>网页查看器测试样本</h1>
<p>这是由 <b>MauiMultimedia</b> 网页查看器渲染的页面。</p>
<img src="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==" alt="1px">
</body>
</html>
""")

w_html("sample.htm", """<html>
<head><title>HTM 样本</title></head>
<body><h2>HTM 扩展名页面</h2><p>与 .html 内容相同的传统扩展名。</p></body>
</html>
""")

w_html("sample.mht", """From: <saved by MauiMultimedia>
Snapshot-Content-Location: https://example.com/page
Subject: MHT 测试样本
MIME-Version: 1.0
Content-Type: multipart/related;
	boundary="----=_NextPart_000_0001_01D9A1B0.00000000"

This is a multi-part message in MIME format.

------=_NextPart_000_0001_01D9A1B0.00000000
Content-Type: text/html;
	charset="utf-8"
Content-Transfer-Encoding: 8bit

<html><head><title>MHT 测试样本</title></head>
<body><h1>MIME HTML 归档</h1><p>单文件网页归档格式。</p></body></html>

------=_NextPart_000_0001_01D9A1B0.00000000--
""")

w_html("sample.mhtml", """From: <saved by MauiMultimedia>
Snapshot-Content-Location: https://example.com/page2
Subject: MHTML 测试样本
MIME-Version: 1.0
Content-Type: multipart/related;
	boundary="----=_NextPart_000_0002_01D9A1B1.00000000"

------=_NextPart_000_0002_01D9A1B1.00000000
Content-Type: text/html;
	charset="utf-8"

<html><head><title>MHTML 测试样本</title></head>
<body><h1>MHTML 归档页面</h1><p>与 .mht 相同的格式，不同扩展名。</p></body></html>

------=_NextPart_000_0002_01D9A1B1.00000000--
""")

print("Html samples done.\n")

# ── 3D 文本格式（gltf / obj / stl） ──
print("== Model3D text samples ==")
M3D_BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Model3D"
os.makedirs(M3D_BASE, exist_ok=True)

def w3(name, content):
    path = os.path.join(M3D_BASE, name)
    with open(path, "w", encoding="utf-8") as f:
        f.write(content)
    print(f"  {name}")

# 简单三角锥 glTF 2.0 (ASCII JSON)
w3("sample.gltf", """{
  "asset": {
    "version": "2.0",
    "generator": "MauiMultimedia Sample"
  },
  "scene": 0,
  "scenes": [
    { "nodes": [0] }
  ],
  "nodes": [
    { "mesh": 0, "name": "Pyramid" }
  ],
  "meshes": [
    {
      "primitives": [
        {
          "attributes": { "POSITION": 0, "NORMAL": 1 },
          "indices": 2
        }
      ]
    }
  ],
  "accessors": [
    { "bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3", "min": [-1,-1,-1], "max": [1,1,1] },
    { "bufferView": 1, "componentType": 5126, "count": 4, "type": "VEC3" },
    { "bufferView": 2, "componentType": 5123, "count": 12, "type": "SCALAR" }
  ],
  "bufferViews": [
    { "buffer": 0, "byteOffset": 0, "byteLength": 48 },
    { "buffer": 0, "byteOffset": 48, "byteLength": 48 },
    { "buffer": 0, "byteOffset": 96, "byteLength": 24 }
  ],
  "buffers": [
    { "byteLength": 120, "uri": "sample.bin" }
  ]
}
""")

w3("sample.obj", """# OBJ 测试样本 - 简单四棱锥
mtllib sample.mtl
o Pyramid

v 0.0 1.0 0.0
v -1.0 -1.0 1.0
v 1.0 -1.0 1.0
v 1.0 -1.0 -1.0
v -1.0 -1.0 -1.0

vn 0.0 0.4472 0.8944
vn 0.8944 0.4472 0.0
vn 0.0 0.4472 -0.8944
vn -0.8944 0.4472 0.0
vn 0.0 -1.0 0.0

f 1//1 2//1 3//1
f 1//2 3//2 4//2
f 1//3 4//3 5//3
f 1//4 5//4 2//4
f 2//5 5//5 4//5 3//5
""")

w3("sample.mtl", """# MTL 材质测试样本
newmtl PyramidMat
Ka 0.2 0.2 0.2
Kd 0.8 0.4 0.1
Ks 0.5 0.5 0.5
Ns 32.0
d 1.0
illum 2
""")

w3("sample.stl", """solid MauiMultimediaSample
  facet normal 0.0 0.0 1.0
    outer loop
      vertex 0.0 0.0 0.0
      vertex 10.0 0.0 0.0
      vertex 0.0 10.0 0.0
    endloop
  endfacet
  facet normal 0.0 0.0 1.0
    outer loop
      vertex 10.0 0.0 0.0
      vertex 10.0 10.0 0.0
      vertex 0.0 10.0 0.0
    endloop
  endfacet
endsolid MauiMultimediaSample
""")

print("Model3D text samples done.")
