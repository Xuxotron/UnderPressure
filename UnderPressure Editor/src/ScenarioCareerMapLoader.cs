// Updated: 2026-10-12
// Aplica mapas .upmap al comenzar una PARTIDA NUEVA sin modificar las definiciones originales.
// Protege las partidas normales: el juego no guarda progreso mientras se usa un mapa personalizado.
// Destino: UnderPressure Editor/src/ScenarioCareerMapLoader.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FullInspector;
using HarmonyLib;
using TH20;
using UnityEngine;

namespace UnderPressure.Editor
{
    internal static class ScenarioCareerMapLoader
    {
        private sealed class PlotData
        {
            internal int Width;
            internal int Height;
            internal Color32[] Pixels;
            internal readonly List<ItemData> Items = new List<ItemData>();
        }
        private sealed class ItemData
        {
            internal int Layer;
            internal Guid Guid;
            internal string Tag;
            internal Vector3 Position;
            internal float Rotation;
        }
        private static readonly CultureInfo Invariant=CultureInfo.InvariantCulture;
        private static readonly MethodInfo CloneMethod=typeof(object).GetMethod("MemberwiseClone",
            BindingFlags.NonPublic|BindingFlags.Instance);
        private static readonly FieldInfo BaseField=AccessTools.Field(typeof(HospitalPlotDefinition),"Items");
        private static readonly FieldInfo BuiltField=AccessTools.Field(typeof(HospitalPlotDefinition),"BuiltItems");
        private static readonly FieldInfo UnbuiltField=AccessTools.Field(typeof(HospitalPlotDefinition),"UnbuiltItems");
        private static readonly Dictionary<int,PlotData> Plots=new Dictionary<int,PlotData>();
        private static readonly List<Texture2D> TemporaryTextures=new List<Texture2D>();
        private static readonly List<Material> TemporaryMaterials=new List<Material>();
        private static readonly Dictionary<Renderer, Material[]> OriginalRendererMaterials =
            new Dictionary<Renderer, Material[]>();
        private static LevelConfig _base;
        private static string _path;
        private static bool _active;
        private static int _nextPlot;
        private static Dictionary<Guid,SharedInstance<RoomItemDefinition>> _defs;
        private static Dictionary<string,SharedInstance<RoomItemDefinition>> _tags;

        internal static bool IsCustomSession => _active || _path != null;

