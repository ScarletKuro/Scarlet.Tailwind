#!/bin/bash
set -euo pipefail

# End-to-end test for download mode under parallel builds.
#
# Several projects sharing one TailwindRuntimeDirectory is the case the named mutex exists for: without it
# they race to write the same ~110 MB executable and some of them get a truncated or locked file.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

PROJECT_COUNT=4
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

TEST_DIR="/tmp/scarlet-tailwind-monorepo-download-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

# Git Bash's /tmp is a user-profile directory, but a native Windows process interprets /tmp at the root
# of the current drive. Resolve the directory to its native path before writing it into MSBuild properties.
TEST_DIR="$(pwd -W 2>/dev/null || pwd)"

export NUGET_PACKAGES="$TEST_DIR/nuget-packages"

# The shared cache all the projects point at. One download has to serve all of them.
RUNTIME_DIR="$TEST_DIR/shared-runtimes"

section "E2E Test: Monorepo Download Coordination"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Shared runtime directory: $RUNTIME_DIR"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

for i in $(seq 1 "$PROJECT_COUNT"); do
    dotnet new razorclasslib -n "Project$i" > /dev/null || fatal "Could not create Project$i"
    mkdir -p "Project$i/Styles"
    cp "$TEMPLATES_DIR/app.css.template" "Project$i/Styles/app.css"
    cp "$TEMPLATES_DIR/Component.razor.template" "Project$i/Component.razor"

    RUNTIME_DIR_ESCAPED="${RUNTIME_DIR//\\/\\\\}"
    sed -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        -e "s|{{RUNTIME_DIRECTORY}}|$RUNTIME_DIR_ESCAPED|g" \
        "$TEMPLATES_DIR/Project.csproj.template" > "Project$i/Project$i.csproj"
done
echo "✓ Created $PROJECT_COUNT projects sharing one runtime directory"

# No runtime package anywhere: every project has to get Tailwind by downloading it.
dotnet new sln -n MonorepoTest > /dev/null || fatal "Could not create the solution"
for i in $(seq 1 "$PROJECT_COUNT"); do
    dotnet sln add "Project$i/Project$i.csproj" > /dev/null
done
echo "✓ Created a solution containing all $PROJECT_COUNT projects"

dotnet restore --configfile nuget.config || fatal "Restore failed"
echo "✓ Packages restored"

section "Parallel Build"
# -m builds the projects concurrently, which is what puts several processes into the download path at once.
BUILD_LOG="$TEST_DIR/build.log"
dotnet build --no-restore -m --verbosity normal > "$BUILD_LOG" 2>&1 || {
    tail -n 60 "$BUILD_LOG"
    fatal "Parallel build failed - the projects may have raced for the runtime"
}
echo "✓ Parallel build completed"

section "Verifying Coordination"
# `|| true` because head closes the pipe early, which can hand find a SIGPIPE that pipefail would turn
# into a script-ending failure.
EXECUTABLE="$(find "$RUNTIME_DIR" -name "tailwindcss" -o -name "tailwindcss.exe" | head -n 1 || true)"
if [ -z "$EXECUTABLE" ]; then
    echo "✗ No Tailwind executable was downloaded into the shared directory"
    FAILED=1
else
    echo "✓ Tailwind was downloaded into the shared directory"

    # One RID directory means one download served everyone.
    RID_DIR_COUNT="$(find "$RUNTIME_DIR" -mindepth 1 -maxdepth 1 -type d | wc -l)"
    if [ "$RID_DIR_COUNT" -eq 1 ]; then
        echo "✓ Exactly one runtime identifier directory was created"
    else
        echo "✗ Expected one runtime identifier directory, found $RID_DIR_COUNT"
        FAILED=1
    fi

    # The marker is written only after the binary is in place, so its presence means a complete download.
    if [ -f "$EXECUTABLE.version" ]; then
        echo "✓ Version marker was written, so the download completed rather than being interrupted"
    else
        echo "✗ Version marker is missing - the cached binary cannot be trusted"
        FAILED=1
    fi

    # Staged files are named .<exe>.<guid>.tmp and cleaned up on every path. Any left behind means a
    # download failed or cleanup was skipped.
    STAGED_COUNT="$(find "$RUNTIME_DIR" -name '*.tmp' | wc -l)"
    if [ "$STAGED_COUNT" -eq 0 ]; then
        echo "✓ No staged temporary files were left behind"
    else
        echo "✗ Found $STAGED_COUNT leftover staged file(s)"
        find "$RUNTIME_DIR" -name '*.tmp'
        FAILED=1
    fi
fi

# Whether another project reaches the mutex before the first download completes is timing-dependent, so
# report the wait count for diagnostics without requiring one.
WAITED="$(grep -c "Another process is downloading" "$BUILD_LOG" || true)"
echo "✓ $WAITED project(s) waited for another process to finish downloading"

for i in $(seq 1 "$PROJECT_COUNT"); do
    CSS="Project$i/wwwroot/css/app.css"
    if [ ! -f "$CSS" ]; then
        echo "✗ Project$i did not produce CSS"
        FAILED=1
    elif ! grep -q "text-3xl" "$CSS"; then
        echo "✗ Project$i produced CSS without the scanned class"
        FAILED=1
    else
        echo "✓ Project$i produced CSS containing the scanned class"
    fi
done

section "Second Build Reuses the Cache"
SECOND_LOG="$TEST_DIR/build2.log"
dotnet build --no-restore -m --verbosity normal > "$SECOND_LOG" 2>&1 || {
    tail -n 60 "$SECOND_LOG"
    fatal "Second build failed"
}
echo "✓ Second build completed"

# A pinned version with a matching marker takes the fast path and never asks GitHub.
CACHED="$(grep -c "is already cached" "$SECOND_LOG" || true)"
if [ "$CACHED" -ge 1 ]; then
    echo "✓ The second build reused the cached runtime ($CACHED project(s) reported a cache hit)"
else
    echo "✗ The second build did not report a cache hit; it may have re-downloaded"
    FAILED=1
fi

if [ -z "${CI:-}" ]; then
    cd /
    rm -rf "$TEST_DIR"
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E monorepo download test failed"
    exit 1
fi
echo "✓ E2E monorepo download test completed successfully - one coordinated download served every project"
