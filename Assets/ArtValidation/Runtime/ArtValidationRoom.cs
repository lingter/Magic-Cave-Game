using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.ArtValidation
{
    public sealed class ArtValidationRoom : MonoBehaviour
    {
        public const int Width = 10, Height = 10;
        // The player's opaque art is 0.75 units wide/high; leave a small contact margin.
        public const float PlayerCollisionSize = 0.8f;
        // Art indices in screen order: first row is the north edge. Zero is walkable floor.
        static readonly int[,] Rows =
        {
            { 4, 2, 2, 2,14, 2, 2, 2, 2, 5 },
            {11, 0, 0, 0,11, 0, 0, 0, 0,11 },
            {11, 0, 0, 0,12, 0, 0, 0, 0,11 },
            {11, 0, 0, 0, 0, 0, 0, 0, 0,11 },
            {11, 0, 1, 2, 2, 3, 0,10, 0,11 },
            {11, 0, 0, 0, 0, 0, 0,11, 0,11 },
            {11, 0,10, 0, 0, 0, 0,12, 0,11 },
            {11, 0,11, 0, 0, 0, 0, 0, 0,11 },
            {11, 0,11, 0, 0, 0, 0, 0, 0,11 },
            { 8, 2,13, 2, 2, 2, 2, 2, 2, 9 }
        };

        public int[] wallTiles = CreateLayout();
        public Camera roomCamera;
        public GameObject artRoot, whiteboxRoot, gridRoot;
        public SpriteRenderer player;
        public Sprite playerDown, playerUp, playerSide, whiteboxSprite;
        public ArtValidationProp[] props;
        public bool WhiteboxVisible { get; private set; }
        public bool GridVisible { get; private set; }
        public Vector2 PlayerPosition => player.transform.position;
        readonly List<Vector2Int> route = new List<Vector2Int>();
        string message = "沿通道走一圈，检查墙角接缝和人物前后遮挡。";
        GUIStyle titleStyle, textStyle, buttonStyle;
        Font font;

        public static int[] CreateLayout()
        {
            var result = new int[Width * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
                result[y * Width + x] = Rows[Height - 1 - y, x];
            return result;
        }

        public int WallAt(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? -1 : wallTiles[y * Width + x];
        public static int SortOrder(float y) => 2000 - Mathf.RoundToInt(y * 100);

        void Start() { ResetRoom(); }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.vKey.wasPressedThisFrame) SetWhitebox(!WhiteboxVisible);
                if (keyboard.gKey.wasPressedThisFrame) SetGrid(!GridVisible);
                if (keyboard.rKey.wasPressedThisFrame) ResetRoom();
                if (keyboard.eKey.wasPressedThisFrame) InteractNearest();
                var input = new Vector2(
                    ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1 : 0) - ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1 : 0),
                    ((keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1 : 0) - ((keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1 : 0));
                if (input.sqrMagnitude > 0)
                {
                    route.Clear(); MovePlayer(input.normalized * (3 * Mathf.Min(Time.deltaTime, 0.05f)));
                }
            }
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && PointerInMap(mouse.position.ReadValue()))
            {
                Vector3 world = roomCamera.ScreenToWorldPoint(mouse.position.ReadValue());
                Navigate(new Vector2Int(Mathf.RoundToInt(world.x), Mathf.RoundToInt(world.y)));
            }
            if (route.Count > 0)
            {
                var target = (Vector2)route[0];
                var delta = Vector2.MoveTowards(PlayerPosition, target, 3 * Mathf.Min(Time.deltaTime, 0.05f)) - PlayerPosition;
                if (!MovePlayer(delta)) route.Clear();
                else if (Vector2.Distance(PlayerPosition, target) < 0.01f) route.RemoveAt(0);
            }
        }

        void LateUpdate()
        {
            // Overview stays fixed; only the player and the sorting order change.
            roomCamera.transform.SetPositionAndRotation(new Vector3(4.5f, 4.5f, -10), Quaternion.identity);
            int pixelScale = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(Screen.width / 200f, Screen.height / 224f)));
            roomCamera.orthographicSize = Screen.height / (32f * pixelScale);
            player.sortingOrder = SortOrder(PlayerPosition.y) + 1;
        }

        bool PointerInMap(Vector2 screen)
        {
            if (screen.y < Screen.height * 0.14f || screen.y > Screen.height * 0.9f) return false;
            Vector3 point = roomCamera.ScreenToWorldPoint(screen);
            return point.x >= -0.5f && point.y >= -0.5f && point.x < 9.5f && point.y < 9.5f;
        }

        bool CellOpen(int x, int y)
        {
            if (WallAt(x, y) != 0) return false;
            foreach (var prop in props)
                if (prop.gameObject.activeSelf && prop.blocksMovement &&
                    Mathf.RoundToInt(prop.transform.position.x) == x && Mathf.RoundToInt(prop.transform.position.y) == y) return false;
            return true;
        }

        public bool CanStand(Vector2 position)
        {
            const float radius = PlayerCollisionSize * 0.5f;
            int left = Mathf.FloorToInt(position.x - radius + 0.5f), right = Mathf.FloorToInt(position.x + radius + 0.5f);
            int bottom = Mathf.FloorToInt(position.y - radius + 0.5f), top = Mathf.FloorToInt(position.y + radius + 0.5f);
            return CellOpen(left, bottom) && CellOpen(right, bottom) && CellOpen(left, top) && CellOpen(right, top);
        }

        public bool MovePlayer(Vector2 delta)
        {
            var previous = PlayerPosition;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.04f));
            var step = delta / steps;
            for (int i = 0; i < steps; i++)
            {
                var next = PlayerPosition;
                if (CanStand(next + new Vector2(step.x, 0))) next.x += step.x;
                if (CanStand(next + new Vector2(0, step.y))) next.y += step.y;
                player.transform.position = new Vector3(next.x, next.y, 0);
            }
            if (!WhiteboxVisible && delta.sqrMagnitude > 0)
            {
                bool horizontal = Mathf.Abs(delta.x) > Mathf.Abs(delta.y);
                player.sprite = horizontal ? playerSide : delta.y > 0 ? playerUp : playerDown;
                player.flipX = horizontal && delta.x < 0;
            }
            player.sortingOrder = SortOrder(PlayerPosition.y) + 1;
            return Vector2.Distance(previous, PlayerPosition) > 0.0001f;
        }

        public void Navigate(Vector2Int target)
        {
            route.Clear();
            if (!CellOpen(target.x, target.y)) { message = "该格被墙体或物件占据。"; return; }
            var start = new Vector2Int(Mathf.RoundToInt(PlayerPosition.x), Mathf.RoundToInt(PlayerPosition.y));
            var queue = new Queue<Vector2Int>(); var parents = new Dictionary<Vector2Int, Vector2Int>();
            queue.Enqueue(start); parents[start] = start;
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0 && !parents.ContainsKey(target))
            {
                var current = queue.Dequeue();
                foreach (var direction in directions)
                {
                    var next = current + direction;
                    if (!parents.ContainsKey(next) && CellOpen(next.x, next.y)) { parents[next] = current; queue.Enqueue(next); }
                }
            }
            if (!parents.ContainsKey(target)) { message = "这里没有可通行的路线。"; return; }
            var reverse = new List<Vector2Int>();
            for (var current = target; current != start; current = parents[current]) reverse.Add(current);
            reverse.Reverse();
            if (Vector2.Distance(PlayerPosition, start) > 0.01f) route.Add(start);
            route.AddRange(reverse);
        }

        public void InteractNearest()
        {
            ArtValidationProp nearest = null; float distance = 1.3f;
            foreach (var prop in props)
            {
                if (!prop.gameObject.activeSelf) continue;
                float candidate = Vector2.Distance(PlayerPosition, prop.transform.position);
                if (candidate < distance) { nearest = prop; distance = candidate; }
            }
            if (nearest == null) { message = "附近没有可交互物件。"; return; }
            route.Clear();
            if (nearest.kind == ValidationPropKind.Chest || nearest.kind == ValidationPropKind.Bag)
            { message = "已拾取：" + nearest.displayName + "。按 R 可恢复。"; nearest.gameObject.SetActive(false); }
            else if (nearest.kind == ValidationPropKind.Exit) message = "已到达出口。按 R 重置验证场景。";
            else if (nearest.kind == ValidationPropKind.Trap) message = "尖刺区域：可以检查贴地位置和人物覆盖关系。";
            else if (nearest.kind == ValidationPropKind.Merchant) message = "商人：欢迎来到素材验证房间。";
            else message = nearest.displayName + "：检查绕行、碰撞和前后遮挡。";
        }

        public void ResetRoom()
        {
            route.Clear();
            foreach (var prop in props) prop.gameObject.SetActive(true);
            player.transform.position = new Vector3(1, 1, 0);
            player.GetComponent<BoxCollider2D>().size = Vector2.one * PlayerCollisionSize;
            SetWhitebox(false); SetGrid(false);
            message = "固定10×10地图；沿通道检查拼接，靠近物件按 E。";
        }

        public void SetWhitebox(bool enabled)
        {
            WhiteboxVisible = enabled; artRoot.SetActive(!enabled); whiteboxRoot.SetActive(enabled);
            foreach (var prop in props) prop.ShowWhitebox(enabled);
            player.sprite = enabled ? whiteboxSprite : playerDown;
            player.color = enabled ? new Color(0.2f, 0.8f, 1) : Color.white;
            player.drawMode = enabled ? SpriteDrawMode.Sliced : SpriteDrawMode.Simple;
            player.size = enabled ? Vector2.one * PlayerCollisionSize : Vector2.one;
            // Switching back to Simple can bake the sliced size into the Transform.
            player.transform.localScale = Vector3.one;
            player.flipX = false;
        }

        public void SetGrid(bool enabled) { GridVisible = enabled; gridRoot.SetActive(enabled); }

        void OnGUI()
        {
            if (titleStyle == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Arial" }, 20);
                titleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 23, fontStyle = FontStyle.Bold };
                textStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 17 };
                buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17 };
            }
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            GUI.Box(new Rect(0, 0, 1280, 64), GUIContent.none);
            GUI.Label(new Rect(22, 13, 620, 36), "10×10 素材验证 · 固定地图 / 固定正交镜头", titleStyle);
            if (GUI.Button(new Rect(800, 15, 155, 34), WhiteboxVisible ? "美术显示 [V]" : "白模对照 [V]", buttonStyle)) SetWhitebox(!WhiteboxVisible);
            if (GUI.Button(new Rect(965, 15, 145, 34), GridVisible ? "隐藏网格 [G]" : "显示网格 [G]", buttonStyle)) SetGrid(!GridVisible);
            if (GUI.Button(new Rect(1120, 15, 138, 34), "重置 [R]", buttonStyle)) ResetRoom();
            GUI.Box(new Rect(0, 626, 1280, 94), GUIContent.none);
            GUI.Label(new Rect(22, 635, 1236, 26), "WASD / 方向键移动　左键地面寻路　E 交互　V 白模对照　G 网格　R 重置", textStyle);
            GUI.Label(new Rect(22, 671, 1236, 28), message, textStyle);
            GUI.matrix = matrix;
        }

        void OnDestroy() { if (font != null) Destroy(font); }
    }
}
