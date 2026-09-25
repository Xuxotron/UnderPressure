# Investigación de arquitectura: electricidad 1.1

## Objetivo

Añadir a *Two Point Hospital* una simulación eléctrica formada por:

- una sala nueva, **Central eléctrica**;
- generadores construidos dentro de ella;
- baldosas/cableado colocables que formen circuitos conectados;
- consumo real de las máquinas;
- máquinas y salas inutilizables cuando no reciben suministro;
- una vista visual clara de la red y de los apagones.

Este documento separa lo que el juego ya ofrece de lo que debe implementar el mod.

## Hallazgos confirmados en `Assembly-CSharp.dll`

### 1. Ya existe un concepto parcial de electricidad

`RoomItemDefinition` contiene dos datos nativos relevantes:

- `_energyCost` / `EnergyCost(int)`: actualmente representa el coste económico de energía;
- `_generatesElectricity` / `GeneratesElectricity`: marca ciertos objetos como generadores.

También existe `ChallengeElectricity`, una mecánica de desafío que:

- cuenta un punto por cada objeto con `GeneratesElectricity`;
- reparte esos puntos entre salas, aspirantes y flujo de pacientes;
- impide abrir salas si no queda una unidad asignable.

Sin embargo, este sistema **no es una red física**: no calcula cables, distancia, componentes
conectados ni consumo por máquina. Es útil como referencia de interfaz y como prueba de que el
juego tolera salas cerradas por falta de energía, pero no sirve como núcleo del nuevo sistema.

### 2. El calor, atractivo e higiene usan mapas escalares

`WorldState.HospitalAttributeMaps` contiene los mapas nativos de:

1. temperatura;
2. atractivo;
3. higiene.

Los objetos aplican `RoomModifierMapAttribute` con:

- atributo;
- valor;
- radio en celdas;
- modificación por mantenimiento.

`HospitalAttributeMap.ModifyMapAttribute` dibuja un círculo alrededor del objeto y reduce el
valor linealmente con la distancia. Además respeta en buena medida los límites entre salas.

Esto es apropiado para calor y decoración porque ambos se propagan como una intensidad. No es
apropiado para electricidad: dos trozos de cable separados pero dentro del mismo radio no deben
quedar mágicamente unidos. La electricidad requiere un **grafo de conectividad discreto**.

Sí conviene reutilizar de este sistema:

- los eventos de añadir/quitar objetos y construir/eliminar salas;
- la conversión entre posición mundial y `GridCoord`;
- el patrón de vista de datos coloreada;
- el refresco incremental al cambiar un objeto.

### 3. El punto central para inutilizar máquinas ya existe

`RoomItem.IsFunctional()` decide si un objeto se puede usar. Entre otras cosas devuelve falso
cuando la máquina está averiada, ardiendo, siendo mejorada o no tiene el personal requerido.
`InteractionFilterFunctional.IsValid()` consulta directamente este método.

Por tanto, un parche final sobre `RoomItem.IsFunctional()` puede devolver falso cuando:

- el objeto consume electricidad;
- no está conectado a una red alimentada;
- y no es un generador ni un elemento exento.

Para cubrir interacciones que no incluyan el filtro funcional habrá que comprobar también
`ObjectInteraction.IsAvailable()` y las entradas principales de los trabajos de sala. No se debe
forzar `Room.Close()`, porque eso alteraría la decisión manual del jugador y puede interferir con
el desafío eléctrico nativo.

### 4. Las salas ya tienen un estado funcional independiente

`Room.IsFunctional()` comprueba objetos requeridos averiados, incendios y requisitos faltantes.
Se puede ampliar con un estado de suministro eléctrico sin modificar el guardado base. Esto
permitirá que la interfaz diga “sin electricidad” y que una sala completa quede no operativa.

### 5. Las baldosas reales y las texturas de habitación no son lo mismo

`RoomFloorPlanVisual` crea los objetos visuales de suelo de toda una habitación y permite un
`FloorVisualOverride`. La personalización oficial del Workshop aplica un suelo a la habitación
entera. No proporciona una capa de circuitos por celda.

