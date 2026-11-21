@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  CourtSim demo launcher
rem
rem    demo.bat            Start the local AI model and launch the built game
rem                        (builds it first if Builds\Windows\CourtSim.exe is missing).
rem    demo.bat rebuild    Rebuild the game, then launch it.
rem    demo.bat editor     Open the Unity Editor straight into Play mode instead.
rem
rem  Needs: Unity 2022.3.62f3 and Ollama (with the model pulled). No API key.
rem  Close the Unity Editor first if it has this project open (builds run headless).
rem ===========================================================================

cd /d "%~dp0"
set "PROJECT=%~dp0"
set "PLAYER=%~dp0Builds\Windows\CourtSim.exe"
set "UNITY=%ProgramFiles%\Unity 2022.3.62f3\Editor\Unity.exe"
if not exist "%UNITY%" set "UNITY=%ProgramFiles%\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
set "OLLAMA=%LOCALAPPDATA%\Programs\Ollama\ollama.exe"

rem The model the game asks for (keep in sync with OllamaClientOptions.DefaultModel).
set "MODEL=qwen3.5:9b"

echo.
echo  ==  CourtSim: adaptive courtroom training demo  ==
echo.

rem --- 1. Local AI model -------------------------------------------------------
if not exist "%OLLAMA%" (
    echo  [!] Ollama is not installed. The assistant will use the authored offline advice.
    echo      Install it with:  winget install Ollama.Ollama
    goto :launch
)

powershell -NoProfile -Command "try { Invoke-RestMethod http://127.0.0.1:11434/api/version -TimeoutSec 2 ^| Out-Null; exit 0 } catch { exit 1 }"
if errorlevel 1 (
    echo  [1/3] Starting Ollama...
    start "" /min "%OLLAMA%" serve
    timeout /t 4 /nobreak >nul
) else (
    echo  [1/3] Ollama is running.
)

"%OLLAMA%" list | findstr /b /c:"%MODEL%" >nul
if errorlevel 1 (
    echo  [1/3] Downloading %MODEL% ^(one time only^)...
    "%OLLAMA%" pull %MODEL%
)

echo  [2/3] Loading %MODEL% onto the GPU so the first suggestion is instant...
powershell -NoProfile -Command "$b = '{\"model\":\"%MODEL%\",\"prompt\":\"ok\",\"stream\":false,\"think\":false,\"keep_alive\":\"30m\",\"options\":{\"num_predict\":1}}'; try { Invoke-RestMethod http://127.0.0.1:11434/api/generate -Method Post -Body $b -ContentType 'application/json' -TimeoutSec 300 ^| Out-Null; Write-Host '        Model ready.' } catch { Write-Host '        Could not warm the model; the first suggestion may be slow.' }"

:launch
if not exist "%UNITY%" (
    echo  [!] Unity 2022.3.62f3 was not found at "%UNITY%".
    if exist "%PLAYER%" goto :run
    pause
    exit /b 1
)

rem --- 2. Editor mode ------------------------------------------------------------
if /i "%~1"=="editor" (
    echo  [3/3] Opening the Unity Editor in Play mode...
    start "" "%UNITY%" -projectPath "%PROJECT%." -executeMethod CourtSim.EditorTools.DemoTools.OpenAndPlay
    exit /b 0
)

rem --- 3. Build if needed, then run the game --------------------------------------
if /i "%~1"=="rebuild" if exist "%PLAYER%" del /q "%PLAYER%"
if not exist "%PLAYER%" (
    echo  [3/3] Building the game ^(first time takes a few minutes^)...
    "%UNITY%" -batchmode -nographics -projectPath "%PROJECT%." -executeMethod CourtSim.EditorTools.DemoTools.BuildWindowsPlayer -logFile "%PROJECT%Logs\batch\build.log"
    if errorlevel 1 (
        echo  [!] Build failed. See Logs\batch\build.log
        pause
        exit /b 1
    )
)

:run
echo  [3/3] Launching CourtSim...
echo.
echo   Controls: WASD move ^| mouse look ^| E interact ^| Tab assistant ^| Esc menu
echo   Walk through the gate, look at the judge's bench and press E to rule.
echo.
start "" "%PLAYER%" -screen-fullscreen 1 -window-mode borderless
exit /b 0
