# INVESTIGACIÓN DEL EDITOR DE ESCENARIOS

## 1. Alcance y estado

Este documento recoge el estado técnico comprobado del módulo independiente `UnderPressure Editor`, la investigación de la reconstrucción de paredes al pintar y la investigación del filtrado de objetos exteriores pertenecientes a DLC deshabilitados.

Las dos primeras correcciones derivadas de esta investigación se implementaron el 12 de octubre de 2026. La pintura usa ahora el refresco nativo que conserva la habitación y el catálogo exterior infiere la procedencia desde los niveles y parcelas reales. Las optimizaciones parciales por rectángulo y una procedencia basada directamente en assets siguen pendientes.

Estado funcional confirmado:

- El editor se abre desde el menú principal mediante un selector propio de hospitales originales.
- La carga se realiza en una sesión aislada y no utiliza la interfaz de Sandbox como selector.
- Se pueden pintar superficies y colocar o eliminar objetos exteriores.
- Los mapas editados se guardan y abren como archivos `.upmap` independientes.
- Las parcelas conservan las capas `Base`, `Built` y `Unbuilt`.
- Se ha restaurado el control general de visibilidad de parcelas.
- La decoración fija duplicada se oculta correctamente sin ocultar los objetos editables.

> **Solución protegida:** la ocultación de la decoración fija bajo `LandscapeItems_Base` funciona correctamente. No debe reescribirse, sustituirse ni eliminarse al abordar los problemas pendientes.

## 2. Arquitectura actual

El editor está separado de Base y Power:

- Proyecto: `UnderPressure Editor/UnderPressureEditor.csproj`
- Ensamblado: `UnderPressure.Editor.dll`
- Plugin: `UnderPressureEditorPlugin`
- Versión actual: `0.1.1`

### `ScenarioEditorFlow.cs`

Gestiona la entrada y salida del editor:

- Añade el botón del editor al menú principal.
- Construye un selector propio a partir de los niveles originales disponibles en `App.SandboxSettingsConfig.LevelOptions`.
- Carga el modo de carrera mediante `GameModeCareer.LoadAsync(true, 0)`.
- Inserta el `LevelConfig` seleccionado en el flujo de carga.
- Inicia el editor nativo mediante `HospitalEditEvents.OnStart`.
- Sustituye la salida nativa por una vuelta al menú sin guardar una partida de carrera.
- Impide el guardado normal, el guardado rápido y el autoguardado durante una sesión de edición.

Este flujo no depende de que exista una última partida guardada y no borra partidas del usuario.

### `ScenarioEditorSession.cs`

Protege los datos originales durante la sesión:

- Crea una copia en memoria de `HospitalPlotDefinition.FloorImage` para cada parcela.
- Copia por separado las listas `Items`, `BuiltItems` y `UnbuiltItems`.
- Restaura las referencias originales al abandonar el editor.
- Activa `WorldState.Config.createBaseLandscapeItems` durante la edición.
- Mantiene la posición de los objetos exteriores al mover una parcela.
- Habilita el botón nativo de objetos exteriores.
- Contiene el parche provisional `ScenarioEditorPaintPreserveRoomStatePatch`.

### `ScenarioEditorStorage.cs`

Implementa el formato propio `.upmap`:

- Carpeta: `BepInEx/config/UnderPressure/maps`.
- Cada mapa queda asociado al identificador del hospital original del que procede.
- Guarda los píxeles de `FloorImage` de cada parcela.
- Guarda la capa, GUID, etiqueta, posición y rotación de cada objeto exterior.
- Valida completamente el archivo antes de modificar la primera parcela.
- Reconstruye las parcelas de forma limpia al abrir un mapa.
- Conserva una copia de seguridad en memoria para restaurar el estado anterior si falla la carga.

### `ScenarioEditorObjects.cs`

Reúne las adaptaciones visuales y de catálogo:

