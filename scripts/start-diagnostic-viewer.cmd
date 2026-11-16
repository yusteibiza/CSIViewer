@echo off
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NoExit -ExecutionPolicy RemoteSigned -File "%~dp0Start-Diagnostic.ps1" -Role Viewer -RunName vmware04
