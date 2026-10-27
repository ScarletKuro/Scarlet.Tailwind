# Windows ARM Tailwind crash capture

The Windows ARM CI leg installs an Image File Execution Options (IFEO) debugger for `tailwindcss.exe`
before the build. Every real invocation, including ones from packed packages and concurrent projects,
starts under ProcDump. No rerun is needed to reproduce the failing invocation.

`Enable-TailwindCapture.ps1` first publishes a self-contained x64 probe and runs it through the same IFEO
hook. Setup fails unless normal exit, exit 37, arguments with spaces, and a `0xC0000005` exception all
behave as expected and the exception produces a dump. The probe's synthetic dumps are deleted after
validation; its logs remain under `capture-self-test/`. This probe also exercises x64 emulation on ARM64.

Each actual invocation gets its own directory in the `tailwind-crash-dumps-windows-11-arm` artifact:

- `invocation.json`: executable, original argument vector, working directory and start time.
- `procdump.log`: exception notifications, dump capture results and process exit.
- `exit-code.txt`: the original process's signed exit code, also returned to MSBuild/the CLI.
- On an access violation: full `.dmp` files, the executable and its SHA-256 hash.

Capture uses **first-chance** access violations because application exception handlers can consume an
exception and terminate without triggering WER or a second-chance debugger notification. A captured
exception can therefore be handled successfully: check `exit-code.txt` before calling it a fatal crash.
At most three dumps are captured per invocation. If that limit is reached, the wrapper waits for the
original process and propagates its exit code after ProcDump detaches.

The hook changes process timing, so it can still affect an intermittent failure. It is diagnostic CI
instrumentation, with no changes to the shipped task, CLI or runtime packages. ProcDump's own exit code
does not represent the monitored program's result; the wrapper preserves the original result explicitly.
The final collection step removes the hook, and the artifact uploads even after a failed build or test.

References: [ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump) and Microsoft's
[IFEO debugging guide](https://techcommunity.microsoft.com/blog/askperf/two-minute-drill-configuring-a-debugger-using-image-file-execution-options/373478).
