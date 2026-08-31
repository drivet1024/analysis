@echo off
setlocal
call "%~dp0publish\start-publish.cmd"
exit /b %errorlevel%
