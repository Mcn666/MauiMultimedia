@echo off
title MauiMultimedia - Android APK Builder
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%Shell"
set "CSPROJ=%PROJECT_DIR%\MauiMultimedia.Shell.csproj"

set "FRAMEWORK=net10.0-android"
set "CONFIG=Release"
set "RUNTIME=android-arm64"
set "SKIP_TRIM="
set "DO_CLEAN="
set "VERBOSE= n"

:parse_args
if "%~1"=="" goto :parse_done
if /i "%~1"=="--clean"       set "DO_CLEAN=true" & shift & goto :parse_args
if /i "%~1"=="--no-trim"     set "SKIP_TRIM=true" & shift & goto :parse_args
if /i "%~1"=="--verbose"     set "VERBOSE= diag" & shift & goto :parse_args
if /i "%~1"=="--help"        goto :show_help
echo Unknown arg: %~1
goto :show_help

:parse_done

if not exist "%CSPROJ%" (
    echo [ERROR] Project file not found:
    echo   %CSPROJ%
    echo Make sure build-apk.bat is placed at the solution root.
    pause
    exit /b 1
)

set "BUILD_ARGS=-f %FRAMEWORK% -c %CONFIG% -r %RUNTIME%"

set "SIGN_ARGS= -p:AndroidSigningKeyStore=""%USERPROFILE%\.android\debug.keystore"" -p:AndroidSigningKeyAlias=androiddebugkey -p:AndroidSigningKeyStorePass=android -p:AndroidSigningKeyPass=android"

set "AOT_ARG=-p:RunAOTCompilation=false"

if defined SKIP_TRIM (
    set "TRIM_ARG=-p:PublishTrimmed=false"
    set "TRIM_DESC=OFF"
) else (
    set "TRIM_ARG="
    set "TRIM_DESC=ON"
)

echo ============================================
echo   MauiMultimedia Android APK Builder
echo ============================================
echo.
echo  Target:   %FRAMEWORK% / %RUNTIME%
echo  Config:   %CONFIG%
echo  Trimming: %TRIM_DESC%
echo  Sign:     debug.keystore
echo.

choice /C YN /M "Build now?"
if errorlevel 2 (
    echo Cancelled.
    pause
    exit /b 0
)

echo.
echo ========== Building APK ==========
echo.

if defined DO_CLEAN (
    echo [Clean] Running dotnet clean first...
    dotnet clean "%CSPROJ%" -f %FRAMEWORK% -c %CONFIG% -v%VERBOSE%
    echo.
)

dotnet publish "%CSPROJ%" ^
    %BUILD_ARGS% ^
    %SIGN_ARGS% ^
    %AOT_ARG% ^
    %TRIM_ARG% ^
    -v%VERBOSE%

if errorlevel 1 (
    echo.
    echo [ERROR] Build failed (exit code: %ERRORLEVEL%)
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========== Build succeeded! Looking for APK... ==========
echo.

set "APK_DIR=%PROJECT_DIR%\bin\%CONFIG%\%FRAMEWORK%\%RUNTIME%"
set "APK_SRC=%APK_DIR%\com.companyname.mauimultimedia.shell-Signed.apk"
set "APK_DST=%APK_DIR%\MauiMultimedia-release-arm64.apk"

if exist "%APK_SRC%" (
    copy /Y "%APK_SRC%" "%APK_DST%" >nul
    for %%F in ("%APK_DST%") do set "SIZE=%%~zF"
    echo  Output: %APK_DST%
    echo  Size:   !SIZE! bytes
) else (
    echo [WARN] APK not found at expected path.
    dir /s /b "%PROJECT_DIR%"\bin\*.apk 2>nul
)

echo.
echo ========== Done ==========
echo.
pause
exit /b 0

:show_help
echo Usage: %~nx0 [options]
echo.
echo Options:
echo   --clean      Full rebuild (clean before build)
echo   --no-trim    Disable IL Linker trimming (larger APK)
echo   --verbose    Detailed diagnostic output
echo   --help       Show this help
echo.
echo Examples:
echo   %~nx0              Normal build
echo   %~nx0 --clean      Full rebuild
echo   %~nx0 --no-trim    Disable trimming
echo.
pause
exit /b 0
