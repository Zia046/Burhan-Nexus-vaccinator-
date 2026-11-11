@echo off
rem Burhan Nexus Vaccinator - restore, build and test in one go (Windows).
rem Everything that happens is also written to build-log.txt. If something fails, send that file.

setlocal
cd /d "%~dp0"
set LOG=%~dp0build-log.txt

echo Burhan Nexus Vaccinator build log > "%LOG%"
echo Started: %DATE% %TIME% >> "%LOG%"
echo. >> "%LOG%"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo The .NET SDK was not found.
    echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download and run this file again.
    echo .NET SDK not found >> "%LOG%"
    pause
    exit /b 1
)

echo === dotnet --info === >> "%LOG%"
dotnet --info >> "%LOG%" 2>&1

echo.
echo [1/3] Restoring packages...
echo. >> "%LOG%"
echo === restore === >> "%LOG%"
dotnet restore BurhanNexus.Vaccinator.sln >> "%LOG%" 2>&1
if errorlevel 1 goto failed

echo [2/3] Building...
echo. >> "%LOG%"
echo === build === >> "%LOG%"
dotnet build BurhanNexus.Vaccinator.sln -c Release --no-restore >> "%LOG%" 2>&1
if errorlevel 1 goto failed

echo [3/3] Running tests...
echo. >> "%LOG%"
echo === test === >> "%LOG%"
dotnet test tests\BurhanNexus.Vaccinator.Tests\BurhanNexus.Vaccinator.Tests.csproj -c Release --no-build --logger "console;verbosity=normal" >> "%LOG%" 2>&1
if errorlevel 1 goto failed

echo.
echo ALL GOOD: packages restored, solution built, all tests passed.
echo ALL GOOD >> "%LOG%"
pause
exit /b 0

:failed
echo.
echo SOMETHING FAILED. Please send the file build-log.txt (in this folder) to Claude.
echo FAILED >> "%LOG%"
pause
exit /b 1
