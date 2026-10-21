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
setlocal EnableExtensions DisableDelayedExpansion
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
REM  %~1 already strips surrounding quotes from a dragged/quoted argument.
REM  :ask reads interactively (when no argument) and strips typed quotes safely.
set "BAK=%~1"
if defined BAK (
  call :dequote BAK "%BAK%"
) else (
  call :ask BAK "Enter the full path to the Clinic.bak file: "
)
if not defined BAK (
  echo [ERROR] No input file was provided.
  echo.
  pause
  exit /b 1
)
if not exist "%BAK%" (
  echo [ERROR] Input file not found: %BAK%
  echo.
  pause
  exit /b 1
)

REM --- resolve output path ---------------------------------------------------
set "OUT=%~2"
if defined OUT (
  call :dequote OUT "%OUT%"
) else (
  call :ask OUT "Enter output bundle path [default: clinic-import.animora.json]: "
)
if not defined OUT set "OUT=clinic-import.animora.json"

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

REM ============================================================================
REM  :ask <varName> <promptText>
REM  Prompts the user and stores the (de-quoted) reply in <varName>. Delayed
REM  expansion is left OFF so special characters such as & ( ) ^ in a pasted
REM  path stay literal, and the surrounding quotes are stripped with a FOR
REM  tokenizer, so pasted paths never break the script.
REM ============================================================================
:ask
setlocal DisableDelayedExpansion
set "_reply="
set /p "_reply=%~2"
REM  strip one surrounding pair of double quotes via a FOR tokenizer (always
REM  quote-balanced; cannot produce the odd-quote syntax error that crashed
REM  the original script). Empty reply -> loop skipped -> _reply stays empty.
if defined _reply for /f "tokens=* delims=" %%A in ("%_reply%") do set "_reply=%%~A"
endlocal & set "%~1=%_reply%"
goto :eof

REM ============================================================================
REM  :dequote <varName> "<value>"
REM  Stores <value> into <varName> with any surrounding quotes removed. The
REM  value arrives as %~2 (already quote-safe), so the body never evaluates an
REM  unbalanced-quote expression.
REM ============================================================================
:dequote
set "%~1=%~2"
goto :eof
