#!/bin/bash
set -euo pipefail

# End-to-end test for installing Scarlet.Tailwind.MSBuild as a real NuGet package.
#
# The in-process integration tests resolve the task through a ProjectReference, so they cannot catch a
# dependency that was never packed into tools/netstandard2.0/ - the task loads fine for them and fails for
# every actual consumer. This scenario installs the packed nupkg into a fresh Razor Class Library and
# asserts the whole chain: the task loads, Tailwind runs, the generated CSS lands in staticwebassets/,
# and clean removes it again.
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

detect_dotnet_rid() {
    dotnet --info 2>/dev/null | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n 1
}

select_runtime_package() {
    case "$1" in
        # win-arm64 has no package of its own: the windows-x64 package serves it with the x64 binary,
        # which Windows on ARM runs under emulation.
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

TEST_DIR="/tmp/scarlet-tailwind-package-installation-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

# A private package cache: without it a previously restored build of the same version could satisfy the
# restore and the packed nupkg under test would never be opened.
export NUGET_PACKAGES="$TEST_DIR/nuget-packages"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fatal "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Package Installation"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

dotnet new razorclasslib -n TestRcl > /dev/null || fatal "Could not create the Razor Class Library"
cd TestRcl
echo "✓ Created Razor Class Library"

process_template "$TEMPLATES_DIR/TestRcl.csproj.template" "TestRcl.csproj"
echo "✓ Updated project file with the Tailwind package references"

mkdir -p Styles
cp "$TEMPLATES_DIR/app.css.template" "Styles/app.css"
cp "$TEMPLATES_DIR/Component.razor.template" "Component.razor"
echo "✓ Created source assets (entry stylesheet and a component)"

dotnet restore --configfile ../nuget.config || fatal "Restore failed"
echo "✓ Packages restored from the local feed"

section "Build"
dotnet build --no-restore --configuration Debug --verbosity minimal || fatal "Build failed - the task may be missing a packed dependency"
echo "✓ Build completed"

CSS="wwwroot/css/app.css"
if [ ! -f "$CSS" ]; then
    echo "✗ CSS output was not created"
    FAILED=1
else
    echo "✓ CSS output was created"

    # The class only exists in Component.razor, so finding it proves the scanner ran against the project
    # rather than emitting a fixed stylesheet.
    if grep -q "text-3xl" "$CSS"; then
        echo "✓ Generated CSS contains a rule for a class found by scanning the project"
    else
        echo "✗ Generated CSS is missing the scanned class"
        FAILED=1
    fi

    # Nothing in the project uses this one. Its presence would mean the scan is not driving the output.
    if grep -q "bg-lime-300" "$CSS"; then
        echo "✗ Generated CSS contains a class that appears nowhere in the project"
        FAILED=1
    else
        echo "✓ Generated CSS omits classes the project never uses"
    fi
fi

section "Pack"
dotnet pack --no-build --configuration Debug --output nupkg --verbosity minimal || fatal "Pack failed"
echo "✓ Pack completed"

PACKAGE="$(find nupkg -name '*.nupkg' | head -n 1)"
if [ -z "$PACKAGE" ]; then
    fatal "No package was produced"
fi
echo "✓ Package produced: $PACKAGE"

# staticwebassets/ is what makes the file reachable as _content/TestRcl/css/app.css in a consuming app.
# A plain Content glob still packs it, but under content/, where nothing will serve it.
if unzip -l "$PACKAGE" | grep -q "staticwebassets/css/app.css"; then
    echo "✓ Generated CSS is packed as a static web asset"
else
    echo "✗ Generated CSS was not packed under staticwebassets/"
    unzip -l "$PACKAGE"
    FAILED=1
fi

section "Verifying Clean"
dotnet clean --configuration Debug --verbosity minimal > /dev/null || fatal "Clean failed"
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
    echo "✗ E2E package installation test failed"
    exit 1
fi
echo "✓ E2E package installation test completed successfully - the packed task loads, compiles, packs and cleans"
