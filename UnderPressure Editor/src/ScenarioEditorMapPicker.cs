// Updated: 2026-10-13
// Selector unico y compacto de hospitales/mapas.
// Las filas visibles son botones NATIVOS de TH20 situados manualmente; sin Mask,
// ScrollRect, VerticalLayoutGroup ni ContentSizeFitter que puedan ocultarlas.
// Cada columna tiene un Scrollbar de Unity y admite rueda del raton.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using TH20;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal static class ScenarioEditorMapPicker
    {
        private sealed class MapChoice
        {
            internal string Path;
            internal string Name;
        }

        private sealed class HospitalChoice
        {
            internal LevelConfig Level;
            internal string Label;
        }

        private sealed class RowEntry
        {
            internal string Label;
            internal Action OnClick;
            internal bool Green;
        }

        private sealed class ScrollColumn
        {
            internal RectTransform Viewport;
            internal Scrollbar Bar;
            internal float Width;
            internal readonly List<RowEntry> Entries = new List<RowEntry>();
            internal readonly List<GameObject> VisibleRows = new List<GameObject>();
            internal int FirstRow;
            internal bool ChangingBar;
        }

        private const int VisibleRows = 7;
        private const float RowHeight = 52f;
        private const float RowSpacing = 5f;
        private const float ColumnHeight = 422f;
        private const float RowInset = 6f;

        private static GameObject _root;
        private static DynamicButton _nativeButtonTemplate;
        private static TMP_Text _nativeTextTemplate;
        private static Action<LevelConfig, string> _onOpen;
        private static Action _onCancel;
        private static LevelConfig _selectedLevel;
        private static ScrollColumn _hospitalsColumn;
        private static ScrollColumn _mapsColumn;
        private static TMP_Text _selectedHospitalTitle;
        private static readonly List<HospitalChoice> _hospitalChoices = new List<HospitalChoice>();
        private static string _pendingMap;

        internal static GameObject CreateSelector(
            Transform canvas, GameObject rowTemplate, IReadOnlyList<LevelConfig> levels,
            Action<LevelConfig, string> openMap, Action cancel)
        {
            if (canvas == null || rowTemplate == null || levels == null || levels.Count == 0)
                throw new ArgumentException("El selector necesita un Canvas, un boton y hospitales.");

            Reset();
            _nativeButtonTemplate = rowTemplate.GetComponentInChildren<DynamicButton>(true);
            if (_nativeButtonTemplate == null)
                throw new InvalidOperationException("No se encontro el DynamicButton nativo.");
            _nativeTextTemplate = _nativeButtonTemplate.GetComponentInChildren<TMP_Text>(true);
            if (_nativeTextTemplate == null)
                throw new InvalidOperationException("No se encontro el texto nativo del boton.");
            _onOpen = openMap;
            _onCancel = cancel;

            _root = Ui("UP_ScenarioEditorSelector", canvas, typeof(Image));
            Stretch((RectTransform)_root.transform);
            var overlay = _root.GetComponent<Image>();
            overlay.color = new Color(0f, 0f, 0f, 0.58f);
            overlay.raycastTarget = true;
            _root.transform.SetAsLastSibling();

            var panel = Ui("Panel", _root.transform, typeof(Image));
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.69f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(864f, 595f);
            var panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.175f, 0.200f, 0.225f, 0.97f);
            panelImage.raycastTarget = true;

            CreateHeader(panel.transform, "Escenario base",
                new Vector2(23f, -15f), new Vector2(418f, 44f));
            _selectedHospitalTitle = CreateHeader(panel.transform, "Hospital",
                new Vector2(466f, -15f), new Vector2(375f, 44f));

            _hospitalsColumn = CreateColumn(panel.transform, "Hospitales",
                new Vector2(22f, -68f), new Vector2(421f, ColumnHeight));
            _mapsColumn = CreateColumn(panel.transform, "Mapas",
                new Vector2(464f, -68f), new Vector2(379f, ColumnHeight));

            // Este mismo tipo de boton se ve correctamente FUERA del area de listas.
            CreateNativeRow(panel.transform, EditorLocalization.Get("scenario_editor.return_to_menu"),
                new Vector2(23f, -511f), new Vector2(418f, 56f), () =>
                {
                    ClearPendingMap();
                    _onCancel?.Invoke();
                }, false);

            foreach (var level in levels.Where(l => l != null).Distinct()
                                        .OrderBy(l => l.GetLocalisedDisplayName(),
                                                 StringComparer.CurrentCultureIgnoreCase))
            {
                var captured = level;
                var label = captured.GetLocalisedDisplayName();
                _hospitalChoices.Add(new HospitalChoice { Level = captured, Label = label });
                _hospitalsColumn.Entries.Add(new RowEntry
                {
                    Label = label,
                    OnClick = () => SelectHospital(captured)
                });
            }

            RefreshRows(_hospitalsColumn);
            if (_hospitalChoices.Count > 0)
                SelectHospital(_hospitalChoices[0].Level);
            Canvas.ForceUpdateCanvases();
            return _root;
        }

        private static void SelectHospital(LevelConfig level)
        {
            if (level == null || _root == null) return;
            _selectedLevel = level;
            if (_selectedHospitalTitle != null)
                _selectedHospitalTitle.text = level.GetLocalisedDisplayName();

            // Solo cambia el texto de las filas visibles; no se recrea la columna izquierda.
            RefreshRows(_hospitalsColumn);

            _mapsColumn.Entries.Clear();
            _mapsColumn.FirstRow = 0;
            _mapsColumn.Entries.Add(new RowEntry
            {
                Label = "Nuevo mapa",
                Green = true,
                OnClick = () => OpenSelected(null)
            });

            foreach (var map in ReadMaps(level.UniqueId))
            {
                var path = map.Path;
                _mapsColumn.Entries.Add(new RowEntry
                {
                    Label = map.Name,
                    OnClick = () => OpenSelected(path)
                });
            }
            RefreshRows(_mapsColumn);
        }

        private static void OpenSelected(string mapPath)
        {
            if (_selectedLevel == null || _onOpen == null) return;
            // Comprobar el mapa antes de abandonar la pantalla inicial. No
            // comenzar a cargar un escenario cuyo JSON/PNG no puede abrirse.
            if (!string.IsNullOrEmpty(mapPath))
            {
                try { ScenarioMapFormat.Read(mapPath); }
                catch (Exception ex)
                {
                    UnderPressureEditorPlugin.Log?.LogError(
                        "No se puede abrir el mapa seleccionado: " + ex);
                    if (_selectedHospitalTitle != null)
                        _selectedHospitalTitle.text = "Mapa incorrecto o incompleto";
                    return;
                }
            }
            _pendingMap = mapPath;
            _onOpen(_selectedLevel, mapPath);
        }

        internal static void OnEditorOpened(Level level)
        {
            var mapPath = _pendingMap;
            if (string.IsNullOrEmpty(mapPath)) return;
            // No consumir el mapa cuando el evento de apertura aun no termino
            // de inicializar el editor nativo. Si se pierde este valor se abre
            // silenciosamente el hospital original en lugar del mapa solicitado.
            if (level == null || !ScenarioEditorSession.Active)
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Mapa pendiente; el editor aun no esta activo: " + mapPath);
                return;
            }
            _pendingMap = null;
            try
            {
                ScenarioEditorStorage.Load(level, mapPath);
                UnderPressureEditorPlugin.Log?.LogInfo("Editor: mapa cargado: " + mapPath);
            }
            catch (Exception exception)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudo cargar el mapa: " + exception);
            }
        }

        internal static void Reset()
        {
            _root = null;
            _nativeButtonTemplate = null;
            _nativeTextTemplate = null;
            _onOpen = null;
            _onCancel = null;
            _selectedLevel = null;
            _hospitalsColumn = null;
            _mapsColumn = null;
            _selectedHospitalTitle = null;
            // La seleccion puede sobrevivir al cierre/recreacion del Canvas
            // mientras se carga el nivel. Solo se borra al aplicarla o cancelar.
            _hospitalChoices.Clear();
        }

        internal static void ClearPendingMap() => _pendingMap = null;

        private static List<MapChoice> ReadMaps(string hospitalId)
        {
            var result = new List<MapChoice>();
            var folder = ScenarioEditorStorage.Folder;
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "*.upmap", SearchOption.AllDirectories))
            {
                try
                {
                    var header = ScenarioMapFormat.ReadHeader(file);
                    if (!string.Equals(header.baseHospitalId, hospitalId, StringComparison.Ordinal)) continue;
                    result.Add(new MapChoice
                    {
                        Path = file,
                        Name = string.IsNullOrWhiteSpace(header.name)
                            ? Path.GetFileNameWithoutExtension(file) : header.name
                    });
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Mapa omitido: " + file + " - " + exception.Message);
                }
            }
            return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static ScrollColumn CreateColumn(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var host = Ui(name, parent, typeof(Image));
            var hostRect = (RectTransform)host.transform;
            hostRect.anchorMin = hostRect.anchorMax = new Vector2(0f, 1f);
            hostRect.pivot = new Vector2(0f, 1f);
            hostRect.anchoredPosition = pos;
            hostRect.sizeDelta = size;
            var background = host.GetComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.06f);
            background.raycastTarget = true;

            // NUNCA hay elementos fuera de pantalla: se crean solo las 7 filas visibles.
            // Por eso no necesitamos Mask / RectMask2D ni ContentSizeFitter.
            var viewport = Ui("VisibleButtons", host.transform, typeof(Image));
            Stretch((RectTransform)viewport.transform);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0f);
            viewportImage.raycastTarget = true;

            var column = new ScrollColumn
            {
                Viewport = (RectTransform)viewport.transform,
                Width = size.x
            };

            var wheel = host.AddComponent<ScenarioEditorWheelHandler>();
            wheel.Scroll = delta =>
            {
                var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
                if (maxFirst == 0 || Mathf.Approximately(delta, 0f)) return;
                column.FirstRow = Mathf.Clamp(column.FirstRow + (delta > 0f ? -2 : 2), 0, maxFirst);
                RefreshRows(column);
            };

            column.Bar = CreateScrollbar(host.transform);
            column.Bar.onValueChanged.AddListener(value =>
            {
                if (column.ChangingBar) return;
                var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
                var row = Mathf.Clamp(Mathf.RoundToInt((1f - value) * maxFirst), 0, maxFirst);
                if (row == column.FirstRow) return;
                column.FirstRow = row;
                RefreshRows(column);
            });
            return column;
        }

        private static void RefreshRows(ScrollColumn column)
        {
            if (column == null || column.Viewport == null) return;
            foreach (var existing in column.VisibleRows)
            {
                if (existing == null) continue;
                existing.SetActive(false); // Evita solapamientos durante el Destroy diferido.
                UnityEngine.Object.Destroy(existing);
            }
            column.VisibleRows.Clear();

            var maxFirst = Math.Max(0, column.Entries.Count - VisibleRows);
            column.FirstRow = Mathf.Clamp(column.FirstRow, 0, maxFirst);
            var hasScroll = maxFirst > 0;
            column.Bar.gameObject.SetActive(hasScroll);
            column.ChangingBar = true;
            column.Bar.numberOfSteps = Math.Max(1, maxFirst + 1);
            column.Bar.size = Mathf.Clamp01((float)VisibleRows / Math.Max(1, column.Entries.Count));
            column.Bar.value = maxFirst == 0 ? 1f : 1f - (float)column.FirstRow / maxFirst;
            column.ChangingBar = false;

            var width = column.Width - (hasScroll ? 30f : 13f);
            for (var row = 0; row < VisibleRows && row + column.FirstRow < column.Entries.Count; ++row)
            {
                var entry = column.Entries[row + column.FirstRow];
                var label = entry.Label;
                if (ReferenceEquals(column, _hospitalsColumn))
                {
                    var selected = _hospitalChoices.FirstOrDefault(
                        x => x.Label == entry.Label && x.Level == _selectedLevel);
                    if (selected != null) label = "› " + label;
                }
                var buttonRow = CreateNativeRow(column.Viewport, label,
                    new Vector2(RowInset, -(RowInset + row * (RowHeight + RowSpacing))),
                    new Vector2(width, RowHeight), entry.OnClick, entry.Green);
                column.VisibleRows.Add(buttonRow);
            }
        }

        // Todos los botones, tanto el visible inferior como los de las listas,
        // usan IDENTICA creacion y posicion absoluta (ninguno depende de LayoutGroup).
        private static GameObject CreateNativeRow(
            Transform parent, string label, Vector2 position, Vector2 size, Action action, bool green)
        {
            var row = Ui("UP_" + label, parent);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var button = UnityEngine.Object.Instantiate(_nativeButtonTemplate, row.transform, false);
            button.gameObject.name = "NativeButton";
            button.gameObject.SetActive(true);
            button.enabled = true;
            var buttonRect = button.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.one;
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.offsetMin = Vector2.zero;
                buttonRect.offsetMax = Vector2.zero;
                buttonRect.anchoredPosition = Vector2.zero;
                buttonRect.localScale = Vector3.one;
            }

            DisableNativeLocalizers(button.gameObject);
            button.onPrimaryDown.RemoveAllListeners();
            button.onPrimaryDown.AddListener(() => action?.Invoke());
            button.interactable = true;
            button.SetTMPText(label);

            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.gameObject.SetActive(true);
                text.enabled = true;
                text.enableAutoSizing = true;
                text.fontSizeMin = 13f;
                text.fontSizeMax = 23f;
                text.fontSize = Mathf.Min(text.fontSize, 23f);
                text.raycastTarget = false;
            }

            if (green)
            {
                var colors = button.colors;
                colors.normalColor = new Color(0.22f, 0.96f, 0.22f, 1f);
                colors.highlightedColor = new Color(0.42f, 1f, 0.34f, 1f);
                colors.pressedColor = new Color(0.13f, 0.75f, 0.16f, 1f);
                colors.selectedColor = colors.highlightedColor;
                button.colors = colors;
                if (button.targetGraphic != null)
                    button.targetGraphic.color = colors.normalColor;
            }
            return row;
        }

        private static TMP_Text CreateHeader(Transform parent, string label, Vector2 pos, Vector2 size)
        {
            var header = Ui("Header_" + label, parent);
            var rect = (RectTransform)header.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var textObject = UnityEngine.Object.Instantiate(_nativeTextTemplate.gameObject, header.transform, false);
            textObject.name = "Title";
            textObject.SetActive(true);
            DisableNativeLocalizers(textObject);
            var text = textObject.GetComponent<TMP_Text>();
            if (text == null) return null;
            Stretch(text.rectTransform);
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 16f;
            text.fontSizeMax = 28f;
            text.raycastTarget = false;
            return text;
        }

        private static Scrollbar CreateScrollbar(Transform parent)
        {
            var track = Ui("Scrollbar", parent, typeof(Image), typeof(Scrollbar));
            var rect = (RectTransform)track.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-14f, 4f);
            rect.offsetMax = new Vector2(-3f, -4f);
            track.GetComponent<Image>().color = new Color(0.03f, 0.08f, 0.11f, 0.65f);

            var sliding = Ui("Sliding Area", track.transform);
            Stretch((RectTransform)sliding.transform);
            var handle = Ui("Handle", sliding.transform, typeof(Image));
            var handleRect = (RectTransform)handle.transform;
            Stretch(handleRect);
            var handleImage = handle.GetComponent<Image>();
            var sourceImage = _nativeButtonTemplate.GetComponent<Image>();
            if (sourceImage != null)
            {
                handleImage.sprite = sourceImage.sprite;
                handleImage.type = sourceImage.type;
            }
            handleImage.color = new Color(0.08f, 0.77f, 0.93f, 1f);
            var scrollbar = track.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.value = 1f;
            return scrollbar;
        }

        private static void DisableNativeLocalizers(GameObject root)
        {
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (component != null && component.GetType().FullName == "I2.Loc.Localize")
                    component.enabled = false;
        }

        private static GameObject Ui(string name, Transform parent, params Type[] components)
        {
            var types = new List<Type> { typeof(RectTransform), typeof(CanvasRenderer) };
            types.AddRange(components);
            var result = new GameObject(name, types.ToArray());
            result.transform.SetParent(parent, false);
            return result;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    // Maneja la rueda del raton sin depender del ScrollRect o de mascaras UI.
    public sealed class ScenarioEditorWheelHandler : MonoBehaviour, IScrollHandler
    {
        internal Action<float> Scroll;

        public void OnScroll(PointerEventData eventData)
        {
            if (Scroll != null) Scroll(eventData.scrollDelta.y);
        }
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "OpenNativeEditor")]
    internal static class ScenarioEditorMapPickerOpenPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Level level) => ScenarioEditorMapPicker.OnEditorOpened(level);
    }

    [HarmonyPatch(typeof(ScenarioEditorFlow), "ResetAtOpeningScreen")]
    internal static class ScenarioEditorMapPickerResetPatch
    {
        private static void Prefix()
        {
            // Nunca perder la ruta mientras sigue en curso la carga del Editor.
            // Reset solo limpia el Canvas. La ruta se consume en OnEditorOpened.
            // El flujo puede transicionar por OpeningScreen antes de abrir Level:
            // NO borrar aqui _pendingMap, aunque IsRunning pase a false.
            ScenarioEditorMapPicker.Reset();
        }
    }
}
