@echo off
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
