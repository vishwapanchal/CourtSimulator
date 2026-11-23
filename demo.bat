@echo off
setlocal EnableExtensions
rem ===========================================================================
rem  CourtSim demo launcher
rem
rem    demo.bat            Start the local AI model and launch the built game
rem                        (builds it first if Builds\Windows\CourtSim.exe is missing).
rem    demo.bat rebuild    Rebuild the game, then launch it.
rem    demo.bat editor     Open the Unity Editor straight into Play mode instead.
rem    demo.bat guided     Launch the game straight into the guided demo (Esc stops it).
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

rem Ollama sometimes ends up running the model on the CPU (for example if the GPU was busy when it
rem started), which makes each suggestion take 30+ seconds. A fresh start lets it claim the GPU again.
echo  [1/3] Restarting Ollama so the model loads onto the GPU...
taskkill /f /im "ollama app.exe" >nul 2>&1
taskkill /f /im ollama.exe >nul 2>&1
taskkill /f /im ollama_llama_server.exe >nul 2>&1
timeout /t 2 /nobreak >nul
start "" /min "%OLLAMA%" serve

rem Wait (up to 20 s) until the server answers.
powershell -NoProfile -Command "for ($i = 0; $i -lt 40; $i++) { try { $null = Invoke-RestMethod http://127.0.0.1:11434/api/version -TimeoutSec 1; exit 0 } catch { Start-Sleep -Milliseconds 500 } }; exit 1"
if errorlevel 1 (
    echo  [!] Ollama did not start. The assistant will use the authored offline advice.
    goto :launch
)

"%OLLAMA%" list | findstr /b /c:"%MODEL%" >nul
if errorlevel 1 (
    echo  [1/3] Downloading %MODEL% ^(one time only^)...
    "%OLLAMA%" pull %MODEL%
)

echo  [2/3] Loading %MODEL% onto the GPU so the first suggestion is instant...
rem A one-token request loads the model and keeps it loaded for an hour (the game also asks for 60m).
powershell -NoProfile -Command "$b = ConvertTo-Json -Compress -InputObject @{ model = '%MODEL%'; prompt = 'ok'; stream = $false; think = $false; keep_alive = '60m'; options = @{ num_predict = 1 } }; try { $null = Invoke-RestMethod http://127.0.0.1:11434/api/generate -Method Post -Body $b -ContentType 'application/json' -TimeoutSec 300; Write-Host '        Model ready.' } catch { Write-Host '        Could not warm the model; the first suggestion may be slow.' }"

rem The PROCESSOR column should read "100% GPU". Anything mentioning CPU means slow advice.
"%OLLAMA%" ps
"%OLLAMA%" ps | findstr /i /c:"CPU" >nul
if not errorlevel 1 (
    echo  [!] WARNING: Ollama is running the model on the CPU, so advice will be slow.
    echo      Close other programs using the GPU, then run demo.bat again to restart Ollama.
    echo      The game still works: slow advice falls back to the case file's own advice after 25 s.
)

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
set "EXTRA="
if /i "%~1"=="guided" set "EXTRA=-courtsimGuidedDemo"
start "" "%PLAYER%" -screen-fullscreen 1 -window-mode borderless %EXTRA%
exit /b 0
