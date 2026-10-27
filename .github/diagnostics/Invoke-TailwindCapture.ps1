# IFEO appends the original executable and its arguments to this script's command line.
# Read the OS argument vector: PowerShell's -File binding splits flags such as --input=C:\app.css
# at the colon before populating $args, even when the script declares no parameters.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$processArguments = [Environment]::GetCommandLineArgs()
$fileIndex = [Array]::IndexOf($processArguments, '-File')
if ($fileIndex -lt 0 -or $fileIndex + 2 -ge $processArguments.Length) {
    throw 'Capture must be launched with -File <script> <executable> [arguments].'
}
$targetArguments = @($processArguments[($fileIndex + 2)..($processArguments.Length - 1)])
$invocationDirectory = Join-Path $env:SCARLET_TAILWIND_DUMP_ROOT ("{0:yyyyMMddTHHmmssfff}-{1}" -f (Get-Date), $PID)
New-Item -ItemType Directory -Path $invocationDirectory -Force | Out-Null
$logPath = Join-Path $invocationDirectory 'procdump.log'
@{
    Executable = $targetArguments[0]
    Arguments = @($targetArguments | Select-Object -Skip 1)
    WorkingDirectory = (Get-Location).Path
    StartedUtc = [DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $invocationDirectory 'invocation.json') -Encoding utf8

# Capture first-chance access violations, before an application crash handler can consume them and exit.
# Some first-chance exceptions can be handled successfully; keep the final exit code alongside the dumps.
# Limit full dumps to three per invocation, then wait on the original process handle if ProcDump detaches.
# ProcDump itself returns 0 even when the target fails.
$targetExitCode = $null
$targetProcess = $null
& $env:SCARLET_TAILWIND_PROCDUMP -accepteula -ma -e 1 -f C0000005 -n 3 -x $invocationDirectory @targetArguments 2>&1 |
    ForEach-Object {
        # ProcDump emits UTF-16 alongside the target's UTF-8. Remove NULs from its ASCII status lines.
        $line = $_.ToString().Replace([string][char]0, '')
        Add-Content -LiteralPath $logPath -Value $line -Encoding utf8
        Write-Output $line
        if ($line -match '^Process:\s+.+\((\d+)\)') {
            try {
                $targetProcess = [Diagnostics.Process]::GetProcessById([int]$Matches[1])
                # Open the handle now so the exit code remains readable after process termination.
                $null = $targetProcess.Handle
            } catch [ArgumentException] {
                # A very short-lived process may already have exited; its exit event is still in the log.
                $targetProcess = $null
            }
        }
        if ($line -match 'Process Exit: PID \d+, Exit Code 0x([0-9a-fA-F]+)') {
            $targetExitCode = [BitConverter]::ToInt32([BitConverter]::GetBytes([Convert]::ToUInt32($Matches[1], 16)), 0)
        }
    }

if ($null -ne $targetProcess) {
    try {
        $targetProcess.WaitForExit()
        $targetExitCode = $targetProcess.ExitCode
    } finally {
        $targetProcess.Dispose()
    }
}

if (Get-ChildItem -LiteralPath $invocationDirectory -Filter '*.dmp') {
    Get-FileHash -LiteralPath $targetArguments[0] -Algorithm SHA256 |
        Format-List | Out-File (Join-Path $invocationDirectory 'executable-sha256.txt') -Encoding utf8
    Copy-Item -LiteralPath $targetArguments[0] -Destination (Join-Path $invocationDirectory 'faulting-executable.exe')
}
if ($null -eq $targetExitCode) {
    Write-Error "ProcDump did not report the target exit code; see $logPath"
    exit 1
}
Set-Content (Join-Path $invocationDirectory 'exit-code.txt') -Value $targetExitCode -Encoding utf8
exit $targetExitCode
