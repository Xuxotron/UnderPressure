// Updated: 2026-10-11
// Mapas .upmap independientes; carga limpia sin superponer vistas ni objetos anteriores.
// La interfaz utiliza Unity UI; no necesita UnityEngine.IMGUIModule (GUI/GUILayout).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using FullInspector;
using HarmonyLib;
using TMPro;
using TH20;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal static class ScenarioEditorStorage
    {
        private const string Magic = "UNDERPRESSURE_MAP_1";
        private const int MaxMapBytes = 128 * 1024 * 1024;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly System.Reflection.FieldInfo ItemsField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "Items");
        private static readonly System.Reflection.FieldInfo BuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "BuiltItems");
        private static readonly System.Reflection.FieldInfo UnbuiltField =
            AccessTools.Field(typeof(HospitalPlotDefinition), "UnbuiltItems");

        internal sealed class MapInfo
        {
            internal string Path;
            internal string Name;
            internal string BaseId;
            internal string BaseName;
            internal bool Valid;
        }

        private sealed class SavedItem
        {
            internal HospitalPlotLayer Layer;
            internal Guid Guid;
            internal string Tag;
            internal Vector3 Position;
            internal float Rotation;
        }

        private sealed class SavedPlot
        {
            internal int Index;
            internal int Width;
            internal int Height;
            internal Color32[] Pixels;
            internal readonly List<SavedItem> Items = new List<SavedItem>();
        }

        private sealed class SavedMap
        {
            internal string BaseId;
            internal string BaseName;
            internal string Name;
            internal readonly Dictionary<int, SavedPlot> Plots = new Dictionary<int, SavedPlot>();
        }

        // Paths.ConfigPath apunta a BepInEx/config, independientemente de la unidad de instalación.
        internal static string Folder => Path.Combine(Paths.ConfigPath, "UnderPressure", "maps");

        internal static List<MapInfo> ListSaved()
        {
            var result = new List<MapInfo>();
            if (!Directory.Exists(Folder)) return result;
            foreach (var path in Directory.GetFiles(Folder, "*.upmap", SearchOption.TopDirectoryOnly))
            {
                var info = ReadInfo(path);
                result.Add(info);
            }
            result.Sort((left, right) =>
            {
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(left.BaseName, right.BaseName);
                return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });
            return result;
        }

        private static MapInfo ReadInfo(string path)
        {
            var result = new MapInfo
            {
                Path = path,
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                BaseId = "",
                BaseName = "Desconocido",
                Valid = false
            };
            try
            {
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    if (reader.ReadLine() != Magic) return result;
                    var parts = (reader.ReadLine() ?? "").Split('|');
                    if (parts.Length != 4 || parts[0] != "M") return result;
                    result.BaseId = Dec(parts[1]);
                    result.BaseName = Dec(parts[2]);
                    result.Name = Dec(parts[3]);
                    result.Valid = !string.IsNullOrEmpty(result.BaseId) &&
                                   !string.IsNullOrEmpty(result.Name);
                }
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Mapa ilegible " + path + ": " + exception.Message);
            }
            return result;
        }

        internal static string Save(Level level, string displayName)
        {
            RequireEditor(level);
            var name = (displayName ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("Escribe un nombre para el mapa.");
            if (name.Length > 80) throw new ArgumentException("El nombre supera los 80 caracteres.");
            if (ItemsField == null || BuiltField == null || UnbuiltField == null)
                throw new MissingFieldException("No se localizaron las listas originales del paisaje.");

            var baseId = level.Config.UniqueId;
            var baseName = level.Config.GetLocalisedDisplayName();
            var lines = new List<string>
            {
                Magic,
                "M|" + Enc(baseId) + "|" + Enc(baseName) + "|" + Enc(name)
            };
            var index = 0;
            foreach (var plot in level.WorldState.HospitalPlots)
            {
                var definition = plot?.Definition;
                if (definition?.FloorImage == null)
                    throw new InvalidOperationException("Parcela " + index + " sin imagen de suelo.");
                var texture = definition.FloorImage;
                lines.Add("P|" + index.ToString(Invariant) + "|" +
                          texture.width.ToString(Invariant) + "|" +
                          texture.height.ToString(Invariant) + "|" +
                          Convert.ToBase64String(ToBytes(texture.GetPixels32())));
                foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                {
                    foreach (var item in definition.GetItems(layer))
                    {
                        var def = item?.Definition?.Instance;
                        if (def == null) continue;
                        lines.Add(string.Join("|", "I", index.ToString(Invariant),
                            ((int)layer).ToString(Invariant), def.GUID.ToString("D"),
                            Enc(def.DebugTag ?? ""), F(item.Position.x), F(item.Position.y),
                            F(item.Position.z), F(item.Rotation)));
                    }
                }
                ++index;
            }
            lines.Add("END");

            Directory.CreateDirectory(Folder);
            var filename = SafeName(baseId) + "__" + SafeName(name) + ".upmap";
            var fullPath = Path.Combine(Folder, filename);
            var temporary = fullPath + ".tmp";
            try
            {
                File.WriteAllLines(temporary, lines.ToArray(), new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Copy(fullPath, fullPath + ".bak", true);
                File.Copy(temporary, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            UnderPressureEditorPlugin.Log?.LogInfo("Mapa .upmap guardado: " + fullPath);
            return fullPath;
        }

        internal static void Load(Level level, string path)
        {
            RequireEditor(level);
            if (ItemsField == null || BuiltField == null || UnbuiltField == null)
                throw new MissingFieldException("No se localizaron las listas originales del paisaje.");
            var full = System.IO.Path.GetFullPath(path);
            var allowed = System.IO.Path.GetFullPath(Folder).TrimEnd(System.IO.Path.DirectorySeparatorChar) +
                          System.IO.Path.DirectorySeparatorChar;
            if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Solo pueden abrirse .upmap de la carpeta maps.");
            var data = ReadMap(full);
            if (!string.Equals(data.BaseId, level.Config.UniqueId, StringComparison.Ordinal))
                throw new InvalidOperationException("Mapa de otro hospital: " + data.BaseName +
                                                    ". Abre primero ese hospital en el editor.");

            // Validar TODOS los registros antes de modificar la primera parcela.
            var plots = level.WorldState.HospitalPlots;
            if (data.Plots.Count != plots.Count)
                throw new InvalidDataException("El numero de parcelas del mapa no coincide con el hospital.");
            var definitions = BuildDefinitionIndex(level);
            var resolved = new Dictionary<int, List<HospitalPlotItem>>();
            foreach (var pair in data.Plots)
            {
                var plot = pair.Value;
                if (plot.Index < 0 || plot.Index >= plots.Count)
                    throw new InvalidDataException("Indice de parcela desconocido: " + plot.Index);
                var texture = plots[plot.Index]?.Definition?.FloorImage;
                if (texture == null || texture.width != plot.Width || texture.height != plot.Height)
                    throw new InvalidDataException("Dimensiones incompatibles para la parcela " + plot.Index);
                var items = new List<HospitalPlotItem>();
                foreach (var saved in plot.Items)
                {
                    SharedInstance<RoomItemDefinition> wrapper;
                    if (!definitions.TryGetValue(saved.Guid, out wrapper) || wrapper?.Instance == null)
                        throw new InvalidDataException("No existe la definicion del objeto " + saved.Tag +
                                                       " (" + saved.Guid + ").");
                    items.Add(new HospitalPlotItem
                    {
                        Definition = wrapper,
                        Position = saved.Position,
                        Rotation = saved.Rotation
                    });
                }
                resolved.Add(plot.Index, items);
            }

            // Detener las herramientas ANTES de destruir/crear los HospitalMap.
            // MovePlot mantiene referencias a los antiguos FloorPlan y RoomVisual.
            // RoomItem y Build pueden tener una colocacion a medio completar.
            StopActiveEditorTools(level);

            // Preparar primero todas las capas. El mapa cargado REEMPLAZA al anterior.
            var replacements = new Dictionary<int, List<HospitalPlotItem>[]>();
            foreach (var pair in data.Plots)
            {
                var saved = pair.Value;
                var layers = new[]
                {
                    new List<HospitalPlotItem>(),
                    new List<HospitalPlotItem>(),
                    new List<HospitalPlotItem>()
                };
                var items = resolved[saved.Index];
                for (var i = 0; i < items.Count; ++i)
                    layers[(int)saved.Items[i].Layer].Add(items[i]);
                replacements.Add(saved.Index, layers);
            }

            // Conservar la escena actual por si una parcela falla durante la reconstruccion.
            // De ese modo el fallo no deja medio mapa abierto encima del anterior.
            var backups = new Dictionary<int, PlotBackup>();
            foreach (var pair in data.Plots)
            {
                var definition = plots[pair.Key].Definition;
                backups.Add(pair.Key, new PlotBackup
                {
                    Pixels = definition.FloorImage.GetPixels32(),
                    Base = ItemsField.GetValue(definition) as List<HospitalPlotItem>,
                    Built = BuiltField.GetValue(definition) as List<HospitalPlotItem>,
                    Unbuilt = UnbuiltField.GetValue(definition) as List<HospitalPlotItem>
                });
            }

            var lastIndex = -1;
            try
            {
                foreach (var pair in data.Plots)
                {
                    lastIndex = pair.Key;
                    var saved = pair.Value;
                    var definition = plots[lastIndex].Definition;
                    definition.FloorImage.SetPixels32(saved.Pixels);
                    definition.FloorImage.Apply(false, false);
                    var layers = replacements[lastIndex];
                    ItemsField.SetValue(definition, layers[0]);
                    BuiltField.SetValue(definition, layers[1]);
                    UnbuiltField.SetValue(definition, layers[2]);
                }
                // Build(false) reutiliza el HospitalMap, lo que puede dejar restos
                // visuales y referencias anteriores. SetLayerVisible reconstruye
                // por la via nativa completa: destruye el mapa viejo y crea uno nuevo.
                foreach (var pair in data.Plots)
                {
                    lastIndex = pair.Key;
                    RebuildPlotCleanly(plots[lastIndex]);
                    level.HospitalEditEvents.OnHospitalPlotUpdated?.Invoke(plots[lastIndex]);
                }
                level.WorldState.CalculateLighting();
                UnderPressureEditorPlugin.Log?.LogInfo("Mapa .upmap cargado sin superposiciones: " + full);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Error al reconstruir parcela " + lastIndex + " desde " + full + ": " + exception);
                foreach (var pair in backups)
                {
                    var definition = plots[pair.Key].Definition;
                    var backup = pair.Value;
                    definition.FloorImage.SetPixels32(backup.Pixels);
                    definition.FloorImage.Apply(false, false);
                    ItemsField.SetValue(definition, backup.Base);
                    BuiltField.SetValue(definition, backup.Built);
                    UnbuiltField.SetValue(definition, backup.Unbuilt);
                }
                foreach (var pair in backups)
                {
                    try { RebuildPlotCleanly(plots[pair.Key]); }
                    catch (Exception rollbackException)
                    {
                        UnderPressureEditorPlugin.Log?.LogError(
                            "Error restaurando parcela " + pair.Key + ": " + rollbackException);
                    }
                }
                level.WorldState.CalculateLighting();
                throw new InvalidOperationException(
                    "No se pudo abrir el mapa; se han restaurado los datos anteriores. " +
                    "Parcela " + lastIndex + ": " + exception.Message, exception);
            }
        }

        private sealed class PlotBackup
        {
            internal Color32[] Pixels;
            internal List<HospitalPlotItem> Base;
            internal List<HospitalPlotItem> Built;
            internal List<HospitalPlotItem> Unbuilt;
        }

        private static void StopActiveEditorTools(Level level)
        {
            var cursor = level.CursorManager;
            if (cursor == null) return;
            // El orden evita que queden abiertos submodos de colocacion de objetos.
            cursor.PopMode<CursorRoomItem>();
            cursor.PopMode<CursorEditHospitalItem>();
            cursor.PopMode<CursorEditHospitalMovePlot>();
            cursor.PopMode<CursorEditHospitalBuild>();
        }

        private static void RebuildPlotCleanly(HospitalPlot plot)
        {
            if (plot == null) return;
            var visible = plot.IsVisible();
            // Reconstruir con la infraestructura nativa, sin apilar RoomVisual viejos.
            // SetLayerVisible llama internamente a RefreshHospitalMap(false, true).
            var baseVisible = plot.IsLayerVisible(HospitalPlotLayer.Base);
            plot.SetLayerVisible(HospitalPlotLayer.Base, baseVisible);
            if (visible) plot.SetVisible(true);
        }

        private static void RequireEditor(Level level)
        {
            if (!ScenarioEditorSession.Active || !ReferenceEquals(ScenarioEditorSession.Level, level))
                throw new InvalidOperationException("Solo se permite usar mapas dentro del editor de escenarios.");
        }

        private static SavedMap ReadMap(string path)
        {
            var size = new FileInfo(path).Length;
            if (size > MaxMapBytes) throw new InvalidDataException("Mapa demasiado grande.");
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length < 4 || lines[0] != Magic || lines[lines.Length - 1] != "END")
                throw new InvalidDataException("Archivo .upmap incompleto o formato desconocido.");
            var meta = lines[1].Split('|');
            if (meta.Length != 4 || meta[0] != "M")
                throw new InvalidDataException("Cabecera de mapa incorrecta.");
            var map = new SavedMap
            {
                BaseId = Dec(meta[1]),
                BaseName = Dec(meta[2]),
                Name = Dec(meta[3])
            };
            for (var i = 2; i < lines.Length - 1; ++i)
            {
                var parts = lines[i].Split('|');
                if (parts[0] == "P" && parts.Length == 5)
                {
                    var index = int.Parse(parts[1], Invariant);
                    var width = int.Parse(parts[2], Invariant);
                    var height = int.Parse(parts[3], Invariant);
                    if (width <= 0 || height <= 0 || (long)width * height > 16000000)
                        throw new InvalidDataException("Tamano de textura incorrecto.");
                    var raw = Convert.FromBase64String(parts[4]);
                    if (raw.Length != (long)width * height * 4)
                        throw new InvalidDataException("Datos de textura incompletos.");
                    map.Plots.Add(index, new SavedPlot
                    {
                        Index = index,
                        Width = width,
                        Height = height,
                        Pixels = FromBytes(raw)
                    });
                }
                else if (parts[0] == "I" && parts.Length == 9)
                {
                    var index = int.Parse(parts[1], Invariant);
                    SavedPlot plot;
                    if (!map.Plots.TryGetValue(index, out plot))
                        throw new InvalidDataException("Objeto sin parcela anterior.");
                    var layer = int.Parse(parts[2], Invariant);
                    if (layer < 0 || layer > 2)
                        throw new InvalidDataException("Capa de paisaje desconocida.");
                    plot.Items.Add(new SavedItem
                    {
                        Layer = (HospitalPlotLayer)layer,
                        Guid = Guid.Parse(parts[3]),
                        Tag = Dec(parts[4]),
                        Position = new Vector3(Parse(parts[5]), Parse(parts[6]), Parse(parts[7])),
                        Rotation = Parse(parts[8])
                    });
                }
                else
                {
                    throw new InvalidDataException("Registro desconocido: " + parts[0]);
                }
            }
            if (map.Plots.Count == 0) throw new InvalidDataException("El mapa no contiene parcelas.");
            return map;
        }

        private static Dictionary<Guid, SharedInstance<RoomItemDefinition>> BuildDefinitionIndex(Level level)
        {
            var results = new Dictionary<Guid, SharedInstance<RoomItemDefinition>>();
            foreach (var plot in level.WorldState.HospitalPlots)
                foreach (HospitalPlotLayer layer in Enum.GetValues(typeof(HospitalPlotLayer)))
                    foreach (var item in plot.Definition.GetItems(layer))
                        AddDefinition(results, item?.Definition);

            var metagame = level.App.Metagame;
            var landscape = metagame?.LandscapeItemDatabase?.Instance?.RoomItems;
            if (landscape != null)
                foreach (var item in landscape) AddDefinition(results, item);
            var regular = metagame?.RoomItemDatabase?.Instance?.RoomItems;
            if (regular != null)
                foreach (var item in regular) AddDefinition(results, item);
            return results;
        }

        private static void AddDefinition(Dictionary<Guid, SharedInstance<RoomItemDefinition>> results,
            SharedInstance<RoomItemDefinition> wrapper)
        {
            var definition = wrapper?.Instance;
            if (definition == null || results.ContainsKey(definition.GUID)) return;
            results.Add(definition.GUID, wrapper);
        }

        private static string Enc(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        private static string Dec(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
        private static string F(float value) => value.ToString("R", Invariant);
        private static float Parse(string value) => float.Parse(value, NumberStyles.Float, Invariant);
        private static string SafeName(string value)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Trim(' ', '.').Replace(' ', '_');
            if (value.Length == 0) throw new ArgumentException("El nombre no contiene caracteres validos.");
            return value;
        }
        private static byte[] ToBytes(Color32[] pixels)
        {
            var bytes = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; ++i)
            {
                var o = i * 4;
                bytes[o] = pixels[i].r;
                bytes[o + 1] = pixels[i].g;
                bytes[o + 2] = pixels[i].b;
                bytes[o + 3] = pixels[i].a;
            }
            return bytes;
        }
        private static Color32[] FromBytes(byte[] data)
        {
            var pixels = new Color32[data.Length / 4];
            for (var i = 0; i < pixels.Length; ++i)
            {
                var o = i * 4;
                pixels[i] = new Color32(data[o], data[o + 1], data[o + 2], data[o + 3]);
            }
            return pixels;
        }
    }

    // Panel Unity UI: evita depender de GUILayout/GUI y utiliza botones nativos del editor.
    internal sealed class ScenarioEditorStorageMenu : MonoBehaviour
    {
        private static ScenarioEditorStorageMenu _active;
        private readonly List<GameObject> _buttons = new List<GameObject>();
        private EditHospitalMenu _menu;
        private Level _level;
        private Button _template;
        private GameObject _dialog;
        private InputField _nameInput;
        private Text _statusText;
        private Font _font;

        internal static bool IsDialogOpen => _active != null && _active._dialog != null;

        internal void Initialise(EditHospitalMenu menu, Level level)
        {
            if (_menu != null) return;
            _active = this;
            _menu = menu;
            _level = level;
            _template = AccessTools.Field(typeof(EditHospitalMenu), "_quitButton")?.GetValue(menu) as Button;
            if (_template == null)
            {
                UnderPressureEditorPlugin.Log?.LogError("El prefab del editor no contiene su boton de salida.");
                return;
            }
            _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            AddToolbarButton("Guardar mapa", 0, ShowSave);
            AddToolbarButton("Abrir mapa", 1, ShowLoad);
        }

        private void AddToolbarButton(string name, int row, Action action)
        {
            var button = CloneButton(_template, transform, name, action);
            var rect = button.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-18f, -18f - row * 41f);
                rect.sizeDelta = new Vector2(168f, 34f);
            }
            _buttons.Add(button.gameObject);
        }

        private void ShowSave()
        {
            BuildDialog("Guardar mapa");
            if (_dialog == null) return;
            AddText(_dialogPanel, "Nombre del mapa", 16, 62, 500, 28, 20, TextAnchor.MiddleLeft);
            var inputRoot = AddUi("Nombre editable", _dialogPanel);
            SetRect(inputRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(16f, -94f), new Vector2(500f, 37f));
            var inputBackground = inputRoot.AddComponent<Image>();
            inputBackground.color = new Color(0.96f, 0.96f, 0.96f, 1f);
            _nameInput = inputRoot.AddComponent<InputField>();
            _nameInput.targetGraphic = inputBackground;
            _nameInput.characterLimit = 80;
            var text = AddText(inputRoot.transform, "InputText", 10, 3, 478, 31, 19, TextAnchor.MiddleLeft);
            text.color = Color.black;
            text.supportRichText = false;
            _nameInput.textComponent = text;
            var placeholder = AddText(inputRoot.transform, "Placeholder", 10, 3, 478, 31, 19,
                TextAnchor.MiddleLeft);
            placeholder.color = new Color(0.5f, 0.5f, 0.5f, 0.8f);
            placeholder.text = "Nombre del mapa";
            _nameInput.placeholder = placeholder;
            _nameInput.text = "Mi mapa";
            AddDialogButton("Guardar", new Vector2(-85f, -289f), () =>
            {
                try
                {
                    var path = ScenarioEditorStorage.Save(_level, _nameInput.text);
                    ShowStatus("Guardado: " + System.IO.Path.GetFileName(path), false);
                }
                catch (Exception exception)
                {
                    ShowStatus(exception.Message, true);
                    UnderPressureEditorPlugin.Log?.LogError(exception);
                }
            });
        }

        private void ShowLoad()
        {
            BuildDialog("Abrir mapa .upmap");
            if (_dialog == null) return;
            var list = ScenarioEditorStorage.ListSaved();
            if (list.Count == 0)
            {
                AddText(_dialogPanel, "Ningun mapa guardado en la carpeta maps.",
                    20, 100, 480, 40, 18, TextAnchor.MiddleCenter);
                return;
            }
            var scrollRoot = AddUi("Lista de mapas", _dialogPanel);
            SetRect(scrollRoot.GetComponent<RectTransform>(), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -63f),
                new Vector2(500f, 238f));
            var scroll = scrollRoot.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            var viewport = AddUi("Viewport", scrollRoot.transform);
            Stretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            var viewImage = viewport.AddComponent<Image>();
            viewImage.color = new Color(0f, 0f, 0f, 0.14f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var content = AddUi("Contenido", viewport.transform);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(5, 5, 6, 6);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;

            foreach (var map in list)
            {
                var selected = map;
                var matches = map.Valid && string.Equals(map.BaseId, _level.Config.UniqueId,
                    StringComparison.Ordinal);
                var name = map.Name + "  —  " + map.BaseName;
                if (!map.Valid) name += " (archivo invalido)";
                else if (!matches) name += " (otro hospital)";
                var button = CloneButton(_template, content.transform, name, () =>
                {
                    try
                    {
                        ScenarioEditorStorage.Load(_level, selected.Path);
                        CloseDialog();
                    }
                    catch (Exception exception)
                    {
                        ShowStatus(exception.Message, true);
                        UnderPressureEditorPlugin.Log?.LogError(exception);
                    }
                });
                button.interactable = matches;
                var element = button.gameObject.GetComponent<LayoutElement>() ??
                              button.gameObject.AddComponent<LayoutElement>();
                element.minHeight = 39f;
                element.preferredHeight = 39f;
                element.flexibleWidth = 1f;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.fontSize = Mathf.Min(label.fontSize, 19f);
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        }

        private void BuildDialog(string title)
        {
            CloseDialog();
            var canvas = _menu.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se encontro el Canvas del editor.");
                return;
            }
            var backdrop = AddUi("UP Map Dialog Background", canvas.transform);
            Stretch(backdrop.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            backdrop.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.68f);
            backdrop.transform.SetAsLastSibling();
            _dialog = backdrop;
            var panel = AddUi("Panel", backdrop.transform);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(532f, 372f);
            rect.anchoredPosition = Vector2.zero;
            panel.AddComponent<Image>().color = new Color(0.18f, 0.21f, 0.25f, 0.98f);
            _dialogPanel = panel.transform;
            AddText(_dialogPanel, title, 14, 12, 500, 42, 26, TextAnchor.MiddleCenter);
            _statusText = AddText(_dialogPanel, "", 17, 302, 493, 27, 16, TextAnchor.MiddleLeft);
            _statusText.color = Color.white;
            AddDialogButton("Cerrar", new Vector2(85f, -289f), CloseDialog);
        }

        private Transform _dialogPanel;

        private Button AddDialogButton(string title, Vector2 localPosition, Action action)
        {
            var copy = CloneButton(_template, _dialogPanel, title, action);
            var rect = copy.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = localPosition;
                rect.sizeDelta = new Vector2(158f, 35f);
            }
            return copy;
        }

        private static Button CloneButton(Button source, Transform parent, string text, Action action)
        {
            var button = UnityEngine.Object.Instantiate(source, parent, false);
            button.gameObject.name = "UP Map - " + text;
            foreach (var component in button.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
            button.interactable = true;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
            button.gameObject.SetActive(true);
            return button;
        }

        private Text AddText(Transform parent, string content, float x, float y, float width,
            float height, int size, TextAnchor alignment)
        {
            var root = AddUi("Text", parent);
            SetRect(root.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
            var label = root.AddComponent<Text>();
            label.font = _font;
            label.text = content;
            label.fontSize = size;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
            label.raycastTarget = false;
            return label;
        }

        private void ShowStatus(string message, bool error)
        {
            if (_statusText == null) return;
            _statusText.text = message;
            _statusText.color = error ? new Color(1f, 0.54f, 0.44f, 1f) : Color.white;
        }

        private static GameObject AddUi(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot,
            Vector2 anchored, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = anchored;
            rect.sizeDelta = size;
        }

        private void CloseDialog()
        {
            if (_dialog != null) UnityEngine.Object.Destroy(_dialog);
            _dialog = null;
            _dialogPanel = null;
            _nameInput = null;
            _statusText = null;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_active, this)) _active = null;
            CloseDialog();
            foreach (var button in _buttons)
                if (button != null) UnityEngine.Object.Destroy(button);
            _buttons.Clear();
        }
    }

    // Los cursores nativos no deben recibir clics mientras se abre el cuadro de mapas.
    [HarmonyPatch(typeof(CursorEditHospitalBuild), "CursorUpdate")]
    internal static class ScenarioEditorBlockBuildDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(CursorEditHospitalMovePlot), "CursorUpdate")]
    internal static class ScenarioEditorBlockMoveDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(CursorEditHospitalItem), "CursorUpdate")]
    internal static class ScenarioEditorBlockItemsDuringDialogPatch
    {
        private static bool Prefix() => !ScenarioEditorStorageMenu.IsDialogOpen;
    }

    [HarmonyPatch(typeof(EditHospitalMenu), "Setup")]
    internal static class ScenarioEditorStorageUiPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(EditHospitalMenu __instance, Level __0)
        {
            if (!ScenarioEditorSession.Active || __instance == null || __0 == null) return;
            var component = __instance.GetComponent<ScenarioEditorStorageMenu>() ??
                            __instance.gameObject.AddComponent<ScenarioEditorStorageMenu>();
            component.Initialise(__instance, __0);
        }
    }
}
