#!/bin/bash
set -euo pipefail

# End-to-end test for Tailwind Watch with the Blazor Hot Reload development loop.
#
# A Razor edit is applied by dotnet watch as a metadata update; it does not run arbitrary MSBuild targets.
# The MSBuild package therefore owns ordinary builds and the CLI owns the long-lived Tailwind watch. This
# scenario runs `dotnet tailwind watch` without repeating the input or output paths, proving that the CLI
# discovers the packed MSBuild package's evaluated entry point. It then changes only a .razor file and
# proves the rest of the observable contract: the Blazor process stays alive, the new markup is served,
# Tailwind emits the new utility, and the running application serves the regenerated stylesheet.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version> <cli-version>

if [ $# -ne 4 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version> <cli-version>"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"
CLI_VERSION="$4"

FAILED=0
DOTNET_WATCH_PID=""
TAILWIND_WATCH_PID=""

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

PROCESS_TREE_PIDS=()

collect_process_tree() {
    local pid="$1"
    local child_pid

    PROCESS_TREE_PIDS+=("$pid")
    if ! command -v pgrep > /dev/null 2>&1; then
        return
    fi

    while IFS= read -r child_pid; do
        if [ -n "$child_pid" ]; then
            collect_process_tree "$child_pid"
        fi
    done < <(pgrep -P "$pid" 2>/dev/null || true)
}

stop_process_tree() {
    local pid="$1"
    if [ -z "$pid" ] || ! kill -0 "$pid" 2>/dev/null; then
        return
    fi

    if [ "${OS:-}" = "Windows_NT" ]; then
        # Git Bash reports an MSYS PID in $!, while taskkill expects the native Windows PID.
        local windows_pid
        windows_pid="$(ps -W | awk -v target="$pid" '$1 == target { print $4; exit }')"
        taskkill.exe //PID "${windows_pid:-$pid}" //T //F > /dev/null 2>&1 || true
    else
        local process_pid
        local attempt
        local any_running

        PROCESS_TREE_PIDS=()
        collect_process_tree "$pid"

        for process_pid in "${PROCESS_TREE_PIDS[@]}"; do
            kill -TERM "$process_pid" 2>/dev/null || true
        done

        # A watcher can take a moment to relay shutdown to its child, but cleanup must never wait forever.
        for ((attempt = 0; attempt < 20; attempt++)); do
            any_running=0
            for process_pid in "${PROCESS_TREE_PIDS[@]}"; do
                if kill -0 "$process_pid" 2>/dev/null; then
                    any_running=1
                    break
                fi
            done
            if [ "$any_running" -eq 0 ]; then
                break
            fi
            sleep 0.1
        done

        for process_pid in "${PROCESS_TREE_PIDS[@]}"; do
            kill -KILL "$process_pid" 2>/dev/null || true
        done
        wait "$pid" 2>/dev/null || true
    fi
}

cleanup() {
    local exit_code=$?
    set +e
    stop_process_tree "$DOTNET_WATCH_PID"
    stop_process_tree "$TAILWIND_WATCH_PID"
    if [ -z "${CI:-}" ] && [ -n "${TEST_DIR:-}" ] && [ -d "$TEST_DIR" ]; then
        rm -rf "$TEST_DIR"
    fi
    exit "$exit_code"
}

wait_for_file_text() {
    local file="$1"
    local text="$2"
    local attempts="${3:-240}"
    local i

    for ((i = 0; i < attempts; i++)); do
        if [ -f "$file" ] && grep -Fq "$text" "$file"; then
            return 0
        fi
        sleep 0.5
    done
    return 1
}

wait_for_url_text() {
    local url="$1"
    local text="$2"
    local output_file="$3"
    local attempts="${4:-240}"
    local i

    for ((i = 0; i < attempts; i++)); do
        if curl --fail --silent --show-error --max-time 2 "$url" --output "$output_file" 2>/dev/null \
            && grep -Fq "$text" "$output_file"; then
            return 0
        fi
        sleep 0.5
    done
    return 1
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

TEST_DIR="/tmp/scarlet-tailwind-hot-reload-$$"
PROJECT_DIR="$TEST_DIR/HotReloadApp"
mkdir -p "$PROJECT_DIR/Components/Pages" "$PROJECT_DIR/Styles" "$PROJECT_DIR/wwwroot/css"
trap cleanup EXIT

export NUGET_PACKAGES="$TEST_DIR/nuget-packages"
export SCARLET_TAILWIND_CACHE="$TEST_DIR/tailwind-cache"
export DOTNET_CLI_UI_LANGUAGE="en-US"
# Native file-system notifications have occasionally stalled during watcher startup on hosted macOS
# runners. Polling is slower but deterministic for this small, short-lived project.
export DOTNET_USE_POLLING_FILE_WATCHER=1
export DOTNET_WATCH_SUPPRESS_EMOJIS=1
export DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1

DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE="$(select_runtime_package "$DOTNET_RID")"
if [ -z "$RUNTIME_PACKAGE" ]; then
    fatal "Unsupported dotnet RID: ${DOTNET_RID:-<empty>}"
fi

section "E2E Test: Tailwind Watch with Blazor Hot Reload"
echo "Workspace: $WORKSPACE_PATH"
echo "Package version: $PACKAGE_VERSION"
echo "Runtime version: $RUNTIME_VERSION"
echo "CLI package version: $CLI_VERSION"
echo "✓ Created test directory: $TEST_DIR"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} package=$RUNTIME_PACKAGE"

process_template "$TEMPLATES_DIR/nuget.config.template" "$TEST_DIR/nuget.config"
process_template "$TEMPLATES_DIR/HotReloadApp.csproj.template" "$PROJECT_DIR/HotReloadApp.csproj"
cp "$TEMPLATES_DIR/Program.cs.template" "$PROJECT_DIR/Program.cs"
cp "$TEMPLATES_DIR/_Imports.razor.template" "$PROJECT_DIR/Components/_Imports.razor"
cp "$TEMPLATES_DIR/App.razor.template" "$PROJECT_DIR/Components/App.razor"
cp "$TEMPLATES_DIR/Routes.razor.template" "$PROJECT_DIR/Components/Routes.razor"
cp "$TEMPLATES_DIR/Home.razor.template" "$PROJECT_DIR/Components/Pages/Home.razor"
cp "$TEMPLATES_DIR/app.css.template" "$PROJECT_DIR/Styles/app.css"
echo "✓ Created a Blazor Web App using the packed MSBuild package"

TOOL_PATH="$TEST_DIR/tools"
dotnet tool install Scarlet.Tailwind.Cli --tool-path "$TOOL_PATH" --version "$CLI_VERSION" \
    --configfile "$TEST_DIR/nuget.config" > /dev/null || fatal "CLI tool install failed"
export PATH="$TOOL_PATH:$PATH"
echo "✓ Installed packed CLI $CLI_VERSION into an isolated tool path"

dotnet restore "$PROJECT_DIR/HotReloadApp.csproj" --configfile "$TEST_DIR/nuget.config" \
    || fatal "Restore failed"
echo "✓ Packages restored"

DOTNET_WATCH_LOG="$TEST_DIR/dotnet-watch.log"
TAILWIND_WATCH_LOG="$TEST_DIR/tailwind-watch.log"
INITIAL_PAGE="$TEST_DIR/initial-page.html"
UPDATED_PAGE="$TEST_DIR/updated-page.html"
UPDATED_CSS="$TEST_DIR/updated.css"

section "Starting Watchers"
(
    cd "$PROJECT_DIR"
    # Port zero lets Kestrel ask the OS for an unused port instead of racing another process on a
    # guessed port. Build-server isolation keeps this test independent of earlier builds in the job.
    dotnet watch --project HotReloadApp.csproj --no-restore --non-interactive --disable-build-servers \
        --no-launch-profile --urls http://127.0.0.1:0
) > "$DOTNET_WATCH_LOG" 2>&1 &
DOTNET_WATCH_PID=$!

if ! wait_for_file_text "$DOTNET_WATCH_LOG" "Now listening on: http://127.0.0.1:"; then
    cat "$DOTNET_WATCH_LOG"
    fatal "Blazor app did not start listening"
fi

BASE_URL="$(sed -n 's|.*Now listening on: \(http://127\.0\.0\.1:[0-9][0-9]*\).*|\1|p' \
    "$DOTNET_WATCH_LOG" | tail -n 1)"
if [ -z "$BASE_URL" ]; then
    cat "$DOTNET_WATCH_LOG"
    fatal "Could not read the Blazor app URL"
fi

if ! wait_for_url_text "$BASE_URL/" "Before hot reload" "$INITIAL_PAGE"; then
    cat "$DOTNET_WATCH_LOG"
    fatal "Blazor app did not serve the initial page"
fi
echo "✓ dotnet watch started the Blazor app at $BASE_URL"

INITIAL_PROCESS_ID="$(grep -o 'data-process-id="[0-9]*"' "$INITIAL_PAGE" | head -n 1 | tr -cd '0-9' || true)"
if [ -z "$INITIAL_PROCESS_ID" ]; then
    fatal "Could not read the Blazor process id from the initial page"
fi
echo "✓ Initial page reports process $INITIAL_PROCESS_ID"

(
    cd "$PROJECT_DIR"
    dotnet tailwind watch --project HotReloadApp.csproj
) > "$TAILWIND_WATCH_LOG" 2>&1 &
TAILWIND_WATCH_PID=$!

# dotnet watch's initial build has already generated app.css, so that file cannot be used as the CLI's
# readiness signal. Wait for the project-aware command to finish its MSBuild query before inspecting it.
if ! wait_for_file_text "$TAILWIND_WATCH_LOG" "Scarlet.Tailwind: watching 1 Tailwind entry point"; then
    cat "$TAILWIND_WATCH_LOG"
    fatal "CLI did not report the MSBuild-configured entry point"
fi
echo "✓ CLI discovered the MSBuild-configured entry point"

if ! wait_for_file_text "$PROJECT_DIR/wwwroot/css/app.css" ".text-3xl"; then
    cat "$TAILWIND_WATCH_LOG"
    fatal "Tailwind watcher did not produce the initial stylesheet"
fi
echo "✓ Project-aware Tailwind watcher produced the initial stylesheet"

if grep -Fq ".tracking-widest" "$PROJECT_DIR/wwwroot/css/app.css"; then
    echo "✗ Initial stylesheet already contains the not-yet-used utility"
    FAILED=1
else
    echo "✓ Initial stylesheet does not contain the not-yet-used utility"
fi

section "Changing a Razor Class"
cp "$TEMPLATES_DIR/Home.updated.razor.template" "$PROJECT_DIR/Components/Pages/Home.razor"
echo "✓ Added tracking-widest to the running component"

if wait_for_url_text "$BASE_URL/" "After hot reload" "$UPDATED_PAGE"; then
    echo "✓ Blazor serves the hot-reloaded component markup"
else
    cat "$DOTNET_WATCH_LOG"
    echo "✗ Blazor did not apply the Razor change"
    FAILED=1
fi

if wait_for_url_text "$BASE_URL/css/app.css" ".tracking-widest" "$UPDATED_CSS"; then
    echo "✓ The running app serves CSS for the newly used class"
else
    cat "$TAILWIND_WATCH_LOG"
    echo "✗ The newly used class is missing from the served stylesheet"
    FAILED=1
fi

UPDATED_PROCESS_ID="$(grep -o 'data-process-id="[0-9]*"' "$UPDATED_PAGE" 2>/dev/null | head -n 1 | tr -cd '0-9' || true)"
if [ -n "$UPDATED_PROCESS_ID" ] && [ "$UPDATED_PROCESS_ID" = "$INITIAL_PROCESS_ID" ]; then
    echo "✓ The Blazor process stayed alive; this was Hot Reload, not a restart"
else
    echo "✗ The Blazor process changed (${INITIAL_PROCESS_ID:-unknown} -> ${UPDATED_PROCESS_ID:-unknown})"
    FAILED=1
fi

section "Result"
if [ "$FAILED" -ne 0 ]; then
    echo "✗ E2E Tailwind Watch with Blazor Hot Reload test failed"
    exit 1
fi
echo "✓ E2E Tailwind Watch with Blazor Hot Reload test completed successfully - markup and generated CSS updated without restarting the app"
