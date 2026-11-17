// Traducción automática de las cuatro descripciones del mapa mediante MyMemory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using HarmonyLib;
using TH20;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        private const int MyMemoryMaximumSegmentBytes = 450;
        private const string MyMemoryEndpoint =
            "https://api.mymemory.translated.net/get?q={0}&langpair={1}%7C{2}";

        private static readonly string[] TranslatableMapKeys =
        {
            ScenarioMapLocalization.DescriptionKey,
            ScenarioMapLocalization.Star1Key,
            ScenarioMapLocalization.Star2Key,
            ScenarioMapLocalization.Star3Key
        };

        private Button _generalTranslateLanguagesButton;
        private Button _objectiveTranslateLanguagesButton;
        private GameObject _translationConfirmationFlag;
        private bool _translationInProgress;

        [Serializable]
        private sealed class MyMemoryResponse
        {
            public MyMemoryResponseData responseData = new MyMemoryResponseData();
            public string responseDetails = "";
            public int responseStatus = 0;
            public bool quotaFinished = false;
        }

        [Serializable]
        private sealed class MyMemoryResponseData
        {
            public string translatedText = "";
        }

        private sealed class TranslationResult
        {
            internal string Text = "";
            internal string Error = "";
        }

        private void ShowTranslateLanguagesConfirmation()
        {
            if (_translationInProgress || _level?.App?.MessageBox == null) return;

            SaveCurrentLocalizedScenarioInputs();
            SetObjectiveFlagExpanded(false);
            SetGeneralFlagExpanded(false);

            var sourceIndex = Mathf.Clamp(
                _objectiveFlagIndex, 0, ObjectiveFlagLanguageCodes.Length - 1);
            var sourceLanguage = ObjectiveFlagLanguageCodes[sourceIndex];
            var hasSource = TranslatableMapKeys.Any(key =>
                !string.IsNullOrWhiteSpace(
                    ScenarioEditorStorage.GetMapText(_level, key, sourceLanguage)));
            if (!hasSource)
            {
                ShowTranslationMessage(
                    EditorLocalization.Get(
                        "editor.scenario_settings.translation.confirm_title"),
                    EditorLocalization.Get(
                        "editor.scenario_settings.translation.no_source"));
                return;
            }

            var messageBox = _level.App.MessageBox;
            if (messageBox.IsVisibleOrClosing) return;
            DestroyTranslationConfirmationFlag();

            var body = "\n\n" + EditorLocalization.Format(
                "editor.scenario_settings.translation.confirm_body",
                ObjectiveNativeFlagName(sourceIndex));
            messageBox.ShowAsYesNo(
                EditorLocalization.Get(
                    "editor.scenario_settings.translation.confirm_title"),
                body,
                EditorLocalization.Get(
                    "editor.scenario_settings.translation.action"),
                EditorLocalization.Get("common.cancel"),
                () =>
                {
                    DestroyTranslationConfirmationFlag();
                    if (!_translationInProgress)
                        StartCoroutine(TranslateMapDescriptions(sourceIndex));
                },
                DestroyTranslationConfirmationFlag,
                true);
            AddTranslationConfirmationFlag(messageBox, sourceIndex);
        }

        private void AddTranslationConfirmationFlag(MessageBox messageBox, int sourceIndex)
        {
            var body = AccessTools.Field(typeof(MessageBox), "_bodyText")
                ?.GetValue(messageBox) as TMP_Text;
            var sprite = LoadObjectiveFlag(ObjectiveFlagFileNames[sourceIndex]);
            if (body == null || sprite == null) return;

            _translationConfirmationFlag = new GameObject(
                "UP Translation Source Flag", typeof(RectTransform), typeof(Image));
            _translationConfirmationFlag.transform.SetParent(body.transform, false);
            var rect = _translationConfirmationFlag.transform as RectTransform;
            rect.anchorMin = new Vector2(.5f, 1f);
            rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(61f, 36f);
            var image = _translationConfirmationFlag.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private void DestroyTranslationConfirmationFlag()
        {
            if (_translationConfirmationFlag != null)
                Destroy(_translationConfirmationFlag);
            _translationConfirmationFlag = null;
        }

        private IEnumerator TranslateMapDescriptions(int sourceIndex)
        {
            _translationInProgress = true;
            SetTranslationButtonsInteractable(false);

            var sourceLanguage = ObjectiveFlagLanguageCodes[sourceIndex];
            var sourceTexts = TranslatableMapKeys.ToDictionary(
                key => key,
                key => ScenarioEditorStorage.GetMapText(
                    _level, key, sourceLanguage),
                StringComparer.OrdinalIgnoreCase);
            var translated = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);
            var error = "";

            foreach (var targetLanguage in ObjectiveFlagLanguageCodes)
            {
                if (string.Equals(targetLanguage, sourceLanguage,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetValues = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                translated[targetLanguage] = targetValues;
                foreach (var key in TranslatableMapKeys)
                {
                    var source = sourceTexts[key];
                    if (string.IsNullOrWhiteSpace(source)) continue;

                    var result = new TranslationResult();
                    yield return TranslateTextWithMyMemory(
                        source, sourceLanguage, targetLanguage, result);
                    if (!string.IsNullOrEmpty(result.Error))
                    {
                        error = result.Error;
                        break;
                    }
                    targetValues[key] = result.Text;
                }
                if (!string.IsNullOrEmpty(error)) break;
            }

            if (string.IsNullOrEmpty(error))
            {
                foreach (var language in translated)
                    foreach (var value in language.Value)
                        ScenarioEditorStorage.SetMapText(
                            _level, value.Key, language.Key, value.Value);
                RefreshLocalizedScenarioInputs();
            }

            _translationInProgress = false;
            SetTranslationButtonsInteractable(true);
            ShowTranslationMessage(
                EditorLocalization.Get(
                    "editor.scenario_settings.translation.confirm_title"),
                string.IsNullOrEmpty(error)
                    ? EditorLocalization.Get(
                        "editor.scenario_settings.translation.success")
                    : EditorLocalization.Format(
                        "editor.scenario_settings.translation.error", error));
        }

        private IEnumerator TranslateTextWithMyMemory(string source,
            string sourceLanguage, string targetLanguage, TranslationResult result)
        {
            var translatedLines = new List<string>();
            var lines = (source ?? "").Replace("\r\n", "\n")
                .Replace('\r', '\n').Split(new[] { '\n' },
                    StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (line.Length == 0)
                {
                    translatedLines.Add("");
                    continue;
                }

                var translatedChunks = new List<string>();
                foreach (var chunk in SplitMyMemorySegments(line))
                {
                    var url = string.Format(
                        MyMemoryEndpoint,
                        UnityWebRequest.EscapeURL(chunk),
                        UnityWebRequest.EscapeURL(MyMemoryLanguage(sourceLanguage)),
                        UnityWebRequest.EscapeURL(MyMemoryLanguage(targetLanguage)));
                    using (var request = UnityWebRequest.Get(url))
                    {
                        yield return request.SendWebRequest();
                        if (request.result == UnityWebRequest.Result.ConnectionError ||
                            request.result == UnityWebRequest.Result.ProtocolError ||
                            request.result == UnityWebRequest.Result.DataProcessingError)
                        {
                            result.Error = string.IsNullOrWhiteSpace(request.error)
                                ? "HTTP " + request.responseCode
                                : request.error;
                            yield break;
                        }

                        MyMemoryResponse response;
                        string parseError;
                        if (!TryReadMyMemoryResponse(
                                request.downloadHandler.text,
                                out response, out parseError))
                        {
                            result.Error = parseError;
                            yield break;
                        }
                        if (response == null || response.responseStatus != 200 ||
                            response.quotaFinished ||
                            string.IsNullOrWhiteSpace(
                                response.responseData?.translatedText))
                        {
                            result.Error = response?.responseDetails;
                            if (string.IsNullOrWhiteSpace(result.Error))
                                result.Error = "MyMemory HTTP " +
                                    (response?.responseStatus ?? 0);
                            yield break;
                        }
                        translatedChunks.Add(WebUtility.HtmlDecode(
                            response.responseData.translatedText));
                    }
                }
                translatedLines.Add(string.Join(" ", translatedChunks.ToArray()));
            }
            result.Text = string.Join("\n", translatedLines.ToArray());
        }

        private static bool TryReadMyMemoryResponse(string json,
            out MyMemoryResponse response, out string error)
        {
            try
            {
                response = JsonUtility.FromJson<MyMemoryResponse>(json ?? "");
                error = "";
                return response != null;
            }
            catch (Exception exception)
            {
                response = null;
                error = exception.Message;
                return false;
            }
        }

        private static IEnumerable<string> SplitMyMemorySegments(string source)
        {
            var remaining = (source ?? "").Trim();
            while (Encoding.UTF8.GetByteCount(remaining) >
                   MyMemoryMaximumSegmentBytes)
            {
                var length = MaximumUtf8PrefixLength(
                    remaining, MyMemoryMaximumSegmentBytes);
                var split = -1;
                for (var index = length - 1; index > 0; --index)
                {
                    if (!char.IsWhiteSpace(remaining[index]) &&
                        remaining[index] != '.' && remaining[index] != '!' &&
                        remaining[index] != '?' && remaining[index] != ';')
                        continue;
                    split = index;
                    if (!char.IsWhiteSpace(remaining[index])) ++split;
                    break;
                }
                if (split <= 0) split = length;
                var chunk = remaining.Substring(0, split).Trim();
                if (chunk.Length > 0) yield return chunk;
                remaining = remaining.Substring(split).TrimStart();
            }
            if (remaining.Length > 0) yield return remaining;
        }

        private static int MaximumUtf8PrefixLength(string value, int maximumBytes)
        {
            var bytes = 0;
            var length = 0;
            while (length < value.Length)
            {
                var characterLength = char.IsHighSurrogate(value[length]) &&
                                      length + 1 < value.Length &&
                                      char.IsLowSurrogate(value[length + 1])
                    ? 2 : 1;
                var characterBytes = Encoding.UTF8.GetByteCount(
                    value.Substring(length, characterLength));
                if (bytes + characterBytes > maximumBytes) break;
                bytes += characterBytes;
                length += characterLength;
            }
            return Math.Max(1, length);
        }

        private static string MyMemoryLanguage(string language)
        {
            var normalized = ScenarioMapLocalization.NormalizeLanguage(language);
            if (normalized == "zh-hans") return "zh-CN";
            if (normalized == "zh-hant") return "zh-TW";
            if (normalized == "pt") return "pt-BR";
            return normalized;
        }

        private void SetTranslationButtonsInteractable(bool value)
        {
            if (_generalTranslateLanguagesButton != null)
                _generalTranslateLanguagesButton.interactable = value;
            if (_objectiveTranslateLanguagesButton != null)
                _objectiveTranslateLanguagesButton.interactable = value;
        }

        private void ShowTranslationMessage(string title, string body)
        {
            var messageBox = _level?.App?.MessageBox;
            if (messageBox == null || messageBox.IsVisibleOrClosing) return;
            messageBox.Show(title ?? "", body ?? "",
                EditorLocalization.Get(
                    "editor.scenario_settings.translation.ok"), null);
        }
    }
}
