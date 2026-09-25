#!/bin/bash
set -euo pipefail

# End-to-end test for the Scarlet.Tailwind.Cli .NET tool.
#
# The claim is that the RID-specific tool package carries Tailwind, so `dotnet tool install` followed by
# `dotnet tailwind ...` runs without downloading anything. This proves it by compiling a real stylesheet,
# checking that no download cache was created, and asking --scarlet-info what it resolved.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

FAILED=0

section() {
    echo ""
    echo "=========================================="
    echo "$1"
    echo "=========================================="

}

fatal() {
    echo "✗ $1"
    exit 1
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES_DIR="$SCRIPT_DIR/templates"

process_template() {
    local template_file="$1"
    local output_file="$2"
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/scarlet-tailwind-cli-tool-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

# Private caches keep the assertion honest: if the tool needs anything outside the local feed, or downloads
# Tailwind, the script can see it.
export NUGET_PACKAGES="$TEST_DIR/nuget-packages"
export SCARLET_TAILWIND_CACHE="$TEST_DIR/tailwind-cache"

section "E2E Test: Scarlet.Tailwind.Cli"
echo "Workspace: $WORKSPACE_PATH"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

# A local manifest uses the SDK's global tool-resolver cache, whose entry keeps the absolute path into this
# temporary NuGet cache after cleanup. An isolated tool path makes repeated runs use this run's package.
TOOL_PATH="$TEST_DIR/tools"
dotnet tool install Scarlet.Tailwind.Cli --tool-path "$TOOL_PATH" --version "$RUNTIME_VERSION" --configfile nuget.config > /dev/null \
    || fatal "Tool install failed"
export PATH="$TOOL_PATH:$PATH"
echo "✓ Installed Scarlet.Tailwind.Cli $RUNTIME_VERSION from the local feed into an isolated tool path"

section "Diagnostics"
INFO="$(dotnet tailwind --scarlet-info 2>&1)" || fatal "--scarlet-info failed"
echo "$INFO"

# The whole point of the RID package: the binary ships inside it, so nothing is fetched on first use.
if echo "$INFO" | grep -qi "Source .*embedded"; then
    echo "✓ Tailwind resolved to the binary embedded in the package"
else
    echo "✗ Tailwind did not resolve to an embedded binary"
    FAILED=1
fi

if echo "$INFO" | grep -q "$RUNTIME_VERSION"; then
    echo "✓ Diagnostics report the expected Tailwind version"
else
    echo "✗ Diagnostics do not mention version $RUNTIME_VERSION"
    FAILED=1
fi

section "Compiling"
cp "$TEMPLATES_DIR/input.css.template" "input.css"
cp "$TEMPLATES_DIR/markup.html.template" "markup.html"
echo "✓ Created an entry stylesheet and markup to scan"

# SCARLET_TAILWIND_DIAGNOSTICS prints the resolved path to stderr before handing over, which is how the
# next assertion can tell where the binary came from.
SCARLET_TAILWIND_DIAGNOSTICS=1 dotnet tailwind --input input.css --output output.css 2> run.log \
    || { cat run.log; fatal "dotnet tailwind failed"; }
echo "✓ dotnet tailwind compiled the stylesheet"

if grep -q "Scarlet.Tailwind: using Tailwind at" run.log; then
    echo "✓ Diagnostics reported the resolved Tailwind path"
else
    echo "✗ Diagnostics did not report the resolved path"
    cat run.log
    FAILED=1
fi

if [ ! -f "output.css" ]; then
    echo "✗ No CSS was produced"
    FAILED=1
else
    echo "✓ CSS output was created"
    if grep -q "text-3xl" "output.css"; then
        echo "✓ Generated CSS contains a rule for a class found in the markup"
    else
        echo "✗ Generated CSS is missing the scanned class"
        FAILED=1
    fi
fi

section "Verifying Nothing Was Downloaded"
# The cache directory is only created when a download happens. Its absence is the proof.
if [ -d "$SCARLET_TAILWIND_CACHE" ]; then
    echo "✗ A download cache was created; the embedded binary should have been used"
    find "$SCARLET_TAILWIND_CACHE" -maxdepth 3
    FAILED=1
else
    echo "✓ No download cache was created"
fi

section "Argument Forwarding"
# Tailwind has no --version flag and silently ignores unknown ones, so --help is what reports the version.
HELP="$(dotnet tailwind --help 2>&1)" || fatal "dotnet tailwind --help failed"
if echo "$HELP" | grep -q "tailwindcss"; then
    echo "✓ Arguments reach Tailwind itself rather than being interpreted by the tool"
else
    echo "✗ --help did not reach Tailwind"
    echo "$HELP"
    FAILED=1
fi

# --scarlet-info is only reserved as the very first argument; anywhere else it belongs to Tailwind.
if dotnet tailwind --help --scarlet-info 2>&1 | grep -q "Scarlet.Tailwind.Cli  "; then
    echo "✗ --scarlet-info was intercepted even though it was not the first argument"
    FAILED=1
else
    echo "✓ --scarlet-info is only reserved as the first argument"
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E CLI tool test failed"
    exit 1
fi
echo "✓ E2E CLI tool test completed successfully - the embedded Tailwind ran with no download"