        internal static void Prepare(LevelConfig level, string path)
        {
            if (level==null || string.IsNullOrEmpty(path)) throw new ArgumentException("Mapa incorrecto.");
            var full=Path.GetFullPath(path);
            var folder=Path.GetFullPath(ScenarioEditorStorage.Folder)
                .TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(folder,StringComparison.OrdinalIgnoreCase) ||
                !full.EndsWith(".upmap",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("El mapa no esta en la carpeta maps.");
            var lines=File.ReadAllLines(full,Encoding.UTF8);
            if(lines.Length<4 || lines[0]!="UNDERPRESSURE_MAP_1" || lines[lines.Length-1]!="END")
                throw new InvalidDataException("Formato .upmap incorrecto.");
            if(new FileInfo(full).Length>128L*1024*1024)throw new InvalidDataException("Mapa demasiado grande.");
            var meta=lines[1].Split('|');
            if(meta.Length!=4 || meta[0]!="M" ||
               Encoding.UTF8.GetString(Convert.FromBase64String(meta[1]))!=level.UniqueId)
                throw new InvalidDataException("Este mapa corresponde a otro hospital.");
            var read=new Dictionary<int,PlotData>();
            for(int n=2;n<lines.Length-1;n++)
            {
                var parts=lines[n].Split('|');
                if(parts[0]=="P" && parts.Length==5)
                {
                    var index=int.Parse(parts[1],Invariant);
                    var w=int.Parse(parts[2],Invariant);
                    var h=int.Parse(parts[3],Invariant);
                    if(w<1 || h<1 || (long)w*h>16000000L)
                        throw new InvalidDataException("Dimensiones de suelo invalidas.");
                    var bytes=Convert.FromBase64String(parts[4]);
                    if(bytes.Length!=(long)w*h*4L)
                        throw new InvalidDataException("Pixeles incompletos.");
                    var pixels=new Color32[w*h];
                    for(var i=0;i<pixels.Length;i++)
                        pixels[i]=new Color32(bytes[4*i],bytes[4*i+1],bytes[4*i+2],bytes[4*i+3]);
                    read.Add(index,new PlotData{Width=w,Height=h,Pixels=pixels});
                }
                else if(parts[0]=="I" && parts.Length==9)
                {
                    PlotData plot;
                    if(!read.TryGetValue(int.Parse(parts[1],Invariant),out plot))
                        throw new InvalidDataException("Objeto sin parcela.");
                    var layer=int.Parse(parts[2],Invariant);
                    if(layer<0 || layer>2)throw new InvalidDataException("Capa incorrecta.");
                    plot.Items.Add(new ItemData{
                        Layer=layer,Guid=Guid.Parse(parts[3]),
                        Tag=Encoding.UTF8.GetString(Convert.FromBase64String(parts[4])),
                        Position=new Vector3(float.Parse(parts[5],Invariant),float.Parse(parts[6],Invariant),
                            float.Parse(parts[7],Invariant)),Rotation=float.Parse(parts[8],Invariant)});
                }
                else throw new InvalidDataException("Registro desconocido.");
            }
            if(read.Count<1)throw new InvalidDataException("El mapa esta vacio.");
            // Only after full validation do we enable the new-game flow.
            Cancel();
            foreach(var pair in read)Plots.Add(pair.Key,pair.Value);
            _base=level;
            _path=full;
            _active=false;
            UnderPressureEditorPlugin.Log?.LogInfo("Carrera: escenario personalizado seleccionado: "+_path);
        }

        internal static void Cancel()
        {
            foreach(var pair in OriginalRendererMaterials)
                if(pair.Key!=null)pair.Key.sharedMaterials=pair.Value;
            OriginalRendererMaterials.Clear();
            foreach(var material in TemporaryMaterials)
                if(material!=null)UnityEngine.Object.Destroy(material);
            TemporaryMaterials.Clear();
            foreach(var texture in TemporaryTextures)
                if(texture!=null)UnityEngine.Object.Destroy(texture);
            TemporaryTextures.Clear();
            Plots.Clear();
            _defs=null;_tags=null;_base=null;_path=null;_nextPlot=0;_active=false;
        }

        internal static bool MatchesConfig(LevelConfig config) =>
            config!=null && _base!=null &&
            string.Equals(config.UniqueId,_base.UniqueId,StringComparison.Ordinal);

        internal static bool Match(Level level)
        {
            return level!=null && _base!=null && level.Config!=null &&
                string.Equals(level.Config.UniqueId,_base.UniqueId,StringComparison.Ordinal);
        }

        internal static void StartBuilding(Level level,Metagame metagame)
        {
            if(!Match(level) || string.IsNullOrEmpty(_path)) return;
            _nextPlot=0;
            _active=true;
            _defs=new Dictionary<Guid,SharedInstance<RoomItemDefinition>>();
            _tags=new Dictionary<string,SharedInstance<RoomItemDefinition>>(StringComparer.Ordinal);
            // Direct assets; never remove anything from the game's catalog.
            if(metagame?.LandscapeItemDatabase?.Instance?.RoomItems!=null)
                foreach(var item in metagame.LandscapeItemDatabase.Instance.RoomItems)
                    AddDefinition(item);
            if(metagame?.RoomItemDatabase?.Instance?.RoomItems!=null)
                foreach(var item in metagame.RoomItemDatabase.Instance.RoomItems)
                    AddDefinition(item);
            UnderPressureEditorPlugin.Log?.LogInfo("Carrera: cargando mapa en la construccion inicial del nivel.");
        }

        private static void AddDefinition(SharedInstance<RoomItemDefinition> wrapper)
        {
            var item=wrapper?.Instance;
            if(item==null)return;
            if(item.GUID!=Guid.Empty && !_defs.ContainsKey(item.GUID))_defs.Add(item.GUID,wrapper);
            if(!string.IsNullOrEmpty(item.DebugTag)&&!_tags.ContainsKey(item.DebugTag))
                _tags.Add(item.DebugTag,wrapper);
        }

        internal static void ReplacePlot(ref HospitalPlotDefinition definition,Level level)
        {
            if(!_active || !Match(level) || definition==null)return;
            var index=_nextPlot++;
            PlotData data;
            if(!Plots.TryGetValue(index,out data))return;
            if(definition.FloorImage==null || definition.FloorImage.width!=data.Width ||
                definition.FloorImage.height!=data.Height)
            {
                UnderPressureEditorPlugin.Log?.LogWarning("Parcela "+index+": tamano distinto; sin cambios.");
                return;
            }
            try
            {
                var cloned=(HospitalPlotDefinition)CloneMethod.Invoke(definition,null);
                var image=new Texture2D(data.Width,data.Height,TextureFormat.RGBA32,false)
                {
                    name="UP_PLAY_"+index,hideFlags=HideFlags.DontSave,
                    filterMode=definition.FloorImage.filterMode,wrapMode=definition.FloorImage.wrapMode
                };
                image.SetPixels32(data.Pixels);
                image.Apply(false,false);
                TemporaryTextures.Add(image);
                cloned.FloorImage=image;
                var layers=new []{new List<HospitalPlotItem>(),new List<HospitalPlotItem>(),new List<HospitalPlotItem>()};
                foreach(var item in data.Items)
                {
                    SharedInstance<RoomItemDefinition> resolved;
                    if(!_defs.TryGetValue(item.Guid,out resolved) &&
                       !_tags.TryGetValue(item.Tag??"",out resolved))
                    {
                        UnderPressureEditorPlugin.Log?.LogWarning("Objeto del mapa no reconocido: "+item.Tag);
                        continue;
                    }
                    layers[item.Layer].Add(new HospitalPlotItem{
                        Definition=resolved, Position=item.Position,Rotation=item.Rotation});
                }
                BaseField.SetValue(cloned,layers[0]);
                BuiltField.SetValue(cloned,layers[1]);
                UnbuiltField.SetValue(cloned,layers[2]);
                // Replace only the constructor argument: shared game definitions remain intact.
                definition=cloned;
            }
            catch(Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudo preparar la parcela "+index+": "+ex);
            }
        }

        internal static void LoadTerrain(Level level)
        {
            if(!_active || !Match(level) || _path==null)return;
            try
            {
                var hospital=SafeName(level.Config.UniqueId);
                var mapName=SafeName(Path.GetFileNameWithoutExtension(_path));
                var folder=Path.Combine(ScenarioEditorStorage.Folder,hospital,mapName);
                if(!Directory.Exists(folder))return;
                var roots=UnityEngine.Object.FindObjectsOfType<Transform>();
                var root=roots.FirstOrDefault(t=>t!=null && t.name.StartsWith("Terrain_",StringComparison.OrdinalIgnoreCase));
                if(root==null)return;
                var renderers=root.GetComponentsInChildren<Renderer>(true)
                    .Where(r=>r!=null && r.gameObject.name.StartsWith("Pass",StringComparison.OrdinalIgnoreCase)).ToList();
                for(var i=0;i<renderers.Count;i++)
                {
                    var path=Path.Combine(folder,"alpha_"+i.ToString(Invariant)+".png");
                    if(!File.Exists(path))continue;
                    var renderer=renderers[i];
                    var originals=renderer.sharedMaterials;
                    if(originals==null || originals.Length==0 || originals[0]==null ||
                       !originals[0].HasProperty("_Control"))continue;
                    if(!OriginalRendererMaterials.ContainsKey(renderer))
                        OriginalRendererMaterials.Add(renderer,originals);
                    var texture=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
                    if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        continue;
                    }
                    TemporaryTextures.Add(texture);
                    var material=new Material(originals[0]);
                    TemporaryMaterials.Add(material);
                    material.SetTexture("_Control",texture);
                    var assigned=(Material[])originals.Clone();
                    assigned[0]=material;
                    renderer.sharedMaterials=assigned;
                }
            }
            catch(Exception ex)
            {
                UnderPressureEditorPlugin.Log?.LogError("No se pudieron aplicar las texturas del mapa: "+ex);
            }
        }

