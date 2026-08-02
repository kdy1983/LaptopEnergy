@echo off
powercfg /setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 75
powercfg /setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX 75
powercfg /setactive SCHEME_CURRENT
echo 최대 프로세서 상태를 75%%로 변경했습니다.
timeout /t 2 > nul
