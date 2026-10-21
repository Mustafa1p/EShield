@echo off
set "ES_VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%ES_VSWHERE%" (
  echo Visual Studio 2022 or newer with Desktop development with C++ is required.
  exit /b 1
)
set "ES_VSROOT="
for /f "usebackq tokens=*" %%I in (`"%ES_VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "ES_VSROOT=%%I"
if not defined ES_VSROOT (
  echo MSVC x64 build tools were not found.
  exit /b 1
)
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "%ES_VSROOT%\VC\Auxiliary\Build\vcvars64.bat" >nul
exit /b %errorlevel%
