using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Prototipo exclusivamente visual. No modifica atributos ni jugabilidad.
    /// </summary>
    internal static class HospitalLightingPrototype
    {
        private const string WindowLightName = "Under Pressure - Window Daylight";
        private const string TestLampLightName = "Under Pressure - GP Lamp Light";
        private const string LightHeadedLightName = "Under Pressure - Light Headed Bulb";
        private const string LightHeadedIllnessTerm = "Illness/LightHeaded_Name";

        private static readonly FieldInfo OpenLightMaterialField =
            AccessTools.Field(typeof(Room), "_roomOpenLightMaterial");
        private static readonly FieldInfo ClosedLightMaterialField =
            AccessTools.Field(typeof(Room), "_roomClosedLightMaterial");
        private static readonly FieldInfo OperationalLightMaterialField =
            AccessTools.Field(typeof(Room), "_roomOperationalLightMaterial");
        private static readonly FieldInfo PointLightsField =
            AccessTools.Field(typeof(RoomLightingManager), "_clippablePointLights");
        private static readonly FieldInfo SpotLightsField =
            AccessTools.Field(typeof(RoomLightingManager), "_clippableSpotLights");
        private static readonly FieldInfo InteriorLightField =
            AccessTools.Field(typeof(RoomLightingManager), "_interiorLight");
        private static readonly FieldInfo ExteriorLightField =
            AccessTools.Field(typeof(RoomLightingManager), "_exteriorLight");
        private static readonly FieldInfo RoomLightDirectionField =
            AccessTools.Field(typeof(RoomLightingManager), "_roomLightDirection");
        private static readonly FieldInfo ExteriorLightDirectionField =
            AccessTools.Field(typeof(RoomLightingManager), "_exteriorLightDirection");
        private static readonly MethodInfo ReloadRoomLightsMethod =
            AccessTools.Method(typeof(Room), "ReloadRoomLights");
        private static readonly FieldInfo FloorTileRenderersField =
            AccessTools.Field(typeof(RoomFloorPlanVisual), "_floorTileRenderers");

        private static readonly HashSet<Room> KnownRooms = new HashSet<Room>();
        private static readonly HashSet<RoomItem> KnownItems = new HashSet<RoomItem>();
        private static readonly HashSet<Patient> KnownPatients = new HashSet<Patient>();
        private static readonly Dictionary<int, RoomLightingMaterialState> RoomLightingMaterialStates =
            new Dictionary<int, RoomLightingMaterialState>();
        private static readonly Dictionary<RoomLightingManager, DirectionalLightState>
            OriginalDirectionalLightStates = new Dictionary<RoomLightingManager, DirectionalLightState>();
        private static readonly Dictionary<HospitalMap, GameObject> CeilingObjects =
            new Dictionary<HospitalMap, GameObject>();
        private static Level _knownLevel;
        private static int _windowLightsCreated;
        private static int _testLampLightsCreated;
        private static int _lightHeadedLightsCreated;
        private static bool _materialFormatLogged;
        private static bool _windowAnchorLogged;
        private static readonly HashSet<int> LoggedDiagnosticMaterials = new HashSet<int>();
        private static Cubemap _neutralReflectionCubemap;
        private static UnityEngine.Rendering.ShadowCastingMode _shadowCeilingMode =
            LightingParameters.ShadowCeilingModeAtStart;

        internal static bool Enabled => UnderPressurePlugin.ShouldUseVisualLighting;
        internal static bool ShadowCeilingVisible =>
            _shadowCeilingMode == LightingParameters.ShadowCeilingVisibleMode;

        internal static bool ToggleShadowCeilingVisibility()
        {
            _shadowCeilingMode = ShadowCeilingVisible
                ? LightingParameters.ShadowCeilingHiddenMode
                : LightingParameters.ShadowCeilingVisibleMode;
            foreach (var pair in CeilingObjects)
            {
                var renderer = pair.Value != null ? pair.Value.GetComponent<MeshRenderer>() : null;
                if (renderer != null)
                    renderer.shadowCastingMode = _shadowCeilingMode;
            }
            return ShadowCeilingVisible;
        }

        internal static Cubemap NeutralReflectionCubemap
        {
            get
            {
                if (_neutralReflectionCubemap != null) return _neutralReflectionCubemap;
                _neutralReflectionCubemap = new Cubemap(1, TextureFormat.RGBA32, false)
                {
                    name = "Under Pressure - Neutral Reflection",
                    hideFlags = HideFlags.HideAndDontSave
                };
                var black = new[] { LightingParameters.NeutralReflectionColor };
                for (var face = 0; face < 6; ++face)
                    _neutralReflectionCubemap.SetPixels(black, (CubemapFace)face);
                _neutralReflectionCubemap.Apply(false, true);
                return _neutralReflectionCubemap;
            }
        }

        internal static void TrackRoom(Room room)
        {
            if (room == null) return;
            EnsureLevel(room.Level);
            KnownRooms.Add(room);
            if (Enabled)
            {
                UseExteriorDirectionForInterior(room.Level?.VisualManager?.RoomLightingManager);
                EnsureShadowCeiling(room.FloorPlan?.HospitalMap);
                DarkenRoomMaterials(room);
            }
        }

        internal static void TrackItem(RoomItem item)
        {
            if (item == null) return;
            EnsureLevel(item.Level);
            KnownItems.Add(item);
            if (Enabled)
            {
                ApplyRoomLightingToRenderers(item.Visual?.GameObject);
                LogRelevantItemMaterials(item);
                EnsureVisualLights(item);
            }
        }

        internal static void TrackPatient(Patient patient)
        {
            if (patient == null) return;
            EnsureLevel(patient.Level);
            if (!IsLightHeaded(patient)) return;
            KnownPatients.Add(patient);
            if (Enabled) EnsureLightHeadedLight(patient);
        }

        private static void EnsureLevel(Level level)
        {
            if (level == null || ReferenceEquals(level, _knownLevel)) return;
            RemoveAllCeilings();
            _knownLevel = level;
            KnownRooms.Clear();
            KnownItems.Clear();
            KnownPatients.Clear();
            OriginalDirectionalLightStates.Clear();
            _windowLightsCreated = 0;
            _testLampLightsCreated = 0;
            _lightHeadedLightsCreated = 0;
            _materialFormatLogged = false;
            _windowAnchorLogged = false;
            LoggedDiagnosticMaterials.Clear();
            if (Enabled) ApplyRoomLightingToLoadedMaterials();
        }

        internal static void NotifySettingChanged()
        {
            var managers = new HashSet<RoomLightingManager>();
            if (Enabled) ApplyRoomLightingToLoadedMaterials();
            if (!Enabled)
            {
                RestoreRoomLightingMaterials();
                RemoveAllCeilings();
            }
            foreach (var room in KnownRooms)
            {
                if (room == null) continue;
                try
                {
                    ReloadRoomLightsMethod?.Invoke(room, null);
                    var manager = room.Level?.VisualManager?.RoomLightingManager;
                    if (manager != null) managers.Add(manager);
                    if (Enabled) EnsureShadowCeiling(room.FloorPlan?.HospitalMap);
                }
                catch (Exception exception)
                {
                    UnderPressurePlugin.Log.LogWarning(
                        "No se pudo actualizar la iluminación de una sala: " + exception.Message);
                }
            }

            foreach (var item in KnownItems)
            {
                if (item == null) continue;
                if (Enabled)
                {
                    ApplyRoomLightingToRenderers(item.Visual?.GameObject);
                    EnsureVisualLights(item);
                }
                else RemoveVisualLights(item);
            }

            foreach (var patient in KnownPatients)
            {
                if (patient == null) continue;
                if (Enabled) EnsureLightHeadedLight(patient);
                else RemoveLightHeadedLight(patient);
                var manager = patient.Level?.VisualManager?.RoomLightingManager;
                if (manager != null) managers.Add(manager);
            }
            foreach (var manager in managers)
            {
                if (Enabled) UseExteriorDirectionForInterior(manager);
                else RestoreInteriorDirection(manager);
                manager.RegenerateInteriorVolumeLights();
            }

            UnderPressurePlugin.Log.LogInfo(
                $"Iluminación visual {(Enabled ? "activada" : "desactivada")}: " +
                $"{KnownRooms.Count} salas, {KnownItems.Count} objetos rastreados, " +
                $"{_windowLightsCreated} luces de ventana, {_testLampLightsCreated} lámparas de prueba y " +
                $"{_lightHeadedLightsCreated} pacientes Cabeza Bombilla iluminados.");
        }

        internal static void RefreshTrackedVisualLights()
        {
            if (!Enabled) return;
            ApplyRoomLightingToLoadedMaterials();
            foreach (var room in KnownRooms)
            {
                if (room == null) continue;
                UseExteriorDirectionForInterior(room.Level?.VisualManager?.RoomLightingManager);
                EnsureShadowCeiling(room.FloorPlan?.HospitalMap);
            }
            var windowsPrepared = 0;
            foreach (var item in KnownItems)
            {
                if (item == null) continue;
                ApplyRoomLightingToRenderers(item.Visual?.GameObject);
                EnsureVisualLights(item);
                if (item.IsHospitalWindow && item.Visual?.GameObject != null)
                    ++windowsPrepared;
            }
            foreach (var patient in KnownPatients)
            {
                if (patient != null) EnsureLightHeadedLight(patient);
            }
            UnderPressurePlugin.Log.LogInfo(
                $"Iluminación tras carga: {windowsPrepared} ventanas preparadas para luz direccional.");
        }

        internal static void DarkenRoomMaterials(Room room)
        {
            if (!Enabled || room == null) return;
            var roomType = room.Definition != null ? room.Definition._type.ToString() : "SinDefinicion";
            var open = OpenLightMaterialField?.GetValue(room) as Material;
            var closed = ClosedLightMaterialField?.GetValue(room) as Material;
            var operational = OperationalLightMaterialField?.GetValue(room) as Material;
            LogMaterialDiagnostic(open, $"SALA {roomType} / abierta");
            LogMaterialDiagnostic(closed, $"SALA {roomType} / cerrada");
            LogMaterialDiagnostic(operational, $"SALA {roomType} / operativa");
            DarkenMaterial(open);
            DarkenMaterial(closed);
            DarkenMaterial(operational);
        }

        private static void LogRelevantItemMaterials(RoomItem item)
        {
            var root = item?.Visual?.GameObject;
            if (root == null) return;
            var roomType = item.OwningRoom?.Definition != null
                ? item.OwningRoom.Definition._type.ToString()
                : string.Empty;
            var definition = item.Definition as RoomItemDefinition;
            var debugTag = definition?.DebugTag ?? string.Empty;
            var prefabName = definition?.GetPrefab(0)?.name ?? string.Empty;
            var search = (roomType + " " + debugTag + " " + prefabName + " " + root.name).ToLowerInvariant();
            if (!search.Contains("diagn") && !search.Contains("ward") &&
                !search.Contains("nurse") && !search.Contains("bed") &&
                !search.Contains("computer") && !search.Contains("monitor") &&
                !search.Contains("pc") && !search.Contains("machine")) return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? renderer.name;
                foreach (var material in renderer.sharedMaterials)
                    LogMaterialDiagnostic(material,
                        $"OBJETO sala={roomType}, tag={debugTag}, prefab={prefabName}, malla={meshName}");
            }
        }

        private static void LogMaterialDiagnostic(Material material, string context)
        {
            if (material == null || !LoggedDiagnosticMaterials.Add(material.GetInstanceID())) return;
            var values = string.Empty;
            AppendColor(material, "_Color", ref values);
            AppendColor(material, "_BaseColor", ref values);
            AppendColor(material, "_EmissionColor", ref values);
            AppendColor(material, "_EmissiveColor", ref values);
            AppendColor(material, "_SpecColor", ref values);
            AppendFloat(material, "_Metallic", ref values);
            AppendFloat(material, "_Glossiness", ref values);
            AppendFloat(material, "_Smoothness", ref values);
            AppendFloat(material, "_SpecularIntensity", ref values);
            AppendFloat(material, "_Emission", ref values);
            AppendFloat(material, "_EmissionIntensity", ref values);
            AppendFloat(material, "_SelfIllum", ref values);
            AppendFloat(material, "_SelfIllumination", ref values);
            AppendFloat(material, "_GlowIntensity", ref values);
            AppendVector(material, "_AmbientLightColorIntensity", ref values);
            AppendVector(material, "_DirectionalLightColorIntensity", ref values);
            AppendFloat(material, "_ShadowIntensity", ref values);
            AppendFloat(material, "_FalloffStrength", ref values);
            AppendFloat(material, "_FalloffThickness", ref values);
            AppendFloat(material, "_CeilingFalloffAmplitude", ref values);
            AppendFloat(material, "_CeilingHeight", ref values);
            UnderPressurePlugin.Log.LogInfo(
                $"MATERIAL INVESTIGACION: {context}; material='{material.name}'; " +
                $"shader='{material.shader?.name}'; cola={material.renderQueue}; " +
                $"keywords=[{string.Join(",", material.shaderKeywords)}]; GI={material.globalIlluminationFlags};{values}");
        }

        private static void ApplyRoomLightingToLoadedMaterials()
        {
            foreach (var material in Resources.FindObjectsOfTypeAll<Material>())
                ApplyRoomLighting(material);
        }

        private static void ApplyRoomLightingToRenderers(GameObject root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                foreach (var material in renderer.sharedMaterials)
                    ApplyRoomLighting(material);
            }
        }

        private static void ApplyRoomLighting(Material material)
        {
            if (material == null || !material.HasProperty("_ApplyRoomLighting")) return;
            var id = material.GetInstanceID();
            if (RoomLightingMaterialStates.TryGetValue(id, out var previous))
            {
                if (ReferenceEquals(previous.Material, material)) return;
                RoomLightingMaterialStates.Remove(id);
            }
            RoomLightingMaterialStates.Add(id, new RoomLightingMaterialState
            {
                Material = material,
                ApplyRoomLighting = material.GetFloat("_ApplyRoomLighting"),
                ApplyRoomLightingOff = material.IsKeywordEnabled("_APPLYROOMLIGHTING_OFF")
            });

            material.SetFloat("_ApplyRoomLighting", LightingParameters.MaterialRoomLighting);
            if (LightingParameters.MaterialRoomLighting > 0.5f)
                material.DisableKeyword("_APPLYROOMLIGHTING_OFF");
            else
                material.EnableKeyword("_APPLYROOMLIGHTING_OFF");
        }

        private static void RestoreRoomLightingMaterials()
        {
            foreach (var state in RoomLightingMaterialStates.Values)
            {
                var material = state.Material;
                if (material == null || !material.HasProperty("_ApplyRoomLighting")) continue;
                material.SetFloat("_ApplyRoomLighting", state.ApplyRoomLighting);
                if (state.ApplyRoomLightingOff)
                    material.EnableKeyword("_APPLYROOMLIGHTING_OFF");
                else
                    material.DisableKeyword("_APPLYROOMLIGHTING_OFF");
            }
            RoomLightingMaterialStates.Clear();
        }

        private static void AppendColor(Material material, string property, ref string values)
        {
            if (material.HasProperty(property)) values += $" {property}={material.GetColor(property)};";
        }

        private static void AppendFloat(Material material, string property, ref string values)
        {
            if (material.HasProperty(property)) values += $" {property}={material.GetFloat(property):0.###};";
        }

        private static void AppendVector(Material material, string property, ref string values)
        {
            if (material.HasProperty(property)) values += $" {property}={material.GetVector(property)};";
        }

        private static void DarkenMaterial(Material material)
        {
            if (material == null) return;
            if (LightingParameters.NativeCeilingLights > 0f) material.EnableKeyword("CEILINGLIGHTS_ON");
            else material.DisableKeyword("CEILINGLIGHTS_ON");
            if (material.HasProperty("CeilingLights"))
                material.SetFloat("CeilingLights", LightingParameters.NativeCeilingLights);
            var hasAmbient = material.HasProperty("_AmbientLightColorIntensity");
            var hasDirectional = material.HasProperty("_DirectionalLightColorIntensity");
            if (!_materialFormatLogged)
            {
                _materialFormatLogged = true;
                UnderPressurePlugin.Log.LogInfo(
                    $"Material de iluminación '{material.name}': " +
                    $"ambient={hasAmbient}, directional={hasDirectional}.");
            }
            if (hasAmbient)
            {
                var ambient = material.GetVector("_AmbientLightColorIntensity");
                ambient.x = LightingParameters.AmbientColor.r;
                ambient.y = LightingParameters.AmbientColor.g;
                ambient.z = LightingParameters.AmbientColor.b;
                ambient.w = LightingParameters.AmbientIntensity;
                material.SetVector("_AmbientLightColorIntensity", ambient);
            }
            if (hasDirectional)
            {
                var directional = material.GetVector("_DirectionalLightColorIntensity");
                directional.x = LightingParameters.DirectionalColor.r;
                directional.y = LightingParameters.DirectionalColor.g;
                directional.z = LightingParameters.DirectionalColor.b;
                directional.w = LightingParameters.DirectionalIntensity;
                material.SetVector("_DirectionalLightColorIntensity", directional);
            }
            if (material.HasProperty("_AmbientLightColor"))
                material.SetColor("_AmbientLightColor", LightingParameters.AmbientColor);
            if (material.HasProperty("_AmbientLightIntensity"))
                material.SetFloat("_AmbientLightIntensity", LightingParameters.AmbientIntensity);
            if (material.HasProperty("_DirectionalLightColor"))
                material.SetColor("_DirectionalLightColor", LightingParameters.DirectionalColor);
            if (material.HasProperty("_DirectionalLightIntensity"))
                material.SetFloat("_DirectionalLightIntensity", LightingParameters.DirectionalIntensity);
            if (material.HasProperty("_ShadowIntensity"))
                material.SetFloat("_ShadowIntensity", LightingParameters.IsolateWindowGlassShadow
                    ? 0f
                    : LightingParameters.RoomShadowIntensity);
            if (material.HasProperty("_FalloffStrength"))
                material.SetFloat("_FalloffStrength", LightingParameters.RoomLightFalloffStrength);
            if (material.HasProperty("_FalloffThickness"))
                material.SetFloat("_FalloffThickness", LightingParameters.RoomLightFalloffThickness);
            if (material.HasProperty("_CeilingFalloffAmplitude"))
                material.SetFloat("_CeilingFalloffAmplitude", LightingParameters.IsolateWindowGlassShadow
                    ? 0f
                    : LightingParameters.RoomCeilingFalloffAmplitude);
            if (material.HasProperty("_CeilingHeight"))
                material.SetFloat("_CeilingHeight", LightingParameters.NativeCeilingLightHeight);
            if (material.HasProperty("_CeilingLightIntensity"))
                material.SetFloat("_CeilingLightIntensity", LightingParameters.NativeCeilingLightIntensity);
            if (material.HasProperty("_CeilingLightOffset"))
                material.SetFloat("_CeilingLightOffset", LightingParameters.NativeCeilingLightOffset);
            if (material.HasProperty("_CeilingLightParams"))
                material.SetVector("_CeilingLightParams", Vector4.zero);
            if (material.HasProperty("_CeilingLightParams1"))
                material.SetVector("_CeilingLightParams1", Vector4.zero);
        }

        private static void EnsureVisualLights(RoomItem item)
        {
            if (!Enabled || item?.Visual?.GameObject == null) return;
            if (item.IsHospitalWindow)
            {
                RemoveWindowSpot(item);
                if (LightingParameters.UseWindowSpotFallback) EnsureWindowLight(item);
            }
            else if (IsVerifiedGpLamp(item.Definition as RoomItemDefinition))
                EnsureTestLampLight(item);
        }

        private static void UseExteriorDirectionForInterior(RoomLightingManager manager)
        {
            if (manager == null) return;
            var interiorLight = InteriorLightField?.GetValue(manager) as UnityEngine.Light;
            var exteriorLight = ExteriorLightField?.GetValue(manager) as UnityEngine.Light;
            var roomDirection = RoomLightDirectionField?.GetValue(manager);
            if (!OriginalDirectionalLightStates.ContainsKey(manager))
            {
                OriginalDirectionalLightStates.Add(manager, new DirectionalLightState
                {
                    Direction = roomDirection is Vector3 direction ? direction : Vector3.forward,
                    Rotation = interiorLight != null ? interiorLight.transform.rotation : Quaternion.identity,
                    CullingMask = interiorLight != null ? interiorLight.cullingMask : 0,
                    Shadows = interiorLight != null ? interiorLight.shadows : LightShadows.None,
                    ShadowStrength = interiorLight != null ? interiorLight.shadowStrength : 0f
                });
            }

            if (LightingParameters.CopyExteriorDirectionToInterior)
            {
                var exteriorDirection = ExteriorLightDirectionField?.GetValue(manager);
                if (exteriorDirection is Vector3 directionValue)
                    RoomLightDirectionField?.SetValue(manager, directionValue);
            }
            if (interiorLight != null)
            {
                if (LightingParameters.CopyExteriorDirectionToInterior && exteriorLight != null)
                {
                    interiorLight.transform.rotation = exteriorLight.transform.rotation;
                }
                interiorLight.shadows = LightShadows.None;
                interiorLight.shadowStrength = 0f;
            }
        }

        private static void RestoreInteriorDirection(RoomLightingManager manager)
        {
            if (manager == null || !OriginalDirectionalLightStates.TryGetValue(manager, out var state)) return;
            RoomLightDirectionField?.SetValue(manager, state.Direction);
            var interiorLight = InteriorLightField?.GetValue(manager) as UnityEngine.Light;
            if (interiorLight != null)
            {
                interiorLight.transform.rotation = state.Rotation;
                interiorLight.cullingMask = state.CullingMask;
                interiorLight.shadows = state.Shadows;
                interiorLight.shadowStrength = state.ShadowStrength;
            }
            OriginalDirectionalLightStates.Remove(manager);
        }

        private static void EnsureShadowCeiling(HospitalMap hospitalMap)
        {
            if (!Enabled || hospitalMap?.IndoorState == null ||
                hospitalMap.RoomVisual?.GameObject == null)
                return;
            if (CeilingObjects.TryGetValue(hospitalMap, out var existing) && existing != null) return;

            var indoorState = hospitalMap.IndoorState;
            var floorRenderers = FloorTileRenderersField?.GetValue(hospitalMap.RoomVisual) as List<Renderer>;
            Material ceilingMaterial = null;
            if (floorRenderers != null)
            {
                foreach (var floorRenderer in floorRenderers)
                {
                    if (floorRenderer?.sharedMaterial == null) continue;
                    ceilingMaterial = floorRenderer.sharedMaterial;
                    break;
                }
            }
            if (ceilingMaterial == null)
            {
                UnderPressurePlugin.Log.LogWarning(
                    "No se pudo crear el falso techo: el suelo del hospital no tiene material válido.");
                return;
            }

            var ceilingObject = new GameObject("Under Pressure - Invisible Shadow Ceiling");
            ceilingObject.transform.SetParent(hospitalMap.RoomVisual.GameObject.transform, false);
            ceilingObject.layer = LayerMask.NameToLayer("Default");
            var maximumTileCount = indoorState.GetLength(0) * indoorState.GetLength(1);
            var vertices = new List<Vector3>(maximumTileCount * 4);
            var triangles = new List<int>(maximumTileCount * 6);
            var uvs = new List<Vector2>(maximumTileCount * 4);
            var anchor = hospitalMap.Anchor;
            var parent = ceilingObject.transform;
            for (var y = 0; y < indoorState.GetLength(1); ++y)
            {
                for (var x = 0; x < indoorState.GetLength(0); ++x)
                {
                    if (!indoorState[x, y]) continue;
                    var worldCenter = GridCoord.GridCoordToWorldPosition(anchor.X + x, anchor.Y + y);
                    worldCenter.y += LightingParameters.ShadowCeilingHeight;
                    var center = parent.InverseTransformPoint(worldCenter);
                    var first = vertices.Count;
                    vertices.Add(center + new Vector3(-1f, 0f, -1f));
                    vertices.Add(center + new Vector3(-1f, 0f, 1f));
                    vertices.Add(center + new Vector3(1f, 0f, 1f));
                    vertices.Add(center + new Vector3(1f, 0f, -1f));
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(0f, 1f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(1f, 0f));
                    triangles.Add(first);
                    triangles.Add(first + 1);
                    triangles.Add(first + 2);
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 3);
                }
            }

            var mesh = new Mesh
            {
                name = "Under Pressure - Hospital Shadow Ceiling Mesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ceilingObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = ceilingObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ceilingMaterial;
            renderer.shadowCastingMode = _shadowCeilingMode;
            renderer.receiveShadows = true;
            CeilingObjects[hospitalMap] = ceilingObject;
            UnderPressurePlugin.Log.LogInfo(
                $"Falso techo de sombras creado: {vertices.Count / 4} baldosas a {LightingParameters.ShadowCeilingHeight:0.00} m.");
        }

        private static void RemoveAllCeilings()
        {
            foreach (var pair in CeilingObjects)
            {
                if (pair.Value == null) continue;
                pair.Value.SetActive(false);
                var renderer = pair.Value.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;
                var filter = pair.Value.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (filter != null) filter.sharedMesh = null;
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                UnityEngine.Object.Destroy(pair.Value);
            }
            CeilingObjects.Clear();
        }

        private static void EnsureWindowLight(RoomItem item)
        {
            var root = item.Visual.GameObject;
            var existingOwner = root.GetComponent<PrototypeWindowLightOwner>();
            if (existingOwner != null && existingOwner.HasLight) return;
            var room = item.OwningRoom;
            var manager = item.Level?.VisualManager?.RoomLightingManager;
            if (room?.FloorPlan == null || manager == null) return;

            Vector3 lightPosition;
            Vector3 inwardDirection;
            if (!TryFindWindowLightPosition(item, room, out lightPosition, out inwardDirection))
            {
                UnderPressurePlugin.Log.LogWarning(
                    $"No se encontró una casilla interior válida para la ventana '{item}'.");
                return;
            }
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var windowCenter = item.WorldCenter;
            if (renderers != null && renderers.Length > 0)
            {
                var visualBounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; ++index)
                    visualBounds.Encapsulate(renderers[index].bounds);
                windowCenter = visualBounds.center;
            }
            lightPosition.y = windowCenter.y;
            var aimDirection = (inwardDirection + Vector3.down * LightingParameters.WindowSpotDownwardAim).normalized;
            var lightObject = new GameObject(WindowLightName);
            lightObject.transform.SetParent(room.FloorPlanVisual.GameObject.transform, true);
            lightObject.transform.position = windowCenter - inwardDirection * LightingParameters.WindowSpotExteriorOffset;
            lightObject.transform.rotation = Quaternion.LookRotation(aimDirection, Vector3.up);

            // Una sola fuente exterior. El cono casi hemisférico y sin núcleo duro
            // reparte la caída por toda su anchura para imitar luz ambiental diurna.
            var unityLight = lightObject.AddComponent<UnityEngine.Light>();
            unityLight.type = LightingParameters.WindowLightType;
            unityLight.color = LightingParameters.WindowSpotColor;
            unityLight.range = LightingParameters.WindowSpotRange;
            unityLight.intensity = LightingParameters.WindowSpotIntensity;
            unityLight.spotAngle = LightingParameters.WindowSpotAngle;
            unityLight.innerSpotAngle = LightingParameters.WindowSpotInnerAngle;
            unityLight.shadows = LightingParameters.WindowSpotShadows;
            unityLight.renderMode = LightingParameters.WindowSpotRenderMode;
            unityLight.shadowStrength = LightingParameters.WindowSpotShadowStrength;
            unityLight.shadowBias = LightingParameters.WindowSpotShadowBias;
            unityLight.shadowNormalBias = LightingParameters.WindowSpotShadowNormalBias;
            unityLight.shadowNearPlane = LightingParameters.WindowSpotShadowNearPlane;
            unityLight.bounceIntensity = LightingParameters.WindowSpotBounceIntensity;
            unityLight.cullingMask = LightingParameters.WindowSpotCullingMask;
            var owner = existingOwner ?? root.AddComponent<PrototypeWindowLightOwner>();
            owner.Initialise(manager, lightObject, null);
            if (!_windowAnchorLogged)
            {
                _windowAnchorLogged = true;
                UnderPressurePlugin.Log.LogInfo(
                    $"Anclaje real de ventana: visual='{root.name}', renderers={renderers?.Length ?? 0}, " +
                    $"interior detectado={lightPosition}, foco exterior={lightObject.transform.position}, " +
                    $"dirección={inwardDirection}, padre='{lightObject.transform.parent?.name}'.");
            }
            ++_windowLightsCreated;
        }

        private static bool TryFindWindowLightPosition(RoomItem item, Room room,
            out Vector3 lightPosition, out Vector3 inwardDirection)
        {
            var origin = item.WorldCenter;
            var worldState = item.Level?.WorldState;
            var towardCenter = room.Center - origin;
            towardCenter.y = 0f;
            var candidates = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            var distances = new[] { 0.75f, 1.25f, 1.75f, 2.25f, 2.75f };
            var bestScore = float.NegativeInfinity;
            var bestPosition = Vector3.zero;
            var bestDirection = Vector3.zero;

            if (worldState != null)
            {
                foreach (var candidate in candidates)
                {
                    foreach (var distance in distances)
                    {
                        var position = origin + candidate * distance;
                        var grid = GridCoordUtils.ToGridCoord(position);
                        var resolvedRoom = worldState.GetRoomAtWorldCoord(grid, true, true);
                        if (!ReferenceEquals(resolvedRoom, room)) continue;
                        var score = Vector3.Dot(candidate, towardCenter.normalized) - distance * 0.01f;
                        if (score <= bestScore) continue;
                        bestScore = score;
                        bestPosition = position;
                        bestDirection = candidate;
                    }
                }
            }

            if (bestScore > float.NegativeInfinity)
            {
                // WorldCenter ya contiene la altura real de la ventana.
                lightPosition = bestPosition;
                inwardDirection = bestDirection;
                return true;
            }

            lightPosition = Vector3.zero;
            inwardDirection = Vector3.zero;
            return false;
        }

        private static bool IsLightHeaded(Patient patient)
        {
            var illness = patient?.Illness;
            return illness != null &&
                   string.Equals(illness.Name.Term, LightHeadedIllnessTerm,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureLightHeadedLight(Patient patient)
        {
            if (!Enabled || !IsLightHeaded(patient)) return;
            var socket = patient.Visual?.HeadSocket;
            var manager = patient.Level?.VisualManager?.RoomLightingManager;
            if (socket == null || manager == null) return;
            if (FindDirectChild(socket, LightHeadedLightName) != null) return;

            var lightObject = new GameObject(LightHeadedLightName);
            lightObject.transform.SetParent(socket, false);
            lightObject.transform.localPosition = Vector3.up * LightingParameters.LightHeadedHeight;

            var light = lightObject.AddComponent<ClippableLight>();
            light.Type = (ClippableLight.LightType)0;
            light.Color = LightingParameters.LightHeadedColor;
            light.Range = LightingParameters.LightHeadedRange;
            light.Intensity = LightingParameters.LightHeadedIntensity;
            lightObject.AddComponent<PrototypeClippableLightOwner>().Initialise(manager, light);
            RegisterOnce(manager, light);
            ++_lightHeadedLightsCreated;
        }

        private static void RemoveLightHeadedLight(Patient patient)
        {
            var socket = patient?.Visual?.HeadSocket;
            var child = FindDirectChild(socket, LightHeadedLightName);
            if (child == null) return;
            child.GetComponent<PrototypeClippableLightOwner>()?.Unregister();
            child.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        private static void EnsureTestLampLight(RoomItem item)
        {
            var root = item.Visual.GameObject;
            if (FindDirectChild(root.transform, TestLampLightName) != null) return;
            var manager = item.Level?.VisualManager?.RoomLightingManager;
            if (manager == null) return;

            var lightObject = new GameObject(TestLampLightName);
            lightObject.transform.SetParent(root.transform, true);
            lightObject.transform.position = item.WorldCenter + Vector3.up * LightingParameters.GpLampHeight;

            var light = lightObject.AddComponent<ClippableLight>();
            light.Type = (ClippableLight.LightType)0;
            light.Color = LightingParameters.GpLampColor;
            light.Range = LightingParameters.GpLampRange;
            light.Intensity = LightingParameters.GpLampIntensity;
            RegisterOnce(manager, light);
            ++_testLampLightsCreated;
        }

        private static void RemoveVisualLights(RoomItem item)
        {
            var root = item?.Visual?.GameObject;
            if (root == null) return;
            RemoveWindowSpot(item);
            RemoveDirectChild(item, FindDirectChild(root.transform, TestLampLightName));
        }

        private static void RemoveWindowSpot(RoomItem item)
        {
            var root = item?.Visual?.GameObject;
            if (root == null) return;
            var owner = root.GetComponent<PrototypeWindowLightOwner>();
            if (owner != null) owner.RemoveLight();
            RemoveDirectChild(item, FindDirectChild(root.transform, WindowLightName));
        }

        private static void RemoveDirectChild(RoomItem item, Transform child)
        {
            if (child == null) return;
            var light = child.GetComponent<ClippableLight>();
            var manager = item.Level?.VisualManager?.RoomLightingManager;
            if (manager != null && IsRegistered(manager, light))
                manager.UnregisterClippableLight(light);
            UnityEngine.Object.Destroy(child.gameObject);
        }

        private static Vector3 FindInwardDirection(RoomItem item, Room room)
        {
            var origin = item.WorldCenter;
            var candidates = new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            var towardCenter = room.Center - origin;
            towardCenter.y = 0f;
            var best = Vector3.zero;
            var bestScore = float.NegativeInfinity;

            foreach (var candidate in candidates)
            {
                var nearInside = RoomAlgorithms.RoomContainsWorldPosition(
                    room.FloorPlan, origin + candidate * 0.7f, 0f);
                var farInside = RoomAlgorithms.RoomContainsWorldPosition(
                    room.FloorPlan, origin + candidate * 1.35f, 0f);
                if (!nearInside && !farInside) continue;
                var score = Vector3.Dot(candidate, towardCenter);
                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            if (best != Vector3.zero) return best;
            if (Mathf.Abs(towardCenter.x) > Mathf.Abs(towardCenter.z))
                return towardCenter.x >= 0f ? Vector3.right : Vector3.left;
            return towardCenter.z >= 0f ? Vector3.forward : Vector3.back;
        }

        private static bool IsVerifiedGpLamp(RoomItemDefinition definition)
        {
            if (definition == null) return false;
            if (string.Equals(definition.DebugTag, "gp_lamp", StringComparison.OrdinalIgnoreCase))
                return true;
            var prefab = definition.GetPrefab(0);
            return prefab != null &&
                   string.Equals(prefab.name, "A_Prop_GP_Lamp_V1", StringComparison.OrdinalIgnoreCase);
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null) return null;
            for (var index = 0; index < parent.childCount; ++index)
            {
                var child = parent.GetChild(index);
                if (child != null && child.name == childName) return child;
            }
            return null;
        }

        internal static bool IsRegistered(RoomLightingManager manager, ClippableLight light)
        {
            if (manager == null || light == null) return false;
            var field = light.Type == (ClippableLight.LightType)0 ? PointLightsField : SpotLightsField;
            var lights = field?.GetValue(manager) as List<ClippableLight>;
            return lights != null && lights.Contains(light);
        }

        internal static bool IsPrototypeLight(ClippableLight light) =>
            light != null && (light.name == WindowLightName || light.name == TestLampLightName ||
                              light.name == LightHeadedLightName);

        private static void RegisterOnce(RoomLightingManager manager, ClippableLight light)
        {
            if (!IsRegistered(manager, light)) manager.RegisterClippableLight(light);
        }

        private struct DirectionalLightState
        {
            internal Vector3 Direction;
            internal Quaternion Rotation;
            internal int CullingMask;
            internal LightShadows Shadows;
            internal float ShadowStrength;
        }

        private struct RoomLightingMaterialState
        {
            internal Material Material;
            internal float ApplyRoomLighting;
            internal bool ApplyRoomLightingOff;
        }
    }

    internal sealed class PrototypeWindowLightOwner : MonoBehaviour
    {
        private RoomLightingManager _manager;
        private GameObject _lightObject;
        private ClippableLight _clippableLight;

        internal bool HasLight => _lightObject != null;

        internal void Initialise(RoomLightingManager manager, GameObject lightObject,
            ClippableLight clippableLight)
        {
            _manager = manager;
            _lightObject = lightObject;
            _clippableLight = clippableLight;
        }

        internal void RemoveLight()
        {
            if (_manager != null && _clippableLight != null &&
                HospitalLightingPrototype.IsRegistered(_manager, _clippableLight))
                _manager.UnregisterClippableLight(_clippableLight);
            if (_lightObject != null) UnityEngine.Object.Destroy(_lightObject);
            _lightObject = null;
            _clippableLight = null;
        }

        private void OnDestroy() => RemoveLight();
    }

    internal sealed class PrototypeClippableLightOwner : MonoBehaviour
    {
        private RoomLightingManager _manager;
        private ClippableLight _light;

        internal void Initialise(RoomLightingManager manager, ClippableLight light)
        {
            _manager = manager;
            _light = light;
        }

        internal void Unregister()
        {
            if (_manager != null && _light != null &&
                HospitalLightingPrototype.IsRegistered(_manager, _light))
                _manager.UnregisterClippableLight(_light);
            _manager = null;
            _light = null;
        }

        private void OnDestroy() => Unregister();
    }

    [HarmonyPatch(typeof(Room), "Initialise")]
    internal static class DarkRoomInitialisePatch
    {
        private static void Postfix(Room __instance) => HospitalLightingPrototype.TrackRoom(__instance);
    }

    [HarmonyPatch(typeof(Room), "RestoreFromSave")]
    internal static class DarkRoomRestorePatch
    {
        private static void Postfix(Room __instance) => HospitalLightingPrototype.TrackRoom(__instance);
    }

    [HarmonyPatch(typeof(Room), "ReloadRoomLights")]
    internal static class DarkRoomReloadPatch
    {
        private static void Postfix(Room __instance) =>
            HospitalLightingPrototype.DarkenRoomMaterials(__instance);
    }

    [HarmonyPatch(typeof(RoomItemVisual), "UpdateFrom")]
    internal static class RoomItemVisualLightingPatch
    {
        private static void Postfix(RoomItem __0) => HospitalLightingPrototype.TrackItem(__0);
    }

    [HarmonyPatch(typeof(FloorPlan), "AddHospitalWindow")]
    internal static class HospitalWindowLightingPatch
    {
        private static void Postfix(RoomItem __0) => HospitalLightingPrototype.TrackItem(__0);
    }

    [HarmonyPatch(typeof(RoomItem), "RestoreFromSave")]
    internal static class RestoredWindowLightingPatch
    {
        private static void Postfix(RoomItem __instance) => HospitalLightingPrototype.TrackItem(__instance);
    }

    [HarmonyPatch(typeof(RoomLightingManager), "RegisterClippableLight")]
    internal static class ClippableLightDuplicateGuardPatch
    {
        private static bool Prefix(RoomLightingManager __instance, ClippableLight __0) =>
            !HospitalLightingPrototype.IsPrototypeLight(__0) ||
            !HospitalLightingPrototype.IsRegistered(__instance, __0);
    }

    [HarmonyPatch(typeof(RoomLightingManager), "RengerateAfterLoad")]
    internal static class LoadedHospitalLightingRefreshPatch
    {
        private static void Postfix() => HospitalLightingPrototype.RefreshTrackedVisualLights();
    }

    [HarmonyPatch(typeof(RoomLightingManager), "GetRoomLightCubeMap")]
    internal static class NeutralHospitalVolumeReflectionPatch
    {
        private static void Postfix(ref Cubemap __result)
        {
            if (HospitalLightingPrototype.Enabled)
                __result = HospitalLightingPrototype.NeutralReflectionCubemap;
        }
    }

    [HarmonyPatch(typeof(Room), "GetRoomReflectionCubeMap")]
    internal static class NeutralRoomReflectionPatch
    {
        private static void Postfix(ref Cubemap __result)
        {
            if (HospitalLightingPrototype.Enabled)
                __result = HospitalLightingPrototype.NeutralReflectionCubemap;
        }
    }

    [HarmonyPatch(typeof(RoomItemVisual), "UpdateRoomLighting")]
    internal static class NeutralRoomItemLightingPatch
    {
        private static void Prefix(ref Color __0, ref Color __2, ref Cubemap __5)
        {
            if (!HospitalLightingPrototype.Enabled) return;
            __5 = HospitalLightingPrototype.NeutralReflectionCubemap;
        }
    }

    [HarmonyPatch(typeof(Patient), "CreateVisuals")]
    internal static class LightHeadedPatientVisualPatch
    {
        private static void Postfix(Patient __instance) =>
            HospitalLightingPrototype.TrackPatient(__instance);
    }

    [HarmonyPatch(typeof(Patient), "RestoreFromSave")]
    internal static class RestoredLightHeadedPatientPatch
    {
        private static void Postfix(Patient __instance) =>
            HospitalLightingPrototype.TrackPatient(__instance);
    }

}
