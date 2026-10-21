using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicCave.Prototype.Editor
{
    public static class WorldMapSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/WorldMap.unity";
        [MenuItem("Magic Cave/World Map/Create or Update Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出 Play 模式。");
            foreach (string path in Directory.GetFiles("Assets/MagicCavePrototype/Resources/WorldMapArt", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = 16; importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }
            var art = PrototypeSceneBuilder.EnsureArtAssets();
            if (File.Exists(ScenePath)) { Debug.Log("[WorldMap] Updated town art; existing scene preserved."); return; }
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            PrototypeGame.ConfigurePixelCamera(camera); camera.transform.position = Vector3.back * 10;
            camera.gameObject.AddComponent<AudioListener>();
            var light = new GameObject("Town Light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1;
            var controller = new GameObject("World Map and Cave Controller").AddComponent<PrototypeGame>(); controller.artSet = art;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            AssetDatabase.SaveAssets();
            Debug.Log("[WorldMap] Ready: " + ScenePath);
        }
    }
}
