using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MagicCave.Prototype.Editor
{
    public static class PrototypeSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MagicCavePrototype.unity";
        const string ArtPath = "Assets/MagicCavePrototype/Resources/PrototypeArt.asset";
        const string SpritePath = "Assets/ArtValidation/Sprites/";
        const string UpgradeRequest = "Temp/PrototypePixel.upgrade.request";
        const string BuildRequest = "Temp/PrototypePixel.build.request";

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            if (!File.Exists(ArtPath) || File.Exists(UpgradeRequest) || File.Exists(BuildRequest)) EditorApplication.update += ProcessRequests;
        }

        static void ProcessRequests()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(ArtPath) || File.Exists(UpgradeRequest))
            {
                EditorApplication.update -= ProcessRequests;
                Upgrade();
                if (File.Exists(UpgradeRequest)) File.Delete(UpgradeRequest);
                EditorApplication.update += ProcessRequests;
                return;
            }
            if (File.Exists(BuildRequest) && !PrototypeVerification.IsRunning && !File.Exists(PrototypeVerification.RequestPath))
            {
                EditorApplication.update -= ProcessRequests;
                File.Delete(BuildRequest);
                BuildWindows();
                return;
            }
            if (!File.Exists(BuildRequest)) EditorApplication.update -= ProcessRequests;
        }

        public static PrototypeArtSet EnsureArtAssets()
        {
            Directory.CreateDirectory("Assets/MagicCavePrototype/Resources");
            AssetDatabase.Refresh();
            foreach (string name in new[] { "scene_wall_2", "scene_wall_6", "scene_wall_11" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath + name + ".png");
                if (importer == null) throw new InvalidOperationException("请先导出项目的Aseprite素材。");
                if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
            }
            var art = AssetDatabase.LoadAssetAtPath<PrototypeArtSet>(ArtPath);
            if (art == null) { art = ScriptableObject.CreateInstance<PrototypeArtSet>(); AssetDatabase.CreateAsset(art, ArtPath); }
            art.floors = new Sprite[3]; art.walls = new Sprite[14];
            for (int i = 0; i < 3; i++) art.floors[i] = Sprite("scene_floor_" + (i + 1));
            for (int i = 0; i < 14; i++) art.walls[i] = Sprite("scene_wall_" + (i + 1));
            art.playerDown = Sprite("Player_Down"); art.playerUp = Sprite("Player_Up"); art.playerSide = Sprite("Player_LeftAndRight");
            art.square = Sprite("Whitebox"); art.stairs = Sprite("Downstair");
            art.enemies = new[] { Sprite("Enemy_2"), Sprite("Enemy_6"), Sprite("Enemy_9") };
            art.loot = new[] { Sprite("Chest_EquipmentChest"), Sprite("Chest_EquipmentChest"), Sprite("Chest_TreasureChest"), Sprite("Chest_ConsumablesBag") };
            art.spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ArtValidation/SpriteUnlit.mat");
            if (art.spriteMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) throw new InvalidOperationException("Sprite shader unavailable.");
                art.spriteMaterial = new Material(shader);
                AssetDatabase.CreateAsset(art.spriteMaterial, "Assets/MagicCavePrototype/Resources/PixelSprite.mat");
            }
            art.Validate(); EditorUtility.SetDirty(art); AssetDatabase.SaveAssets();
            return art;
        }

        static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath + name + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing sprite: " + name);
            return sprite;
        }

        [MenuItem("Magic Cave/Create Playable Prototype Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before rebuilding the prototype.");
            if (File.Exists(ScenePath)) { Upgrade(); return; }
            var art = EnsureArtAssets();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
            PrototypeGame.ConfigurePixelCamera(camera); camera.transform.position = Vector3.back * 10;
            camera.gameObject.AddComponent<AudioListener>();
            new GameObject("Magic Cave Prototype").AddComponent<PrototypeGame>().artSet = art;
            EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        }

        [MenuItem("Magic Cave/Upgrade Prototype to Pixel Art")]
        public static void Upgrade()
        {
            var art = EnsureArtAssets();
            if (!File.Exists(ScenePath)) { Create(); return; }
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (alreadyOpen && scene.isDirty) throw new InvalidOperationException("Save the prototype scene before upgrading its visual settings.");
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                PrototypeGame game = null; Camera camera = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.GetComponent<PrototypeGame>() != null) game = root.GetComponent<PrototypeGame>();
                    if (root.GetComponent<Camera>() != null) camera = root.GetComponent<Camera>();
                    if (root.name == "Cave Key Light") UnityEngine.Object.DestroyImmediate(root);
                }
                if (game == null || camera == null) throw new InvalidOperationException("Prototype scene requires its game controller and camera.");
                game.artSet = art; EditorUtility.SetDirty(game);
                PrototypeGame.ConfigurePixelCamera(camera); camera.transform.position = Vector3.back * 10;
                EditorUtility.SetDirty(camera); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the pixel prototype scene.");
                AssetDatabase.SaveAssets();
                Debug.Log("[MagicCave] Pixel art prototype configured: " + ScenePath);
            }
            finally
            {
                if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        [MenuItem("Magic Cave/Build Windows Pixel Prototype")]
        public static void BuildWindows()
        {
            Directory.CreateDirectory("Builds/WindowsPixel");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/WindowsPixel/MagicCaveGame.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            Directory.CreateDirectory("Captures/MagicCavePrototype/PixelArt");
            File.WriteAllText("Captures/MagicCavePrototype/PixelArt/build-summary.txt",
                $"{report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings; {report.summary.totalSize} bytes.\n");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Pixel prototype build failed; inspect the Editor log.");
            Debug.Log("[MagicCave] Windows pixel prototype ready: Builds/WindowsPixel/MagicCaveGame.exe");
        }
    }
}
