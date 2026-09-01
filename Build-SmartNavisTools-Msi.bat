@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Installer\Build-Msi.ps1" ^
  -PluginProjectDir "%~dp0SmartNavisTools" ^
  -ProductName "SmartNavisTools" ^
  -AssemblyFileName "SmartNavisTools" ^
  -BundleName "SmartNavisTools.bundle" ^
  -UpgradeCode "E8F3B5C2-4D6A-5B9F-AE1C-2F7A3B9D5E6F" ^
  -StagingSubDir "staging-smartnavistools" ^
  -WxsFileName "Product.SmartNavisTools.generated.wxs" ^
  -IconFileName "SmartNavisToolsIcon_Small.ico" ^
  -OutputDir "%~dp0Installer\artifacts-smartnavistools" ^
  -AllowSameVersionUpgrades ^
  %*

set EXITCODE=%ERRORLEVEL%
endlocal & exit /b %EXITCODE%