Para el cableado hay dos alternativas:

1. **Objeto tipo alfombra por celda**: se guarda como `RoomItem`, no bloquea navegación y puede
   convivir con objetos encima si se configura como `CollisionType.Rug`.
2. **Capa visual propia y modo de pintura**: mejor experiencia para trazar líneas, pero requiere
   implementar cursor, compra, venta, selección y persistencia.

La primera opción es la adecuada para el prototipo porque usa el guardado, la construcción y los
eventos nativos. Después se puede añadir pintura por arrastre sin cambiar el modelo de datos.

## Diseño recomendado

### Decisión posterior: segundo plano de suelo eléctrico

Se descarta que el cableado sea una sala nativa y se adopta un plano B independiente,
`PowerFloorPlan`. No ocupará `HospitalMap.WorldRooms`, por lo que podrá atravesar habitaciones
existentes y corredores sin reemplazarlos.

La experiencia de construcción imitará el constructor de salas:

- arrastrar para añadir una franja o rectángulo;
- modo separado para restar, igual que añadir/quitar área de una habitación;
- previsualización válida/inválida durante el arrastre;
- coste calculado por número de celdas nuevas;
- sin colocar manualmente un objeto por clic.

La capa será invisible en la vista normal. Al activar el modo eléctrico personalizado 602 ya
existente, el hospital se desatura y se dibujarán suelo, conexiones, generadores y consumidores.
El código actual de esa vista ya colorea los consumidores; se ampliará para mostrar la nueva
capa y sus estados.

### Regla de conexión

Cada baldosa conductora ocupa una coordenada de la cuadrícula. Dos conductores están conectados
solo si son vecinos ortogonales (norte, sur, este u oeste). Las diagonales no conectan.

No se usará un radio de propagación para el cable. La búsqueda será un recorrido por componentes
conectados (BFS/union-find) que parte de los generadores y marca todas las baldosas alcanzables.

### Cómo recibe energía un consumidor

Se recomienda distinguir dos casos:

- **Máquina en pasillo o exterior de sala**: recibe energía si un conductor alimentado ocupa una
  celda de su huella o una celda ortogonalmente adyacente a ella.
- **Máquina dentro de una sala**: la sala entera recibe energía cuando el segundo suelo eléctrico
  pasa bajo al menos una celda de su planta. Todos los consumidores de la sala se conectan a ese
  punto común.

Una celda situada fuera y pegada a una pared no alimentará automáticamente la sala mientras se
use el plano B. El jugador deberá prolongar la franja al menos una celda bajo el suelo interior.
Esto hace visible la intención y evita conexiones accidentales.

### Central eléctrica

La central debe ser una sala utilitaria, sin pacientes y sin personal obligatorio en la primera
versión. Requisitos propuestos:

- tamaño mínimo 3x3;
- puerta;
- al menos un generador;
- generadores exentos de necesitar suministro externo;
- cada generador aporta capacidad a la componente de cable a la que está conectado;
- un generador averiado, ardiendo o en mantenimiento deja de aportar capacidad.

Más adelante se puede añadir un trabajo de conserje/ingeniero y combustible, pero no conviene
mezclar esas mecánicas con la primera prueba de conectividad.

### Capacidad y demanda

`EnergyCost` está expresado en dinero y no debe interpretarse directamente como vatios. Se
mantendrá un perfil propio por GUID de definición:

- exento;
- consumo bajo;
- consumo medio;
- consumo alto;
- generador con capacidad concreta.

Como valor de respaldo, un objeto con `EnergyCost > 0` se considerará consumidor de nivel bajo,
pero las máquinas de diagnóstico y tratamiento tendrán ajustes explícitos.

Cuando una red tenga menos capacidad que demanda, se aplicará un orden estable para evitar que
las máquinas parpadeen entre encendido y apagado. Orden inicial recomendado:

1. tratamiento en curso y emergencias;
2. diagnóstico;
3. operación de salas auxiliares;
4. máquinas de pasillo y confort;
5. decoración animada y objetos secundarios.

Dentro de la misma prioridad se conserva un identificador estable del objeto. La asignación se
recalcula solo cuando cambia la topología, generación, demanda o estado funcional.

### Apariencia de las baldosas

Cada segmento debe escoger una variante según sus vecinos: aislado, extremo, recta, esquina,
unión T o cruce. El material cambia de estado:

- apagado: gris oscuro;
- conectado pero sin capacidad: rojo/naranja;
- alimentado: azul o verde luminoso;
- modo construcción: resaltado de validez.

No hace falta crear seis modelos distintos: basta un plano o alfombra con material y rotación,
o un atlas con variantes. La capa debe quedar visualmente por debajo de las máquinas.

## Arquitectura propuesta del mod

`PowerGridManager` por nivel:

- escucha `BuildEvents.OnRoomItemAdded/Removed`, salas construidas/eliminadas y cambios de
  mantenimiento;
- indexa conductores, generadores y consumidores por `GridCoord`;
- calcula componentes conectados;
- determina capacidad, demanda, asignación y habitaciones alimentadas;
- expone `IsPowered(RoomItem)` e `IsPowered(Room)`;
- actualiza visuales e iconos solo si cambia un estado.

`PowerDefinitionRegistry`:

- clasifica definiciones por GUID;
- contiene capacidades, demandas, prioridad y exenciones;
- permite ajustes de configuración sin parches repartidos por todo el código.

`PowerTileVisualController`:

- calcula máscara de vecinos N/E/S/O;
- rota o cambia la variante visual;
- aplica color de estado.

Parches Harmony mínimos:

- alta/baja de nivel para crear/destruir el gestor;
- eventos de construcción para marcar la red como sucia;
- `RoomItem.IsFunctional()` para bloquear consumidores sin energía;
- `Room.IsFunctional()` y panel de inspector para el estado de sala;
- comprobación adicional de disponibilidad de interacción;
- estado de mantenimiento/reparación de generadores;
- iconos/tooltip y futura vista de datos eléctrica.

## Guardado y compatibilidad

La red se deriva enteramente de los objetos colocados y no necesita guardar su grafo.

Para el segundo plano se estudiaron tres formas de persistencia:

1. archivo lateral por partida: sencillo, pero se rompe con nube, copias, renombrados y copias de
   seguridad;
2. componente serializable propio: compacto, pero el nombre de su ensamblado queda dentro del
   guardado y complicaría fusionar después una DLL experimental;
3. marcadores `RoomItem` invisibles de una celda: el jugador no los coloca como objetos, pero el
   cursor crea o elimina internamente los necesarios al confirmar cada arrastre.

Se recomienda la tercera para el prototipo. La interfaz seguirá siendo un suelo continuo del
segundo plano; los marcadores solo serán el soporte de guardado nativo. No tendrán colisión,
navegación, coste energético, selección ni render normal. En la vista eléctrica el controlador
dibujará una malla agrupada, no cientos de alfombras visibles.

Esta solución evita modificar el formato del archivo de partida. También permite reconstruir el
`PowerFloorPlan` al cargar enumerando los marcadores con un GUID estable.

La opción más segura es que cada conductor sea un `RoomItem` con GUID estable: así la posición,
compra, venta y pertenencia a sala viajan en el guardado nativo. Las definiciones personalizadas
deben registrarse antes de restaurar la partida.

El mayor riesgo es la **sala completamente nueva**. `RoomDefinition.Type` es un enum cerrado y
muchos sistemas del juego lo usan como identificador. Técnicamente se puede registrar una
`RoomDefinition` creada en tiempo de ejecución con un valor numérico reservado, pero hay que
probar:

- lista de construcción;
- creación y edición;
- guardado/carga;
- plantillas de sala;
- filtros de objetos;
- sandbox y desbloqueos;
- partidas que se abren sin el mod.

