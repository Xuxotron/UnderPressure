# Under Pressure

Plugin de BepInEx 5 para Two Point Hospital 1.29.52 (Unity 2020.3.12f1, Mono).

## Desarrollo

Abre `UnderPressure Base\UnderPressure.csproj` en Rider. Las referencias necesarias están en `Libs`.

Compilación manual:

```powershell
dotnet build "UnderPressure Base\UnderPressure.csproj" -c Debug
```

Si BepInEx está instalado en `D:\Games\Two Point Hospital`, la compilación copia automáticamente `UnderPressure.dll` y su PDB a `BepInEx\plugins\UnderPressure`.
