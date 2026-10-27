$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$dumpRoot = Join-Path $env:RUNNER_TEMP 'TailwindCrashDumps'
New-Item -ItemType Directory -Path $dumpRoot -Force | Out-Null
$procDumpDirectory = Join-Path $env:RUNNER_TEMP 'ProcDump'
$archive = Join-Path $env:RUNNER_TEMP 'ProcDump.zip'
Invoke-WebRequest 'https://download.sysinternals.com/files/Procdump.zip' -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $procDumpDirectory -Force
$procDumpName = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'procdump64a.exe' } else { 'procdump64.exe' }
$env:SCARLET_TAILWIND_PROCDUMP = Join-Path $procDumpDirectory $procDumpName
$env:SCARLET_TAILWIND_DUMP_ROOT = $dumpRoot
$captureScript = Join-Path $PSScriptRoot 'Invoke-TailwindCapture.ps1'
$powerShell = (Get-Process -Id $PID).Path
$debuggerCommand = '"{0}" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{1}"' -f $powerShell, $captureScript
$ifeoRoot = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options'

$tailwindKey = Join-Path $ifeoRoot 'tailwindcss.exe'
New-Item -Path $tailwindKey -Force | Out-Null
New-ItemProperty -Path $tailwindKey -Name Debugger -PropertyType String -Value $debuggerCommand -Force | Out-Null
"SCARLET_TAILWIND_PROCDUMP=$env:SCARLET_TAILWIND_PROCDUMP" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
"SCARLET_TAILWIND_DUMP_ROOT=$dumpRoot" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
Get-ItemProperty -Path $tailwindKey | Out-String | Set-Content (Join-Path $dumpRoot 'capture-configuration.txt') -Encoding utf8
exit 0