- Muestra las superficies pintadas con una transparencia del 50 % durante la edición.
- Intercepta la obtención de objetos exteriores para aplicar el filtro de DLC.
- Oculta renderizadores y colisionadores de la decoración estática bajo `LandscapeItems_Base`.
- Protege los componentes pertenecientes a objetos editables `LandscapeRoomItem`.

### `EditorLocalization.cs`

Carga las traducciones del editor desde `translations.csv` y `translations_extra.csv` sin crear una dependencia del módulo Editor hacia Base.

## 3. Motivo original de «Items Locked»

El texto no procedía de un fallo de localización ni de una lista vacía. `EditHospitalMenu.Setup` comprueba `WorldState.ShouldCreateBaseLandscapeItems()` y, cuando devuelve `false`, deshabilita el botón de objetos e introduce literalmente el estado `Items Locked`.

La sesión del editor establece temporalmente `WorldState.Config.createBaseLandscapeItems = true` y vuelve a habilitar el control nativo. De esta manera el editor puede utilizar el catálogo exterior sin modificar permanentemente la configuración original del nivel.

## 4. Investigación de la pintura y las paredes

### 4.1 Flujo exacto al pintar

El flujo original parte de `CursorEditHospitalBuild.CursorUpdate()`:

1. Mientras se arrastra el cursor, el juego prepara la previsualización mediante un `Blueprint`.
2. Al soltar el botón, recorre el rectángulo seleccionado y modifica `HospitalPlotDefinition.FloorImage` con `Texture2D.SetPixel`.
3. Una pulsación sobre una única celda se trata como un rectángulo de 1 × 1; no existe una ruta especial más ligera.
4. Si se ha modificado algún píxel, ejecuta `Texture2D.Apply()`.
5. A continuación llama a `HospitalMap.Build(false)`.
6. Después vuelve a ejecutar `WorldState.CalculateLighting()` y emite el evento de actualización de la parcela.

Seleccionar un tipo de suelo solo cambia `_tileType`. El coste importante aparece al confirmar la pintura, tanto para una celda como para un rectángulo.

### 4.2 Qué reconstruye `HospitalMap.Build(false)`

`Build(false)` no actualiza únicamente los tiles modificados. Realiza una reconstrucción general de la parcela:

1. `Demolish()` elimina el estado construido anterior.
2. Se emite `BuildEvents.DeleteRoom`.
3. Se destruyen el `Room`, su `FloorPlan` y los elementos visuales de pasillos y paredes.
4. `CreateIndoorState()` vuelve a leer la imagen completa de suelo.
5. Se crea un nuevo `FloorPlan`.
6. Se recalculan entradas, puertas y ventanas.
7. Se vuelven a cargar las tres capas de objetos exteriores.
8. `RoomAlgorithms.CalculateWalls` recalcula las paredes completas.
9. Se eliminan paredes adyacentes y se recalculan pilares.
10. Se crean de nuevo los `WorldRooms`.
11. Se crea un nuevo `RoomFloorPlanVisual` y se ejecuta `UpdateFromRoom`.
12. Se crea un nuevo `Room`.
13. Se crea un nuevo `CorridorWallsVisual`.
14. Se recalculan pasillos y conectividad del mapa.
15. `WorldState.BuildRoom` actualiza navegación, iluminación y el gestor de iluminación de la sala.
16. Se actualizan detalles del terreno y perímetro.

El cursor llama después otra vez a `WorldState.CalculateLighting()`. Por tanto, la ruta actual puede calcular la iluminación dos veces por una sola operación de pintura.

### 4.3 Causa comprobada del retraso

El retraso no se debe al número de píxeles escritos. Incluso una celda provoca una reconstrucción completa de la parcela.

Las operaciones con mayor coste potencial son:

- destrucción y recreación de objetos visuales;
- cálculo completo de paredes y pasillos;
- reconstrucción del `RoomFloorPlanVisual`;
- actualización de navegación;
- reconstrucción de iluminación;
- carga de las capas de paisaje.

No se dispone todavía de un perfil temporal que reparta el coste exacto entre estas operaciones. Es correcto afirmar que la reconstrucción completa es la causa estructural; no es correcto asignar porcentajes concretos sin instrumentación adicional.

