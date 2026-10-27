using System.Collections.Generic;
using UnityEngine;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        sealed class TileVisual
        {
            public int x, y;
            public SpriteRenderer floor, wall, fog;
            public BoxCollider2D wallCollider;
            public GameObject feature;
        }
        sealed class ObjectVisual { public GameObject root; public int x, y; }

        public PrototypeArtSet artSet;
        GameObject worldRoot;
        Transform actor, hoverMarker;
        SpriteRenderer actorVisual;
        Vector3 cameraVelocity, cameraFocus;
        Vector2 facing = Vector2.down;
        readonly List<TileVisual> tiles = new List<TileVisual>();
        readonly List<ObjectVisual> objects = new List<ObjectVisual>();
        PixelWallTiles wallTiles;
        Texture2D markerTexture;
        Sprite markerSprite;
        static readonly Color FogColor = new Color(0.018f, 0.025f, 0.04f);
        static readonly Color DoorHintColor = new Color(1, 0.77f, 0.25f);

        public static int PixelScale => Mathf.Max(1, Mathf.FloorToInt(Screen.height / 180f));
        public static int SpriteOrder(float y) => 10000 - Mathf.RoundToInt(y * 100);
        public static void ConfigurePixelCamera(Camera camera)
        {
            camera.orthographic = true;
            camera.orthographicSize = Screen.height / (32f * PixelScale);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 40;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = FogColor;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.transform.rotation = Quaternion.identity;
        }

        Vector3 World(float x, float y, float depth = 0)
        {
            float half = (Run.Map.size - 1) * 0.5f;
            return new Vector3(x - half, y - half, depth);
        }

        static Vector3 PixelPosition(Vector3 position)
        {
            float pixels = 16 * PixelScale;
            return new Vector3(Mathf.Round(position.x * pixels) / pixels, Mathf.Round(position.y * pixels) / pixels, position.z);
        }

        SpriteRenderer Pixel(string name, Transform parent, Sprite sprite, Vector3 position, int order, Color? tint = null)
        {
            var node = new GameObject(name); node.transform.SetParent(parent, false); node.transform.localPosition = position;
            var renderer = node.AddComponent<SpriteRenderer>(); renderer.sprite = sprite;
            renderer.sharedMaterial = artSet.spriteMaterial; renderer.sortingOrder = order;
            renderer.color = tint ?? Color.white;
            return renderer;
        }

        void TargetTag(GameObject piece, TargetKind kind, int x, int y, int id = 0, bool trigger = false)
        {
            var tag = piece.AddComponent<PrototypeTarget>(); tag.kind = kind; tag.x = x; tag.y = y; tag.id = id;
            var collider = piece.AddComponent<BoxCollider2D>(); collider.size = Vector2.one; collider.isTrigger = trigger;
        }

        void WorldLabel(Transform parent, string text, float height)
        {
            var label = new GameObject("Label"); label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0, height, 0);
            var plate = Pixel("Label background", label.transform, artSet.square, Vector3.zero, 30000, new Color(0.035f, 0.05f, 0.08f, 0.92f));
            plate.transform.localScale = new Vector3(Mathf.Max(0.8f, text.Length * 0.24f), 0.32f, 1);
            var mesh = label.AddComponent<TextMesh>(); mesh.font = interfaceFont;
            mesh.fontSize = 28; mesh.characterSize = 0.085f; mesh.anchor = TextAnchor.MiddleCenter;
            mesh.text = text; mesh.color = Color.white;
            var renderer = label.GetComponent<MeshRenderer>(); renderer.sharedMaterial = interfaceFont.material; renderer.sortingOrder = 30001;
        }

        void EnsureArt()
        {
            if (artSet == null) artSet = Resources.Load<PrototypeArtSet>("PrototypeArt");
            if (artSet == null) throw new System.InvalidOperationException("缺少原型素材配置，请使用 Magic Cave → Create Playable Prototype Scene。");
            artSet.Validate();
            if (wallTiles == null) wallTiles = new PixelWallTiles(artSet);
            if (markerSprite == null)
            {
                markerTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "Selection frame", filterMode = FilterMode.Point };
                var pixels = new Color32[256];
                for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                    if (x == 0 || x == 15 || y == 0 || y == 15) pixels[y * 16 + x] = new Color32(255, 190, 65, 255);
                markerTexture.SetPixels32(pixels); markerTexture.Apply(false, true);
                markerSprite = Sprite.Create(markerTexture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 16);
            }
        }

        partial void BuildWorld(bool snap)
        {
            EnsureArt(); ConfigurePixelCamera(gameCamera);
            if (worldRoot != null) { worldRoot.SetActive(false); Destroy(worldRoot); }
            worldRoot = new GameObject("Magic Cave · Pixel Runtime Map"); worldRoot.transform.SetParent(transform, false);
            tiles.Clear(); objects.Clear();
            for (int y = 0; y < Run.Map.size; y++) for (int x = 0; x < Run.Map.size; x++)
            {
                Tile tile = Run.Map.At(x, y);
                var floor = Pixel("Cell " + x + "," + y, worldRoot.transform, artSet.floors[(x * 17 + y * 31) % 3], World(x, y), -1000);
                if (tile == Tile.Ground) TargetTag(floor.gameObject, TargetKind.Ground, x, y, trigger: true);
                var visual = new TileVisual { x = x, y = y, floor = floor };
                if (tile == Tile.Wall)
                {
                    var wall = Pixel("Wall", worldRoot.transform, wallTiles.Get(Run.Map, x, y), World(x, y), SpriteOrder(y));
                    TargetTag(wall.gameObject, TargetKind.Wall, x, y); visual.wall = wall; visual.feature = wall.gameObject;
                    visual.wallCollider = wall.GetComponent<BoxCollider2D>();
                }
                else if (tile == Tile.Advance || tile == Tile.Return)
                {
                    var door = Pixel(tile == Tile.Advance ? "前进门" : "返回门", worldRoot.transform, artSet.stairs,
                        World(x, y), -500, tile == Tile.Return ? DoorHintColor : new Color(0.55f, 1, 0.85f));
                    TargetTag(door.gameObject, TargetKind.Door, x, y);
                    WorldLabel(door.transform, door.name, 0.65f); visual.feature = door.gameObject;
                }
                visual.fog = Pixel("Fog " + x + "," + y, worldRoot.transform, artSet.square, World(x, y), 50000, FogColor);
                tiles.Add(visual);
            }
            foreach (var enemy in Run.Map.enemies)
            {
                if (enemy.defeated) continue;
                var visual = Pixel(Rules.EnemyLabel(enemy), worldRoot.transform, artSet.enemies[(int)enemy.kind], World(enemy.x, enemy.y), SpriteOrder(enemy.y) + 1);
                TargetTag(visual.gameObject, TargetKind.Enemy, enemy.x, enemy.y, enemy.id);
                WorldLabel(visual.transform, visual.name, 0.65f);
                objects.Add(new ObjectVisual { root = visual.gameObject, x = enemy.x, y = enemy.y });
            }
            foreach (var loot in Run.Map.loot)
            {
                bool empty = loot.units.Count == 0;
                if (empty && !loot.IsContainer) continue;
                ItemKind kind = empty ? loot.appearanceKind : loot.units[0].kind;
                var tint = kind == ItemKind.Crystal ? new Color(0.5f, 0.85f, 1) : Color.white;
                if (empty) tint.a = 0.25f;
                var visual = Pixel("Item " + loot.id, worldRoot.transform, artSet.loot[Rules.ArtIndex(kind)], World(loot.x, loot.y),
                    empty || loot.loose ? -400 : SpriteOrder(loot.y) + 1, tint);
                if (empty)
                {
                    // An opened container is scenery: no interaction target, collider or item label.
                    objects.Add(new ObjectVisual { root = visual.gameObject, x = loot.x, y = loot.y });
                    continue;
                }
                TargetTag(visual.gameObject, TargetKind.Loot, loot.x, loot.y, loot.id, loot.loose);
                if (loot.loose)
                {
                    // Dropped stacks lie flat and do not become a solid container under the player's feet.
                    var image = Pixel("Loose item art", visual.transform, visual.sprite, Vector3.zero, -400, tint);
                    image.transform.localScale = Vector3.one * 0.55f; visual.enabled = false;
                }
                var labelRoot = new GameObject("Item label"); labelRoot.transform.SetParent(worldRoot.transform, false); labelRoot.transform.localPosition = World(loot.x, loot.y);
                WorldLabel(labelRoot.transform, Rules.ItemNames[(int)kind] + " ×" + loot.units.Count, 0.65f);
                objects.Add(new ObjectVisual { root = visual.gameObject, x = loot.x, y = loot.y });
                objects.Add(new ObjectVisual { root = labelRoot, x = loot.x, y = loot.y });
            }
            actorVisual = Pixel("玩家", worldRoot.transform, artSet.playerDown, World(Run.x, Run.y), SpriteOrder(Run.y) + 2);
            actor = actorVisual.transform;
            actor.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one * MapData.PlayerCollisionSize;
            hoverMarker = Pixel("Target mark", worldRoot.transform, markerSprite, Vector3.zero, 40000).transform;
            RefreshVisibility();
            if (snap) { cameraVelocity = Vector3.zero; cameraFocus = World(Run.x, Run.y); }
            gameCamera.transform.SetPositionAndRotation(PixelPosition(cameraFocus) + Vector3.back * 10, Quaternion.identity);
        }

        partial void RefreshVisibility()
        {
            if (Run == null || worldRoot == null) return;
            foreach (var visual in tiles)
            {
                bool known = Run.Map.Known(visual.x, visual.y);
                bool showWall = visual.wall != null && PixelWallTiles.IsOutlineVisible(Run.Map, visual.x, visual.y);
                visual.fog.gameObject.SetActive(!(known || showWall));
                visual.floor.enabled = known && !(Run.Map.Border(visual.x, visual.y) && Run.Map.At(visual.x, visual.y) == Tile.Wall);
                if (visual.wall != null)
                {
                    // Exploration can change a decorative corner into a fuller
                    // junction without rebuilding the terrain or discovering it.
                    if (showWall) visual.wall.sprite = wallTiles.Get(Run.Map, visual.x, visual.y);
                    visual.wall.color = MapGenerator.IsDoorHintWall(Run.Map, visual.x, visual.y) ? DoorHintColor : Color.white;
                }
                if (visual.wallCollider != null) visual.wallCollider.enabled = known;
                if (visual.feature != null) visual.feature.SetActive(known || showWall);
            }
            foreach (var visual in objects) visual.root.SetActive(Run.Map.Known(visual.x, visual.y));
        }

        partial void FollowWorld()
        {
            bool exploring = Run != null && ScreenMode == GameScreen.Explore;
            if (worldRoot != null && worldRoot.activeSelf != exploring) worldRoot.SetActive(exploring);
            if (!exploring || gameCamera == null || actor == null) return;
            ConfigurePixelCamera(gameCamera);
            actor.position = PixelPosition(World(Run.x, Run.y));
            actorVisual.sortingOrder = SpriteOrder(Run.y) + 2;
            bool horizontal = Mathf.Abs(facing.x) > Mathf.Abs(facing.y);
            actorVisual.sprite = horizontal ? artSet.playerSide : facing.y > 0 ? artSet.playerUp : artSet.playerDown;
            actorVisual.flipX = horizontal && facing.x < 0;
            cameraFocus = Vector3.SmoothDamp(cameraFocus, World(Run.x, Run.y), ref cameraVelocity, 0.1f);
            gameCamera.transform.SetPositionAndRotation(PixelPosition(cameraFocus) + Vector3.back * 10, Quaternion.identity);
            var mark = hover.kind != TargetKind.None ? hover : highlighted;
            hoverMarker.gameObject.SetActive(Panel == GamePanel.None && mark.kind != TargetKind.None);
            if (mark.kind != TargetKind.None) hoverMarker.position = World(mark.x, mark.y);
        }

        void OnDestroy()
        {
            ReleaseInterfaceInput(); wallTiles?.Dispose();
            if (markerSprite != null) Destroy(markerSprite);
            if (markerTexture != null) Destroy(markerTexture);
            if (interfaceFont != null) Destroy(interfaceFont);
        }
    }
}
