@echo off
setlocal EnableExtensions DisableDelayedExpansion

set "NUGET_API_KEY=PUT_YOUR_NUGET_API_KEY_HERE"

if "%NUGET_API_KEY%"=="PUT_YOUR_NUGET_API_KEY_HERE" (
  echo Please edit %~nx0 and replace PUT_YOUR_NUGET_API_KEY_HERE with your nuget.org API key.
  exit /b 1
)

set "CONFIGURATION=Release"
set "NUGET_SOURCE=https://api.nuget.org/v3/index.json"

set "ANALYZER_PROJECT=%~dp0RitsuLibModAnalyzer\STS2RitsuLib.ModAnalyzers.csproj"
set "TEST_PROJECT=%~dp0RitsuLibModAnalyzer.Tests\RitsuLibModAnalyzer.Tests.csproj"

if not exist "%ANALYZER_PROJECT%" (
  echo Analyzer project not found: "%ANALYZER_PROJECT%"
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
echo Project: "%ANALYZER_PROJECT%"
echo Configuration: %CONFIGURATION%
echo Source: %NUGET_SOURCE%
echo ============================================================
echo.

echo [1/2] Running tests in %CONFIGURATION%...
dotnet test "%TEST_PROJECT%" -c %CONFIGURATION%
if errorlevel 1 (
  echo Tests failed.
  popd >nul
  exit /b 1
)

echo.
echo [2/2] Packing and publishing to NuGet...
dotnet msbuild "%ANALYZER_PROJECT%" /t:PublishAnalyzerToNuGet /p:Configuration=%CONFIGURATION% /p:NuGetApiKey="%NUGET_API_KEY%" /p:NuGetPushSource="%NUGET_SOURCE%"
if errorlevel 1 (
  echo NuGet publish failed.
  popd >nul
  exit /b 1
)

popd >nul
echo.
echo NuGet package published successfully.
exit /b 0
