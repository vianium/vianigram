@echo off
REM build-validate.cmd verifica que Vianigram.sln compila junto con sus
REM siblings del workspace Vianium.
REM Ejecutar desde un Developer Command Prompt for VS2013 (Visual Studio 14)
REM
REM Uso:
REM   build-validate.cmd [Debug|Release] [x86|ARM]
REM
REM Default: Debug x86

setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Debug

set PLATFORM=%2
if "%PLATFORM%"=="" set PLATFORM=x86

REM Validate sibling repo dependencies (Vianium foundation + protocols)
set VIANIUM_ROOT=%~dp0..
set MISSING=0
if not exist "%VIANIUM_ROOT%\vianium-kernel\Vianium.Core.Kernel.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-tls\Vianium.Core.Tls.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-net\Vianium.Core.Net.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-http\Vianium.Core.Http.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-crypto\Vianium.Core.Crypto.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-mtproto\Vianium.MTProto.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-mtproto\Vianium.Tl.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-voip\VianiumVoIP.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-voip\Vianium.Tgcalls.vcxproj" set MISSING=1
if not exist "%VIANIUM_ROOT%\vianium-managed-kernel\Vianium.Browser.Kernel.csproj" set MISSING=1
if "%MISSING%"=="1" (
    echo ERROR: Sibling repos del workspace Vianium no encontrados en %VIANIUM_ROOT%
    echo.
    echo Vianigram depende de project references a los siguientes siblings:
    echo   ..\vianium-kernel\Vianium.Core.Kernel.vcxproj
    echo   ..\vianium-tls\Vianium.Core.Tls.vcxproj
    echo   ..\vianium-net\Vianium.Core.Net.vcxproj
    echo   ..\vianium-http\Vianium.Core.Http.vcxproj
    echo   ..\vianium-crypto\Vianium.Core.Crypto.vcxproj
    echo   ..\vianium-mtproto\Vianium.MTProto.vcxproj
    echo   ..\vianium-mtproto\Vianium.Tl.vcxproj
    echo   ..\vianium-voip\VianiumVoIP.vcxproj
    echo   ..\vianium-voip\Vianium.Tgcalls.vcxproj
    echo   ..\vianium-managed-kernel\Vianium.Browser.Kernel.csproj
    echo.
    echo Clona los repos faltantes como hermanos de vianigram y reintenta.
    exit /b 1
)

set MSBUILD="C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe"
if not exist %MSBUILD% (
    echo ERROR: MSBuild 14.0 no encontrado en %MSBUILD%
    echo Instala VS2015 build tools o ajusta la ruta arriba.
    exit /b 1
)

set SLN=%~dp0Vianigram.sln
if not exist "%SLN%" (
    echo ERROR: Vianigram.sln no encontrado en %SLN%
    exit /b 1
)

echo ========================================
echo Building Vianigram.sln
echo Config:        %CONFIG%
echo Platform:      %PLATFORM%
echo MSBuild:       %MSBUILD%
echo Workspace:     %VIANIUM_ROOT%
echo ========================================

REM Build full solution
%MSBUILD% "%SLN%" /p:Configuration=%CONFIG% /p:Platform=%PLATFORM% /m:1 /v:minimal /nologo
if errorlevel 1 (
    echo.
    echo ========================================
    echo BUILD FAILED
    echo ========================================
    exit /b 1
)

echo.
echo ========================================
echo BUILD SUCCEEDED
echo ========================================
echo.
echo Artifacts:
dir /b "%~dp0Core\Vianigram.Kernel\bin\%PLATFORM%\%CONFIG%\Vianigram.Kernel.dll" 2^>nul && echo   - Vianigram.Kernel.dll
dir /b "%~dp0Core\Vianigram.Composition\bin\%PLATFORM%\%CONFIG%\Vianigram.Composition.dll" 2^>nul && echo   - Vianigram.Composition.dll
dir /b "%~dp0Core\Vianigram.Core.Media\%PLATFORM%\%CONFIG%\Vianigram.Core.Media.dll" 2^>nul && echo   - Vianigram.Core.Media.dll
dir /b "%~dp0..\vianium-voip\%PLATFORM%\%CONFIG%\Vianium.VoIP.dll" 2^>nul && echo   - Vianium.VoIP.dll (sibling repo)

endlocal
