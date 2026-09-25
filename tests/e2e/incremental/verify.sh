#!/bin/bash
set -euo pipefail

# End-to-end test for the Scarlet.Tailwind incremental contract.
#
# Tailwind has no --update and no way to tell MSBuild which files it scanned, so the target deliberately has
# no Inputs/Outputs and runs on every build. The property that actually matters to a developer is therefore
# the positive one: a class added to a .razor file must appear in the next build's CSS. A wrapper that tried
# to be clever about skipping would fail exactly here, because the stylesheet did not change - only a file
# the scanner reads did.
#
# The scenario also covers the settings stamp: changing a property must discard the previous output rather
# than leave it in place.
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

TEST_DIR="/tmp/scarlet-tailwind-incremental-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

export NUGET_PACKAGES="$TEST_DIR/nuget-packages"

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fatal "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Incremental Tailwind Compilation"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

dotnet new razorclasslib -n TestRclIncremental > /dev/null || fatal "Could not create the Razor Class Library"
cd TestRclIncremental
echo "✓ Created Razor Class Library"

process_template "$TEMPLATES_DIR/TestRclIncremental.csproj.template" "TestRclIncremental.csproj"
echo "✓ Updated project file with the Tailwind package references"

mkdir -p Styles
cp "$TEMPLATES_DIR/app.css.template" "Styles/app.css"
cp "$TEMPLATES_DIR/Component.razor.template" "Component.razor"
echo "✓ Created source assets (entry stylesheet and a component)"

dotnet restore --configfile ../nuget.config || fatal "Restore failed"
echo "✓ Packages restored"

section "First Build"
dotnet build --no-restore --verbosity minimal || fatal "First build failed"
echo "✓ First build completed"

CSS="wwwroot/css/app.css"
if [ ! -f "$CSS" ]; then
    fatal "CSS output was not created"
fi
echo "✓ CSS output was created"

if grep -q "text-3xl" "$CSS"; then
    echo "✓ First build emitted a rule for the class in the component"
else
    echo "✗ First build did not emit the class in the component"
    FAILED=1
fi

# The class the second build should discover. Its absence now is what makes the next assertion meaningful.
if grep -q "tracking-widest" "$CSS"; then
    echo "✗ First build already contains the class that has not been added yet"
    FAILED=1
else
    echo "✓ First build does not contain the not-yet-added class"
fi

section "Build After Adding a Class to a Component"
# The stylesheet is untouched. Only a file Tailwind scans has changed, which is the case a naive
# input/output check would wrongly skip.
cp "$TEMPLATES_DIR/Component.updated.razor.template" "Component.razor"
echo "✓ Added a new class to the component, leaving the stylesheet unchanged"

dotnet build --no-restore --verbosity minimal || fatal "Second build failed"
echo "✓ Second build completed"

if grep -q "tracking-widest" "$CSS"; then
    echo "✓ The newly used class appears in the rebuilt CSS"
else
    echo "✗ The newly used class is missing - the build skipped Tailwind after a scanned file changed"
    FAILED=1
fi

section "Build After Changing a Setting"
# TailwindMinify is a settings-stamp input, so the previous output must be discarded rather than reused.
dotnet build --no-restore --verbosity minimal -p:TailwindMinify=true || fatal "Third build failed"
echo "✓ Third build completed with TailwindMinify=true"

LINE_COUNT="$(wc -l < "$CSS")"
if [ "$LINE_COUNT" -lt 10 ]; then
    echo "✓ Changing a setting regenerated the CSS (now minified, $LINE_COUNT lines)"
else
    echo "✗ Changing a setting left the previous unminified output in place ($LINE_COUNT lines)"
    FAILED=1
fi

section "Verifying Clean"
dotnet clean --verbosity minimal > /dev/null || fatal "Clean failed"
# Clean reads the generated-file manifest, so it removes exactly what the task produced.
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
    echo "✗ E2E incremental test failed"
    exit 1
fi
echo "✓ E2E incremental test completed successfully - rescans, setting changes and clean behaved correctly"