        private static string SafeName(string value)
        {
            foreach(var invalid in Path.GetInvalidFileNameChars())value=value.Replace(invalid,'_');
            return value.Trim(' ','.').Replace(' ','_');
        }
    }

    // A custom map must start from original assets, never from an existing career save.
    [HarmonyPatch(typeof(App), "LoadLevel",new []{
        typeof(LevelConfig),typeof(SaveFileHeader),typeof(bool),typeof(bool)})]
    internal static class ScenarioCareerMapIgnoreSavePatch
    {
        private static void Prefix(LevelConfig __0,ref SaveFileHeader __1,ref bool __2)
        {
            if(!ScenarioCareerMapLoader.IsCustomSession || ScenarioEditorFlow.IsRunning)return;
            if(!ScenarioCareerMapLoader.MatchesConfig(__0))
            {
                ScenarioCareerMapLoader.Cancel();
                return;
            }
            __1=null;
            __2=true;
        }
    }

    [HarmonyPatch(typeof(WorldState), MethodType.Constructor,
        new []{typeof(WorldState.Config),typeof(Level),typeof(Metagame),
            typeof(VisualManager),typeof(Material),typeof(RoomItemVisualEdit.Config)})]
    internal static class ScenarioCareerMapWorldBeginPatch
    {
        private static void Prefix(Level __1,Metagame __2)
        {
            if(ScenarioCareerMapLoader.IsCustomSession && !ScenarioEditorFlow.IsRunning)
                ScenarioCareerMapLoader.StartBuilding(__1,__2);
        }
    }

