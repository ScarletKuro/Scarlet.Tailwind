param (
  [string]$ExecutablePath,
  [string]$DownloadFilename,
  [string]$TailwindVersion
)

# Any failure must stop the script before the version marker is written. Without this, a failed download or
# move would still stamp the marker, and every later build would skip the download and keep a stale binary.
$ErrorActionPreference = 'Stop'

# Version marker file to track which version is downloaded
$versionFile = "$ExecutablePath.version"

# Check if Tailwind exists and the version matches
$needsDownload = $true
if (Test-Path $ExecutablePath) {
  Write-Host "Tailwind binary found at $ExecutablePath, checking version..."

  # Check version from the marker file instead of executing the binary
  # This avoids trying to execute binaries for other platforms (e.g., Linux binary on Windows)
  if (Test-Path $versionFile) {
    $storedVersion = Get-Content $versionFile -Raw
    $storedVersion = $storedVersion.Trim()
    Write-Host "Stored version: $storedVersion, Required: $TailwindVersion"

    if ($storedVersion -eq $TailwindVersion) {
      Write-Host "Version matches! No download needed."
      $needsDownload = $false
    } else {
      Write-Host "Version mismatch! Will download correct version."
    }
  } else {
    Write-Host "No version marker found. Will download to ensure correct version."
  }
}

if (-not $needsDownload) {
  Write-Host "Tailwind setup complete at $ExecutablePath"
  exit 0
}

$releaseUrl = "https://github.com/tailwindlabs/tailwindcss/releases/download/v$($TailwindVersion)"
$downloadUrl = "$releaseUrl/$($DownloadFilename)"
$checksumsUrl = "$releaseUrl/sha256sums.txt"

# Unique temp paths so parallel project builds never collide.
#
# Downloaded to a temp file, NEVER straight to $ExecutablePath. A download that fails halfway would
# otherwise leave a truncated binary sitting where the next build expects a working one, and an interrupted
# run would leave it there with no marker to reveal that it is junk.
$unique = [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
$tempBin = Join-Path $env:TEMP "tailwind-$unique.bin"
$tempSums = Join-Path $env:TEMP "tailwind-shasums-$unique.txt"

try {
  Write-Host "Downloading Tailwind from $downloadUrl"
  Invoke-WebRequest -Uri $downloadUrl -OutFile $tempBin -UseBasicParsing

  # Verify the download against upstream's published SHA-256 sums before it is put in place. Tailwind
  # publishes sha256sums.txt alongside every release; checking it catches a corrupt or truncated download
  # before it is packaged into a consumer build as a working-looking executable.
  Write-Host "Downloading checksums from $checksumsUrl"
  Invoke-WebRequest -Uri $checksumsUrl -OutFile $tempSums -UseBasicParsing

  # Tailwind prefixes every filename in sha256sums.txt with "./":
  #   55fd0b24...398195  ./tailwindcss-windows-x64.exe
  # so the prefix is optional in the pattern. Without that, no entry ever matches and every release looks
  # like it shipped without checksums.
  $escapedFilename = [regex]::Escape($DownloadFilename)
  $sumLine = Select-String -Path $tempSums -Pattern "^\S+\s+(\./)?$escapedFilename$" | Select-Object -First 1
  if (-not $sumLine) {
    throw "No checksum entry for '$DownloadFilename' in $checksumsUrl"
  }
  $expectedSha256 = ($sumLine.Line -split '\s+')[0]

  $actualSha256 = (Get-FileHash -Path $tempBin -Algorithm SHA256).Hash
  if ($actualSha256 -ne $expectedSha256) {
    throw "Checksum mismatch for '$DownloadFilename'. Expected $expectedSha256, got $actualSha256."
  }
  Write-Host "Checksum verified: $actualSha256"

  Write-Host "Moving $tempBin to $ExecutablePath"
  New-Item -ItemType Directory -Path (Split-Path $ExecutablePath) -Force | Out-Null
  Move-Item -Path $tempBin -Destination $ExecutablePath -Force

  if (-not (Test-Path $ExecutablePath)) {
    throw "Tailwind was not present at $ExecutablePath after the move."
  }

  # Only now is the marker true. Writing it any earlier means a failed run leaves behind a marker claiming a
  # version that is not on disk, and every later build trusts it and skips the download.
  Set-Content -Path $versionFile -Value $TailwindVersion -NoNewline
  Write-Host "Version marker created: $versionFile"
  Write-Host "Tailwind setup complete at $ExecutablePath"
}
finally {
  Remove-Item -Path $tempBin -Force -ErrorAction SilentlyContinue
  Remove-Item -Path $tempSums -Force -ErrorAction SilentlyContinue
}
