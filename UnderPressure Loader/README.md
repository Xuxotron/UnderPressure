# UnderPressure Loader — 2026-11-05

Proyecto **C++ x64** completamente independiente de los tres proyectos C# de Under Pressure.
Base: [TPM-Mod-Loader](https://github.com/SeanB-Mod/TPM-Mod-Loader), cuyo autor ha autorizado al usuario a adaptar el código.

## Funcionamiento (como Museum)

- `winhttp.dll` situado junto a `TPH.exe`, que reenvía las 92 funciones de WinHTTP a Windows.
- `Mods/loader.ini` conserva `[Loader] Enabled=1` como interruptor local.
- En cada arranque se deriva `steamapps` desde la ubicación real de `TPH.exe`; no existe una unidad de Steam fijada en el código.
- Para probar una compilación local, `TPH/Mods` tiene prioridad cuando contiene `UnderPressure.dll` y `Core/0Harmony.dll`.
- Al renombrar o retirar esa carpeta, se usa el artículo oficial `steamapps/workshop/content/535930/3823463218`.
- Ambas fuentes se aceptan únicamente cuando contienen `UnderPressure.dll` y `Core/0Harmony.dll`; nunca se mezclan DLL de ambas fuentes.
- En la fuente elegida se respetan primero las entradas numeradas de `loader.ini`. Cualquier DLL adicional se incluye después. Las tres DLL de Under Pressure conservan Base → Editor → Energy aunque el INI no exista.
- El `winhttp.dll` que pueda acompañar al artículo de Workshop se ignora: solo se ejecuta la copia instalada junto a `TPH.exe`.
- Diagnósticos compartidos en **`TPH/Mods/mods.log`**, incluida la fuente y ruta elegidas.
- Reconoce únicamente el proceso `TPH.exe`.
- Para las DLL .NET/C#, inicializa Mono e identifica componentes de inicio `MonoBehaviour` con nombre de clase terminado en `Plugin` y método `Awake()`; invoca `GameObject.AddComponent(System.Type)` en el hilo de Unity. Para DLL nativas usa `LoadLibraryExW`.
- No existe `LoaderBootstrap` ni dependencia del runtime BepInEx dentro del loader.

## Compilación

Abre `UnderPressureLoader.sln` en Rider o Visual Studio y compila **Release | x64**.
El resultado esperado es `bin/x64/Release/winhttp.dll`.

## Instalación

```text
TPH/
  TPH.exe
  winhttp.dll
  Mods/
    loader.ini
    mods.log
```

Solo es obligatorio copiar `winhttp.dll` una vez. `Mods` permanece como almacenamiento local de configuración, registros, mapas y guardados, y también como fuente alternativa para instalaciones ajenas a Steam Workshop.

## Contenido del artículo oficial

```text
3823463218/
  loader.ini
  UnderPressure.dll
  UnderPressure.Editor.dll
  UnderPressure.Energy.dll
  translations.csv
  translations_extra.csv
  Assets/
    underpressure
    ...
  Core/
    0Harmony.dll
    ...
  winhttp.dll        (copia de actualización; el loader la ignora)
```

Las entradas actuales heredan `BaseModComponent`, que a su vez hereda `MonoBehaviour`, y pueden iniciarse directamente mediante el cargador nativo.

Para evitar interferencias, no instales este `winhttp.dll` simultáneamente con el Doorstop de BepInEx.
