# Under Pressure

Under Pressure is a BepInEx 5 gameplay and infrastructure mod for Two Point Hospital 1.29.52 (Unity 2020.3.12f1, Mono).

The repository contains two active assemblies:

- `UnderPressure Base` provides configurable gameplay systems, difficulty controls, localization, lighting, maintenance, data views, and the shared asset loader.
- `UnderPressure Power` provides the electrical grid, Energy Room, electrical objects, batteries, contracted power, billing, and energy campaigns.

## Development

Open `SolutionPressure.sln` or the required project in Rider or Visual Studio. Local game and framework assemblies belong in `Libs`; that directory is intentionally excluded from Git.

Build the complete electrical system with:

```powershell
dotnet build "UnderPressure Power\UnderPressurePower.csproj" -c Debug
```

The power project references and builds the base project. By default, both DLLs are deployed directly to `D:\Games\Two Point Hospital\BepInEx\plugins\UnderPressure`.

To verify a build without deploying it, use:

```powershell
dotnet build "UnderPressure Power\UnderPressurePower.csproj" -c Debug -p:SkipDeploy=true
```

The compiled AssetBundle is produced separately from the Unity project and is never replaced by a C# build.
