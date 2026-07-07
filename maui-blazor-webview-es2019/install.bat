@echo off
setlocal

REM Toolkit directory = folder containing this .bat
set "TOOLKIT=%~dp0"
if "%TOOLKIT:~-1%"=="\" set "TOOLKIT=%TOOLKIT:~0,-1%"

REM Target project: drag-dropped folder, else parent of toolkit (double-click case)
if "%~1"=="" (
    for %%I in ("%TOOLKIT%\..") do set "TARGET=%%~fI"
) else (
    set "TARGET=%~f1"
)

REM --- Locate node: PATH -> WorkBuddy managed -> common installs -> nvm ---
set "NODEEXE="
where node >nul 2>nul
if not errorlevel 1 (
    set "NODEEXE=node"
    goto :havenode
)

REM WorkBuddy managed node: %USERPROFILE%\.workbuddy\binaries\node\versions\*\node.exe
if exist "%USERPROFILE%\.workbuddy\binaries\node\versions" (
    for /d %%V in ("%USERPROFILE%\.workbuddy\binaries\node\versions\*") do (
        if exist "%%V\node.exe" (
            set "NODEEXE=%%V\node.exe"
            goto :havenode
        )
    )
)

REM Common install locations
for %%P in (
    "%ProgramFiles%\nodejs\node.exe"
    "%LOCALAPPDATA%\Programs\nodejs\node.exe"
) do (
    if exist "%%P" (
        set "NODEEXE=%%P"
        goto :havenode
    )
)

REM nvm
if exist "%APPDATA%\nvm" (
    for /d %%V in ("%APPDATA%\nvm\*") do (
        if exist "%%V\node.exe" (
            set "NODEEXE=%%V\node.exe"
            goto :havenode
        )
    )
)

echo [ERROR] node not found.
echo   install needs Node.js. Choose one:
echo     1) Install Node.js from https://nodejs.org/ and add to PATH (recommended)
echo     2) WorkBuddy managed node already exists - this build should auto-detect it
echo     3) Set NODEEXE env var to your node.exe path, then re-run this .bat
echo.
echo   Download: https://nodejs.org/
pause
exit /b 1

:havenode
REM Ensure npm (shipped alongside node) is also on PATH for install.mjs
for %%I in ("%NODEEXE%") do set "NODEDIR=%%~dpI"
set "PATH=%NODEDIR%;%PATH%"

echo.
echo Applying es2019 WebView fix to:
echo   %TARGET%
echo   (node: %NODEEXE%)
echo.

"%NODEEXE%" "%TOOLKIT%\install.mjs" "%TARGET%"
if errorlevel 1 (
    echo.
    echo [ERROR] install failed. See output above.
    pause
    exit /b 1
)

echo.
echo Build now? (Y/N)
set /p BUILD=
if /i "%BUILD%"=="Y" (
    pushd "%TARGET%"
    dotnet build
    popd
)

echo.
echo All done.
pause
