# Scarlet.Tailwind.Sample.Download

Sample ASP.NET Core app that uses `TailwindRuntimeDownload=true` instead of referencing a platform runtime
package.

The sample itself sets `TailwindVersionDownload` to `$(TailwindVersion)`, the version this repository pins in
`Directory.Build.props`, so it always builds against the Tailwind the packages ship. In your own project, write
the version out as below.

```xml
<PropertyGroup>
  <TailwindRuntimeDownload>true</TailwindRuntimeDownload>
  <TailwindVersionDownload>4.3.5</TailwindVersionDownload>
  <TailwindRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</TailwindRuntimeDirectory>
</PropertyGroup>
```

The project compiles `Styles/app.css` to `wwwroot/css/app.css` before static web assets are discovered. It
also enables minification and writes an external source map, exercising the two-file output path.

Run:

```bash
dotnet build
dotnet run
```

Expected generated files:

- `wwwroot/css/app.css`
- `wwwroot/css/app.css.map`

The first build downloads the checksum-verified Tailwind executable into `runtimes/`; later builds reuse it.
`dotnet clean` removes the generated CSS and source map but intentionally leaves the downloaded runtime
cache in place.
