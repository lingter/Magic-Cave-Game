using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MagicCave.ArtValidation.Editor
{
    public static class ArtValidationSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/ArtValidation10x10.unity";
        const string SpritePath = "Assets/ArtValidation/Sprites/";
        const string MaterialPath = "Assets/ArtValidation/SpriteUnlit.mat";
        const string CapturePath = "Captures/ArtValidation10x10";
        const string RebuildRequest = "Temp/ArtValidation10x10.rebuild.request";
        const int PreviewLayer = 30;
        static Material material;

        [InitializeOnLoadMethod]
        static void CreateOnFirstImport()
        {
            // Seed the new scene once. Afterwards it is an ordinary editable scene asset.
            if (!File.Exists(ScenePath) || File.Exists(RebuildRequest)) EditorApplication.delayCall += BuildWhenReady;
        }

        static void BuildWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { EditorApplication.delayCall += BuildWhenReady; return; }
            if (!File.Exists(ScenePath) || File.Exists(RebuildRequest))
            {
                Create();
                if (File.Exists(RebuildRequest)) File.Delete(RebuildRequest);
            }
        }

        [MenuItem("Magic Cave/Art Validation/Create or Rebuild 10x10 Scene")]
        public static void Create()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before rebuilding the validation scene.");
            var existing = SceneManager.GetSceneByPath(ScenePath);
            bool rebuildOpenScene = existing.IsValid() && existing.isLoaded;
            if (rebuildOpenScene && existing.isDirty) throw new InvalidOperationException("Save the validation scene before rebuilding it.");
            bool canOpen = true;
            for (int i = 0; i < SceneManager.sceneCount; i++) canOpen &= !SceneManager.GetSceneAt(i).isDirty;
            var previous = SceneManager.GetActiveScene();
            ConfigureSprites();
            bool useEmptyScene = SceneManager.sceneCount == 1 && previous.path == "" && !previous.isDirty && previous.rootCount == 0;
            bool closeAfterBuild = !rebuildOpenScene && !useEmptyScene;
            var scene = rebuildOpenScene ? existing : useEmptyScene ? previous :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            if (rebuildOpenScene)
                foreach (var oldRoot in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(oldRoot);
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) throw new InvalidOperationException("Sprite unlit shader is unavailable.");
                material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, MaterialPath); }
                var root = Node("Art Validation - Fixed 10x10");
                var room = root.AddComponent<ArtValidationRoom>();
                room.wallTiles = ArtValidationRoom.CreateLayout();
                room.whiteboxSprite = Sprite("Whitebox");
                room.artRoot = Node("Art - 16px Tiles", root.transform);
                room.whiteboxRoot = Node("Whitebox - Same Logical Cells", root.transform);
                var floors = Node("Floors - 100 Cells", room.artRoot.transform);
                var walls = Node("Walls - Fixed Art Selection", room.artRoot.transform);
                var collisions = Node("Wall Collision - 1 Unit Per Cell", root.transform);
                for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++)
                {
                    var artFloor = Visual($"Floor {x},{y}", floors.transform, x, y, Sprite("scene_floor_" + (1 + (x + 2 * y) % 3)), -1000);
                    // Rounded outside wall corners reveal the background, not white floor pixels.
                    artFloor.enabled = x > 0 && y > 0 && x < 9 && y < 9;
                    var floor = Visual($"Floor {x},{y}", room.whiteboxRoot.transform, x, y, room.whiteboxSprite, -1000);
                    floor.color = (x + y) % 2 == 0 ? new Color(0.64f, 0.67f, 0.71f) : new Color(0.59f, 0.62f, 0.66f);
                    int tile = room.WallAt(x, y);
                    if (tile == 0) continue;
                    Visual($"Wall {x},{y} - Art {tile}", walls.transform, x, y, Sprite("scene_wall_" + tile), ArtValidationRoom.SortOrder(y));
                    var whiteWall = Visual($"Wall {x},{y}", room.whiteboxRoot.transform, x, y, room.whiteboxSprite, ArtValidationRoom.SortOrder(y));
                    whiteWall.color = new Color(0.25f, 0.28f, 0.34f);
                    var collider = Node($"Wall Collider {x},{y}", collisions.transform);
                    collider.transform.position = new Vector3(x, y, 0);
                    collider.AddComponent<BoxCollider2D>().size = Vector2.one;
                }
                var objectRoot = Node("Objects - Fixed Positions", root.transform);
                room.props = new[]
                {
                    Prop(objectRoot.transform, "Merchant_Merchant", "商人", ValidationPropKind.Merchant, 1, 8, true),
                    Prop(objectRoot.transform, "Chest_TreasureChest", "宝箱", ValidationPropKind.Chest, 3, 8, true),
                    Prop(objectRoot.transform, "Chest_EquipmentChest", "装备箱", ValidationPropKind.Chest, 8, 8, true),
                    Prop(objectRoot.transform, "Chest_ConsumablesBag", "补给袋", ValidationPropKind.Bag, 5, 1, true),
                    Prop(objectRoot.transform, "Downstair", "楼梯出口", ValidationPropKind.Exit, 8, 1, false),
                    Prop(objectRoot.transform, "Trap_Spike", "尖刺", ValidationPropKind.Trap, 5, 6, false),
                    Prop(objectRoot.transform, "Enemy_2", "史莱姆", ValidationPropKind.Enemy, 2, 6, true),
                    Prop(objectRoot.transform, "Enemy_6", "守卫", ValidationPropKind.Enemy, 6, 3, true),
                    Prop(objectRoot.transform, "Enemy_9", "精英守卫", ValidationPropKind.Enemy, 8, 3, true)
                };
                room.playerDown = Sprite("Player_Down"); room.playerUp = Sprite("Player_Up"); room.playerSide = Sprite("Player_LeftAndRight");
                room.player = Visual("Player - WASD or Click", root.transform, 1, 1, room.playerDown, ArtValidationRoom.SortOrder(1) + 1);
                room.player.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * ArtValidationRoom.PlayerCollisionSize;
                room.gridRoot = CreateGrid(root.transform, room.whiteboxSprite);
                room.roomCamera = Node("Main Camera", root.transform).AddComponent<Camera>();
                room.roomCamera.tag = "MainCamera";
                room.roomCamera.transform.position = new Vector3(4.5f, 4.5f, -10);
                room.roomCamera.orthographic = true; room.roomCamera.orthographicSize = 6.25f;
                room.roomCamera.nearClipPlane = 0.1f; room.roomCamera.farClipPlane = 30;
                room.roomCamera.cullingMask = 1 << PreviewLayer;
                room.roomCamera.clearFlags = CameraClearFlags.SolidColor;
                room.roomCamera.backgroundColor = new Color(0.075f, 0.095f, 0.14f);
                room.roomCamera.allowHDR = false; room.roomCamera.allowMSAA = false;
                room.roomCamera.gameObject.AddComponent<AudioListener>();
                room.ResetRoom();
                Validate(room);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(CapturePath);
                Capture(room.roomCamera, CapturePath + "/art-preview.png");
                room.SetWhitebox(true); Capture(room.roomCamera, CapturePath + "/whitebox-preview.png");
                room.SetWhitebox(false); room.SetGrid(true); Capture(room.roomCamera, CapturePath + "/grid-preview.png");
                room.SetGrid(false);
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save " + ScenePath);
                File.WriteAllText(CapturePath + "/scene-validation.txt", "PASS: fixed 10x10 layout; 100 floors; enclosed border; connected walkable cells; all props on floor; one-unit point-filtered sprites; orthographic fixed camera; art and whitebox share collision data.\n");
                Debug.Log("[ArtValidation] Built and validated: " + ScenePath);
            }
            finally
            {
                if (closeAfterBuild) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            if (canOpen) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static void ConfigureSprites()
        {
            foreach (string path in Directory.GetFiles(SpritePath, "*.png"))
            {
                string normalized = path.Replace('\\', '/');
                AssetDatabase.ImportAsset(normalized);
                var importer = (TextureImporter)AssetImporter.GetAtPath(normalized);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16;
                importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteAlignment = (int)SpriteAlignment.Center;
                importer.SetTextureSettings(settings); importer.SaveAndReimport();
            }
        }

        static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath + name + ".png");
            if (sprite == null) throw new InvalidOperationException("Missing sprite: " + name);
            return sprite;
        }

        static GameObject Node(string name, Transform parent = null)
        {
            var node = new GameObject(name) { layer = PreviewLayer };
            if (parent != null) node.transform.SetParent(parent, false);
            return node;
        }

        static SpriteRenderer Visual(string name, Transform parent, int x, int y, Sprite sprite, int order)
        {
            var node = Node(name, parent); node.transform.position = new Vector3(x, y, 0);
            var renderer = node.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.sharedMaterial = material; renderer.sortingOrder = order;
            return renderer;
        }

        static ArtValidationProp Prop(Transform parent, string spriteName, string displayName, ValidationPropKind kind, int x, int y, bool solid)
        {
            int order = kind == ValidationPropKind.Trap || kind == ValidationPropKind.Exit ? -900 : ArtValidationRoom.SortOrder(y) + 1;
            var renderer = Visual(spriteName + $" {x},{y}", parent, x, y, Sprite(spriteName), order);
            var prop = renderer.gameObject.AddComponent<ArtValidationProp>();
            prop.kind = kind; prop.displayName = displayName; prop.blocksMovement = solid;
            prop.artSprite = renderer.sprite; prop.whiteboxSprite = Sprite("Whitebox");
            prop.whiteboxColor = kind == ValidationPropKind.Enemy ? new Color(0.84f, 0.32f, 0.34f) :
                kind == ValidationPropKind.Exit ? new Color(0.2f, 0.8f, 0.7f) :
                kind == ValidationPropKind.Merchant ? new Color(0.6f, 0.45f, 0.9f) : new Color(0.9f, 0.7f, 0.25f);
            var collider = renderer.gameObject.AddComponent<BoxCollider2D>();
            collider.size = solid ? Vector2.one : Vector2.one * 0.8f; collider.isTrigger = !solid;
            return prop;
        }

        static GameObject CreateGrid(Transform parent, Sprite square)
        {
            var root = Node("Grid Overlay - G", parent);
            for (int i = 0; i <= 10; i++)
            {
                var vertical = Visual("Column " + i, root.transform, 0, 0, square, 4000);
                vertical.transform.position = new Vector3(i - 0.5f, 4.5f, 0); vertical.transform.localScale = new Vector3(0.015f, 10, 1);
                vertical.color = new Color(0.15f, 0.6f, 0.85f, 0.5f);
                var horizontal = Visual("Row " + i, root.transform, 0, 0, square, 4000);
                horizontal.transform.position = new Vector3(4.5f, i - 0.5f, 0); horizontal.transform.localScale = new Vector3(10, 0.015f, 1);
                horizontal.color = vertical.color;
            }
            return root;
        }

        static void Validate(ArtValidationRoom room)
        {
            if (room.wallTiles.Length != 100 || room.artRoot.transform.Find("Floors - 100 Cells").childCount != 100)
                throw new InvalidOperationException("The map must contain exactly 100 cells.");
            var cells = new HashSet<Vector2Int>();
            for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++)
            {
                if ((x == 0 || y == 0 || x == 9 || y == 9) && room.WallAt(x, y) == 0)
                    throw new InvalidOperationException("Open border at " + x + "," + y);
                if (room.CanStand(new Vector2(x, y))) cells.Add(new Vector2Int(x, y));
            }
            foreach (var prop in room.props)
                if (room.WallAt(Mathf.RoundToInt(prop.transform.position.x), Mathf.RoundToInt(prop.transform.position.y)) != 0)
                    throw new InvalidOperationException("Prop overlaps a wall: " + prop.name);
            var visited = new HashSet<Vector2Int>(); var queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(1, 1)); visited.Add(new Vector2Int(1, 1));
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var next = current + direction;
                    if (cells.Contains(next) && visited.Add(next)) queue.Enqueue(next);
                }
            }
            if (visited.Count != cells.Count) throw new InvalidOperationException("Disconnected walkable cells.");
            foreach (var renderer in room.GetComponentsInChildren<SpriteRenderer>(true))
                if (renderer.sprite == null || renderer.sprite.texture.filterMode != FilterMode.Point ||
                    Mathf.Abs(renderer.sprite.bounds.size.x - 1) > 0.001f || Mathf.Abs(renderer.sprite.bounds.size.y - 1) > 0.001f)
                    throw new InvalidOperationException("Sprite size/filter mismatch: " + renderer.name);
        }

        static void Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1000, 1000, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                var image = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
    }
}
