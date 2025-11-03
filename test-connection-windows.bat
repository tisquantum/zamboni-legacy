@echo off
REM Simple batch script to test connection from Windows
REM Can be run from Windows CMD or PowerShell

echo Testing connection to WSL2 server on port 13337...
echo.

REM Method 1: Test using telnet (if available)
where telnet >nul 2>&1
if %ERRORLEVEL% == 0 (
    echo Method 1: Testing with telnet (Ctrl+] then 'quit' to exit)...
    timeout /t 2 >nul
    telnet 127.0.0.1 13337
) else (
    echo telnet not available, skipping telnet test...
)

echo.
echo Method 2: Testing with PowerShell Test-NetConnection...
powershell -Command "Test-NetConnection -ComputerName 127.0.0.1 -Port 13337 -InformationLevel Detailed"

echo.
echo Method 3: Testing with PowerShell Test-NetConnection to WSL IP...
powershell -Command "Test-NetConnection -ComputerName 123.1.1.123 -Port 13337 -InformationLevel Detailed"

pause

