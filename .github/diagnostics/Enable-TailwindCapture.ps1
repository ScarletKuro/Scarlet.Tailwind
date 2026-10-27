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

# Prove capture works for an x64 executable, including on an ARM64 runner. Publish outside the checkout.
$probeSource = Join-Path $env:RUNNER_TEMP 'TailwindCaptureProbeSource'
New-Item -ItemType Directory -Path $probeSource -Force | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'CrashProbe/CrashProbe.csproj'), (Join-Path $PSScriptRoot 'CrashProbe/Program.cs') -Destination $probeSource
$probeOutput = Join-Path $env:RUNNER_TEMP 'TailwindCaptureProbe'
dotnet publish (Join-Path $probeSource 'CrashProbe.csproj') -c Release -r win-x64 --self-contained true -o $probeOutput
if ($LASTEXITCODE -ne 0) { throw 'Failed to build the x64 capture probe.' }
$probeKey = Join-Path $ifeoRoot 'CrashProbe.exe'
if (Test-Path $probeKey) { throw 'The capture probe already has IFEO settings; refusing to overwrite them.' }
$probeDumpRoot = Join-Path $dumpRoot 'capture-self-test'
$env:SCARLET_TAILWIND_DUMP_ROOT = $probeDumpRoot
try {
    New-Item -Path $probeKey -Force | Out-Null
    New-ItemProperty -Path $probeKey -Name Debugger -PropertyType String -Value $debuggerCommand -Force | Out-Null
    foreach ($scenario in @(@('0', 0), @('37', 37), @('crash', -1073741819))) {
        & (Join-Path $probeOutput 'CrashProbe.exe') $scenario[0] 'argument with spaces'
        if ($LASTEXITCODE -ne $scenario[1]) {
            throw "Capture self-test '$($scenario[0])' returned $LASTEXITCODE; expected $($scenario[1])."
        }
    }
    if (-not (Get-ChildItem -LiteralPath $probeDumpRoot -Filter '*.dmp' -Recurse)) {
        throw 'Capture self-test produced no dump. Do not trust this capture configuration.'
    }
    # Keep probe logs as evidence, but do not upload synthetic dumps as Tailwind crashes.
    Get-ChildItem -LiteralPath $probeDumpRoot -Recurse -File |
        Where-Object { $_.Extension -eq '.dmp' -or $_.Name -eq 'faulting-executable.exe' } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName }
    'PASS: x64 access violation captured; exit codes 0, 37 and 0xC0000005 and spaced arguments preserved.' |
        Set-Content (Join-Path $probeDumpRoot 'self-test-result.txt') -Encoding utf8
} finally {
    if (Test-Path $probeKey) { Remove-Item -LiteralPath $probeKey }
    $env:SCARLET_TAILWIND_DUMP_ROOT = $dumpRoot
}

$tailwindKey = Join-Path $ifeoRoot 'tailwindcss.exe'
New-Item -Path $tailwindKey -Force | Out-Null
New-ItemProperty -Path $tailwindKey -Name Debugger -PropertyType String -Value $debuggerCommand -Force | Out-Null
"SCARLET_TAILWIND_PROCDUMP=$env:SCARLET_TAILWIND_PROCDUMP" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
"SCARLET_TAILWIND_DUMP_ROOT=$dumpRoot" | Out-File $env:GITHUB_ENV -Append -Encoding utf8
Get-ItemProperty -Path $tailwindKey | Out-String | Set-Content (Join-Path $dumpRoot 'capture-configuration.txt') -Encoding utf8
# The final probe deliberately crashed. Do not let its LASTEXITCODE fail the successful setup step.
exit 0
