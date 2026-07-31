#!/bin/bash
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
