using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildBatteryAsset
{
    private const string SpritePath = "Assets/UI/bateria.png";
    private const string BundleName = "underpressure";
    private const string BuildMarker = "BUILD_BATTERY_ASSET";

    [InitializeOnLoadMethod]
    public static void BuildWhenRequested()
    {
        var marker = Path.GetFullPath(Path.Combine(Application.dataPath, "../" + BuildMarker));
        if (!File.Exists(marker))
            return;

        EditorApplication.delayCall += () =>
        {
            var exitCode = 0;
            try
            {
                Build();
                File.Delete(marker);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }
            finally
            {
                EditorApplication.Exit(exitCode);
            }
        };
    }

    public static void Build()
    {
        var importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("No se pudo importar " + SpritePath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.spritePixelsPerUnit = 100f;
        importer.SaveAndReimport();

        importer.assetBundleName = BundleName;
        importer.assetBundleVariant = string.Empty;
        importer.SaveAndReimport();

        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../BuildCombined"));
        Directory.CreateDirectory(output);

        var manifest = BuildPipeline.BuildAssetBundles(
            output,
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null || !File.Exists(Path.Combine(output, BundleName)))
            throw new InvalidOperationException("Unity no produjo el AssetBundle " + BundleName);

        Debug.Log("UNDERPRESSURE_BATTERY_ASSET_OK=" + Path.Combine(output, BundleName));
    }
}
