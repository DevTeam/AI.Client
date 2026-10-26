@echo off
set "GH_CONFIG_DIR=%~dp0..\external\gh-config"
gh config get git_protocol
