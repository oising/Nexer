# Contributing to Nexer

## Build and test

Clone with `--recurse-submodules` (XamlX lives in `external/XamlX`), then run from the repository root using the
.NET 10 SDK:

```powershell
dotnet build .\Nexer.slnx
dotnet test .\Nexer.slnx
dotnet format .\Nexer.slnx --verify-no-changes --exclude external
dotnet run --project samples\Nexer.Demo -- --smoke
```

Pull requests are gated by `.github/workflows/ci.yml`, which runs build, tests and the demo `--smoke` run on Windows
and Ubuntu plus the format check. To make these checks required, enable branch protection on `main` and add the
`Build & test (windows-latest)`, `Build & test (ubuntu-latest)` and `Format check` status checks.

See [docs/architecture.md](docs/architecture.md) for the repository map, project ownership and engineering rules.

## README screenshots

The images in `docs/images` are rendered headlessly from the demo (`ScreenDump.Svg` in `Nexer.Ui.Testing`) and are
deterministic. Regenerate them after visual changes:

```powershell
dotnet run --project samples\Nexer.Demo -- --screenshots docs\images
```

## Building an app against this repository's sources

Instead of the NuGet packages, reference the build step and the generator as projects and import the targets (see
[docs/xaml.md](docs/xaml.md)):

```xml
<ItemGroup>
  <ProjectReference Include="..\..\src\Nexer.Ui\Nexer.Ui.csproj" />
  <ProjectReference Include="..\..\src\Nexer.Hosting\Nexer.Hosting.csproj" />
  <ProjectReference Include="..\..\src\Nexer.Xaml.Build\Nexer.Xaml.Build.csproj"
                    ReferenceOutputAssembly="false" OutputItemType="NexerXamlBuildTool" />
  <ProjectReference Include="..\..\src\Nexer.Xaml.Generators\Nexer.Xaml.Generators.csproj"
                    ReferenceOutputAssembly="false" OutputItemType="Analyzer" />
</ItemGroup>
<Import Project="..\..\build\Nexer.Xaml.targets" />
```

## Packaging and releases

`dotnet pack .\Nexer.slnx -c Release -o artifacts\pkg` produces the NuGet packages (CI uploads them as the `packages`
artifact). Pushing a `v*` tag (for example `v1.0.0-preview.1`) runs `.github/workflows/release.yml`, which packs with
that version and publishes to nuget.org via trusted publishing (OIDC; nuget.org policy `nexer-release` for
`release.yml`, so no API key secret is stored). The default version is set in `Directory.Build.props`.

## Dependencies, Unicode data and licensing

The libraries have no third-party runtime dependencies besides XamlX/Mono.Cecil in the XAML build tooling. Tests use
xUnit. Generated Unicode data and the official fixture ship with the Unicode license and attribution; see
`THIRD-PARTY-NOTICES.txt`. Regeneration requires Python and network access (`python .\tools\generate_unicode.py`),
not normal builds/tests. Ported Jexer code is MIT © Autumn Lamonte.
