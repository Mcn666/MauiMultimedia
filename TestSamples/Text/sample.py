# Python 测试样本
import os
from pathlib import Path


def scan_files(root: str, extensions: set[str]) -> list[Path]:
    """递归扫描目录，按扩展名过滤文件。"""
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
