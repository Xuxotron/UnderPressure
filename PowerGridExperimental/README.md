# UnderPressure.PowerGrid (experimental)

Prototipo aislado del segundo plano de suelo eléctrico.

## Estado actual

- DLL BepInEx independiente de `UnderPressure.dll`.
- Se conecta al modo de vista eléctrica personalizado `602` ya existente.
- Construye líneas ortogonales de un cuarto de baldosa (1x1 unidad) mediante arrastre.
- Permite añadir y borrar líneas que atraviesan habitaciones existentes.
- Admite interiores y los caminos autorizados que conectan edificios, usando la máscara
  nativa `IndoorOrPathState`; no permite extender cables libremente por los jardines.
- En la vista eléctrica, esos caminos autorizados reciben una capa blanca para distinguirlos
  visualmente del resto del exterior.
- Mientras la vista eléctrica está activa, el cursor normal no puede seleccionar ni
  arrastrar personajes, habitaciones u objetos situados bajo el cable.
- Las celdas viven únicamente en memoria: todavía no se guardan, cuestan dinero ni alimentan
  máquinas.

## Prueba manual

1. Iniciar el juego con las dos DLL instaladas.
2. Cargar un hospital.
3. Abrir las vistas de datos y activar Electricidad.
4. Elegir `Añadir` o `Eliminar` en los botones de texto clonados del menú principal.
5. Arrastrar con el botón izquierdo para crear o eliminar una línea recta.
6. Al borrar, las losas se ocultan durante el arrastre antes de confirmarlo.
7. El botón derecho cancela el arrastre actual y restaura su previsualización.

## Medidas fijas del panel eléctrico

Medidas tomadas de la referencia de 820x281 px, no estimadas visualmente:

- Borde exterior aproximado: `x=497..596`, `y=174..269`.
- Relleno cian continuo: `x=501..591`, `y=180..268`.
- Altura del panel: `96` unidades de UI.
- El borde inferior comparte exactamente la línea inferior del HUD principal.
- El borde izquierdo conserva un solape de `5` unidades con el panel existente.
- La versión con botones de texto mide como mínimo `118` unidades de ancho y crece
  exclusivamente hacia la derecha.

## Seguridad

El prototipo no modifica el formato de guardado ni parchea la funcionalidad de máquinas. Sus
objetos visuales usan `HideAndDontSave` y se destruyen al abandonar el controlador.
