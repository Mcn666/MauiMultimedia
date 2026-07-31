# -*- coding: utf-8 -*-
"""Generate video samples using ffmpeg (image2 demuxer, no lavfi needed)."""
import os
import subprocess
import shutil
import tempfile
from PIL import Image, ImageDraw
import imageio_ffmpeg

FFMPEG = imageio_ffmpeg.get_ffmpeg_exe()
BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Video"
FRAME_DIR = os.path.join(BASE, "_frames")
W, H, FPS, SECS = 320, 240, 15, 2

os.makedirs(BASE, exist_ok=True)

# ── 生成 30 帧 PNG（2 秒） ──
print("Generating frames...")
os.makedirs(FRAME_DIR, exist_ok=True)
for i in range(FPS * SECS):
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    t = i / (FPS * SECS)
    # 背景渐变 + 移动方块 + 帧号
    for y in range(H):
        c = (int(40 + 60 * t), int(120 * (1 - t) + 100 * t), int(170 - 80 * t))
        d.line([(0, y), (W, y)], fill=c)
    x = int((W - 60) * t)
    d.rectangle([x, H // 2 - 30, x + 60, H // 2 + 30], fill=(255, 200, 50), outline=(255, 255, 255))
    d.text((10, 10), f"frame {i:03d}", fill=(255, 255, 255))
    d.text((W - 110, H - 25), "MauiMultimedia", fill=(255, 255, 255))
    img.save(os.path.join(FRAME_DIR, f"f{i:03d}.png"))

def gen(name, extra=(), desc=""):
    out = os.path.join(BASE, name)
    cmd = [FFMPEG, "-y",
           "-framerate", str(FPS), "-i", os.path.join(FRAME_DIR, "f%03d.png"),
           "-t", str(SECS),
           "-c:v", "libx264", "-preset", "ultrafast", "-crf", "28",
           "-pix_fmt", "yuv420p", "-an"]
    cmd += list(extra)
    cmd += [out]
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=180)
        if r.returncode == 0 and os.path.exists(out) and os.path.getsize(out) > 0:
            print(f"  {name} [{os.path.getsize(out)//1024}KB] {desc}")
        else:
            print(f"  {name} -> FAILED: {r.stderr[-200:]}")
    except Exception as e:
        print(f"  {name} -> ERROR: {e}")

print("== Video samples ==")

gen("sample.mp4", desc="H.264 MP4")
gen("sample.m4v", desc="H.264 m4v")
gen("sample.3gp", extra=["-f", "3gp"], desc="3GP")

# WebM (VP8)
gen("sample.webm", extra=["-c:v", "libvpx", "-b:v", "300k"], desc="VP8 WebM")

# Matroska
gen("sample.mkv", extra=["-f", "matroska"], desc="Matroska")

# MOV / AVI
gen("sample.mov", extra=["-f", "mov"], desc="QuickTime")
gen("sample.avi", extra=["-f", "avi", "-c:v", "mpeg4"], desc="AVI + MPEG4")

# MPEG PS
gen("sample.mpg", extra=["-f", "mpeg", "-c:v", "mpeg2video", "-b:v", "400k"], desc="MPEG-2 PS")
gen("sample.mpeg", extra=["-f", "mpeg", "-c:v", "mpeg1video", "-b:v", "400k"], desc="MPEG-1 PS")

# MPEG-TS 族
gen("sample.ts", extra=["-f", "mpegts", "-c:v", "mpeg2video", "-b:v", "400k"], desc="MPEG-TS")
gen("sample.mts", extra=["-f", "mpegts", "-c:v", "h264", "-b:v", "400k", "-mpegts_m2ts_mode", "1"], desc="MTS (AVCHD)")
gen("sample.m2ts", extra=["-f", "mpegts", "-c:v", "h264", "-b:v", "400k", "-mpegts_m2ts_mode", "1"], desc="M2TS (蓝光)")

# OGV (Theora)
gen("sample.ogv", extra=["-c:v", "libtheora", "-q:v", "6"], desc="Ogg Theora")

# FLV / WMV
gen("sample.flv", extra=["-f", "flv", "-c:v", "flv1"], desc="FLV")
gen("sample.wmv", extra=["-f", "asf", "-c:v", "wmv2", "-b:v", "400k"], desc="WMV2")

# 清理帧目录
shutil.rmtree(FRAME_DIR, ignore_errors=True)
print("Video samples done.")
