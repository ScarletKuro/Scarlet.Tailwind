#!/bin/bash
set -euo pipefail

# End-to-end test for multi-targeted projects.
#
# The target's condition ends with ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == ''), which runs
# Tailwind once in the outer build and skips every inner build. Without it a three-TFM Razor Class Library
# runs Tailwind three times over the same wwwroot, with three processes writing the same file concurrently.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

# Pinned explicitly rather than left to the defaults: `dotnet build` defaults to Debug while `dotnet
# pack` defaults to Release, so a `pack --no-build` after a plain `build` looks for output that was never
# produced and fails with "Manifest file at obj/Release/.../staticwebassets.build.json not found".
CONFIGURATION="Debug"

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

detect_dotnet_rid() {
    dotnet --info 2>/dev/null | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n 1
}

select_runtime_package() {
    case "$1" in
        win-arm64) echo "Scarlet.Tailwind.Runtime.windows-x64" ;;
        win-x64) echo "Scarlet.Tailwind.Runtime.windows-x64" ;;
        linux-arm64) echo "Scarlet.Tailwind.Runtime.linux-arm64" ;;
        linux-x64) echo "Scarlet.Tailwind.Runtime.linux-x64" ;;
        linux-musl-arm64) echo "Scarlet.Tailwind.Runtime.linux-arm64-musl" ;;
        linux-musl-x64) echo "Scarlet.Tailwind.Runtime.linux-x64-musl" ;;
        osx-arm64) echo "Scarlet.Tailwind.Runtime.darwin-arm64" ;;
        osx-x64) echo "Scarlet.Tailwind.Runtime.darwin-x64" ;;
        *) echo "" ;;
    esac
}

process_template() {
    local template_file="$1"
    local output_file="$2"
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_PACKAGE}}|$RUNTIME_PACKAGE|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/scarlet-tailwind-multi-tfm-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

export NUGET_PACKAGES="$TEST_DIR/nuget-packages"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fatal "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Multi-Targeted Project"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

dotnet new razorclasslib -n TestRclMultiTfm > /dev/null || fatal "Could not create the Razor Class Library"
cd TestRclMultiTfm
echo "✓ Created Razor Class Library"

process_template "$TEMPLATES_DIR/TestRclMultiTfm.csproj.template" "TestRclMultiTfm.csproj"
echo "✓ Updated project file with three target frameworks and the Tailwind package references"

mkdir -p Styles
cp "$TEMPLATES_DIR/app.css.template" "Styles/app.css"
cp "$TEMPLATES_DIR/Component.razor.template" "Component.razor"
echo "✓ Created source assets (entry stylesheet and a component)"

dotnet restore --configfile ../nuget.config || fatal "Restore failed"
echo "✓ Packages restored"

section "Build"
# --verbosity normal is load-bearing: the "Executing:" line the count below greps for is logged at High
# importance, which minimal verbosity drops. With minimal, the count is always 0 and the assertion can
# never fail.
BUILD_LOG="$TEST_DIR/build.log"
dotnet build --no-restore --configuration "$CONFIGURATION" --verbosity normal > "$BUILD_LOG" 2>&1 || {
    cat "$BUILD_LOG"
    fatal "Build failed"
}
echo "✓ Build completed across all target frameworks"

INVOCATIONS="$(grep -c "Executing:" "$BUILD_LOG" || true)"
if [ "$INVOCATIONS" -eq 1 ]; then
    echo "✓ Tailwind ran exactly once for the whole multi-targeted build"
else
    echo "✗ Expected Tailwind to run once, it ran $INVOCATIONS times"
    grep "Executing:" "$BUILD_LOG" || true
    FAILED=1
fi

CSS="wwwroot/css/app.css"
if [ ! -f "$CSS" ]; then
    echo "✗ CSS output was not created"
    FAILED=1
else
    echo "✓ CSS output was created"
    if grep -q "text-3xl" "$CSS"; then
        echo "✓ Generated CSS contains a rule for a scanned class"
    else
        echo "✗ Generated CSS is missing the scanned class"
        FAILED=1
    fi
fi

section "Pack"
# Captured before packing, to prove the NoBuild guard below.
CSS_MTIME_BEFORE_PACK="$(stat -c %Y "$CSS" 2>/dev/null || stat -f %m "$CSS")"

dotnet pack --no-build --configuration "$CONFIGURATION" --output nupkg --verbosity minimal || fatal "Pack failed"
echo "✓ Pack completed"

# The target is hooked with BeforeTargets, and a BeforeTargets hook still fires when the target it hooks is
# skipped by its own condition - which ResolveProjectStaticWebAssets is, under --no-build. Without the
# NoBuild guard in the condition, Tailwind re-runs here and rewrites wwwroot *after* the static web asset
# integrity manifest was computed, so the package ships bytes that do not match their own hashes.
CSS_MTIME_AFTER_PACK="$(stat -c %Y "$CSS" 2>/dev/null || stat -f %m "$CSS")"
if [ "$CSS_MTIME_BEFORE_PACK" = "$CSS_MTIME_AFTER_PACK" ]; then
    echo "✓ dotnet pack --no-build left the generated CSS untouched"
else
    echo "✗ dotnet pack --no-build re-ran Tailwind and rewrote the CSS after the integrity manifest"
    FAILED=1
fi

NUPKG="$(find nupkg -name '*.nupkg' | head -n 1)"
if [ -z "$NUPKG" ]; then
    fatal "No package was produced"
fi

# One wwwroot shared by three TFMs means exactly one packed copy; more than one would mean the inner builds
# each contributed their own.
ASSET_COUNT="$(unzip -l "$NUPKG" | grep -c "staticwebassets/css/app.css" || true)"
if [ "$ASSET_COUNT" -eq 1 ]; then
    echo "✓ Generated CSS is packed exactly once as a static web asset"
else
    echo "✗ Expected one packed copy of the generated CSS, found $ASSET_COUNT"
    unzip -l "$NUPKG"
    FAILED=1
fi

section "Verifying Clean"
dotnet clean --configuration "$CONFIGURATION" --verbosity minimal > /dev/null || fatal "Clean failed"
if [ -f "$CSS" ]; then
    echo "✗ dotnet clean did not remove generated CSS"
    FAILED=1
else
    echo "✓ dotnet clean removed generated CSS"
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E multi-TFM test failed"
    exit 1
fi
echo "✓ E2E multi-TFM test completed successfully - Tailwind ran once and packed once"
