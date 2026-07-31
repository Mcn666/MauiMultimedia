# -*- coding: utf-8 -*-
"""Generate archive samples (zip/tar/gz/tgz/bz2/xz/7z/zst). RAR cannot be created freely."""
import os
import tarfile
import zipfile
import gzip
import bz2
import lzma
import zstandard
import py7zr

BASE = r"C:\Users\69562\Desktop\Project\MauiMultimedia\TestSamples\Archive"
os.makedirs(BASE, exist_ok=True)

# 要打包的文件
FILES = {
    "readme.txt": "MauiMultimedia 压缩包测试样本\nThis is a sample archive for testing.\n",
    "data.json": '{"app": "MauiMultimedia", "test": true}\n',
    "image.png": b"\x89PNG\r\n\x1a\n" + b"\x00" * 100,
}

print("== Archive samples ==")

# ZIP
with zipfile.ZipFile(os.path.join(BASE, "sample.zip"), "w", zipfile.ZIP_DEFLATED) as z:
    for name, data in FILES.items():
        z.writestr(name, data)
print("  sample.zip")

# TAR（未压缩）
with tarfile.open(os.path.join(BASE, "sample.tar"), "w") as t:
    for name, data in FILES.items():
        info = tarfile.TarInfo(name)
        if isinstance(data, str):
            info.size = len(data.encode())
        else:
            info.size = len(data)
        t.addfile(info, __import__("io").BytesIO(data.encode() if isinstance(data, str) else data))
print("  sample.tar")

# GZ（单文件 gzip）
with gzip.open(os.path.join(BASE, "sample.gz"), "wt", encoding="utf-8") as f:
    f.write(FILES["readme.txt"])
print("  sample.gz")

# TGZ（tar.gz）
with tarfile.open(os.path.join(BASE, "sample.tgz"), "w:gz") as t:
    for name, data in FILES.items():
        info = tarfile.TarInfo(name)
        if isinstance(data, str):
            info.size = len(data.encode())
        else:
            info.size = len(data)
        t.addfile(info, __import__("io").BytesIO(data.encode() if isinstance(data, str) else data))
print("  sample.tgz")

# TAR.GZ（与 tgz 相同的 tar.gz）
with tarfile.open(os.path.join(BASE, "sample.tar.gz"), "w:gz") as t:
    for name, data in FILES.items():
        info = tarfile.TarInfo(name)
        if isinstance(data, str):
            info.size = len(data.encode())
        else:
            info.size = len(data)
        t.addfile(info, __import__("io").BytesIO(data.encode() if isinstance(data, str) else data))
print("  sample.tar.gz")

# BZ2（单文件 bzip2）
with bz2.open(os.path.join(BASE, "sample.bz2"), "wt", encoding="utf-8") as f:
    f.write(FILES["readme.txt"])
print("  sample.bz2")

# XZ（单文件 lzma）
with lzma.open(os.path.join(BASE, "sample.xz"), "wt", encoding="utf-8") as f:
    f.write(FILES["readme.txt"])
print("  sample.xz")

# ZST（zstandard）
cctx = zstandard.ZstdCompressor(level=3)
with open(os.path.join(BASE, "sample.zst"), "wb") as f:
    with cctx.stream_writer(f) as writer:
        writer.write(FILES["readme.txt"].encode())
print("  sample.zst")

# 7Z（py7zr）
with py7zr.SevenZipFile(os.path.join(BASE, "sample.7z"), "w") as z:
    tmp = os.path.join(BASE, "_tmp_7z")
    os.makedirs(tmp, exist_ok=True)
    for name, data in FILES.items():
        p = os.path.join(tmp, name)
        with open(p, "wb") as f:
            f.write(data.encode() if isinstance(data, str) else data)
    z.writeall(tmp, arcname="")
    for f in os.listdir(tmp):
        os.remove(os.path.join(tmp, f))
    os.rmdir(tmp)
print("  sample.7z")

print("Archive samples done.")
print("\n注意: .rar 是专有格式，无法用免费工具生成，未包含。")
