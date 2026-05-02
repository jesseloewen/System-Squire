@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "BUILD_SCRIPT=%SCRIPT_DIR%build.ps1"

if /I "%~1"=="-h" goto :usage
if /I "%~1"=="--help" goto :usage
if /I "%~1"=="/?" goto :usage

if not exist "%BUILD_SCRIPT%" (
	echo Build script not found: "%BUILD_SCRIPT%"
	exit /b 1
)

where powershell.exe >nul 2>&1
if errorlevel 1 (
	echo Windows PowerShell was not found on PATH.
	exit /b 9009
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%BUILD_SCRIPT%" %*
set "EXIT_CODE=%ERRORLEVEL%"
exit /b %EXIT_CODE%

:usage
echo Usage: build.bat [build.ps1 options]
echo.
echo Mirrors build.ps1 parameters:
echo   -Configuration Debug^|Release
echo   -RuntimeIdentifier RID
echo   -FrameworkDependent
echo   -BuildInstaller
echo   -InstallerVersion X.Y.Z
echo.
echo Examples:
echo   build.bat
echo   build.bat -Configuration Debug
echo   build.bat -RuntimeIdentifier win-arm64 -FrameworkDependent
echo   build.bat -BuildInstaller -InstallerVersion 1.2.0
exit /b 0
