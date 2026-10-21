@echo off
setlocal
call "%~dp0..\tools\initialize-msvc.cmd"
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /MT /O2 "%~dp0NativeFilter.Tests.cpp" /Fo"%~dp0..\src\EShield.Native\bin\NativeFilter.Tests.obj" /Fe"%~dp0..\src\EShield.Native\bin\NativeFilter.Tests.exe" /link "%~dp0..\src\EShield.Native\bin\EShield.Native.lib" ws2_32.lib
exit /b %errorlevel%
