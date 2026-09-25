#!/bin/bash

set -e

EXECUTABLE_PATH="$1"
DOWNLOAD_FILENAME="$2"
TAILWIND_VERSION="$3"

# Version marker file to track which version is downloaded
VERSION_FILE="${EXECUTABLE_PATH}.version"

if [ -f "$EXECUTABLE_PATH" ]; then
  echo "Tailwind binary found at $EXECUTABLE_PATH, checking version..."

  # Check version from the marker file instead of executing the binary
  # This avoids trying to execute binaries for other platforms (e.g., Windows binary on Linux)
  if [ -f "$VERSION_FILE" ]; then
    STORED_VERSION=$(cat "$VERSION_FILE")
    echo "Stored version: $STORED_VERSION"
    echo "Required version: $TAILWIND_VERSION"

    if [ "$STORED_VERSION" = "$TAILWIND_VERSION" ]; then
      echo "Version matches! No download needed."
      echo "Tailwind setup complete at $EXECUTABLE_PATH"
      exit 0
    else
      echo "Version mismatch! Will download correct version."
    fi
  else
    echo "No version marker found. Will download to ensure correct version."
  fi
fi

RELEASE_URL="https://github.com/tailwindlabs/tailwindcss/releases/download/v$TAILWIND_VERSION"
DOWNLOAD_URL="$RELEASE_URL/$DOWNLOAD_FILENAME"
CHECKSUMS_URL="$RELEASE_URL/sha256sums.txt"

# Unique temp paths so parallel project builds never collide.
# No suffix after the X's: BusyBox's mktemp (Alpine) requires the X's to be the very last characters of
# the template and rejects a trailing suffix with "Invalid argument", unlike GNU mktemp.
TMP_BIN="$(mktemp -t tailwind-download-XXXXXXXX)"
TMP_SUMS="$(mktemp -t tailwind-shasums-XXXXXXXX)"

# Download to a temp file, NEVER straight to $EXECUTABLE_PATH. A download that fails halfway would
# otherwise leave a truncated binary sitting where the next build expects a working one, and an
# interrupted run would leave it there with no marker to reveal that it is junk.
cleanup() {
  rm -f "$TMP_BIN" "$TMP_SUMS" 2>/dev/null || true
}
trap cleanup EXIT

echo "Downloading Tailwind from $DOWNLOAD_URL"
curl -fL "$DOWNLOAD_URL" -o "$TMP_BIN"

# Verify the download against upstream's published SHA-256 sums before it is put in place. Tailwind
# publishes sha256sums.txt alongside every release; checking it catches a corrupt or truncated download
# before it is packaged into a consumer build as a working-looking executable.
echo "Downloading checksums from $CHECKSUMS_URL"
curl -fL "$CHECKSUMS_URL" -o "$TMP_SUMS"

# Exact last-field match via awk, not grep -E: interpolating $DOWNLOAD_FILENAME into a regex would let its
# literal "." match any character, weakening the exactness this check exists for.
#
# Tailwind prefixes every filename in sha256sums.txt with "./":
#   55fd0b24...398195  ./tailwindcss-linux-arm64
# so the prefix is stripped before comparing. Without that, no entry ever matches and every release looks
# like it shipped without checksums.
EXPECTED_SHA256=$(awk -v fname="$DOWNLOAD_FILENAME" '{ n = $NF; sub(/^\.\//, "", n); if (n == fname) { print $1; exit } }' "$TMP_SUMS")
if [ -z "$EXPECTED_SHA256" ]; then
  echo "Error: no checksum entry for '$DOWNLOAD_FILENAME' in $CHECKSUMS_URL" >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL_SHA256=$(sha256sum "$TMP_BIN" | awk '{print $1}')
elif command -v shasum >/dev/null 2>&1; then
  ACTUAL_SHA256=$(shasum -a 256 "$TMP_BIN" | awk '{print $1}')
else
  ACTUAL_SHA256=$(openssl dgst -sha256 "$TMP_BIN" | awk '{print $NF}')
fi

if [ "$ACTUAL_SHA256" != "$EXPECTED_SHA256" ]; then
  echo "Error: checksum mismatch for '$DOWNLOAD_FILENAME'. Expected $EXPECTED_SHA256, got $ACTUAL_SHA256." >&2
  exit 1
fi
echo "Checksum verified: $ACTUAL_SHA256"

echo "Moving $TMP_BIN to $EXECUTABLE_PATH"
mkdir -p "$(dirname "$EXECUTABLE_PATH")"
mv -f "$TMP_BIN" "$EXECUTABLE_PATH"

if [ ! -f "$EXECUTABLE_PATH" ]; then
  echo "Error: Tailwind was not present at $EXECUTABLE_PATH after the move." >&2
  exit 1
fi

chmod +x "$EXECUTABLE_PATH"

# Only now is the marker true. Writing it any earlier means a failed run leaves behind a marker claiming a
# version that is not on disk, and every later build trusts it and skips the download.
echo -n "$TAILWIND_VERSION" > "$VERSION_FILE"
echo "Version marker created: $VERSION_FILE"
echo "Tailwind setup complete at $EXECUTABLE_PATH"
