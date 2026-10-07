# Investigacion de estilos nativos de pared

## Fuente verificada

Los JSON simples de `Assets/MonoBehaviour` solo conservan `m_Name`. La informacion completa esta en los YAML del proyecto exportado:

`D:/Games/Two Point Hospital/TPH_Data/EXPORT/Proyect Unity/ExportedProject/Assets/MonoBehaviour`

Cada `Room_*.asset` guarda tres referencias distintas:

- `_wallsInterior`: `RoomWallDefinition` interior.
- `_wallsExterior`: `RoomWallDefinition` exterior.
- `_blueprintWallDefinition`: paredes usadas durante la construccion.

Cada `WallDef_*.asset` contiene una `WallsDefinition` completa. No es una coleccion inferida por nombres: cada pieza se conserva mediante su GUID exacto.

## Piezas de una plantilla

La estructura nativa tiene 22 posiciones. Para los estilos normales interesan estas 16:

- `WallBack` y `WindowBack`.
- `Wall`, `WallCornerLeft`, `WallCornerRight`, `WallCornerBoth`.
- `CornerInner` y `CornerOuter`.
- `Door`, `DoorCornerLeft`, `DoorCornerRight`, `DoorCornerBoth`.
- `Window`, `WindowCornerLeft`, `WindowCornerRight`, `WindowCornerBoth`.

Tambien admite cuatro pilares y dos rellenos, usados solo por algunos estilos.

## Style1 verificado

`I_Wall_Blank_A_V1_2.prefab` pertenece a `WallDef_GenDiagInt`, que usa `Room_D_GeneralDiagnosis` como pared interior. Su grupo completo es:

| Campo | Prefab |
| --- | --- |
| WallBack | Wall_Back |
| WindowBack | Window_Back |
| Wall | I_Wall_Blank_A_V1_2 |
| WallCornerLeft | I_Wall_Blank_SL_A_V1_15 |
| WallCornerRight | I_Wall_Blank_SR_A_V1_11 |
| WallCornerBoth | I_Wall_Blank_Short_A_V1_22 |
| CornerInner | I_Wall_Corner_A_V1_2 |
| CornerOuter | I_Wall_Corner_B_V1_10 |
| Door | I_Wall_Blank_A_V1_2 |
| DoorCornerLeft | I_Wall_Blank_SL_A_V1_15 |
| DoorCornerRight | I_Wall_Blank_SR_A_V1_11 |
| DoorCornerBoth | I_Wall_Blank_Short_A_V1_22 |
| Window | I_Wall_WinFrame_A_V1_10 |
| WindowCornerLeft | I_Wall_WinFrame_SL_A_V1_10 |
| WindowCornerRight | I_Wall_WinFrame_SR_A_V1_22 |
| WindowCornerBoth | I_Wall_WinFrame_Short_A_V1_16 |

Las cuatro piezas de puerta reutilizan las cuatro paredes planas. La puerta fisica sigue siendo el objeto global de puerta de la sala.

## Causa de la variante incorrecta

Los archivos `I_Wall_Blank_A_V1_2.prefab` e `I_Wall_Blank_A_V1_20.prefab` tienen GUID distintos, pero su objeto raiz serializado se llama `I_Wall_Blank_A_V1`. Buscar por `GameObject.name` pierde el sufijo de variante. El antiguo resolver de `RoomCatalog` normalizaba ambos nombres y devolvia la primera referencia encontrada.

`I_Wall_Blank_A_V1_20.prefab` pertenece a `WallDef_FluidAnalysisInt`, no a `WallDef_GenDiagInt`. Por eso tambien cambiaban las esquinas: se estaba recuperando otra plantilla cargada antes en memoria.

La solucion correcta es reutilizar la referencia completa de `_wallsInterior` o `_wallsExterior` de la sala nativa que posee el estilo. No se deben reconstruir estilos nativos buscando prefabs por nombre.
## Hueco de puerta y materiales personalizados

Los campos `Door`, `DoorCornerLeft`, `DoorCornerRight` y `DoorCornerBoth` no tienen que contener necesariamente prefabs llamados `DoorFrame`. En la mayoria de estilos nativos contienen paredes planas. El juego crea el hueco mediante el shader:

1. Calcula los limites de recorte a partir de `RoomItem.TryGetClipBounds()`.
2. Activa la palabra clave `_AACLIPBOX_ON` en los materiales afectados.
3. Escribe `_AAClipBoxPos` y `_AAClipBoxExtents` para recortar la geometria alrededor de la puerta.

`EnergyExterior.mat` usaba inicialmente el shader Standard de Unity. Ese shader no contiene `_AAClipBox`, por lo que al sustituir el material nativo desaparecia el hueco y la pared quedaba dibujada detras de la puerta.

La correccion final esta en `RoomCatalog.PrepareWallMaterial()`:

- Si el material personalizado no admite `_AAClipBox`, adopta el shader de la pared nativa.
- Conserva `_AACLIPBOX_ON`, `_AAClipBoxPos` y `_AAClipBoxExtents` cuando el juego ya ha calculado el hueco.
- Mantiene las texturas y propiedades del material de energia.
- Evita recrear repetidamente un material de recorte ya preparado.

No se sustituyen las cuatro referencias `Door*` de `WallDef_GenDiagInt`, ni se mezclan piezas de otra sala.

## Resultado validado

Validado visualmente en el juego el 7 de octubre de 2026:

- La puerta muestra correctamente su hueco.
- El estilo exterior conserva paredes, laterales y esquinas coherentes.
- La textura exterior se aplica mejor usando el shader nativo compatible.
- No es necesario cargar manualmente prefabs `DoorFrame` para este estilo.

Implementacion: commit `1c242e7` (`Preserve door clipping on custom walls`).