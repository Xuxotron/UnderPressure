# Rutas del proyecto

- Repositorio y directorio de trabajo: `D:\Mods\Two Point Hospital`
- Proyecto principal: `D:\Mods\Two Point Hospital\UnderPressure Base\UnderPressure.csproj`
- Proyecto de electricidad: `D:\Mods\Two Point Hospital\UnderPressure Power\UnderPressurePower.csproj`
- Instalación del juego: `D:\Steam\steamapps\common\TPH`
- Mod instalado: `D:\Steam\steamapps\common\TPH\Mods`
- AssetBundle activo: `D:\Steam\steamapps\common\TPH\Mods\Assets\underpressure`
- Proyecto de Unity: `C:\Users\Artef\UnderPresssure`
- Código principal del mod: `D:\Mods\Two Point Hospital\UnderPressure Base\src`
- Código del sistema eléctrico: `D:\Mods\Two Point Hospital\UnderPressure Power\src`
- Recursos globales: `D:\Mods\Two Point Hospital\Assets`
- Traducciones globales: `D:\Mods\Two Point Hospital\translations.csv` y `translations_extra.csv`
- DLL principal instalada: `D:\Steam\steamapps\common\TPH\Mods\UnderPressure.dll`
- DLL del editor instalada: `D:\Steam\steamapps\common\TPH\Mods\UnderPressure.Editor.dll`
- DLL eléctrica instalada: `D:\Steam\steamapps\common\TPH\Mods\UnderPressure.Energy.dll`

# Reglas específicas

- Trabajar siempre en el repositorio, nunca sobre los fuentes desde la instalación del juego.
- `D:\Games\Two Point Hospital` es una ruta inexistente y prohibida. No leerla, enumerarla,
  comprobarla, escribir en ella ni usarla para compilar o desplegar bajo ninguna circunstancia.
- La única instalación válida del juego es `D:\Steam\steamapps\common\TPH` y todos los
  despliegues del mod deben ir directamente a `D:\Steam\steamapps\common\TPH\Mods`.