### 4.4 Causa de los cambios de paredes

El cambio en el número de paredes no es por sí mismo un error. Si cambia el contorno del edificio, debe cambiar el número de segmentos.

Lo comprobado es que `Build(false)` destruye la habitación y sus representaciones visuales, calcula de nuevo toda la geometría y crea objetos nuevos. Esa sustitución completa explica que cualquier estado visual de la habitación anterior pueda perderse o reaplicarse en un momento incorrecto.

Registros como los siguientes describen recálculos completos, no demuestran por sí solos una pared errónea:

```text
Pintura del editor: paredes recalculadas 100 -> 36
Pintura del editor: paredes recalculadas 36 -> 20
Pintura del editor: paredes recalculadas 20 -> 0
Pintura del editor: paredes recalculadas 0 -> 100
```

La desaparición, visibilidad incorrecta o cambio sin una modificación geométrica equivalente sí debe considerarse un fallo.

### 4.5 Evaluación de `ScenarioEditorPaintPreserveRoomStatePatch`

El parche actual aplica prefijo y sufijo a `CursorEditHospitalBuild.CursorUpdate`, un método que se ejecuta continuamente mientras la herramienta está activa.

Su función es:

- localizar la habitación anterior;
- capturar por reflexión su estado abierto, visible y el número de paredes;
- localizar la habitación nueva después de `Build(false)`;
- reaplicar parte del estado mediante `Open`, `Close` y `SetVisible`.

Conclusiones:

- No evita la destrucción ni la reconstrucción de la geometría.
- No es el origen de `RoomAlgorithms.CalculateWalls`.
- Ejecuta comprobaciones y reflexión en fotogramas donde no se ha pintado nada.
- Puede influir en la apariencia final al reaplicar apertura o visibilidad sobre un `Room` recién creado.
- La captura debería realizarse únicamente cuando vaya a producirse una reconstrucción, o desaparecer si la corrección futura conserva el mismo `Room`.

### 4.6 La transparencia no altera la clasificación del suelo

`HospitalMapTile.IsType` compara los canales RGB con tolerancia e ignora el canal alfa. La transparencia del 50 % usada por el editor no cambia la clasificación del tile y no explica el cálculo erróneo de paredes.

## 5. Alternativas nativas y propuesta de corrección

### 5.1 `HospitalMap.RebuildWalls()`

Existe una ruta nativa más ligera que conserva el `Room` y evita `Demolish()`. Sin embargo, sigue realizando trabajo global:

- vuelve a leer la imagen;
- recalcula paredes, puertas y ventanas;
- actualiza los visuales de suelo;
- recalcula pasillos;
- actualiza navegación e iluminación;
- puede vender objetos de pared inválidos.

Además, esta ruta no reproduce por sí sola todo el orden de `Build(false)`: no ejecuta `AddHospitalEntrancesFromImage` ni `AddPillars`. Sustituir directamente una llamada por otra puede dejar entradas o tipos de pared de pilares obsoletos.

`CorridorWallsVisual.CreateWallObjects` sí admite límites de suciedad y puede reutilizar parcialmente objetos de pared. En cambio, `RoomFloorPlanVisual.UpdateFromRoom` no ofrece una API equivalente por rectángulo y reconstruye sus elementos.

### 5.2 Corrección implementada

La corrección debe limitarse a la llamada originada por la pintura del editor. `HospitalMap.Build(false)` también se usa en la carga inicial y en movimientos de parcela, donde la reconstrucción completa sigue siendo apropiada.

Implementación aplicada:

1. Un transpiler limitado a `CursorEditHospitalBuild.CursorUpdate` sustituye únicamente su llamada a `Build(false)`.
2. `HospitalMap.RebuildWalls()` conserva `HospitalMap`, `Room`, `FloorPlan` y las capas de paisaje existentes.
3. Antes del refresco se reconstruye la lista de entradas desde `FloorImage`.
4. Antes de `RoomFloorPlanVisual.UpdateFromRoom` se aplican los tipos de pared correspondientes a pilares.
5. Se conserva el estado abierto y visible de la misma habitación si el refresco visual lo altera.
6. Se omite el segundo cálculo de iluminación que ejecutaba el cursor inmediatamente después.
7. Si cambian las firmas privadas o el refresco falla, se registra el error y se recupera la ruta nativa `Build(false)`.

Como segunda fase, el rectángulo pintado puede utilizarse para restringir la reconstrucción de `CorridorWallsVisual`. Una actualización realmente parcial de `RoomFloorPlanVisual` exigiría código específico y debe validarse con más cuidado.

La reconstrucción ya se difiere hasta soltar el ratón. Añadir otro aplazamiento sin actualizar el estado intermedio puede dejar la herramienta y la escena temporalmente desincronizadas, por lo que no es la primera corrección recomendada.

## 6. Investigación del catálogo exterior y los DLC

### 6.1 Resultado del registro

El registro actual muestra:

```text
Catalogo exterior: 1790 disponibles; 0 excluidos por DLC;
0 con DLC declarado, 1790 sin DLC declarado.
Comprobacion independiente de Base: activa.
```

Esto confirma tres hechos:

- El parche de `WorldState.GetLandscapeItems` sí se ejecuta.
- La reflexión encuentra `UnderPressure.DLCControl.IsDisabled(uint)` en la ejecución observada.
- Ninguna de las 1790 definiciones recibidas declara un DLC mediante `DlcPackRequired`.

Por tanto, el filtro se ejecuta pero carece del dato necesario para decidir qué objetos excluir.

### 6.2 Fuente real del catálogo exterior

`WorldState.GetLandscapeItems(out List<RoomItemDefinition>)` devuelve directamente las entradas de:

```text
Metagame.Config.LandscapeItemDatabase.RoomItems
```

Esta ruta no usa el catálogo normal construido por `WorldState.SetupAvailableItems`, `_availableRoomItems` y `OnAddRoomItemDefinition`.

Consecuencias:

- Los parches de Base sobre `DLCUtils` funcionan para las comprobaciones normales de disponibilidad.
- Los parches de `OnAddRoomItemDefinition` y `OnAddRoomDefinition` protegen el catálogo normal.
- Ninguno de ellos aporta metadatos al catálogo exterior ni impide que este lea directamente su base de datos.
- `DlcPackRequired` no permite identificar la procedencia de los objetos exteriores observados.

### 6.3 Caché del menú

`LandscapeObjectsMenu.Setup` solicita los objetos solo cuando su campo `_items` todavía es `null`. Después mantiene listas internas, entre ellas:

- `_itemsToCreate`;
- `_items`;
- `_listObjects`;
- `_listItems`.

Cambiar la preferencia de un DLC después de crear el menú no obliga a reconstruir estas listas.

`DLCManager.RevalidatePurchasedDLC()` actualiza el conjunto de DLC instalados conocido por el juego, pero no invalida `LandscapeItemDatabase` ni la caché de `LandscapeObjectsMenu`.

### 6.4 Orden de carga y reflexión

En la ejecución investigada, Base se carga antes que Editor y la comprobación reflejada funciona. El orden de carga no explica ese registro concreto.

Existe, no obstante, un riesgo independiente: si Editor resolviera el método de Base una sola vez antes de cargar `UnderPressure.dll`, una referencia estática nula podría permanecer nula. La resolución futura debería ser perezosa o reintentable, pero corregir solo eso no resolvería el problema actual porque los 1790 objetos carecen de `DlcPackRequired`.

### 6.5 Evidencia de grupos temáticos

Los recursos extraídos contienen grupos con nombres temáticos, por ejemplo:

- `Item_Landscape_Alien_*`;
- `Item_Landscape_Atoll_*`;
- `Item_Landscape_Building_CC_*`;
- `Item_Landscape_Snow_*`.

