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

`dotnet watch` already watches Razor files, but it does not know that Tailwind entry stylesheets are build
inputs. The sample explicitly watches both:

```xml
<ItemGroup>
  <Watch Include="Styles\**\*.css" />
  <Watch Include="**\*.razor" />
</ItemGroup>
```

Then `dotnet watch run` rebuilds — and therefore re-runs Tailwind — whenever the stylesheet changes or a
scanned Razor file gains or loses a class.

Do not watch generated files under `wwwroot`; doing so makes each Tailwind compile trigger another build.
Do not put `--watch` in `TailwindAdditionalArguments`: watch mode does not exit, so an MSBuild invocation
would never finish. Use the `Watch` items above, or run `dotnet tailwind --watch` as a separate process.

See [dotnet watch Integration](../../src/Scarlet.Tailwind.MSBuild/README.md#dotnet-watch-integration) for
the full details.
