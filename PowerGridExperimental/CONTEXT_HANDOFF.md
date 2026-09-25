# Under Pressure / Power Grid — contexto recuperado

Fuente de verdad: código presente en `WorkProject/PowerGridExperimental` el 25-09-2026. Este resumen sustituye la consulta del chat antiguo de ~1,11 GiB.

## Objetivo general

Crear para Two Point Hospital un sistema eléctrico jugable: Sala de energía, transformador, baterías, cableado mediante baldosas, propagación de corriente, consumo energético, bloqueo visual/funcional de objetos sin electricidad y campañas propias de la sala.

## Arquitectura completa de la red desde las primeras baldosas

- Se descartó usar calor/atractivo como algoritmo: sirven como referencia visual de mapas, pero electricidad necesita conectividad discreta.
- `GeneratesElectricity` nativo solo alimenta el contador global del desafío de Windsock; no crea red, radio ni cables. La red es propia.
- El cableado es un segundo plano lógico superpuesto al hospital, no una sala nativa y no ocupa el plano normal de habitaciones.
- Cada cable se representa mediante `PowerCoord`, una celda mundial 1×1 centrada en `(X+0.5, Z+0.5)`.
- Puede tenderse sobre suelo interior o caminos/anexos de parcelas compradas (`IndoorOrPathState`). No puede ocupar las celdas que pertenecen al plano fuente de la Sala de energía.
- El jugador no coloca cables uno a uno: selecciona Añadir o Eliminar y arrastra una línea recta horizontal o vertical de longitud libre hasta el límite válido.
- Añadir y Eliminar son conmutadores. Si se pulsa de nuevo la herramienta activa se desactiva; sin herramienta se pueden seleccionar objetos manteniendo abierta la vista eléctrica.
- Clic derecho cancela el arrastre. El bloqueo de selección del mundo solo se activa durante Añadir/Eliminar.
- La vista eléctrica usa el modo personalizado `602`. Normalmente los cables están ocultos.
- En la vista se blanquean únicamente caminos/anexos; los interiores que el juego ya representa no reciben esa capa adicional.
- La Sala de energía completa es el plano fuente: cada celda nativa de su planta se transforma en cuatro `PowerCoord` 1×1 verdes. No se coloca cable dentro de esa superficie.
- Un cable se conecta a la fuente solo si toca cardinalmente una celda fuente.
- Desde esos cables se ejecuta BFS por vecinos norte/sur/este/oeste. No hay conexión diagonal entre cables.
- Cada cable conectado recibe su distancia mínima a la fuente: los primeros valen 1 y las bifurcaciones pueden compartir el mismo valor.
- Los cables sin ruta hasta una Sala de energía aparecen gris oscuro; los conectados, naranja.
- El diagnóstico de flujo muestra el número de distancia y una flecha de entrada entre el predecesor y la celda. Se orienta en el sentido creciente desde la fuente.
- Las flechas usan borde blanco geométrico e interior naranja; el diseño permite variar después de amarillo a rojo según pérdida/potencia.
- Los números están orientados para leerse desde el sur y se mantienen visibles tras los muros.
- Un objeto eléctrico recibe corriente si existe un cable conectado en exactamente una de las ocho celdas 1×1 que rodean su centro, incluidas diagonales. El cable directamente debajo del centro queda excluido.
- Ya no se electrifica automáticamente una habitación completa: la distribución es por proximidad a cada objeto.
- Si no hay energía almacenada, todos los objetos consumidores se consideran sin corriente aunque estén conectados físicamente.
- La red se recalcula por eventos al cambiar cables, salas, geometría, posición/rotación o elementos; no se ejecuta una simulación completa cada frame.

## Implementado

