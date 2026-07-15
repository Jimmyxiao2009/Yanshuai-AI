@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

set "VCDIR=C:\Program Files\Microsoft Visual Studio\18\Community\VC\Tools\MSVC\14.50.35717"
set "KITDIR=C:\Program Files (x86)\Windows Kits\10"
set "SDKVER=10.0.19041.0"
set "OUTDIR=E:\User Files\Desktop\项目\言枢AI\OnDeviceAI\build"
set "SRCDIR=E:\User Files\Desktop\项目\言枢AI\OnDeviceAI"

set "CL=%VCDIR%\bin\Hostx64\x64\cl.exe"
set "FXC=%KITDIR%\bin\%SDKVER%\x64\fxc.exe"
set "LINK=%VCDIR%\bin\Hostx64\x64\link.exe"

set "INCLUDES=/I"%VCDIR%\include" /I"%KITDIR%\Include\%SDKVER%\um" /I"%KITDIR%\Include\%SDKVER%\shared" /I"%KITDIR%\Include\%SDKVER%\winrt""
set "LIBS=/LIBPATH:"%VCDIR%\lib\x64" /LIBPATH:"%KITDIR%\Lib\%SDKVER%\um\x64""

mkdir "%OUTDIR%" 2>nul

echo ========================================
echo Step 1: Compile HLSL Shaders
echo ========================================

"%FXC%" /T cs_5_0 /E CSMain /Fo "%OUTDIR%\embedding_lookup.cso" "%SRCDIR%\shaders\embedding_lookup.cs.hlsl" 2>&1
if errorlevel 1 echo [FAIL] embedding_lookup & exit /b 1
echo [OK] embedding_lookup.cso

"%FXC%" /T cs_5_0 /E CSMain /Fo "%OUTDIR%\transformer_block.cso" "%SRCDIR%\shaders\transformer_block.cs.hlsl" 2>&1
if errorlevel 1 echo [FAIL] transformer_block & exit /b 1
echo [OK] transformer_block.cso

"%FXC%" /T cs_5_0 /E CSMain /Fo "%OUTDIR%\pooler.cso" "%SRCDIR%\shaders\pooler.cs.hlsl" 2>&1
if errorlevel 1 echo [FAIL] pooler & exit /b 1
echo [OK] pooler.cso

echo ========================================
echo Step 2: Compile OnEmbedder.dll
echo ========================================

"%CL%" /nologo /O2 /EHsc /MD /LD ^
    %INCLUDES% ^
    /Fo"%OUTDIR%\OnEmbedder.obj" /Fe"%OUTDIR%\OnEmbedder.dll" ^
    "%SRCDIR%\OnEmbedder\OnEmbedder.cpp" ^
    /link %LIBS% d3d11.lib d3dcompiler.lib dxgi.lib kernel32.lib user32.lib ^
    /OUT:"%OUTDIR%\OnEmbedder.dll" /IMPLIB:"%OUTDIR%\OnEmbedder.lib" 2>&1

if errorlevel 1 echo [FAIL] OnEmbedder.dll & exit /b 1
echo [OK] OnEmbedder.dll

echo ========================================
echo Summary
echo ========================================
dir "%OUTDIR%\*.cso" "%OUTDIR%\*.dll" "%OUTDIR%\*.lib" 2>nul
echo Done!
