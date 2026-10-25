#!/bin/bash
set -euo pipefail

# End-to-end test for multi-targeted projects.
#
# The target's condition ends with ('$(TargetFrameworks)' == '' OR '$(TargetFramework)' == ''), which runs
# Tailwind once in the outer build and skips every inner build. Without it a three-TFM Razor Class Library
# runs Tailwind three times over the same wwwroot, with three processes writing the same file concurrently.
#
# Inner builds reached some other way ask the outer build to compile instead, so the script also covers the
# two ways that happens: a rebuild, whose Clean phase evaluates the inner builds before anything is compiled,
# and an app that references the library, whose build never runs the library's outer build at all.
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
# Physical path from here on. On macOS /tmp is a symlink to /private/tmp, and a process started inside it sees
# the resolved path as its current directory. Builds started from a project's own directory then record
# /private/tmp paths, while a build addressing the same project through /tmp records the other spelling - and
# the SDK's incremental clean treats the first set as orphans of the second and deletes them, including the
# reference assembly a referencing app is about to compile against (CS0006).
TEST_DIR="$(pwd -P)"

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

# Counts the primary static web asset for app.css in a build manifest. Identity is the CSS file's path; the
# compressed alternatives end in .gz", so they do not match.
count_manifest_css() {
    grep -oE '"Identity"[[:space:]]*:[[:space:]]*"[^"]*"' "$1" | grep -Fc 'app.css"' || true
}

section "Rebuild after deleting the CSS"
# A rebuild's Clean phase evaluates and caches the inner builds while the CSS is missing. Before inner builds
# asked the outer build for the compilation's changes, Content registered in the outer build never reached
# them: the manifests came out empty and pack --no-build shipped a package without its CSS.
rm "$CSS"
REBUILD_LOG="$TEST_DIR/rebuild.log"
dotnet build --no-restore --no-incremental --configuration "$CONFIGURATION" --verbosity normal > "$REBUILD_LOG" 2>&1 || {
    tail -n 40 "$REBUILD_LOG"
    fatal "Rebuild failed"
}

INVOCATIONS="$(grep -c "Executing:" "$REBUILD_LOG" || true)"
if [ "$INVOCATIONS" -eq 1 ]; then
    echo "✓ The rebuild ran Tailwind exactly once"
else
    echo "✗ Expected the rebuild to run Tailwind once, it ran $INVOCATIONS times"
    FAILED=1
fi

for tfm in net8.0 net9.0 net10.0; do
    MATCHES="$(count_manifest_css "obj/$CONFIGURATION/$tfm/staticwebassets.build.json")"
    if [ "$MATCHES" -eq 1 ]; then
        echo "✓ $tfm static web assets manifest carries the CSS"
    else
        echo "✗ Expected the CSS once in the $tfm manifest, found $MATCHES"
        FAILED=1
    fi
done

dotnet pack --no-build --configuration "$CONFIGURATION" --output nupkg-rebuild --verbosity minimal || fatal "Pack after rebuild failed"
REBUILD_NUPKG="$(find nupkg-rebuild -name '*.nupkg' | head -n 1)"
ENTRIES="$(unzip -Z1 "$REBUILD_NUPKG" | grep -Fcx "staticwebassets/css/app.css" || true)"
if [ "$ENTRIES" -eq 1 ] && [ "$(unzip -p "$REBUILD_NUPKG" staticwebassets/css/app.css | wc -c)" -gt 0 ]; then
    echo "✓ pack --no-build after the rebuild carries the CSS once, non-empty"
else
    echo "✗ Expected exactly one non-empty staticwebassets/css/app.css after the rebuild, found $ENTRIES"
    FAILED=1
fi

section "Building a referencing app on its own"
# What dotnet run on an app does: its ProjectReference builds the library's net10.0 inner build directly and
# never the library's outer build, so the inner build has to ask the outer one to compile. Before it did,
# Tailwind never ran here and the app answered 404 for the library's CSS.
RCL_DIR="$(pwd)"
APP_DIR="$TEST_DIR/TestApp"
mkdir -p "$APP_DIR"
cp "$TEMPLATES_DIR/TestApp.csproj.template" "$APP_DIR/TestApp.csproj"
cp "$TEMPLATES_DIR/Program.cs.template" "$APP_DIR/Program.cs"
rm "$CSS"

APP_BUILD_LOG="$TEST_DIR/app-build.log"
dotnet build "$APP_DIR/TestApp.csproj" --configuration "$CONFIGURATION" --verbosity normal > "$APP_BUILD_LOG" 2>&1 || {
    tail -n 40 "$APP_BUILD_LOG"
    fatal "Building the referencing app failed"
}
echo "✓ Referencing app built"

INVOCATIONS="$(grep -c "Executing:" "$APP_BUILD_LOG" || true)"
if [ "$INVOCATIONS" -eq 1 ]; then
    echo "✓ Building the app ran Tailwind exactly once"
else
    echo "✗ Expected building the app to run Tailwind once, it ran $INVOCATIONS times"
    FAILED=1
fi

MATCHES="$(count_manifest_css "$APP_DIR/obj/$CONFIGURATION/net10.0/staticwebassets.build.json")"
if [ "$MATCHES" -eq 1 ]; then
    echo "✓ App manifest carries the library's CSS"
else
    echo "✗ Expected the library's CSS once in the app manifest, found $MATCHES"
    FAILED=1
fi

# Serve it: the manifest is the means, the app answering for the CSS is the point. Development is the
# environment in which an app serves referenced projects' static web assets straight from their wwwroot.
APP_PORT=$((20000 + RANDOM % 10000))
ASPNETCORE_ENVIRONMENT=Development dotnet "$APP_DIR/bin/$CONFIGURATION/net10.0/TestApp.dll" \
    --urls "http://127.0.0.1:$APP_PORT" --contentRoot "$APP_DIR" > "$TEST_DIR/app-run.log" 2>&1 &
APP_PID=$!
CSS_URL="http://127.0.0.1:$APP_PORT/_content/TestRclMultiTfm/css/app.css"
STATUS=000
for _ in $(seq 1 60); do
    STATUS="$(curl --silent --max-time 2 --output "$TEST_DIR/served.css" --write-out '%{http_code}' "$CSS_URL" || true)"
    if [ "$STATUS" != "000" ]; then
        break
    fi
    sleep 0.5
done
kill "$APP_PID" 2>/dev/null || true
wait "$APP_PID" 2>/dev/null || true

if [ "$STATUS" = "200" ] && cmp -s "$TEST_DIR/served.css" "$RCL_DIR/$CSS"; then
    echo "✓ App serves the library's CSS at /_content/TestRclMultiTfm/css/app.css"
else
    echo "✗ App answered $STATUS for $CSS_URL, or served something other than the generated CSS"
    tail -n 20 "$TEST_DIR/app-run.log" || true
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