- Sala de energía personalizada (`RoomDefinition.Type` 1000), disponible en Hogsport.
- Transformador, batería y escritorio de Marketing reutilizado como escritorio de energía.
- Herramienta del HUD para añadir/eliminar suelo eléctrico y visualizar flujo/cobertura.
- Guardado complementario `.upsav`, con rotación de copias, para cables, salas, objetos, asignaciones, energía almacenada, fracción diaria y campañas.
- Capacidad: cada batería aporta 1000; las pruebas utilizaban 2000 y comienza cargada.
- Objetos que requieren electricidad quedan negros y no utilizables sin corriente.
- Costes actuales: escritorios solicitados 100/mes, expendedoras 50/mes y secamanos 5 por uso.
- El coste mensual ya se convierte a consumo diario: demanda mensual / 30. La fracción restante se acumula y se guarda para no perder precisión.
- HUD de batería con icono nativo, relleno fucsia y tooltip: Total (negro), Actual (fucsia), Diario (azul oscuro).
- El marco nativo de las etiquetas de consumo por uso es fucsia; el fondo negro del número no debe cambiarse.
- El marco nativo de las etiquetas mensuales/diarias es azul; no se debe añadir un borde artificial fino.
- Marketing se desbloquea específicamente en Hogsport para pruebas.
- El bedel puede trabajar en el escritorio de energía; se eligió la animación de teclado/trabajo y se restaura la navegación al terminar.
- Transformador: desgaste base 0,5 % diario; no reparable actualmente. La reparación del 3 % quedó conservada pero desactivada.
- Protección contra el fallo de analítica mensual causado por el tipo de sala 1000.
- Textos propios registrados en español e inglés.

## Campañas eléctricas

Tres campañas con progreso independiente:

1. Hackear compañía eléctrica: reduce la factura según el porcentaje.
2. Depurar código con IA: reduce el desgaste del transformador según el porcentaje.
3. Negar el cambio climático: porcentaje guardado; se conectará a la generación cuando se implemente producción.

El progreso aumenta 1 punto por día si existe una campaña activa y hay personal trabajando en la Sala de energía. Al llegar a 100, la campaña deja de estar activa, pero conserva su porcentaje. Estado y progreso se guardan en `.upsav`.

## Último punto exacto / pendiente inmediato

- Se creó `EnergyCampaignMenu.cs`, clonando partes visuales del menú nativo de Marketing sin reutilizar su comportamiento.
- El último test del usuario mostró que al pulsar el transformador se abría una ventana que no podía cerrarse y no cargaba campañas.
- El código actual incluye un bloqueador transparente que cierra al hacer clic fuera y un método `Close()`, pero esta versión todavía necesita prueba real dentro del juego.
- Revisar que la lista de tres campañas aparezca, que los botones respondan, que cerrar funcione y que no queden componentes nativos de Marketing interfiriendo.
- Ajustes visuales solicitados pendientes de validar: magenta algo menos saturado, barra de batería anclada desde la base útil del icono y azul del texto Diario legible sobre blanco.

## Decisiones que no deben revertirse

- No usar una reserva azul que reduzca artificialmente la capacidad máxima.
- No descontar el consumo mensual de golpe: consumir diariamente `mensual / 30`.
- Para 600 al mes, el consumo correcto es 20 al día.
- Mantener separados factura monetaria, consumo energético diario y consumo por uso.
- No tocar el fondo negro detrás de los números al recolorear los marcos.
- No modificar ni sustituir el sistema `.upsav` que ya funciona.

## Archivos principales

- `PowerGridPrototype.cs`: red, HUD, energía almacenada, consumo diario, guardado y visualización.
- `ElectricityGameplay.cs`: clasificación de objetos, costes mensuales/por uso y factura.
- `PowerPlantRoom.cs`: definición y desbloqueo de la Sala de energía/Marketing de prueba.
- `EnergyRoomItems.cs`: objetos de sala, trabajo del bedel, desgaste diario y parches.
- `EnergyCampaigns.cs`: estado, efectos, progreso y persistencia de campañas.
- `EnergyCampaignMenu.cs`: interfaz de campañas; foco inmediato de depuración.
- `EnergyLocalization.cs`: textos ES/EN.

## Última compilación/despliegue conocidos antes del fallo del chat

- Hubo compilaciones correctas con 0 errores y 0 advertencias.
- La última DLL explícitamente documentada antes de los cambios finales del menú tenía SHA-256 `7A05D90D821F593EAB3E935D96175ECBEAB74362C78A3DBE2442D2869BBC7D2E`.
- Después se modificaron `PowerGridPrototype.cs`, `ElectricityGameplay.cs`, `EnergyRoomItems.cs` y especialmente `EnergyCampaignMenu.cs`; por ello ese hash no representa necesariamente el código actual.
- Antes de desplegar de nuevo: compilar, comprobar que el juego está cerrado y validar la DLL resultante.
