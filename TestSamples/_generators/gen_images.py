# -*- coding: utf-8 -*-
"""Generate image samples (jpg/jpeg/jfif/png/gif/bmp/webp/ico/tiff/tif/avif)."""
import os
import struct
from PIL import Image, ImageDraw

BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Image"
os.makedirs(BASE, exist_ok=True)

print("== Image samples ==")

def make_test_image(size=(400, 300), colors=None):
    """Create a test image with a gradient + shapes."""
    if colors is None:
        colors = [(255, 87, 51), (51, 122, 255), (46, 204, 113), (255, 193, 7), (155, 89, 182)]
    w, h = size
    img = Image.new("RGB", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(h - 1, 1)
        r = int(255 * (1 - t) + colors[0][0] * t)
        g = int(150 * (1 - t) + colors[1][1] * t)
        b = int(50 * (1 - t) + colors[2][2] * t)
        d.line([(0, y), (w, y)], fill=(r, g, b))
    # 画形状
    d.ellipse([w * 0.1, h * 0.15, w * 0.4, h * 0.55], fill=colors[0], outline=colors[4], width=4)
    d.rectangle([w * 0.55, h * 0.2, w * 0.85, h * 0.6], fill=colors[1], outline=colors[3], width=4)
    d.polygon([(w * 0.3, h * 0.75), (w * 0.5, h * 0.6), (w * 0.7, h * 0.75), (w * 0.5, h * 0.9)], fill=colors[2])
    d.text((w * 0.08, h * 0.05), "MauiMultimedia", fill=(255, 255, 255))
    return img

def w(img, name, **kw):
    path = os.path.join(BASE, name)
    img.save(path, **kw)
    print(f"  {name}")

# 基础测试图
img = make_test_image()

# 静态格式
w(img, "sample.jpg", quality=92)
w(img, "sample.jpeg", quality=85)
w(img, "sample.jfif", quality=80)          # JFIF 本质是 JPEG
w(img, "sample.png", optimize=True)
w(img, "sample.bmp")
w(img, "sample.webp", quality=90)
w(img, "sample.tiff", compression="tiff_lzw")
w(img, "sample.tif", compression="tiff_lzw")

# ICO（多尺寸）
ico_sizes = [(16, 16), (32, 32), (48, 48), (64, 64)]
ico = Image.new("RGBA", (64, 64))
d = ImageDraw.Draw(ico)
d.rounded_rectangle([2, 2, 62, 62], radius=10, fill=(51, 122, 255))
d.ellipse([18, 12, 46, 40], fill=(255, 255, 255))
d.polygon([(24, 46), (40, 46), (32, 58)], fill=(255, 87, 51))
ico.save(os.path.join(BASE, "sample.ico"), sizes=ico_sizes)
print("  sample.ico")

# 动画 GIF（多帧）
print("  sample.gif (animated)")
frames = []
for i in range(12):
    f = make_test_image()
    dd = ImageDraw.Draw(f)
    x = int(400 * (i / 11))
    dd.ellipse([x, 120, x + 60, 180], fill=(255, 255, 255))
    frames.append(f)
frames[0].save(os.path.join(BASE, "sample.gif"), save_all=True,
               append_images=frames[1:], duration=100, loop=0, optimize=True)

# AVIF（若 Pillow 支持）
try:
    img.save(os.path.join(BASE, "sample.avif"), quality=80)
    print("  sample.avif")
except Exception as e:
    print(f"  sample.avif -> SKIPPED ({e})")

# ── DDS（手写：未压缩 BGRA 头 + 像素数据） ──
print("  sample.dds (hand-crafted)")

def make_dds(path, w=64, h=64):
    # DDS_HEADER (124 bytes) + magic "DDS "
    header = bytearray(128)
    header[0:4] = b"DDS "
    # dwSize = 124
    struct.pack_into("<I", header, 4, 124)
    # dwFlags = DDSD_CAPS|DDSD_HEIGHT|DDSD_WIDTH|DDSD_PIXELFORMAT|DDSD_PITCH
    struct.pack_into("<I", header, 8, 0x1 | 0x2 | 0x4 | 0x1000 | 0x8)
    # dwHeight / dwWidth
    struct.pack_into("<I", header, 12, h)
    struct.pack_into("<I", header, 16, w)
    # dwPitchOrLinearSize = w*4
    struct.pack_into("<I", header, 20, w * 4)
    # dwDepth/dwMipMapCount = 0
    # dwCaps1 = DDSCAPS_TEXTURE
    struct.pack_into("<I", header, 108, 0x1000)
    # DDS_PIXELFORMAT (32 bytes) starts at offset 76：dwSize(76) + dwFlags(80) + dwFourCC(84)
    # + dwRGBBitCount(88) + R(92) + G(96) + B(100) + A(104)。
    # 注意不能从 72 起写（-4 错位会让样本不被 DdsDecoder / Windows 识别）。
    struct.pack_into("<I", header, 76, 32)    # dwSize = 32
    struct.pack_into("<I", header, 80, 0x41)  # dwFlags = DDPF_RGB|DDPF_ALPHAPIXELS
    struct.pack_into("<I", header, 84, 0)     # dwFourCC
    struct.pack_into("<I", header, 88, 32)    # dwRGBBitCount
    # BGRA masks: R=0x00ff0000 G=0x0000ff00 B=0x000000ff A=0xff000000
    struct.pack_into("<I", header, 92, 0x00ff0000)
    struct.pack_into("<I", header, 96, 0x0000ff00)
    struct.pack_into("<I", header, 100, 0x000000ff)
    struct.pack_into("<I", header, 104, 0xff000000)
    # 像素数据：BGRA 渐变
    px = bytearray()
    for y in range(h):
        for x in range(w):
            b = int(255 * x / (w - 1))
            g = int(255 * y / (h - 1))
            r = 255 - b
            px += bytes((b, g, r, 255))
    with open(path, "wb") as f:
        f.write(header)
        f.write(px)

make_dds(os.path.join(BASE, "sample.dds"))

print("Image samples done.")
