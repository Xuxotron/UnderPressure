# UnderPressure Loader — 2026-10-12

Proyecto **C++ x64** completamente independiente de los tres proyectos C# de Under Pressure.
Base: [TPM-Mod-Loader](https://github.com/SeanB-Mod/TPM-Mod-Loader), cuyo autor ha autorizado al usuario a adaptar el código.

## Funcionamiento (como Museum)

- `winhttp.dll` situado junto a `TPH.exe`, que reenvía las 92 funciones de WinHTTP a Windows.
- `Mods/loader.ini` con `[Loader] Enabled=1` y entradas ordenadas `[Mods] 1=archivo.dll, 2=archivo.dll...`.
- Se respetan primero las entradas numeradas del INI. **Cualquier DLL adicional encontrada directamente en `Mods` se incluye automáticamente después**, en orden alfabético. El loader no lleva nombres de DLL de mods fijados en el código. Ni añadir ni renombrar mods exige recompilar C++.
- Diagnósticos en **`Mods/Logs/loader.log`**, el mismo nombre y estructura de Museum.
- Reconoce únicamente el proceso `TPH.exe`.
- Para las DLL .NET/C#, inicializa Mono e identifica componentes de inicio `MonoBehaviour` con nombre de clase terminado en `Plugin` y método `Awake()`; invoca `GameObject.AddComponent(System.Type)` en el hilo de Unity. Para DLL nativas usa `LoadLibraryExW`.
- No existe `LoaderBootstrap` ni dependencia del runtime BepInEx dentro del loader.

## Compilación

Abre `UnderPressureLoader.sln` en Rider o Visual Studio y compila **Release | x64**.
El resultado esperado es `bin/x64/Release/winhttp.dll`.

## Instalación prevista

```text
TPH/
  TPH.exe
  winhttp.dll
  Mods/
    loader.ini
    0Harmony.dll
    UnderPressure.dll
    UnderPressure.Editor.dll
    UnderPressure.Energy.dll
    Logs/
      loader.log
```

**Aviso:** este loader aún **no está probado dentro del juego**. Las tres DLL C# actuales en GitHub aún derivan de `BepInEx.BaseUnityPlugin` y no pueden iniciarse sin migrar sus entradas a `MonoBehaviour` y sustituir la configuración y registros de BepInEx. No sustituyas tu instalación funcional antes de realizar esa migración y tener una copia de seguridad. `0Harmony.dll` sigue siendo necesaria porque los parches utilizan Harmony, que es independiente de BepInEx.

Para evitar interferencias, no instales este `winhttp.dll` simultáneamente con el Doorstop de BepInEx.