- Para el sistema eléctrico, usar `UnderPressure Power\UnderPressurePower.csproj`; su referencia también compila `UnderPressure Base\UnderPressure.csproj`.
- `UnderPressure.actual.csproj` es obsoleto y no debe usarse para compilar.
- Una compilación de C# no debe copiar, reemplazar ni modificar el AssetBundle `Assets\underpressure`; ese archivo se genera desde Unity.
- Para comprobar una compilación sin desplegar, usar la propiedad `SkipDeploy=true`.
- Después de cualquier cambio de código, compilar y desplegar siempre salvo que el usuario indique expresamente que no se compile.
- En Codex, toda compilación que despliegue DLL en la instalación del juego debe ejecutarse con permisos elevados desde el primer intento. No atribuir un error de acceso denegado al juego sin haber comprobado antes si procede del aislamiento de escritura.
- Antes de compilar con despliegue, comprobar si Two Point Hospital está ejecutándose y cerrar automáticamente su proceso si está abierto; el usuario ha autorizado expresamente este cierre para liberar las DLL.
- Cuando el usuario pida compilar, generar las DLL directamente en `D:\Steam\steamapps\common\TPH\Mods` mediante el `OutputPath` de los proyectos; no compilar en `bin` para copiarlas después.
- Usar `SkipDeploy=true` únicamente cuando el usuario pida expresamente una comprobación sin instalar; en ese caso se permite la salida local de `bin`.
- Conservar los cambios existentes del usuario y modificar únicamente lo solicitado.
- Las instrucciones del usuario y las reglas de este archivo se ejecutan literalmente. Codex no puede sustituirlas por una interpretación propia, una solución "equivalente", una simplificación ni algo que considere más rápido o conveniente.
- Cuando el usuario pida copiar, reutilizar o reproducir código nativo, se debe copiar la implementación nativa real después de inspeccionarla por completo. Esto incluye la jerarquía, componentes, propiedades, materiales, efectos, estados, eventos, navegación, foco y entrada. Está prohibido recrearla a ojo, imitar únicamente su aspecto o fabricar una aproximación.
- Nunca priorizar terminar rápido, mostrar progreso o entregar una solución parcial sobre hacer el trabajo correctamente. Si la implementación todavía no está completamente inspeccionada y verificada, no editar ni declarar el resultado terminado.
- No desactivar, eliminar, sustituir ni alterar componentes nativos que el usuario no haya pedido modificar. Antes de filtrar o borrar componentes de un objeto nativo, verificar individualmente la función de cada componente y conservar todos los que formen parte de la referencia solicitada.
- No acumular parches para acercarse visualmente al resultado mediante prueba y error. Si existe una referencia nativa o aprobada, comparar directamente el código y la jerarquía con esa referencia y corregir la causa exacta de cualquier diferencia.
- Una captura sirve para confirmar el resultado visual, no para adivinar la implementación. La estructura debe obtenerse del código, prefab, configuración y datos nativos correspondientes.
- Cuando el usuario pida una interfaz, control o comportamiento nativo, se debe localizar y reutilizar fielmente la estructura real del juego: componentes, prefab, jerarquía, estados, navegación, foco, entrada y eventos. Se permiten cambios de colores, sprites u otros detalles visuales cuando el usuario los solicite, pero nunca reemplazar la estructura o el comportamiento nativos por una recreación, una aproximación personalizada, capas transparentes, interceptores globales, controles simulados o cualquier otro parche. Si la implementación nativa no puede localizarse o verificarse, detenerse y comunicárselo al usuario antes de editar; nunca improvisar una alternativa.
- Toda interfaz nueva debe construirse de forma sistemática reutilizando los mismos componentes y funciones comunes ya aprobados en el proyecto. No crear variantes puntuales ni modificar controles previamente aprobados salvo petición expresa del usuario.
- Guardar siempre cada cambio terminado en un commit independiente para conservar el historial, permitir comparaciones y evitar perder el contexto de lo realizado.
- Ante cualquier regresión —algo que funcionaba y deja de funcionar después de un cambio— detener las modificaciones, identificar primero el commit que introdujo el fallo mediante `git diff`, `git log` o `git bisect`, comparar con el último estado validado por el usuario y revertir el cambio causante. Está prohibido acumular parches sobre una regresión sin haber localizado antes su origen. Conservar únicamente los cambios posteriores que estén comprobados y aprobados explícitamente.
- En este proyecto, completar un commit implica publicarlo inmediatamente en GitHub. Ejecutar siempre `git push` después de cada commit salvo que el usuario indique expresamente lo contrario, y verificar que la rama local y la remota quedan sincronizadas.
- Antes de trabajar, leer este archivo, ejecutar `git status` e inspeccionar completamente los archivos relacionados.
- Un chat nuevo no debe pedir de nuevo las rutas ni confundir el repositorio con la instalación del juego.
- La tabla editable de objetos eléctricos está en `UnderPressure Power\src\ElectricObjectCatalog.cs`.
- La lógica eléctrica está en `UnderPressure Power\src\ElectricityGameplay.cs`; no introducir lógica en la tabla del catálogo.
- En dificultad adaptable, los valores de las barras hijas se ignoran y cada variable debe calcularse desde la raíz de su grupo.
- Los comentarios del código deben escribirse en español.
- Todo texto nuevo visible para el usuario debe usar una clave de localización enlazada a `translations.csv` y `translations_extra.csv`; las traducciones en inglés y español deben estar siempre definidas en los archivos de traducción y nunca incrustadas directamente en el código.

# Control de saturación del chat

- Calcula la ocupación del contexto con `last_token_usage.total_tokens / model_context_window * 100` cuando la sesión activa exponga ambos valores.
- Si la ocupación alcanza el 75%, avisa al usuario. Si alcanza el 85%, guarda primero el trabajo terminado en un commit, resume lo pendiente y recomienda continuar en un chat nuevo.
- Cuando el usuario pregunte por la memoria, ocupación, saturación, contexto, tokens o cuánto queda, responde siempre con el porcentaje actual, aunque sea inferior al 75%.
- No uses `total_token_usage.total_tokens` para calcular la ocupación, porque es un contador acumulativo y no representa el contexto actual.
- Si no existe un contador actual accesible, indícalo expresamente y no inventes un porcentaje. Si la conversación ya ha sido compactada o resumida, indícalo también.
