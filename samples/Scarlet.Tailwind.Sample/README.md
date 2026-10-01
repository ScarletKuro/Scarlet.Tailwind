# Scarlet.Tailwind.Sample

Sample ASP.NET Core app that compiles `Styles/app.css` to `wwwroot/css/app.css` before static web assets are
discovered, using the runtime staged by the development project.

```xml
<TailwindBeforeStaticWebAssets Include="Styles\app.css">
  <OutputPath>wwwroot\css\app.css</OutputPath>
</TailwindBeforeStaticWebAssets>
```

Run:

```bash
dotnet build
dotnet run
```

Expected generated file:

- `wwwroot/css/app.css`

## Watching

Blazor Hot Reload does not run arbitrary MSBuild targets, so changing a class in a `.razor` file does not
rerun Tailwind through the build package. For Hot Reload, install the CLI as a local tool and run both
watchers:

```bash
dotnet watch
dotnet tailwind watch
```

Run each command in its own terminal. The Tailwind command reads this project's existing MSBuild entry
point and settings, so paths do not have to be repeated. Blazor applies Razor changes without restarting,
Tailwind regenerates the stylesheet, and `dotnet watch` refreshes the changed static asset in the browser.

The sample's `Watch` items support the slower MSBuild-only alternative:

```bash
dotnet watch --no-hot-reload
```

Do not watch generated files under `wwwroot`; doing so makes each Tailwind compile trigger another build.
Do not put `--watch` in `TailwindAdditionalArguments`: watch mode does not exit, so an MSBuild invocation
would never finish.

See [dotnet watch Integration](../../src/Scarlet.Tailwind.MSBuild/README.md#dotnet-watch-integration) for
the full details.
