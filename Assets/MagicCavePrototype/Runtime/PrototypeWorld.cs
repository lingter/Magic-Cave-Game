using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        sealed class TileVisual
        {
            public int x, y;
            public Renderer floor, wall;
            public GameObject feature;
            public bool doorHint;
        }
        sealed class ObjectVisual
        {
            public GameObject root;
            public int x, y;
        }
        GameObject worldRoot;
        Transform actor, hoverMarker;
        Vector3 cameraVelocity;
        Vector3 cameraFocus;
        readonly List<Transform> worldLabels = new List<Transform>();
        readonly List<TileVisual> tiles = new List<TileVisual>();
        readonly List<ObjectVisual> objects = new List<ObjectVisual>();
        readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        Material floorMaterial, fogMaterial, wallMaterial, fadedWall, hintWallMaterial, fadedHintWall;

        Material Material(string key, Color color, bool unlit = false, bool faded = false)
        {
            if (materials.TryGetValue(key, out var existing)) return existing;
            var template = faded ? transparentTemplate : unlit ? unlitTemplate : surfaceTemplate;
            var material = template != null ? new Material(template) : new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            material.name = "Prototype " + key;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
            if (faded)
            {
                material.SetFloat("_Surface", 1); material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000;
            }
            materials.Add(key, material); return material;
        }
        Vector3 World(float x, float y, float height = 0)
        {
            float half = (Run.Map.size - 1) * 0.5f;
            return new Vector3(x - half, height, y - half);
        }
        GameObject Piece(string name, Transform parent, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material, bool collider = false)
        {
            var piece = GameObject.CreatePrimitive(primitive);
            piece.name = name; piece.transform.SetParent(parent, false);
            piece.transform.localPosition = position; piece.transform.localScale = scale;
            piece.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Destroy(piece.GetComponent<Collider>());
            return piece;
        }
        void TargetTag(GameObject piece, TargetKind kind, int x, int y, int id = 0)
        {
            var tag = piece.AddComponent<PrototypeTarget>(); tag.kind = kind; tag.x = x; tag.y = y; tag.id = id;
        }
        void WorldLabel(Transform parent, string text, float height)
        {
            var label = new GameObject("Label"); label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0, height, 0);
            label.transform.rotation = gameCamera.transform.rotation;
            worldLabels.Add(label.transform);
            var mesh = label.AddComponent<TextMesh>(); mesh.font = interfaceFont;
            mesh.fontSize = 28; mesh.characterSize = 0.042f; mesh.anchor = TextAnchor.MiddleCenter;
            mesh.text = text; mesh.color = new Color(0.95f, 0.96f, 1f);
            var renderer = label.GetComponent<MeshRenderer>(); renderer.sharedMaterial = interfaceFont.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        partial void BuildWorld(bool snap)
        {
            if (worldRoot != null) { worldRoot.SetActive(false); Destroy(worldRoot); }
            worldRoot = new GameObject("Magic Cave · Runtime Map"); worldRoot.transform.SetParent(transform, false);
            tiles.Clear(); objects.Clear(); worldLabels.Clear();
            floorMaterial = Material("ground", new Color(0.28f, 0.33f, 0.36f));
            fogMaterial = Material("fog", new Color(0.018f, 0.025f, 0.04f), true);
            wallMaterial = Material("wall", new Color(0.43f, 0.47f, 0.50f));
            fadedWall = Material("faded wall", new Color(0.43f, 0.47f, 0.50f, 0.22f), false, true);
            hintWallMaterial = Material("door hint wall", new Color(0.98f, 0.73f, 0.22f));
            fadedHintWall = Material("faded door hint wall", new Color(0.98f, 0.73f, 0.22f, 0.22f), false, true);
            var advance = Material("advance", new Color(0.14f, 0.92f, 0.78f), true);
            var returning = Material("return", new Color(1f, 0.67f, 0.23f), true);
            for (int y = 0; y < Run.Map.size; y++) for (int x = 0; x < Run.Map.size; x++)
            {
                Tile tile = Run.Map.At(x, y);
                var floor = Piece("Cell " + x + "," + y, worldRoot.transform, PrimitiveType.Cube, World(x, y, -0.075f), new Vector3(0.97f, 0.15f, 0.97f), floorMaterial, true);
                TargetTag(floor, tile == Tile.Wall ? TargetKind.Wall : tile == Tile.Ground ? TargetKind.Ground : TargetKind.Door, x, y);
                var visual = new TileVisual { x = x, y = y, floor = floor.GetComponent<Renderer>() };
                if (tile == Tile.Wall)
                {
                    var wall = Piece("Wall", worldRoot.transform, PrimitiveType.Cube, World(x, y, 0.53f), new Vector3(0.98f, 1.06f, 0.98f), wallMaterial, true);
                    TargetTag(wall, TargetKind.Wall, x, y); visual.wall = wall.GetComponent<Renderer>(); visual.feature = wall;
                }
                else if (tile == Tile.Advance || tile == Tile.Return)
                {
                    var door = new GameObject(tile == Tile.Advance ? "前进门" : "返回门"); door.transform.SetParent(worldRoot.transform, false); door.transform.localPosition = World(x, y);
                    var tint = tile == Tile.Advance ? advance : returning;
                    Piece("Left pillar", door.transform, PrimitiveType.Cube, new Vector3(-0.32f, 0.47f, 0), new Vector3(0.10f, 0.94f, 0.14f), tint);
                    Piece("Right pillar", door.transform, PrimitiveType.Cube, new Vector3(0.32f, 0.47f, 0), new Vector3(0.10f, 0.94f, 0.14f), tint);
                    Piece("Lintel", door.transform, PrimitiveType.Cube, new Vector3(0, 0.91f, 0), new Vector3(0.74f, 0.10f, 0.14f), tint);
                    var collision = door.AddComponent<BoxCollider>(); collision.center = new Vector3(0, 0.47f, 0); collision.size = new Vector3(0.8f, 0.94f, 0.35f);
                    TargetTag(door, TargetKind.Door, x, y); WorldLabel(door.transform, door.name, 1.15f); visual.feature = door;
                }
                tiles.Add(visual);
            }
            foreach (var enemy in Run.Map.enemies)
            {
                if (enemy.defeated) continue;
                Color color = enemy.kind == EnemyKind.Bat ? new Color(0.66f, 0.40f, 0.89f) : enemy.kind == EnemyKind.Beetle ? new Color(0.86f, 0.49f, 0.20f) : new Color(0.88f, 0.20f, 0.28f);
                var root = new GameObject(Rules.EnemyNames[(int)enemy.kind]); root.transform.SetParent(worldRoot.transform, false); root.transform.localPosition = World(enemy.x, enemy.y);
                bool boss = enemy.kind == EnemyKind.Boss;
                var body = Piece("Body", root.transform, boss ? PrimitiveType.Capsule : PrimitiveType.Sphere, new Vector3(0, boss ? 0.7f : 0.36f, 0), boss ? new Vector3(0.75f, 0.70f, 0.75f) : new Vector3(0.65f, 0.48f, 0.60f), Material("enemy " + enemy.kind, color), true);
                body.GetComponent<Collider>().isTrigger = false; TargetTag(body, TargetKind.Enemy, enemy.x, enemy.y, enemy.id);
                if (enemy.kind == EnemyKind.Bat)
                {
                    Piece("Wing L", root.transform, PrimitiveType.Cube, new Vector3(-0.38f, 0.36f, 0), new Vector3(0.40f, 0.08f, 0.3f), Material("bat wings", new Color(0.35f, 0.21f, 0.53f)));
                    Piece("Wing R", root.transform, PrimitiveType.Cube, new Vector3(0.38f, 0.36f, 0), new Vector3(0.40f, 0.08f, 0.3f), Material("bat wings", new Color(0.35f, 0.21f, 0.53f)));
                }
                WorldLabel(root.transform, root.name, boss ? 1.65f : 0.87f);
                objects.Add(new ObjectVisual { root = root, x = enemy.x, y = enemy.y });
            }
            foreach (var loot in Run.Map.loot)
            {
                if (loot.units.Count == 0) continue;
                ItemKind kind = loot.units[0].kind;
                Color color = kind == ItemKind.Potion ? new Color(0.31f, 0.91f, 0.54f) : kind == ItemKind.Relic ? new Color(1f, 0.75f, 0.18f) : kind == ItemKind.Crystal ? new Color(0.30f, 0.75f, 1f) : new Color(0.67f, 0.69f, 0.72f);
                var root = Piece("Item " + loot.id, worldRoot.transform, kind == ItemKind.Potion ? PrimitiveType.Cylinder : PrimitiveType.Cube,
                    World(loot.x, loot.y, 0.19f), kind == ItemKind.Potion ? new Vector3(0.24f, 0.19f, 0.24f) : new Vector3(0.31f, 0.31f, 0.31f), Material("item " + kind, color), true);
                root.GetComponent<Collider>().isTrigger = true; TargetTag(root, TargetKind.Loot, loot.x, loot.y, loot.id);
                // A separate unscaled label keeps quantity text readable above tiny item meshes.
                var labelRoot = new GameObject("Item label"); labelRoot.transform.SetParent(worldRoot.transform, false); labelRoot.transform.localPosition = World(loot.x, loot.y);
                WorldLabel(labelRoot.transform, Rules.ItemNames[(int)kind] + " ×" + loot.units.Count, 0.62f);
                objects.Add(new ObjectVisual { root = root, x = loot.x, y = loot.y });
                objects.Add(new ObjectVisual { root = labelRoot, x = loot.x, y = loot.y });
            }
            var actorRoot = new GameObject("玩家"); actorRoot.transform.SetParent(worldRoot.transform, false); actor = actorRoot.transform;
            Piece("Explorer", actor, PrimitiveType.Capsule, new Vector3(0, 0.52f, 0), new Vector3(0.38f, 0.52f, 0.38f), Material("player", new Color(0.18f, 0.73f, 0.95f)));
            Piece("Player mark", actor, PrimitiveType.Cylinder, new Vector3(0, 0.012f, 0), new Vector3(0.50f, 0.008f, 0.50f), Material("player mark", new Color(0.10f, 0.7f, 0.90f), true));
            hoverMarker = Piece("Target mark", worldRoot.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.80f, 0.012f, 0.80f), Material("target", new Color(0.98f, 0.73f, 0.18f), true)).transform;
            actor.position = World(Run.x, Run.y);
            RefreshVisibility(); Physics.SyncTransforms();
            if (snap)
            {
                cameraVelocity = Vector3.zero; cameraFocus = World(Run.x, Run.y, 0.52f);
                gameCamera.transform.rotation = CameraRotation(); gameCamera.transform.position = CameraPosition();
            }
        }
        partial void RefreshVisibility()
        {
            if (Run == null || worldRoot == null) return;
            foreach (var visual in tiles)
            {
                bool known = Run.Map.Known(visual.x, visual.y);
                visual.doorHint = MapGenerator.IsDoorHintWall(Run.Map, visual.x, visual.y);
                visual.floor.sharedMaterial = known ? floorMaterial : fogMaterial;
                if (visual.wall != null) visual.wall.sharedMaterial = visual.doorHint ? hintWallMaterial : wallMaterial;
                if (visual.feature != null) visual.feature.SetActive(known);
            }
            foreach (var visual in objects) visual.root.SetActive(Run.Map.Known(visual.x, visual.y));
            Physics.SyncTransforms();
        }
        Quaternion CameraRotation() { return Quaternion.Euler(50, 0, 0); }
        Vector3 CameraPosition()
        {
            return cameraFocus - CameraRotation() * Vector3.forward * 10f;
        }
        partial void FollowWorld()
        {
            bool exploring = Run != null && ScreenMode == GameScreen.Explore;
            if (worldRoot != null && worldRoot.activeSelf != exploring) worldRoot.SetActive(exploring);
            if (!exploring || gameCamera == null || actor == null) return;
            actor.position = World(Run.x, Run.y);
            cameraFocus = Vector3.SmoothDamp(cameraFocus, World(Run.x, Run.y, 0.52f), ref cameraVelocity, 0.1f);
            gameCamera.transform.rotation = CameraRotation();
            gameCamera.transform.position = CameraPosition();
            foreach (var label in worldLabels) label.rotation = gameCamera.transform.rotation;
            var mark = hover.kind != TargetKind.None ? hover : highlighted;
            hoverMarker.gameObject.SetActive(Panel == GamePanel.None && mark.kind != TargetKind.None);
            if (mark.kind != TargetKind.None) hoverMarker.position = World(mark.x, mark.y, 0.018f);
            foreach (var visual in tiles) if (visual.wall != null) visual.wall.sharedMaterial = visual.doorHint ? hintWallMaterial : wallMaterial;
            Vector3 aim = actor.position + Vector3.up * 0.52f;
            foreach (var hit in Physics.RaycastAll(gameCamera.transform.position, (aim - gameCamera.transform.position).normalized, Vector3.Distance(aim, gameCamera.transform.position), ~0, QueryTriggerInteraction.Ignore))
            {
                var tag = hit.collider.GetComponent<PrototypeTarget>();
                if (tag == null || tag.kind != TargetKind.Wall || !Run.Map.Known(tag.x, tag.y)) continue;
                var renderer = hit.collider.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = MapGenerator.IsDoorHintWall(Run.Map, tag.x, tag.y) ? fadedHintWall : fadedWall;
            }
        }
        void OnDestroy()
        {
            ReleaseInterfaceInput();
            foreach (var material in materials.Values) if (material != null) Destroy(material);
            if (interfaceFont != null) Destroy(interfaceFont);
        }
    }
}
