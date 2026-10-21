@echo off
REM ============================================================================
REM  Animora - Legacy Clinic.bak -> Clinic import bundle converter (Windows)
REM
REM  Double-click this file. It will:
REM    1) find Python 3,
REM    2) ask for the input Clinic.bak file,
REM    3) ask where to save the output bundle,
REM    4) run the converter and leave the window open with the result.
REM
REM  You can also pass the paths directly:
REM    run-converter.cmd "C:\path\Clinic.bak" "C:\path\clinic-import.animora.json"
REM ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

REM --- locate Python 3 -------------------------------------------------------
set "PY="
where py >nul 2>nul && set "PY=py -3"
if not defined PY (
  where python >nul 2>nul && set "PY=python"
)
if not defined PY (
  echo [ERROR] Python 3 was not found on this computer.
  echo         Install Python 3 from https://www.python.org/downloads/ and retry.
  echo.
  pause
  exit /b 1
)

REM --- resolve input path ----------------------------------------------------
set "BAK=%~1"
if "%BAK%"=="" (
  set /p "BAK=Enter the full path to the Clinic.bak file: "
)
REM strip surrounding quotes if the user pasted a quoted path
set "BAK=%BAK:"=%"
if not exist "%BAK%" (
  echo [ERROR] Input file not found: %BAK%
  echo.
  pause
  exit /b 1
)

REM --- resolve output path ---------------------------------------------------
set "OUT=%~2"
if "%OUT%"=="" (
  set /p "OUT=Enter output bundle path [default: clinic-import.animora.json]: "
)
set "OUT=%OUT:"=%"
if "%OUT%"=="" set "OUT=clinic-import.animora.json"

echo.
echo === Converting ===
echo   Input : %BAK%
echo   Output: %OUT%
echo.

%PY% -m converter --bak "%BAK%" --out "%OUT%" --verbose
set "RC=%ERRORLEVEL%"

echo.
if "%RC%"=="0" (
  echo [OK] Bundle written to: %OUT%
  echo      Upload it in the Animora admin panel on the clinic's Import page.
) else (
  echo [FAILED] Converter exited with code %RC%. See messages above.
)
echo.
pause
exit /b %RC%
