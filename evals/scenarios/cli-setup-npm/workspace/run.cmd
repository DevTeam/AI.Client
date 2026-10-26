@echo off
set "npm_config_cache=%~dp0..\external\npm-cache"
set "npm_config_userconfig=%~dp0..\external\npm-config\.npmrc"
call "C:\Program Files\nodejs\npm.cmd" cache verify