Los nombres sugieren procedencias distintas, pero no constituyen una fuente autorizada para asignar DLC. No debe construirse una lista manual por prefijos ni afirmarse que todos esos recursos aparecen en el menú sin instrumentar cada entrada real.

## 7. Filtrado DLC implementado

El editor construye un mapa `GUID de RoomItemDefinition -> DLC de procedencia` independiente de `DlcPackRequired`.

Fuente utilizada:

1. Recorrer los `LevelConfig` reales.
2. Obtener sus requisitos mediante `LevelConfig.GetRequiredDlcPack`.
3. Recorrer las `HospitalPlotDefinition` utilizadas por cada nivel.
4. Examinar `Items`, `BuiltItems` y `UnbuiltItems`.
5. Asociar cada GUID exterior a los niveles base o DLC en los que aparece.
6. Mantener siempre los objetos usados por contenido base.
7. Mantener los objetos compartidos si al menos uno de sus DLC sigue disponible.
8. Excluir un objeto solo cuando su procedencia comprobada sea exclusivamente uno o varios DLC no disponibles.

Esta estrategia evita una lista manual. Las definiciones que no aparecen en ninguna parcela permanecen visibles para evitar falsos positivos. Para clasificarlas también hará falta investigar la procedencia serializada en assets, escenas o dependencias de AssetBundle.

Comportamiento actual:

- la procedencia se prepara en el selector antes de cargar el nivel;
- el filtrado se ejecuta antes de que `LandscapeObjectsMenu.Setup` ordene y almacene el catálogo;
- la consulta reflejada a Base se resuelve de forma perezosa y reintentable;
- al iniciar una nueva selección se reconstruye la procedencia con el estado actual de los DLC;
- los assets originales no se modifican ni desinstalan.

## 8. Riesgos y pruebas necesarias

### Pintura y paredes

- Comparar el contorno esperado y la geometría visual tras pintar una celda, una línea y un rectángulo.
- Probar ampliaciones, recortes, huecos interiores y esquinas.
- Comprobar entradas, puertas, ventanas y pilares después de cada edición.
- Verificar objetos colocados en paredes que dejan de existir.
- Medir por separado paredes, visuales, navegación e iluminación antes de atribuir costes concretos.
- Confirmar que carga inicial, apertura de `.upmap` y movimiento de parcelas conservan la reconstrucción completa.

### DLC

- Registrar GUID, `DebugTag`, `DlcPackRequired` y nivel de procedencia de cada entrada exterior.
- Comprobar objetos exclusivos de cada DLC con un DLC deshabilitado cada vez.
- Comprobar objetos compartidos entre Base y DLC para no ocultar contenido base.
- Abrir y cerrar el catálogo tras cambiar una preferencia para validar la invalidación de caché.
- Probar el orden de carga inverso de Base y Editor.

## 9. Hitos conservados en Git

Los principales avances del editor están registrados en:

- `851145c` — inicio del editor nativo aislado;
- `eddff42` — selector propio del editor;
- `bb2978c` — eliminación de la plantilla de interfaz Sandbox;
- `03d02bc` — carga del editor sin borrar partidas;
- `bae1f40` — conservación del botón del editor;
- `86a0e72` — protección de datos y almacenamiento de escenarios;
- `47aeb5c` — controles individuales de contenido DLC.

## 10. Conclusión

El problema de rendimiento de la pintura estaba causado por el uso de `HospitalMap.Build(false)` para cualquier modificación, incluida una sola celda. La ruta aplicada conserva ahora la habitación y reconstruye sus paredes sin demoler toda la parcela.

El catálogo exterior sigue alimentándose directamente de `LandscapeItemDatabase`, pero el filtro ya puede clasificar los GUID presentes en las parcelas de niveles Base y DLC. Los objetos no clasificables se conservan hasta disponer de procedencia directa desde assets.

Ambas correcciones deben desarrollarse por separado y validarse contra los flujos ya funcionales, especialmente la protección de datos originales, el formato `.upmap` y la ocultación de decoración fija duplicada.
