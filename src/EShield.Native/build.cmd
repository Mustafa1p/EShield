@echo off
setlocal
call "%~dp0..\..\tools\initialize-msvc.cmd"
if errorlevel 1 exit /b 1
if not exist "%~dp0bin" mkdir "%~dp0bin"
cl /nologo /LD /std:c++17 /EHsc /MT /O2 /W4 "%~dp0FilterEngine.cpp" /Fo"%~dp0bin\FilterEngine.obj" /Fe"%~dp0bin\EShield.Native.dll" /link /Brepro fwpuclnt.lib rpcrt4.lib ws2_32.lib
exit /b %errorlevel%
