@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "NUGET_API_KEY=PUT_YOUR_NUGET_API_KEY_HERE"

if "%NUGET_API_KEY%"=="PUT_YOUR_NUGET_API_KEY_HERE" (
  echo Please edit %~nx0 and replace PUT_YOUR_NUGET_API_KEY_HERE with your nuget.org API key.
  exit /b 1
)

set "CONFIGURATION=Release"
set "NUGET_SOURCE=https://api.nuget.org/v3/index.json"

set "PROJECT_FILE=%~dp0RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj"

if not exist "%PROJECT_FILE%" (
  echo Project file not found: "%PROJECT_FILE%"
  exit /b 1
)

where dotnet >nul 2>nul
if errorlevel 1 (
  echo dotnet CLI was not found in PATH.
  exit /b 1
)

pushd "%~dp0" >nul
echo.
echo ============================================================
echo Publishing RitsuLib ModAnalyzers NuGet package
echo Project: "%PROJECT_FILE%"
echo Configuration: %CONFIGURATION%
echo Source: %NUGET_SOURCE%
echo ============================================================
echo.

echo [1/3] Building...
dotnet build "%PROJECT_FILE%" -c %CONFIGURATION%
if errorlevel 1 (
  echo Build failed.
  popd >nul
  exit /b 1
)

echo.
echo [2/3] Running tests...
dotnet test RitsuLibModAnalyzer.Tests\RitsuLibModAnalyzer.Tests.csproj --no-build -c %CONFIGURATION%
if errorlevel 1 (
  echo Tests failed.
  popd >nul
  exit /b 1
)

echo.
echo [3/3] Pushing to NuGet...
for %%f in ("%~dp0RitsuLibModAnalyzer\bin\%CONFIGURATION%\*.nupkg") do (
  echo   Pushing: %%~nxf
  dotnet nuget push "%%f" --api-key "%NUGET_API_KEY%" --source "%NUGET_SOURCE%" --skip-duplicate --no-symbols
  if errorlevel 1 (
    echo NuGet push failed for %%~nxf.
    popd >nul
    exit /b 1
  )
)

popd >nul
echo.
echo NuGet package published successfully.
exit /b 0