    [HarmonyPatch(typeof(HospitalPlot), MethodType.Constructor,
        new []{typeof(HospitalPlotDefinition),typeof(WorldState),typeof(Level)})]
    internal static class ScenarioCareerMapPlotPatch
    {
        private static void Prefix(ref HospitalPlotDefinition __0,Level __2)
        {
            ScenarioCareerMapLoader.ReplacePlot(ref __0,__2);
        }
    }

    [HarmonyPatch(typeof(Metagame), "OnLevelLoaded")]
    internal static class ScenarioCareerMapTerrainLoadedPatch
    {
        private static void Postfix(Level __0) => ScenarioCareerMapLoader.LoadTerrain(__0);
    }

    [HarmonyPatch(typeof(GameMode), "AllowGameToBeSaved")]
    internal static class ScenarioCareerMapNoVanillaSavePatch
    {
        private static void Postfix(ref bool __result)
        {
            if(ScenarioCareerMapLoader.IsCustomSession)__result=false;
        }
    }
    [HarmonyPatch(typeof(Level), "HandleAutosave")]
    internal static class ScenarioCareerMapNoAutosavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(App), "AutoSaveDeferred")]
    internal static class ScenarioCareerMapNoDeferredSavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(App), "QuickSaveInstantly")]
    internal static class ScenarioCareerMapNoQuickSavePatch
    {
        private static bool Prefix() => !ScenarioCareerMapLoader.IsCustomSession;
    }
    [HarmonyPatch(typeof(OpeningScreen), "Show")]
    internal static class ScenarioCareerMapResetPatch
    {
        private static void Postfix() => ScenarioCareerMapLoader.Cancel();
    }
}
