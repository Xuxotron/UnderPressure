// Updated: 2026-11-04 - Emergencias recupera una raiz transparente propia directamente dentro de su pestana.
using System;
using System.Collections.Generic;
using System.Linq;
using TH20;
using TH20.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private const int EmergencyTabIndex = 4;
        private const float EmergencyContentHeight = 650f;
        private const float EmergencyGap = 10f;
        private const float EmergencyLeftWidth = 245f;
        private const int EmergencyRowsBeforeScroll = 4;

        private static readonly Color EmergencyAddColor =
            new Color32(15, 177, 77, 255);
        private static readonly Color EmergencyRemoveColor =
            new Color32(232, 38, 46, 255);
        // Valores tomados del catálogo UI.
        private static readonly Color EmergencyPanelColor =
            new Color32(27, 104, 153, 255);
        private static readonly Color EmergencyFieldColor =
            Color.white;
        private static readonly Color EmergencyDropdownListColor =
            Color.white;
        private static readonly Color EmergencyFieldTextColor =
            new Color32(50, 50, 50, 255);
        private static readonly Color EmergencyToggleBlue =
            new Color32(38, 148, 205, 255);

        private RectTransform _emergencyContent;
        private RectTransform _emergencyRoot;
        private RectTransform _emergencyListBlock;
        private RectTransform _emergencyConfigBlock;
        private RectTransform _emergencyPrerequisiteBlock;
        private RectTransform _emergencyRewardBlock;

        private ScrollRect _emergencyListScroll;
        private RectTransform _emergencyListContent;
        private TMP_Dropdown _emergencyIllnessDropdown;
        private RectTransform _emergencyChipContent;

        private TMP_Dropdown _emergencyFailureDropdown;
        private TMP_Dropdown _emergencyScoreDropdown;
        private TMP_Dropdown _emergencyArrivalDropdown;
        private Toggle _emergencyWarningToggle;
        private Toggle _emergencyRejectableToggle;

        private TMP_InputField _emergencyPatientsInput;
        private TMP_InputField _emergencyDiagnosisInput;
        private TMP_InputField _emergencySuccessInput;
        private TMP_InputField _emergencyArrivalRateInput;
        private TMP_InputField _emergencyDurationInput;
        private TMP_InputField _emergencyDaysInput;

        private TMP_Dropdown _emergencyPrerequisiteTypeDropdown;
        private ScrollRect _emergencyPrerequisiteScroll;
        private RectTransform _emergencyPrerequisiteContent;

        private TMP_Dropdown _emergencyRewardTypeDropdown;
        private ScrollRect _emergencyRewardScroll;
        private RectTransform _emergencyRewardContent;

        private float _emergencyBlockHeight;
        private bool _emergencyBuilt;
        private bool _emergencyIllnessOptionsScanned;
        private int _selectedEmergency;
        private readonly List<EmergencyDraft> _emergencies =
            new List<EmergencyDraft>();
        private readonly List<IllnessDefinition> _emergencyIllnessOptions =
            new List<IllnessDefinition>();

        private static readonly string[] EmergencyFailureOptions =
        {
            "Se quedan",
            "Se marchan",
            "Mueren"
        };

        private static readonly string[] EmergencyScoreOptions =
        {
            "Temporizada",
            "Porcentaje",
            "Pacientes curados"
        };

        private static readonly string[] EmergencyArrivalOptions =
        {
            "Ambulancia",
            "Entrada normal",
            "Helipuerto",
            "Portal"
        };

        private static readonly string[] EmergencyPrerequisiteTypes =
        {
            "Sala desbloqueada",
            "Reputación mínima",
            "Días transcurridos",
            "Estrella mínima",
            "Sala construida",
            "Prestigio mínimo",
            "Saldo mínimo",
            "Moral mínima",
            "Higiene mínima",
            "Número de empleados",
            "Estrella máxima",
            "Objetivo completado"
        };

        private static readonly string[] EmergencyRewardTypes =
        {
            "Dinero",
            "Reputación",
            "Puntos de investigación",
            "Kudosh",
            "Desbloquear sala",
            "Desbloquear objeto",
            "Desbloquear mejora"
        };

        private sealed class EmergencyDraft
        {
            internal readonly List<IllnessDefinition> Illnesses =
                new List<IllnessDefinition>();
            internal readonly List<EmergencyRuleDraft> Prerequisites =
                new List<EmergencyRuleDraft>();
            internal readonly List<EmergencyRuleDraft> Rewards =
                new List<EmergencyRuleDraft>();

            internal int FailureAction = 1;
            internal int ScoreType;
            internal int ArrivalMethod;
            internal bool Warning = true;
            internal bool Rejectable = true;

            internal string Patients = "8";
            internal string Diagnosis = "80";
            internal string Success = "70";
            internal string ArrivalRate = "1.5";
            internal string Duration = "30";
            internal string DaysUntilStart = "3";
        }

        private sealed class EmergencyRuleDraft
        {
            internal int Type;
            internal string Value = "";
        }

        // Se llama desde EditorLayout al crear Ajustes, antes de aplicar la paleta.
        // Aqui NO se construye la UI: solo se prepara la referencia.
        internal void BuildEmergencySettingsContents()
        {
            _emergencyContent = null;
            _emergencyRoot = null;
            _emergencyBuilt = false;
            _emergencyIllnessOptionsScanned = false;
            _selectedEmergency = 0;
            _emergencies.Clear();
            _emergencyIllnessOptions.Clear();

            if (_settingsPages == null ||
                _settingsPages.Length <= EmergencyTabIndex ||
                _settingsPages[EmergencyTabIndex] == null)
                return;

            var page =
                _settingsPages[EmergencyTabIndex].transform
                    as RectTransform;

            if (page == null)
                return;

            var nativeScroll =
                page.GetComponentInChildren<ScrollRect>(true);

            if (nativeScroll != null)
                nativeScroll.gameObject.SetActive(false);

            // El propio page es solo el host de la pestana.
            // BuildEmergencyUi crea dentro una raiz transparente exclusiva.
            _emergencyContent = page;
        }

        // Se llama al entrar en Emergencias. A estas alturas la paleta nativa
        // ya ha terminado, por lo que nuestros cuatro bloques no se repintan.
        internal void RefreshEmergencySettingsContents()
        {
            if (_emergencyContent == null) return;

            if (!_emergencyBuilt)
                BuildEmergencyUi();

            ScanEmergencyIllnesses();
            RefreshEmergencyIllnessDropdown();
            RefreshEmergencyList();
            LoadSelectedEmergency();
            RestoreEmergencyPanelColors();
        }

        private void BuildEmergencyUi()
        {
            if (_emergencyContent == null)
                return;

            _emergencyBuilt = true;

            Canvas.ForceUpdateCanvases();

            var pageHeight =
                _emergencyContent.rect.height > 100f
                    ? _emergencyContent.rect.height
                    : EmergencyContentHeight;

            // Raiz transparente propia de Emergencias.
            // Ningun bloque de este CS se crea fuera de esta raiz.
            var rootObject = new GameObject(
                "UP Emergencies Root",
                typeof(RectTransform));
            rootObject.transform.SetParent(
                _emergencyContent, false);

            _emergencyRoot =
                rootObject.transform as RectTransform;

            Stretch(_emergencyRoot);
            _emergencyRoot.offsetMin =
                new Vector2(10f, 10f);
            _emergencyRoot.offsetMax =
                new Vector2(-10f, -10f);

            var availableHeight =
                Mathf.Max(1f, pageHeight - 20f);

            _emergencyBlockHeight =
                (availableHeight - EmergencyGap) * .5f;

            // ARRIBA: lista + configuración.
            _emergencyListBlock = CreateEmergencyBlock(
                "UP Emergencies - List", _emergencyRoot);
            SetTopLeft(
                _emergencyListBlock,
                0f, 0f,
                EmergencyLeftWidth,
                _emergencyBlockHeight);

            _emergencyConfigBlock = CreateEmergencyBlock(
                "UP Emergencies - Configuration", _emergencyRoot);
            SetEmergencyTopStretch(
                _emergencyConfigBlock,
                EmergencyLeftWidth + EmergencyGap,
                0f,
                0f,
                _emergencyBlockHeight);

            // ABAJO: prerrequisitos + recompensas.
            _emergencyPrerequisiteBlock = CreateEmergencyBlock(
                "UP Emergencies - Prerequisites", _emergencyRoot);
            SetEmergencyBottomHalf(
                _emergencyPrerequisiteBlock, false);

            _emergencyRewardBlock = CreateEmergencyBlock(
                "UP Emergencies - Rewards", _emergencyRoot);
            SetEmergencyBottomHalf(
                _emergencyRewardBlock, true);

            Canvas.ForceUpdateCanvases();

            BuildEmergencyListChildren();
            BuildEmergencyConfigurationChildren();

            BuildEmergencyRuleBlockChildren(
                _emergencyPrerequisiteBlock,
                "Prerrequisitos",
                EmergencyPrerequisiteTypes,
                true);

            BuildEmergencyRuleBlockChildren(
                _emergencyRewardBlock,
                "Recompensas",
                EmergencyRewardTypes,
                false);

            RestoreEmergencyPanelColors();

            _emergencies.Add(new EmergencyDraft());
            _selectedEmergency = 0;
        }

        private void RestoreEmergencyPanelColors()
        {
            RestoreEmergencyPanelColor(_emergencyListBlock);
            RestoreEmergencyPanelColor(_emergencyConfigBlock);
            RestoreEmergencyPanelColor(_emergencyPrerequisiteBlock);
            RestoreEmergencyPanelColor(_emergencyRewardBlock);
        }

        private static void RestoreEmergencyPanelColor(
            RectTransform panel)
        {
            var image = panel != null
                ? panel.GetComponent<Image>()
                : null;

            if (image == null)
                return;

            image.enabled = true;
            image.type = Image.Type.Sliced;
            image.color = EmergencyPanelColor;
        }

        private RectTransform CreateEmergencyBlock(
            string name, Transform parent)
        {
            var block = NewImage(
                name,
                parent,
                _style.ButtonSprite,
                EmergencyPanelColor,
                false);

            var image = block.GetComponent<Image>();
            if (image != null)
            {
                image.type = Image.Type.Sliced;
                image.color = EmergencyPanelColor;
                image.raycastTarget = false;
            }

            var outline = block.GetComponent<Outline>() ??
                          block.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;

            return block.transform as RectTransform;
        }

        private static void SetEmergencyTopStretch(
            RectTransform rect, float left, float right, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2((left - right) * .5f, -top);
            rect.sizeDelta = new Vector2(-(left + right), height);
        }

        private void SetEmergencyBottomHalf(RectTransform rect, bool rightHalf)
        {
            rect.anchorMin = new Vector2(rightHalf ? .5f : 0f, 1f);
            rect.anchorMax = new Vector2(rightHalf ? 1f : .5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(
                rightHalf ? EmergencyGap * .25f : -EmergencyGap * .25f,
                -(_emergencyBlockHeight + EmergencyGap));
            rect.sizeDelta = new Vector2(
                -EmergencyGap * .5f, _emergencyBlockHeight);
        }

        private void BuildEmergencyListChildren()
        {
            CreateEmergencyLabel(_emergencyListBlock, "Emergencias",
                14f, 8f, EmergencyLeftWidth - 28f, 32f, 21f,
                TextAlignmentOptions.MidlineLeft);

            _emergencyListScroll = CreateEmergencyVerticalScroll(
                _emergencyListBlock, "UP Emergency List Scroll",
                12f, 48f, 12f, 62f, out _emergencyListContent);

            var listBar = InstallNativeVerticalScrollbar(
                _emergencyListBlock, _emergencyListScroll,
                "UP Emergency List Scrollbar", 48f, 62f, false, 12f);
            if (listBar != null)
                listBar.gameObject.SetActive(false);

            CreateEmergencyButton(_emergencyListBlock,
                "UP Emergency Add", "Nueva emergencia",
                12f, _emergencyBlockHeight - 47f, 184f, 35f,
                _style.ButtonColor, AddEmergency);

            CreateEmergencyButton(_emergencyListBlock,
                "UP Emergency Delete", "×",
                201f, _emergencyBlockHeight - 47f, 32f, 35f,
                EmergencyRemoveColor, DeleteEmergency);
        }

        private void RefreshEmergencyList()
        {
            if (_emergencyListContent == null || _emergencyListScroll == null)
                return;

            ClearEmergencyChildren(_emergencyListContent);
            Canvas.ForceUpdateCanvases();

            var viewportHeight = Mathf.Max(
                120f, _emergencyListScroll.viewport.rect.height);
            var rowHeight = viewportHeight / EmergencyRowsBeforeScroll;
            var contentHeight = Mathf.Max(
                viewportHeight, _emergencies.Count * rowHeight);

            _emergencyListContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, contentHeight);

            for (var index = 0; index < _emergencies.Count; ++index)
            {
                var captured = index;
                var tint = index == _selectedEmergency
                    ? LayerBuildOrange
                    : _style.ButtonColor;

                var button = CreateEmergencyButton(
                    _emergencyListContent,
                    "UP Emergency Row " + index,
                    "Emergencia " + (index + 1),
                    0f, 0f, -2f, rowHeight - 4f,
                    tint, () => SelectEmergency(captured));

                var rect = button.transform as RectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(.5f, 1f);
                rect.anchoredPosition = new Vector2(
                    0f, -index * rowHeight);
                rect.sizeDelta = new Vector2(-2f, rowHeight - 4f);
            }

            var showScroll =
                _emergencies.Count > EmergencyRowsBeforeScroll;
            _emergencyListScroll.vertical = showScroll;
            if (_emergencyListScroll.verticalScrollbar != null)
                _emergencyListScroll.verticalScrollbar.gameObject
                    .SetActive(showScroll);
        }

        private void AddEmergency()
        {
            _emergencies.Add(new EmergencyDraft());
            _selectedEmergency = _emergencies.Count - 1;
            RefreshEmergencyList();
            LoadSelectedEmergency();

            if (_emergencyListScroll != null)
                _emergencyListScroll.verticalNormalizedPosition = 0f;
        }

        private void DeleteEmergency()
        {
            if (_selectedEmergency < 0 ||
                _selectedEmergency >= _emergencies.Count)
                return;

            _emergencies.RemoveAt(_selectedEmergency);
            if (_emergencies.Count == 0)
                _emergencies.Add(new EmergencyDraft());

            _selectedEmergency = Mathf.Clamp(
                _selectedEmergency, 0, _emergencies.Count - 1);
            RefreshEmergencyList();
            LoadSelectedEmergency();
        }

        private void SelectEmergency(int index)
        {
            if (index < 0 || index >= _emergencies.Count) return;
            _selectedEmergency = index;
            RefreshEmergencyList();
            LoadSelectedEmergency();
        }

        private void BuildEmergencyConfigurationChildren()
        {
            const float margin = 14f;
            const float selectorTop = 76f;
            const float selectorHeight = 36f;
            const float columnsTop = 122f;
            const float rowHeight = 40f;

            CreateEmergencyLabel(
                _emergencyConfigBlock, "Emergencia 1",
                margin, 7f, 170f, 34f, 21f,
                TextAlignmentOptions.MidlineLeft,
                "UP Emergency Title");

            var chipScroll = CreateEmergencyHorizontalScroll(
                _emergencyConfigBlock,
                "UP Emergency Disease Chips",
                190f, 7f, margin,
                _emergencyBlockHeight - 41f,
                out _emergencyChipContent);
            chipScroll.scrollSensitivity = 10f;

            _emergencyIllnessDropdown = CreateEmergencyDropdown(
                _emergencyConfigBlock,
                "UP Emergency Illness Dropdown",
                margin, selectorTop, 315f, selectorHeight,
                new[] { "Seleccionar enfermedad..." });

            CreateEmergencyButton(
                _emergencyConfigBlock,
                "UP Emergency Illness Add", "Añadir",
                margin + 325f, selectorTop,
                94f, selectorHeight,
                EmergencyAddColor,
                AddSelectedEmergencyIllness);

            var leftColumn = CreateEmergencyConfigColumn(
                _emergencyConfigBlock,
                "UP Emergency Configuration Left",
                0f, .5f,
                margin, 10f,
                columnsTop, 10f);

            var rightColumn = CreateEmergencyConfigColumn(
                _emergencyConfigBlock,
                "UP Emergency Configuration Right",
                .5f, 1f,
                10f, margin,
                columnsTop, 10f);

            // COLUMNA IZQUIERDA.
            _emergencyFailureDropdown = CreateEmergencyNativeDropdownRow(
                leftColumn, "Acción al fallar:",
                0f, rowHeight, EmergencyFailureOptions);

            _emergencyScoreDropdown = CreateEmergencyNativeDropdownRow(
                leftColumn, "Tipo de puntuación:",
                rowHeight, rowHeight, EmergencyScoreOptions);

            _emergencyArrivalDropdown = CreateEmergencyNativeDropdownRow(
                leftColumn, "Método de llegada:",
                rowHeight * 2f, rowHeight, EmergencyArrivalOptions);

            _emergencyWarningToggle = CreateEmergencyNativeToggleRow(
                leftColumn, "Aviso previo:",
                rowHeight * 3f, rowHeight);

            _emergencyRejectableToggle = CreateEmergencyNativeToggleRow(
                leftColumn, "Rechazable:",
                rowHeight * 4f, rowHeight);

            // COLUMNA DERECHA. Empieza EXACTAMENTE a la misma altura.
            _emergencyPatientsInput = CreateEmergencyNativeInputRow(
                rightColumn, "Pacientes:",
                0f, rowHeight, true);

            _emergencyDiagnosisInput = CreateEmergencyNativeInputRow(
                rightColumn, "Diagnóstico inicial:",
                rowHeight, rowHeight, true);

            _emergencySuccessInput = CreateEmergencyNativeInputRow(
                rightColumn, "Éxito requerido:",
                rowHeight * 2f, rowHeight, true);

            _emergencyArrivalRateInput = CreateEmergencyNativeInputRow(
                rightColumn, "Ritmo de llegada:",
                rowHeight * 3f, rowHeight, false);

            _emergencyDurationInput = CreateEmergencyNativeInputRow(
                rightColumn, "Duración:",
                rowHeight * 4f, rowHeight, true);

            _emergencyDaysInput = CreateEmergencyNativeInputRow(
                rightColumn, "Días hasta inicio:",
                rowHeight * 5f, rowHeight, true);

            if (_emergencyFailureDropdown != null)
                _emergencyFailureDropdown.onValueChanged.AddListener(
                    value => CurrentEmergency.FailureAction = value);
            if (_emergencyScoreDropdown != null)
                _emergencyScoreDropdown.onValueChanged.AddListener(
                    value => CurrentEmergency.ScoreType = value);
            if (_emergencyArrivalDropdown != null)
                _emergencyArrivalDropdown.onValueChanged.AddListener(
                    value => CurrentEmergency.ArrivalMethod = value);
            if (_emergencyWarningToggle != null)
                _emergencyWarningToggle.onValueChanged.AddListener(
                    value => CurrentEmergency.Warning = value);
            if (_emergencyRejectableToggle != null)
                _emergencyRejectableToggle.onValueChanged.AddListener(
                    value => CurrentEmergency.Rejectable = value);

            BindEmergencyInput(
                _emergencyPatientsInput,
                value => CurrentEmergency.Patients = value);
            BindEmergencyInput(
                _emergencyDiagnosisInput,
                value => CurrentEmergency.Diagnosis = value);
            BindEmergencyInput(
                _emergencySuccessInput,
                value => CurrentEmergency.Success = value);
            BindEmergencyInput(
                _emergencyArrivalRateInput,
                value => CurrentEmergency.ArrivalRate = value);
            BindEmergencyInput(
                _emergencyDurationInput,
                value => CurrentEmergency.Duration = value);
            BindEmergencyInput(
                _emergencyDaysInput,
                value => CurrentEmergency.DaysUntilStart = value);
        }

        private static RectTransform CreateEmergencyConfigColumn(
            Transform parent,
            string name,
            float anchorMinX,
            float anchorMaxX,
            float left,
            float right,
            float top,
            float bottom)
        {
            var columnObject = new GameObject(
                name, typeof(RectTransform));
            columnObject.transform.SetParent(parent, false);

            var rect = columnObject.transform as RectTransform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }


        private static void RemoveEmergencyRowChrome(
            Transform row,
            Transform fieldRoot)
        {
            if (row == null)
                return;

            var rowImage = row.GetComponent<Image>();
            if (rowImage != null)
                rowImage.enabled = false;

            var rowOutline = row.GetComponent<Outline>();
            if (rowOutline != null)
                rowOutline.enabled = false;

            foreach (var image in
                     row.GetComponentsInChildren<Image>(true))
            {
                if (image == null)
                    continue;

                if (fieldRoot != null &&
                    (image.transform == fieldRoot ||
                     image.transform.IsChildOf(fieldRoot)))
                    continue;

                image.enabled = false;
            }

            foreach (var outline in
                     row.GetComponentsInChildren<Outline>(true))
            {
                if (outline == null)
                    continue;

                if (fieldRoot != null &&
                    (outline.transform == fieldRoot ||
                     outline.transform.IsChildOf(fieldRoot)))
                    continue;

                outline.enabled = false;
            }

            var label = row.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(value =>
                    value != null &&
                    (fieldRoot == null ||
                     !value.transform.IsChildOf(fieldRoot)));

            if (label != null)
            {
                label.fontSize = 18f;
                label.fontStyle = FontStyles.Bold;
                label.color = Color.white;
                label.enableWordWrapping = false;
            }
        }

        private TMP_InputField CreateEmergencyNativeInputRow(
            Transform parent,
            string label,
            float top,
            float height,
            bool integer)
        {
            var input = CreateScenarioSettingsInput(
                parent,
                "editor.scenario_settings.emergency." +
                    label.Replace(" ", "_").Replace(":", "").ToLowerInvariant(),
                top, height, false, integer,
                .01f, .99f, 160f);

            if (input == null)
                return null;

            SetScenarioSettingsInputLabel(input, label);

            var row = input.transform;
            while (row != null &&
                   !row.name.StartsWith(
                       "UP Workshop Settings Field ",
                       StringComparison.Ordinal))
                row = row.parent;

            RemoveEmergencyRowChrome(
                row,
                input.transform);

            if (input.textComponent != null)
            {
                input.textComponent.fontSize = 18f;
                input.textComponent.fontStyle = FontStyles.Bold;
            }

            input.contentType = integer
                ? TMP_InputField.ContentType.IntegerNumber
                : TMP_InputField.ContentType.DecimalNumber;
            input.characterLimit = 16;
            return input;
        }

        private TMP_Dropdown CreateEmergencyNativeDropdownRow(
            Transform parent,
            string label,
            float top,
            float height,
            IEnumerable<string> options)
        {
            GameObject panelTemplate;
            Component nativeInputTemplate;
            if (!TryGetWorkshopFieldTemplate(
                    false, out panelTemplate, out nativeInputTemplate))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Emergencias: no se encontró el campo nativo usado por General.");
                return null;
            }

            var row = Instantiate(panelTemplate, parent, false);
            row.name = "UP Emergency Native Dropdown " + label;
            row.SetActive(true);

            var rowRect = row.transform as RectTransform;
            SetRect(
                rowRect,
                new Vector2(.01f, 1f),
                new Vector2(.99f, 1f),
                new Vector2(.5f, 1f),
                new Vector2(0f, -top),
                new Vector2(0f, height));

            var layout = row.GetComponent<LayoutElement>() ??
                         row.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            DisableLocalisers(row.transform);

            var nativeInput = row
                .GetComponentsInChildren(nativeInputTemplate.GetType(), true)
                .OfType<Component>()
                .FirstOrDefault();

            if (nativeInput == null)
            {
                Destroy(row);
                UnderPressureEditorPlugin.Log?.LogError(
                    "Emergencias: el panel nativo de General no contiene su campo.");
                return null;
            }

            var nativeBehaviour = nativeInput as Behaviour;
            if (nativeBehaviour != null)
                nativeBehaviour.enabled = false;

            var fieldObject = nativeInput.gameObject;
            fieldObject.SetActive(true);

            SetWorkshopFieldLabel(
                row.transform, fieldObject.transform, label);
            ArrangeNativeWorkshopColumns(
                row.transform, fieldObject.transform, 220f);
            RemoveEmergencyRowChrome(
                row.transform, fieldObject.transform);

            foreach (var oldText in
                     fieldObject.GetComponentsInChildren<Text>(true))
                oldText.gameObject.SetActive(false);
            foreach (var oldText in
                     fieldObject.GetComponentsInChildren<TMP_Text>(true))
                oldText.gameObject.SetActive(false);

            var dropdown = CreateEmergencyDropdown(
                fieldObject.transform,
                "UP Emergency Native Dropdown Control",
                0f, 0f, 1f, 1f, options);

            var dropdownRect = dropdown.transform as RectTransform;
            Stretch(dropdownRect);

            var image = dropdown.GetComponent<Image>();
            if (image != null)
            {
                image.color = Color.white;
                image.type = Image.Type.Sliced;
            }

            return dropdown;
        }

        private Toggle CreateEmergencyNativeToggleRow(
            Transform parent,
            string label,
            float top,
            float height)
        {
            GameObject panelTemplate;
            Component nativeInputTemplate;
            if (!TryGetWorkshopFieldTemplate(
                    false, out panelTemplate, out nativeInputTemplate))
            {
                UnderPressureEditorPlugin.Log?.LogError(
                    "Emergencias: no se encontró el campo nativo usado por General.");
                return null;
            }

            var row = Instantiate(panelTemplate, parent, false);
            row.name = "UP Emergency Native Toggle " + label;
            row.SetActive(true);

            var rowRect = row.transform as RectTransform;
            SetRect(
                rowRect,
                new Vector2(.01f, 1f),
                new Vector2(.99f, 1f),
                new Vector2(.5f, 1f),
                new Vector2(0f, -top),
                new Vector2(0f, height));

            var layout = row.GetComponent<LayoutElement>() ??
                         row.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            DisableLocalisers(row.transform);

            var nativeInput = row
                .GetComponentsInChildren(nativeInputTemplate.GetType(), true)
                .OfType<Component>()
                .FirstOrDefault();

            if (nativeInput == null)
            {
                Destroy(row);
                return null;
            }

            var nativeBehaviour = nativeInput as Behaviour;
            if (nativeBehaviour != null)
                nativeBehaviour.enabled = false;

            var fieldObject = nativeInput.gameObject;
            fieldObject.SetActive(true);

            SetWorkshopFieldLabel(
                row.transform, fieldObject.transform, label);
            ArrangeNativeWorkshopColumns(
                row.transform, fieldObject.transform, 56f);
            RemoveEmergencyRowChrome(
                row.transform, fieldObject.transform);

            foreach (var image in
                     fieldObject.GetComponentsInChildren<Image>(true))
                image.enabled = false;

            foreach (var outline in
                     fieldObject.GetComponentsInChildren<Outline>(true))
                outline.enabled = false;

            foreach (var oldText in
                     fieldObject.GetComponentsInChildren<Text>(true))
                oldText.gameObject.SetActive(false);
            foreach (var oldText in
                     fieldObject.GetComponentsInChildren<TMP_Text>(true))
                oldText.gameObject.SetActive(false);

            Toggle toggle = null;
            if (_settingsDiseaseNativeToggleTemplate != null)
            {
                toggle = Instantiate(
                    _settingsDiseaseNativeToggleTemplate,
                    fieldObject.transform, false);
                toggle.gameObject.name =
                    "UP Emergency Native Toggle Control";
                toggle.gameObject.SetActive(true);
                DisableLocalisers(toggle.transform);
                toggle.onValueChanged.RemoveAllListeners();

                var rect = toggle.transform as RectTransform;
                rect.anchorMin = rect.anchorMax =
                    new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(32f, 32f);
            }
            else
            {
                toggle = CreateEmergencyToggle(
                    fieldObject.transform, 0f, 0f);
                var rect = toggle.transform as RectTransform;
                rect.anchorMin = rect.anchorMax =
                    new Vector2(.5f, .5f);
                rect.pivot = new Vector2(.5f, .5f);
                rect.anchoredPosition = Vector2.zero;
            }

            return toggle;
        }

        private void LoadSelectedEmergency()
        {
            var draft = CurrentEmergency;
            if (draft == null) return;

            var title = _emergencyConfigBlock != null
                ? _emergencyConfigBlock.Find("UP Emergency Title")
                    ?.GetComponent<TMP_Text>()
                : null;
            if (title != null)
                title.text = "Emergencia " + (_selectedEmergency + 1);

            _emergencyFailureDropdown?.SetValueWithoutNotify(draft.FailureAction);
            _emergencyScoreDropdown?.SetValueWithoutNotify(draft.ScoreType);
            _emergencyArrivalDropdown?.SetValueWithoutNotify(draft.ArrivalMethod);

            _emergencyWarningToggle?.SetIsOnWithoutNotify(draft.Warning);
            _emergencyRejectableToggle?.SetIsOnWithoutNotify(draft.Rejectable);

            _emergencyPatientsInput?.SetTextWithoutNotify(draft.Patients);
            _emergencyDiagnosisInput?.SetTextWithoutNotify(draft.Diagnosis);
            _emergencySuccessInput?.SetTextWithoutNotify(draft.Success);
            _emergencyArrivalRateInput?.SetTextWithoutNotify(draft.ArrivalRate);
            _emergencyDurationInput?.SetTextWithoutNotify(draft.Duration);
            _emergencyDaysInput?.SetTextWithoutNotify(draft.DaysUntilStart);

            RefreshEmergencyChips();
            RefreshEmergencyPrerequisites();
            RefreshEmergencyRewards();
        }

        private EmergencyDraft CurrentEmergency
        {
            get
            {
                return _selectedEmergency >= 0 &&
                       _selectedEmergency < _emergencies.Count
                    ? _emergencies[_selectedEmergency]
                    : null;
            }
        }

        private void ScanEmergencyIllnesses()
        {
            if (_emergencyIllnessOptionsScanned)
                return;

            _emergencyIllnessOptions.Clear();
            var seen = new HashSet<IllnessDefinition>();
            var levels = new HashSet<LevelConfig>();

            if (_level?.Config != null)
                levels.Add(_level.Config);

            var options = _level?.App?.SandboxSettingsConfig?.LevelOptions;
            if (options != null)
            {
                foreach (var option in options)
                {
                    var levelConfig = option?.Level?.Instance;
                    if (levelConfig != null)
                        levels.Add(levelConfig);
                }
            }

            foreach (var levelConfig in levels)
            {
                try
                {
                    var config = levelConfig.GetCharacterManagerConfig();
                    if (config == null) continue;

                    if (config._weightedIllnesses != null)
                    {
                        foreach (var entry in config._weightedIllnesses)
                            AddEmergencyIllness(entry?.Definition?.Instance, seen);
                    }

                    if (config._weightedIllnessesTimePortal != null)
                    {
                        foreach (var entry in config._weightedIllnessesTimePortal)
                            AddEmergencyIllness(entry?.Definition?.Instance, seen);
                    }
                }
                catch (Exception exception)
                {
                    UnderPressureEditorPlugin.Log?.LogWarning(
                        "Emergencias: no se pudieron leer enfermedades de " +
                        levelConfig.UniqueId + ": " + exception.Message);
                }
            }

            try
            {
                if (typeof(UnityEngine.Object)
                    .IsAssignableFrom(typeof(IllnessDefinition)))
                {
                    foreach (var asset in Resources.FindObjectsOfTypeAll(
                                 typeof(IllnessDefinition)))
                    {
                        AddEmergencyIllness(
                            (object)asset as IllnessDefinition, seen);
                    }
}
            }
            catch
            {
                // La tabla de niveles ya cubre el caso normal.
            }

            _emergencyIllnessOptions.Sort((left, right) =>
                string.Compare(EmergencyIllnessName(left),
                    EmergencyIllnessName(right),
                    StringComparison.OrdinalIgnoreCase));

            _emergencyIllnessOptionsScanned = true;
        }

        private void AddEmergencyIllness(
            IllnessDefinition illness, HashSet<IllnessDefinition> seen)
        {
            if (illness != null && seen.Add(illness))
                _emergencyIllnessOptions.Add(illness);
        }

        private static string EmergencyIllnessName(IllnessDefinition illness)
        {
            if (illness == null) return "";
            var name = illness.Name.Translation;
            return string.IsNullOrWhiteSpace(name)
                ? illness.ToString()
                : name;
        }

        private void RefreshEmergencyIllnessDropdown()
        {
            if (_emergencyIllnessDropdown == null) return;

            var options = new List<string> { "Seleccionar enfermedad..." };
            options.AddRange(_emergencyIllnessOptions
                .Select(EmergencyIllnessName));

            _emergencyIllnessDropdown.ClearOptions();
            _emergencyIllnessDropdown.AddOptions(options);
            _emergencyIllnessDropdown.SetValueWithoutNotify(0);
            _emergencyIllnessDropdown.RefreshShownValue();
        }

        private void AddSelectedEmergencyIllness()
        {
            var draft = CurrentEmergency;
            if (draft == null ||
                _emergencyIllnessDropdown == null ||
                _emergencyIllnessDropdown.value <= 0)
                return;

            var index = _emergencyIllnessDropdown.value - 1;
            if (index < 0 || index >= _emergencyIllnessOptions.Count)
                return;

            var illness = _emergencyIllnessOptions[index];
            if (!draft.Illnesses.Contains(illness))
                draft.Illnesses.Add(illness);

            _emergencyIllnessDropdown.SetValueWithoutNotify(0);
            RefreshEmergencyChips();
        }

        private void RefreshEmergencyChips()
        {
            if (_emergencyChipContent == null) return;
            ClearEmergencyChildren(_emergencyChipContent);

            var draft = CurrentEmergency;
            if (draft == null) return;

            var x = 0f;
            foreach (var illness in draft.Illnesses.ToArray())
            {
                var captured = illness;
                var name = EmergencyIllnessName(illness);
                var width = Mathf.Clamp(70f + name.Length * 7f, 125f, 250f);

                var chip = NewImage(
                    "UP Emergency Illness " + name,
                    _emergencyChipContent,
                    _style.ButtonSprite,
                    SettingsSharedPageColor,
                    false);
                var chipRect = chip.transform as RectTransform;
                SetTopLeft(chipRect, x, 0f, width, 30f);

                CreateEmergencyLabel(chip.transform, name,
                    8f, 0f, width - 38f, 30f, 13f,
                    TextAlignmentOptions.Center);

                CreateEmergencyButton(chip.transform,
                    "Remove", "×",
                    width - 34f, 2f, 30f, 26f,
                    EmergencyRemoveColor,
                    () =>
                    {
                        var current = CurrentEmergency;
                        if (current == null) return;
                        current.Illnesses.Remove(captured);
                        RefreshEmergencyChips();
                    });

                x += width + 6f;
            }

            _emergencyChipContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Max(1f, x));
        }

        private void BuildEmergencyRuleBlockChildren(
            RectTransform block,
            string title,
            string[] types,
            bool prerequisite)
        {
            const float margin = 12f;
            const float headerHeight = 46f;
            const float titleWidth = 170f;
            const float addWidth = 38f;

            CreateEmergencyLabel(block, title,
                margin, 6f, titleWidth, 34f, 19f,
                TextAlignmentOptions.MidlineLeft);

            var dropdown = CreateEmergencyDropdown(
                block,
                prerequisite
                    ? "UP Emergency Prerequisite Type"
                    : "UP Emergency Reward Type",
                margin + titleWidth, 6f,
                -(margin + addWidth + 8f), 34f, types);

            CreateEmergencyButton(
                block,
                prerequisite
                    ? "UP Emergency Prerequisite Add"
                    : "UP Emergency Reward Add",
                "+",
                -margin, 6f, addWidth, 34f,
                EmergencyAddColor,
                prerequisite
                    ? (Action)AddEmergencyPrerequisite
                    : AddEmergencyReward);

            RectTransform content;
            var scroll = CreateEmergencyVerticalScroll(
                block,
                prerequisite
                    ? "UP Emergency Prerequisite Scroll"
                    : "UP Emergency Reward Scroll",
                margin, headerHeight + 4f,
                margin, margin, out content);

            var bar = InstallNativeVerticalScrollbar(
                block, scroll,
                prerequisite
                    ? "UP Emergency Prerequisite Scrollbar"
                    : "UP Emergency Reward Scrollbar",
                headerHeight + 4f, margin, false, 12f);

            if (bar != null)
            {
                bar.gameObject.SetActive(true);
                scroll.verticalScrollbarVisibility =
                    ScrollRect.ScrollbarVisibility.Permanent;
            }

            if (prerequisite)
            {
                _emergencyPrerequisiteTypeDropdown = dropdown;
                _emergencyPrerequisiteScroll = scroll;
                _emergencyPrerequisiteContent = content;
            }
            else
            {
                _emergencyRewardTypeDropdown = dropdown;
                _emergencyRewardScroll = scroll;
                _emergencyRewardContent = content;
            }
        }

        private void AddEmergencyPrerequisite()
        {
            var draft = CurrentEmergency;
            if (draft == null || _emergencyPrerequisiteTypeDropdown == null)
                return;

            var type = _emergencyPrerequisiteTypeDropdown.value;
            draft.Prerequisites.Add(new EmergencyRuleDraft
            {
                Type = type,
                Value = DefaultPrerequisiteValue(type)
            });
            RefreshEmergencyPrerequisites();
        }

        private void AddEmergencyReward()
        {
            var draft = CurrentEmergency;
            if (draft == null || _emergencyRewardTypeDropdown == null)
                return;

            var type = _emergencyRewardTypeDropdown.value;
            draft.Rewards.Add(new EmergencyRuleDraft
            {
                Type = type,
                Value = DefaultRewardValue(type)
            });
            RefreshEmergencyRewards();
        }

        private static string DefaultPrerequisiteValue(int type)
        {
            if (type == 0 || type == 4 || type == 11) return "";
            if (type == 3) return "1";
            if (type == 10) return "3";
            return "0";
        }

        private static string DefaultRewardValue(int type)
        {
            switch (type)
            {
                case 0: return "10000";
                case 1: return "10";
                case 2: return "500";
                case 3: return "5";
                default: return "";
            }
        }

        private void RefreshEmergencyPrerequisites()
        {
            var draft = CurrentEmergency;
            RefreshEmergencyRuleRows(
                _emergencyPrerequisiteContent,
                _emergencyPrerequisiteScroll,
                draft != null ? draft.Prerequisites : null,
                EmergencyPrerequisiteTypes,
                true);
        }

        private void RefreshEmergencyRewards()
        {
            var draft = CurrentEmergency;
            RefreshEmergencyRuleRows(
                _emergencyRewardContent,
                _emergencyRewardScroll,
                draft != null ? draft.Rewards : null,
                EmergencyRewardTypes,
                false);
        }

        private void RefreshEmergencyRuleRows(
            RectTransform content,
            ScrollRect scroll,
            List<EmergencyRuleDraft> rules,
            string[] types,
            bool prerequisite)
        {
            if (content == null || scroll == null) return;
            ClearEmergencyChildren(content);

            if (rules == null)
                rules = new List<EmergencyRuleDraft>();

            Canvas.ForceUpdateCanvases();
            var visibleHeight = Mathf.Max(
                160f, scroll.viewport.rect.height);
            var rowHeight = visibleHeight / 4f;

            content.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(visibleHeight, rules.Count * rowHeight));

            for (var index = 0; index < rules.Count; ++index)
            {
                var capturedIndex = index;
                var rule = rules[index];

                var rowObject = NewImage(
                    "UP Emergency Rule " + index,
                    content, _style.ButtonSprite,
                    SettingsSharedPageColor, false);
                var row = rowObject.transform as RectTransform;
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(.5f, 1f);
                row.anchoredPosition = new Vector2(
                    0f, -index * rowHeight);
                row.sizeDelta = new Vector2(0f, rowHeight - 5f);

                var rowWidth = Mathf.Max(
                    420f, scroll.viewport.rect.width);
                const float gap = 8f;
                const float removeWidth = 38f;
                var usable = rowWidth - gap * 4f - removeWidth;
                var typeWidth = usable * .52f;
                var valueWidth = usable - typeWidth;
                var controlHeight = Mathf.Min(38f, rowHeight - 12f);
                var top = (rowHeight - 5f - controlHeight) * .5f;

                var typeDropdown = CreateEmergencyDropdown(
                    row, "Type",
                    gap, top, typeWidth, controlHeight, types);
                typeDropdown.SetValueWithoutNotify(
                    Mathf.Clamp(rule.Type, 0, types.Length - 1));

                typeDropdown.onValueChanged.AddListener(value =>
                {
                    rule.Type = value;
                    rule.Value = prerequisite
                        ? DefaultPrerequisiteValue(value)
                        : DefaultRewardValue(value);

                    if (prerequisite)
                        RefreshEmergencyPrerequisites();
                    else
                        RefreshEmergencyRewards();
                });

                CreateEmergencyRuleValueControl(
                    row, rule, prerequisite,
                    gap * 2f + typeWidth, top,
                    valueWidth, controlHeight);

                CreateEmergencyButton(
                    row, "Remove", "×",
                    rowWidth - removeWidth - gap, top,
                    removeWidth, controlHeight,
                    EmergencyRemoveColor,
                    () =>
                    {
                        var current = CurrentEmergency;
                        if (current == null) return;

                        var list = prerequisite
                            ? current.Prerequisites
                            : current.Rewards;

                        if (capturedIndex < 0 ||
                            capturedIndex >= list.Count)
                            return;

                        list.RemoveAt(capturedIndex);

                        if (prerequisite)
                            RefreshEmergencyPrerequisites();
                        else
                            RefreshEmergencyRewards();
                    });
            }
        }

        private void CreateEmergencyRuleValueControl(
            Transform parent,
            EmergencyRuleDraft rule,
            bool prerequisite,
            float x, float y, float width, float height)
        {
            if (prerequisite &&
                (rule.Type == 0 || rule.Type == 4))
            {
                var rooms = EmergencyRoomOptions();
                var dropdown = CreateEmergencyDropdown(
                    parent, "Value", x, y, width, height, rooms);
                var selected = Array.IndexOf(rooms, rule.Value);
                dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
                dropdown.onValueChanged.AddListener(value =>
                    rule.Value = value >= 0 && value < rooms.Length
                        ? rooms[value]
                        : "");
                return;
            }

            if (prerequisite &&
                (rule.Type == 3 || rule.Type == 10))
            {
                var stars = new[] { "1", "2", "3" };
                var dropdown = CreateEmergencyDropdown(
                    parent, "Value", x, y, width, height, stars);
                var selected = Array.IndexOf(stars, rule.Value);
                dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
                dropdown.onValueChanged.AddListener(value =>
                    rule.Value = stars[
                        Mathf.Clamp(value, 0, stars.Length - 1)]);
                return;
            }

            if (!prerequisite && rule.Type == 4)
            {
                var rooms = EmergencyRoomOptions();
                var dropdown = CreateEmergencyDropdown(
                    parent, "Value", x, y, width, height, rooms);
                var selected = Array.IndexOf(rooms, rule.Value);
                dropdown.SetValueWithoutNotify(Mathf.Max(0, selected));
                dropdown.onValueChanged.AddListener(value =>
                    rule.Value = value >= 0 && value < rooms.Length
                        ? rooms[value]
                        : "");
                return;
            }

            var integer =
                prerequisite
                    ? rule.Type != 0 && rule.Type != 4 && rule.Type != 11
                    : rule.Type <= 3;

            var input = CreateEmergencyInput(
                parent, "Value", x, y, width, height,
                rule.Value, integer);
            input.onEndEdit.AddListener(value =>
                rule.Value = value ?? "");
        }

        private static string[] EmergencyRoomOptions()
        {
            var values = Enum.GetNames(typeof(RoomDefinition.Type));
            var result = new string[values.Length + 1];
            result[0] = "Seleccionar sala...";
            Array.Copy(values, 0, result, 1, values.Length);
            return result;
        }

        private void BindEmergencyInput(
            TMP_InputField input, Action<string> setter)
        {
            if (input == null || setter == null) return;
            input.onEndEdit.AddListener(value => setter(value ?? ""));
        }

        private Button CreateEmergencyButton(
            Transform parent,
            string name,
            string label,
            float x,
            float y,
            float width,
            float height,
            Color color,
            Action action)
        {
            var buttonObject = NewImage(
                name, parent, _style.ButtonSprite, color, true);
            var rect = buttonObject.transform as RectTransform;

            if (width < 0f)
                SetEmergencyTopStretch(rect, x, -width, y, height);
            else if (x < 0f)
                SetTopRight(rect, -x, y, width, height);
            else
                SetTopLeft(rect, x, y, width, height);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.RemoveAllListeners();
            if (action != null)
                button.onClick.AddListener(() => action());

            ApplyButtonStyle(button, color);

            CreateEmergencyLabel(button.transform, label,
                3f, 0f, Mathf.Abs(width) - 6f, height,
                16f, TextAlignmentOptions.Center);
            return button;
        }

        private TMP_Dropdown CreateEmergencyDropdown(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            IEnumerable<string> options)
        {
            var dropdown = CreateFallbackEmergencyDropdown(parent, name);
            var rect = dropdown.transform as RectTransform;

            if (width < 0f)
                SetEmergencyTopStretch(rect, x, -width, y, height);
            else
                SetTopLeft(rect, x, y, width, height);

            dropdown.ClearOptions();
            dropdown.AddOptions(options
                .Select(value => new TMP_Dropdown.OptionData(value))
                .ToList());
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
            return dropdown;
        }

        private TMP_Dropdown CreateFallbackEmergencyDropdown(
            Transform parent, string name)
        {
            // REF_DROPDOWN_DISEASE del catálogo.
            var root = NewImage(
                name, parent, _style.ButtonSprite,
                Color.white, true);
            var image = root.GetComponent<Image>();
            image.type = Image.Type.Sliced;

            var dropdown = root.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = image;
            dropdown.transition = Selectable.Transition.ColorTint;

            var labelObject = new GameObject(
                "Label", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(root.transform, false);
            var labelRect = labelObject.transform as RectTransform;
            Stretch(labelRect);
            labelRect.offsetMin = new Vector2(10f, 0f);
            labelRect.offsetMax = new Vector2(-34f, 0f);

            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 17f;
            label.fontStyle = FontStyles.Bold;
            label.color = Color.black;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = false;
            dropdown.captionText = label;

            var arrowObject = new GameObject(
                "Arrow", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            arrowObject.transform.SetParent(root.transform, false);
            var arrowRect = arrowObject.transform as RectTransform;
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = Vector2.one;
            arrowRect.pivot = new Vector2(1f, .5f);
            arrowRect.anchoredPosition = Vector2.zero;
            arrowRect.sizeDelta = new Vector2(23f, 0f);

            var arrow = arrowObject.GetComponent<TextMeshProUGUI>();
            arrow.text = "▼";
            arrow.fontSize = 11f;
            arrow.color = Color.black;
            arrow.alignment = TextAlignmentOptions.Center;
            arrow.raycastTarget = false;

            var templateObject = NewImage(
                "Template", root.transform,
                _style.ButtonSprite,
                Color.white, true);
            var templateRect = templateObject.transform as RectTransform;
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(0f, 0f);
            templateRect.pivot = new Vector2(0f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -3f);
            templateRect.sizeDelta = new Vector2(260f, 108f);

            var templateScroll = templateObject.AddComponent<ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.vertical = true;
            templateScroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportObject = NewImage(
                "Viewport", templateObject.transform,
                null, new Color(1f, 1f, 1f, .01f), true);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);
            viewport.offsetMin = new Vector2(3f, 3f);
            viewport.offsetMax = new Vector2(-3f, -3f);
            var mask = viewportObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentObject = new GameObject(
                "Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            var content = contentObject.transform as RectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 34f);

            var itemObject = NewImage(
                "Item", contentObject.transform,
                _style.ButtonSprite,
                Color.white, true);
            var itemRect = itemObject.transform as RectTransform;
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.pivot = new Vector2(.5f, 1f);
            itemRect.anchoredPosition = Vector2.zero;
            itemRect.sizeDelta = new Vector2(0f, 34f);

            var itemToggle = itemObject.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemObject.GetComponent<Image>();

            var itemLabelObject = new GameObject(
                "Item Label", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            itemLabelObject.transform.SetParent(itemObject.transform, false);
            var itemLabelRect = itemLabelObject.transform as RectTransform;
            Stretch(itemLabelRect);
            itemLabelRect.offsetMin = new Vector2(10f, 0f);
            itemLabelRect.offsetMax = new Vector2(-8f, 0f);

            var itemLabel = itemLabelObject.GetComponent<TextMeshProUGUI>();
            itemLabel.fontSize = 13f;
            itemLabel.color = Color.black;
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;

            templateScroll.viewport = viewport;
            templateScroll.content = content;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            dropdown.itemImage = null;
            templateObject.SetActive(false);
            return dropdown;
        }

        private TMP_InputField CreateEmergencyInput(
            Transform parent,
            string name,
            float x,
            float y,
            float width,
            float height,
            string initialValue,
            bool integer)
        {
            // REF_INPUT_DISEASE del catálogo.
            var root = NewImage(
                name, parent, _style.ButtonSprite,
                Color.white, true);
            var image = root.GetComponent<Image>();
            image.type = Image.Type.Sliced;

            var rect = root.transform as RectTransform;
            SetTopLeft(rect, x, y, width, height);

            var input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = image;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = integer
                ? TMP_InputField.ContentType.IntegerNumber
                : TMP_InputField.ContentType.DecimalNumber;

            var viewportObject = new GameObject(
                "Text Area", typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(root.transform, false);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);
            viewport.offsetMin = new Vector2(7f, 0f);
            viewport.offsetMax = new Vector2(-7f, 0f);

            var textObject = new GameObject(
                "Text", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(viewportObject.transform, false);
            var textRect = textObject.transform as RectTransform;
            Stretch(textRect);

            var value = textObject.GetComponent<TextMeshProUGUI>();
            value.fontSize = 13f;
            value.fontStyle = FontStyles.Bold;
            value.color = Color.black;
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.enableWordWrapping = false;

            input.textViewport = viewport;
            input.textComponent = value;
            input.SetTextWithoutNotify(initialValue ?? "");
            return input;
        }

        private Toggle CreateEmergencyToggle(
            Transform parent, float x, float y)
        {
            var root = NewImage(
                "UP Emergency Toggle", parent,
                _style.ButtonSprite, Color.white, true);
            var outer = root.GetComponent<Image>();
            outer.type = Image.Type.Sliced;
            outer.color = Color.white;

            var toggle = root.AddComponent<Toggle>();
            toggle.targetGraphic = outer;
            toggle.transition = Selectable.Transition.ColorTint;

            var markObject = new GameObject(
                "Checkmark", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            markObject.transform.SetParent(root.transform, false);
            var markRect = markObject.transform as RectTransform;
            Stretch(markRect);

            var mark = markObject.GetComponent<TextMeshProUGUI>();
            mark.text = "✓";
            mark.fontSize = 19f;
            mark.fontStyle = FontStyles.Bold;
            mark.color = EmergencyToggleBlue;
            mark.alignment = TextAlignmentOptions.Center;
            mark.raycastTarget = false;

            toggle.graphic = mark;

            var rect = toggle.transform as RectTransform;
            SetTopLeft(rect, x, y, 32f, 32f);
            return toggle;
        }

        private ScrollRect CreateEmergencyVerticalScroll(
            Transform parent,
            string name,
            float left,
            float top,
            float right,
            float bottom,
            out RectTransform content)
        {
            var root = NewImage(
                name, parent, null,
                new Color(0f, 0f, 0f, .001f), true);
            var rootRect = root.transform as RectTransform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(left, bottom);
            rootRect.offsetMax = new Vector2(-right, -top);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 10f;

            var viewportObject = NewImage(
                "Viewport", root.transform, null,
                new Color(0f, 0f, 0f, .001f), true);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);
            var mask = viewportObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentObject = new GameObject(
                "Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            content = contentObject.transform as RectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        private ScrollRect CreateEmergencyHorizontalScroll(
            Transform parent,
            string name,
            float left,
            float top,
            float right,
            float bottom,
            out RectTransform content)
        {
            var root = NewImage(
                name, parent, null,
                new Color(0f, 0f, 0f, .001f), true);
            var rootRect = root.transform as RectTransform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = new Vector2(left, bottom);
            rootRect.offsetMax = new Vector2(-right, -top);

            var scroll = root.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportObject = NewImage(
                "Viewport", root.transform, null,
                new Color(0f, 0f, 0f, .001f), true);
            var viewport = viewportObject.transform as RectTransform;
            Stretch(viewport);
            var mask = viewportObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentObject = new GameObject(
                "Content", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            content = contentObject.transform as RectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(1f, 30f);

            scroll.viewport = viewport;
            scroll.content = content;
            return scroll;
        }

        private TMP_Text CreateEmergencyLabel(
            Transform parent,
            string value,
            float x,
            float y,
            float width,
            float height,
            float size,
            TextAlignmentOptions alignment,
            string objectName = "Text")
        {
            var objectRoot = new GameObject(
                objectName, typeof(RectTransform),
                typeof(TextMeshProUGUI));
            objectRoot.transform.SetParent(parent, false);
            var rect = objectRoot.transform as RectTransform;

            if (width < 0f)
                SetEmergencyTopStretch(rect, x, -width, y, height);
            else
                SetTopLeft(rect, x, y, width, height);

            var text = objectRoot.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            return text;
        }


        private void ApplyEmergencyDesignerLayout()
        {
            // La raiz transparente NO se hornea con dimensiones antiguas:
            // siempre ocupa la pestana completa. Solo se conservan aqui las
            // posiciones internas de los bloques.
            SetEmergencyBakedRect(
                _emergencyConfigBlock,
                0f, 1f, 1f, 1f, .5f, 1f,
                126.5f, -11f, -279f, 399f);

            SetEmergencyBakedRect(
                _emergencyListBlock,
                0f, 1f, 0f, 1f, 0f, 1f,
                11f, -13f, 240f, 398f);

            SetEmergencyBakedRect(
                _emergencyPrerequisiteBlock,
                0f, 1f, .5f, 1f, .5f, 1f,
                2f, -425f, -18f, 364f);

            SetEmergencyBakedRect(
                _emergencyRewardBlock,
                .5f, 1f, 1f, 1f, .5f, 1f,
                -4f, -424f, -18f, 363f);

            ApplyEmergencyListDesignerLayout();
            ApplyEmergencyConfigurationDesignerLayout();
            ApplyEmergencyRuleDesignerLayout();
        }

        private void ApplyEmergencyListDesignerLayout()
        {
            if (_emergencyListBlock == null)
                return;

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("Text") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                14f, -8f, 217f, 32f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("UP Emergency Add") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                12f, -351f, 171f, 35f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("UP Emergency Add/Text") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                3f, 0f, 178f, 35f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("UP Emergency Delete") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                196f, -351f, 37f, 35f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("UP Emergency Delete/Text") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                3f, 0f, 26f, 35f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find("UP Emergency List Scroll") as RectTransform,
                0f, 0f, 1f, 1f, .5f, .5f,
                0f, 2f, -24f, -100f);

            SetEmergencyBakedRect(
                _emergencyListBlock.Find(
                    "UP Emergency List Scroll/Viewport") as RectTransform,
                0f, 0f, 1f, 1f, .5f, .5f,
                -6f, 0f, -12f, 0f);
        }

        private void ApplyEmergencyConfigurationDesignerLayout()
        {
            if (_emergencyConfigBlock == null)
                return;

            var oldTitle = _emergencyConfigBlock.Find("UP Emergency Title");
            if (oldTitle != null)
                Destroy(oldTitle.gameObject);

            var oldChips = _emergencyConfigBlock.Find(
                "UP Emergency Disease Chips");
            if (oldChips != null)
                Destroy(oldChips.gameObject);
            _emergencyChipContent = null;

            SetEmergencyBakedRect(
                _emergencyConfigBlock.Find(
                    "UP Emergency Illness Dropdown") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                14f, -53f, 315f, 44f);

            SetEmergencyBakedRect(
                _emergencyConfigBlock.Find(
                    "UP Emergency Illness Dropdown/Label") as RectTransform,
                0f, 0f, 1f, 1f, .5f, .5f,
                -12f, 0f, -44f, 0f);

            SetEmergencyBakedRect(
                _emergencyConfigBlock.Find(
                    "UP Emergency Illness Add") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                339f, -53f, 100f, 41f);

            SetEmergencyBakedRect(
                _emergencyConfigBlock.Find(
                    "UP Emergency Illness Add/Text") as RectTransform,
                0f, 1f, 0f, 1f, 0f, 1f,
                3f, 0f, 95f, 43f);

            CreateEmergencyPatientsDesignerHeader();
            ApplyEmergencyCompactLeftDesignerLayout();
        }

        private void CreateEmergencyPatientsDesignerHeader()
        {
            if (_emergencyConfigBlock == null)
                return;

            var previous = _emergencyConfigBlock.Find("Cabecera Copy");
            if (previous != null)
                Destroy(previous.gameObject);

            var header = NewImage(
                "Cabecera Copy",
                _emergencyConfigBlock,
                _style.ButtonSprite,
                _style.HeaderColor,
                false);

            header.transform.SetSiblingIndex(Mathf.Min(
                6, _emergencyConfigBlock.childCount - 1));

            var headerRect = header.transform as RectTransform;
            SetEmergencyBakedRect(
                headerRect,
                0f, 1f, 1f, 1f, .5f, 1f,
                -1f, -6f, -24f, 42f);

            var title = CreateEmergencyLabel(
                header.transform,
                "Pacientes",
                16f, 0f, 240f, 42f, 22f,
                TextAlignmentOptions.MidlineLeft,
                "Texto");
            title.fontStyle = FontStyles.Bold;
        }

        private void ApplyEmergencyCompactLeftDesignerLayout()
        {
            if (_emergencyConfigBlock == null)
                return;

            var leftColumn =
                _emergencyConfigBlock.Find("UP Emergency Configuration Left");
            if (leftColumn == null)
                return;

            var actionRow = leftColumn.Find(
                "UP Emergency Native Dropdown Acción al fallar:");
            var scoreRow = leftColumn.Find(
                "UP Emergency Native Dropdown Tipo de puntuación:");
            var arrivalRow = leftColumn.Find(
                "UP Emergency Native Dropdown Método de llegada:");
            var warningRow = leftColumn.Find(
                "UP Emergency Native Toggle Aviso previo:");
            var rejectRow = leftColumn.Find(
                "UP Emergency Native Toggle Rechazable:");

            if (actionRow == null ||
                scoreRow == null ||
                arrivalRow == null ||
                warningRow == null ||
                rejectRow == null)
                return;

            var shared = rejectRow.Find("BGSolid") as RectTransform;
            if (shared == null)
                return;

            shared.SetParent(_emergencyConfigBlock, false);
            shared.SetSiblingIndex(Mathf.Min(
                7, _emergencyConfigBlock.childCount - 1));
            SetEmergencyBakedRect(
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                -289.249939f, -26.2999878f,
                -600.65f, -161.31f);

            var sharedImage = shared.GetComponent<Image>();
            if (sharedImage != null)
            {
                sharedImage.enabled = true;
                sharedImage.color = EmergencyPanelColor;
                sharedImage.raycastTarget = false;
            }

            MoveEmergencyDesignerPart(
                actionRow.Find(
                    "LayoutGroup/TitleLabelTextHolder/TitleLabelText")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                -98.4000244f, 84.79999f,
                -216.799927f, -193.69f);

            MoveEmergencyDesignerPart(
                actionRow.Find(
                    "LayoutGroup/TitlePanel/InputFieldContainer")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                139.975037f, 89.30002f,
                -309.350037f, -199.5f);

            if (_emergencyFailureDropdown != null)
            {
                SetEmergencyBakedRect(
                    _emergencyFailureDropdown.transform.Find("Label")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    -12f, 1.20999956f, -44f, 2.41999912f);
            }

            MoveEmergencyDesignerPart(
                scoreRow.Find(
                    "LayoutGroup/TitleLabelTextHolder/TitleLabelText")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                -97.39996f, 39.7999878f,
                -216.799988f, -191.69f);

            MoveEmergencyDesignerPart(
                scoreRow.Find(
                    "LayoutGroup/TitlePanel/InputFieldContainer")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                139.975037f, 40.30002f,
                -309.350037f, -201.5f);

            MoveEmergencyDesignerPart(
                arrivalRow.Find(
                    "LayoutGroup/TitleLabelTextHolder/TitleLabelText")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                -98.39996f, -3.70001221f,
                -216.799988f, -194.69f);

            MoveEmergencyDesignerPart(
                arrivalRow.Find(
                    "LayoutGroup/TitlePanel/InputFieldContainer")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                139.975037f, -4.19998169f,
                -309.350037f, -202.5f);

            MoveEmergencyDesignerPart(
                warningRow.Find(
                    "LayoutGroup/TitleLabelTextHolder/TitleLabelText")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                -138.899963f, -47.7000122f,
                -289.8f, -194.69f);

            MoveEmergencyDesignerPart(
                rejectRow.Find(
                    "LayoutGroup/TitleLabelTextHolder/TitleLabelText")
                    as RectTransform,
                shared,
                0f, 0f, 1f, 1f, .5f, .5f,
                73.60004f, -49.7000122f,
                -258.8f, -194.69f);

            MoveEmergencyDesignerToggle(
                _emergencyWarningToggle,
                shared,
                -32.4849243f, -49.4500122f);

            MoveEmergencyDesignerToggle(
                _emergencyRejectableToggle,
                shared,
                176.515076f, -50.4500122f);

            Destroy(actionRow.gameObject);
            Destroy(scoreRow.gameObject);
            Destroy(arrivalRow.gameObject);
            Destroy(warningRow.gameObject);
            Destroy(rejectRow.gameObject);
        }

        private static void MoveEmergencyDesignerToggle(
            Toggle toggle,
            RectTransform parent,
            float x,
            float y)
        {
            if (toggle == null || parent == null)
                return;

            var rect = toggle.transform as RectTransform;
            if (rect == null)
                return;

            rect.SetParent(parent, false);
            SetEmergencyBakedRect(
                rect,
                .5f, .5f, .5f, .5f, .5f, .5f,
                x, y, 40f, 40f);

            var background = rect.Find("Background") as RectTransform;
            if (background != null)
            {
                SetEmergencyBakedRect(
                    background,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    0f, 0f, 0f, 0f);

                var checkmark =
                    background.Find("Checkmark") as RectTransform;
                if (checkmark != null)
                {
                    SetEmergencyBakedRect(
                        checkmark,
                        0f, 0f, 1f, 1f, .5f, .5f,
                        0f, 0f, -10f, -10f);
                }
            }
        }

        private static void MoveEmergencyDesignerPart(
            RectTransform rect,
            RectTransform parent,
            float anchorMinX,
            float anchorMinY,
            float anchorMaxX,
            float anchorMaxY,
            float pivotX,
            float pivotY,
            float positionX,
            float positionY,
            float width,
            float height)
        {
            if (rect == null || parent == null)
                return;

            rect.SetParent(parent, false);
            SetEmergencyBakedRect(
                rect,
                anchorMinX, anchorMinY,
                anchorMaxX, anchorMaxY,
                pivotX, pivotY,
                positionX, positionY,
                width, height);
        }

        private void ApplyEmergencyRuleDesignerLayout()
        {
            if (_emergencyPrerequisiteBlock != null)
            {
                SetEmergencyBakedRect(
                    _emergencyPrerequisiteBlock.Find("Text") as RectTransform,
                    0f, 1f, 0f, 1f, 0f, 1f,
                    12f, -6f, 170f, 34f);

                SetEmergencyBakedRect(
                    _emergencyPrerequisiteBlock.Find(
                        "UP Emergency Prerequisite Scroll/Viewport")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    -6f, 0f, -12f, 0f);

                SetEmergencyBakedRect(
                    _emergencyPrerequisiteBlock.Find(
                        "UP Emergency Prerequisite Scrollbar")
                        as RectTransform,
                    1f, 0f, 1f, 1f, 1f, .5f,
                    -3f, -19f, 20f, -62f);

                SetEmergencyBakedRect(
                    _emergencyPrerequisiteBlock.Find(
                        "UP Emergency Prerequisite Type/Label")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    -12f, 0f, -44f, 0f);
            }

            if (_emergencyRewardBlock != null)
            {
                SetEmergencyBakedRect(
                    _emergencyRewardBlock.Find(
                        "UP Emergency Reward Scroll") as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    0f, -19f, -24f, -62f);

                SetEmergencyBakedRect(
                    _emergencyRewardBlock.Find(
                        "UP Emergency Reward Scroll/Viewport")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    -6f, 0f, -12f, 0f);

                SetEmergencyBakedRect(
                    _emergencyRewardBlock.Find(
                        "UP Emergency Reward Scrollbar")
                        as RectTransform,
                    1f, 0f, 1f, 1f, 1f, .5f,
                    -2f, -19f, 20f, -62f);

                SetEmergencyBakedRect(
                    _emergencyRewardBlock.Find(
                        "UP Emergency Reward Scrollbar/Sliding Area")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    0f, 0f, 0f, 0f);

                SetEmergencyBakedRect(
                    _emergencyRewardBlock.Find(
                        "UP Emergency Reward Scrollbar/Sliding Area/Handle")
                        as RectTransform,
                    0f, 0f, 1f, 1f, .5f, .5f,
                    0f, 0f, -10f, -10f);
            }
        }

        private static void SetEmergencyBakedRect(
            RectTransform rect,
            float anchorMinX,
            float anchorMinY,
            float anchorMaxX,
            float anchorMaxY,
            float pivotX,
            float pivotY,
            float positionX,
            float positionY,
            float width,
            float height)
        {
            if (rect == null)
                return;

            rect.anchorMin = new Vector2(anchorMinX, anchorMinY);
            rect.anchorMax = new Vector2(anchorMaxX, anchorMaxY);
            rect.pivot = new Vector2(pivotX, pivotY);
            rect.anchoredPosition = new Vector2(positionX, positionY);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void ClearEmergencyChildren(Transform parent)
        {
            if (parent == null) return;

            for (var index = parent.childCount - 1; index >= 0; --index)
            {
                var child = parent.GetChild(index);
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }
    }
}