No se debe reutilizar silenciosamente el tipo de otra sala, porque contaminaría estadísticas,
plantillas y lógica de trabajos. Primero se hará una sala mínima sin lógica propia y se validará
un ciclo completo de guardado/carga.

## DLL experimental separada

Conviene desarrollar esta función como `UnderPressure.PowerGrid.dll`, plugin BepInEx separado,
con GUID Harmony y GUID BepInEx propios. Ventajas:

- una avería no impide cargar el mod 0.1 actual;
- puede activarse/desactivarse retirando una sola DLL;
- sus parches y logs quedan identificados;
- obliga a mantener la arquitectura eléctrica desacoplada.

Reglas para que la fusión futura sea segura:

- no parchear dos veces el mismo método con la misma responsabilidad;
- no duplicar el botón/modo eléctrico 602 que ya instala `UnderPressure.dll`;
- detectar el modo 602 existente y aportar únicamente capa, colores y cursor;
- mantener GUIDs de definiciones constantes antes y después de fusionar;
- no serializar clases cuyo tipo pertenezca a la DLL experimental;
- retirar la DLL experimental al instalar una versión fusionada, pues dejar ambas produciría
  doble cálculo, dobles eventos y posibles cobros duplicados;
- usar un GUID Harmony distinto para que cada DLL solo pueda desparchearse a sí misma.

Mientras sea experimental puede depender de que `UnderPressure.dll` esté presente, pero sin
acceder directamente a sus clases `internal`. Para el primer prototipo basta observar el modo
602 mediante reflexión. Si finalmente se conservan dos módulos a largo plazo, se creará una API
pública mínima en el plugin principal en lugar de seguir usando reflexión.

## Orden de prototipos

### P0 — inspección dinámica

Crear una herramienta de diagnóstico que enumere en una partida:

- definiciones de sala y objetos cargadas;
- GUID, tipo, prefab y restricciones de colocación;
- objetos nativos con `GeneratesElectricity`;
- generadores y elementos visuales que podamos clonar legalmente en memoria.

### P1 — segundo plano y red invisible

Sin sala nueva ni arte:

- marcar temporalmente un objeto existente como generador de prueba;
- implementar una matriz/colección de celdas independiente de las salas;
- imitar el arrastre de `CursorRoomBuild` para añadir y restar franjas;
- calcular componentes y mostrar el resultado en el log;
- verificar adyacencia, separación y cambios al vender/mover.

### P2 — apagado real

- clasificar consumidores por `EnergyCost` y lista explícita;
- parchear funcionalidad;
- añadir tooltip e icono “sin electricidad”;
- probar diagnóstico, tratamiento, colas, mantenimiento y una interacción ya iniciada.

### P3 — central eléctrica mínima

- registrar la nueva `RoomDefinition`;
- registrar generador y baldosa propios;
- probar construir, editar, copiar, vender, guardar y cargar.

### P4 — presentación

- modelos/materiales definitivos;
- trazado por arrastre;
- vista de datos eléctrica;
- panel capacidad/demanda y prioridades;
- sonidos y localización.

## Criterios de aceptación del primer prototipo jugable

1. Dos redes separadas no comparten energía.
2. Quitar una baldosa intermedia corta el suministro inmediatamente.
3. Una sala se alimenta con un único punto interior y no desde el otro lado de una pared.
4. Una máquina sin energía no acepta nuevos usuarios.
5. Reparar un generador o añadir capacidad reactiva consumidores de forma determinista.
6. Guardar y cargar conserva conductores, generadores y sala.
7. Desactivar el mod no corrompe el guardado; como mínimo se rechaza la carga con una advertencia
   clara si contiene definiciones propias.

## Conclusión actual

La idea es viable con BepInEx/Harmony. El juego aporta suficientes puntos de integración para
construcción, cuadrícula, salas, funcionalidad y visualización, pero no existe una red eléctrica
lista para reutilizar. La solución robusta es una red discreta propia, con baldosas como objetos
nativos y el sistema de calor/atractivo usado solo como patrón visual y de eventos.
