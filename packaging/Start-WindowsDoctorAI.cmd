@echo off
setlocal
set "APP=%~dp0WindowsDoctorAI.App.exe"

if not exist "%APP%" goto :missing_app

reg query "HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64" /v Installed /reg:64 2>nul | findstr /R /C:"0x1$" >nul
if errorlevel 1 goto :missing_vc

echo Iniciando o Windows Doctor AI...
start "" /wait "%APP%"
set "APP_EXIT_CODE=%ERRORLEVEL%"
if not "%APP_EXIT_CODE%"=="0" goto :app_failed

echo O Windows Doctor AI foi encerrado sem indicar um codigo de erro.
exit /b 0

:missing_app
echo [ERRO] WindowsDoctorAI.App.exe nao foi encontrado nesta pasta.
echo Extraia o ZIP Alpha por completo e tente novamente.
goto :show_error

:missing_vc
echo [ERRO] O Microsoft Visual C++ Redistributable x64 nao foi detectado.
echo O launcher interrompeu a inicializacao antes de abrir o aplicativo.
echo Este launcher nao baixa, instala nem altera componentes do Windows.
echo Consulte a pagina oficial da Microsoft:
echo https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist
pause
exit /b 2

:app_failed
echo [ERRO] O aplicativo foi encerrado com codigo %APP_EXIT_CODE%.
echo Se nenhuma janela apareceu, anote esse codigo e siga README-ALPHA.md.
echo Nao envie dumps, logs completos nem dados pessoais.

:show_error
echo.
pause
exit /b 1
