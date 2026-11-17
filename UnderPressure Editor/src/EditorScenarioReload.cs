namespace UnderPressure.Editor
{
    internal sealed partial class ScenarioEditorDockLayout
    {
        internal static void NotifyScenarioDataLoaded()
        {
            if (_active == null) return;

            // Estas listas son borradores de la interfaz, no la fuente del mapa.
            // Al abrir otro paquete deben volver a leerse desde ScenarioEditorStorage.
            _active._objectiveDrafts.Clear();
            _active._selectedObjective = 0;
            _active._emergencyDataLoaded = false;
            _active._emergencies.Clear();
            _active._selectedEmergency = 0;
            _active._settingsDiseasesLoaded = false;
            _active._settingsPatientValuesLoaded = false;
            _active._settingsIllnesses.Clear();
            for (var index = 1; index <= EmergencyTabIndex; ++index)
                _active._settingsTabContentsCurrent[index] = false;

            if (_active._settingsOverlay == null ||
                !_active._settingsOverlay.activeInHierarchy) return;
            if (_active._activeSettingsTab == 1)
                _active.RefreshDiseaseSettingsContents();
            else if (_active._activeSettingsTab == ObjectiveTabIndex)
                _active.RefreshObjectiveSettingsContents();
            else if (_active._activeSettingsTab == EmergencyTabIndex)
                _active.RefreshEmergencySettingsContents();
            else if (_active._activeSettingsTab == 3)
                _active.RefreshPlotSettingsContents();
            if (_active._activeSettingsTab >= 1 &&
                _active._activeSettingsTab <= EmergencyTabIndex)
                _active._settingsTabContentsCurrent[_active._activeSettingsTab] = true;
        }
    }
}
