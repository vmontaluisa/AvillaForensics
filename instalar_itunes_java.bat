@echo off
REM Requisitos del modulo iOS de Avilla Forensics:
REM  - iTunes de escritorio (trae AppleMobileBackup.exe y los drivers de Apple)
REM  - Java JDK 11 (para iTunes Backup Explorer)
REM Se instalan las versiones x64: en Windows 11 ARM64 corren por emulacion,
REM igual que Avilla. Ejecutar con doble clic y aceptar los avisos de Windows.
where winget >nul 2>nul
if not %errorlevel%==0 (
  echo No se encontro winget. Instala "Instalador de aplicacion" desde Microsoft Store.
  pause
  exit /b 1
)

echo === 1/2 iTunes (version de escritorio, no la de Microsoft Store) ===
winget install --id Apple.iTunes -e --source winget --architecture x64 --accept-package-agreements --accept-source-agreements

echo === 2/2 Java JDK 11 (Eclipse Temurin, x64) ===
winget install --id EclipseAdoptium.Temurin.11.JDK -e --source winget --architecture x64 --accept-package-agreements --accept-source-agreements

echo.
echo Comprobacion:
if exist "C:\Program Files (x86)\Common Files\Apple\Mobile Device Support\AppleMobileBackup.exe" (
  echo  [OK] AppleMobileBackup.exe encontrado
) else if exist "C:\Program Files\Common Files\Apple\Mobile Device Support\AppleMobileBackup.exe" (
  echo  [OK] AppleMobileBackup.exe encontrado en Program Files
) else (
  echo  [!!] No se encontro AppleMobileBackup.exe. Abre iTunes una vez y vuelve a probar.
)
for /d %%D in ("C:\Program Files\Eclipse Adoptium\jdk-11*") do echo  [OK] JDK 11 en %%D
echo.
echo Cierra y vuelve a abrir Visual Studio / Avilla para que tome el nuevo Java.
pause
