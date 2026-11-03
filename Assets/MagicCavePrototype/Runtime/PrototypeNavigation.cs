using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public sealed partial class PrototypeGame
    {
        bool minimapExpanded, navigationEscapeHandled;
        Rect MinimapPanel => minimapExpanded ? new Rect(322, 94, 636, 556) : new Rect(1038, 100, 228, 268);
        Rect MinimapArea => minimapExpanded ? new Rect(394, 128, 492, 492) : new Rect(1050, 133, 204, 204);
        static readonly Vector2 DefaultWheelCenter = new Vector2(1184, 588);
        bool PointerOverDefaultWheel => (GuiMouse - DefaultWheelCenter).sqrMagnitude <= 64 * 64;
        Vector2 WheelCenter => wheelActive ? pointerOrigin : DefaultWheelCenter;
        float pointerPressedAt;
        bool pointerPending, wheelActive, pointerMinimap, navigationClick;
        Vector2 pointerOrigin;
        Vector2Int wheelDirection;
        Target pointerClickTarget;
        RunData pointerRun;
        MapData pointerMap;
        bool pointerGuild;
        GameScreen pointerScreen;
        Vector2Int MovementDirection => gridDirection != Vector2Int.zero ? gridDirection : wheelDirection;

        void ResetNavigationPointer()
        {
            pointerPending = wheelActive = pointerMinimap = false;
            wheelDirection = Vector2Int.zero;
        }
        void OnApplicationFocus(bool focused) { if (!focused) ResetNavigationPointer(); }
        void SetMinimapExpanded(bool expanded)
        {
            minimapExpanded = expanded;
            ResetNavigationPointer(); CancelRoute();
        }
        void ReadNavigationPointer()
        {
            navigationClick = navigationEscapeHandled = false;
            var mouse = Mouse.current;
            bool explore = ScreenMode == GameScreen.Explore && Run != null;
            bool town = ScreenMode == GameScreen.Home && TownPage == TownPage.None;
            if (!explore || Panel != GamePanel.None) minimapExpanded = false;
            if (mouse == null || Panel != GamePanel.None || (!explore && !town))
            { ResetNavigationPointer(); return; }
            if (minimapExpanded)
            {
                wheelActive = false; wheelDirection = Vector2Int.zero;
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                { SetMinimapExpanded(false); navigationEscapeHandled = true; return; }
                if (mouse.leftButton.wasPressedThisFrame && MinimapArea.Contains(GuiMouse))
                { pointerPending = pointerMinimap = true; }
                // The expanded map owns input and blocks movement and the scene underneath it.
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    if (pointerPending && pointerMinimap && MinimapArea.Contains(GuiMouse))
                    {
                        if (NavigateMinimap(GuiMouse)) minimapExpanded = false;
                        consumedClick = Time.frameCount;
                    }
                    ResetNavigationPointer();
                }
                return;
            }
            if (pointerPending && (pointerRun != Run || pointerMap != (Run == null ? null : Run.Map) ||
                pointerScreen != ScreenMode || pointerGuild != InGuild)) ResetNavigationPointer();
            if (gridDirection != Vector2Int.zero || mouse.rightButton.wasPressedThisFrame)
            { ResetNavigationPointer(); return; }
            if (mouse.leftButton.wasPressedThisFrame)
            {
                ResetNavigationPointer();
                bool minimap = explore && MinimapPanel.Contains(GuiMouse);
                bool fixedWheel = PointerOverDefaultWheel;
                if (!fixedWheel && (explore ? !minimap && PointerOverHud() : !TownMapRect.Contains(GuiMouse))) return;
                pointerPending = true; pointerMinimap = minimap; pointerOrigin = GuiMouse;
                pointerPressedAt = Time.unscaledTime;
                if (fixedWheel) { pointerOrigin = DefaultWheelCenter; wheelActive = true; }
                pointerScreen = ScreenMode; pointerRun = Run; pointerMap = Run == null ? null : Run.Map; pointerGuild = InGuild;
                if (explore) { pointerClickTarget = minimap ? default : PointerTarget(); CancelRoute(); }
                else { townRoute.Clear(); townDestination = -1; }
            }
            if (!pointerPending) return;
            Vector2 delta = GuiMouse - pointerOrigin;
            if (!pointerMinimap && (delta.sqrMagnitude >= 18 * 18 || Time.unscaledTime - pointerPressedAt >= .25f)) wheelActive = true;
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (wheelActive) consumedClick = Time.frameCount;
                else if (pointerMinimap) NavigateMinimap(GuiMouse);
                else if (explore && !PointerOverHud()) { hover = pointerClickTarget; navigationClick = true; }
                ResetNavigationPointer(); return;
            }
            if (!mouse.leftButton.isPressed) { ResetNavigationPointer(); return; }
            if (!wheelActive) return;
            // GUI Y points down. A central dead zone stops movement; hysteresis stabilizes diagonals.
            if (delta.sqrMagnitude < 12 * 12) { wheelDirection = Vector2Int.zero; return; }
            float horizontal = Mathf.Abs(delta.x), vertical = Mathf.Abs(delta.y);
            if (wheelDirection.x != 0 && vertical <= horizontal * 1.25f)
                wheelDirection = new Vector2Int(delta.x >= 0 ? 1 : -1, 0);
            else if (wheelDirection.y != 0 && horizontal <= vertical * 1.25f)
                wheelDirection = new Vector2Int(0, delta.y <= 0 ? 1 : -1);
            else wheelDirection = horizontal > vertical ? new Vector2Int(delta.x >= 0 ? 1 : -1, 0) : new Vector2Int(0, delta.y <= 0 ? 1 : -1);
        }
        bool NavigateMinimap(Vector2 point)
        {
            if (!MinimapArea.Contains(point)) return false;
            int x = Mathf.FloorToInt((point.x - MinimapArea.x) * Run.Map.size / MinimapArea.width);
            int y = Run.Map.size - 1 - Mathf.FloorToInt((point.y - MinimapArea.y) * Run.Map.size / MinimapArea.height);
            if (!Run.Map.Known(x, y) || !Run.Map.CanTraverse(x, y))
            { Notify("请选择已揭示的可通行地块"); return false; }
            if (MapGenerator.Path(Run.Map, new Vector2Int(Run.CellX, Run.CellY), new Vector2Int(x, y), false) == null)
            { Notify("无法到达：先探索并揭示通路"); return false; }
            Navigate(new Target { kind = TargetKind.Ground, x = x, y = y }, false);
            return true;
        }
        Rect MinimapCell(int x, int y)
        {
            float step = MinimapArea.width / Run.Map.size;
            return new Rect(MinimapArea.x + x * step, MinimapArea.y + (Run.Map.size - 1 - y) * step, step, step);
        }
        void MinimapMark(int x, int y, Color color, float inset = 1)
        {
            if (!Run.Map.Known(x, y)) return;
            Rect cell = MinimapCell(x, y);
            Fill(new Rect(cell.x + inset, cell.y + inset, Mathf.Max(1, cell.width - inset * 2), Mathf.Max(1, cell.height - inset * 2)), "mini " + color, color);
        }
        void DrawMinimap()
        {
            Box(MinimapPanel);
            var panel = MinimapPanel;
            Label(new Rect(panel.x + 12, panel.y + 3, minimapExpanded ? 430 : 125, 28), (minimapExpanded ? "探索地图 · " : "小地图 · ") + (Run.mapIndex + 1) + "层", buttonStyle, Gold);
            Button(new Rect(panel.xMax - 78, panel.y + 4, 66, 26), minimapExpanded ? "收起" : "放大", () => SetMinimapExpanded(!minimapExpanded));
            Fill(MinimapArea, "mini fog", new Color(.025f, .035f, .055f));
            var map = Run.Map;
            for (int y = 0; y < map.size; y++)
            for (int x = 0; x < map.size; x++)
                MinimapMark(x, y, map.At(x, y) == Tile.Wall ? new Color(.2f, .25f, .31f) : new Color(.4f, .48f, .53f), .4f);
            foreach (var step in route) MinimapMark(step.x, step.y, Cyan, 2);
            foreach (var item in map.loot.Where(l => l.BlocksMovement || l.HasContents)) MinimapMark(item.x, item.y, item.HasContents ? Gold : new Color(.38f, .28f, .18f));
            foreach (var enemy in map.enemies.Where(e => !e.defeated)) MinimapMark(enemy.x, enemy.y, new Color(.95f, .3f, .3f));
            if (map.hasMerchant) MinimapMark(map.merchantX, map.merchantY, new Color(.65f, .45f, 1));
            for (int y = 0; y < map.size; y++)
            for (int x = 0; x < map.size; x++)
                if (map.At(x, y) == Tile.Advance || map.At(x, y) == Tile.Return) MinimapMark(x, y, new Color(.3f, .85f, .5f));
            MinimapMark(Run.CellX, Run.CellY, Color.white, 0);
            Label(new Rect(panel.x + 6, panel.yMax - 28, panel.width - 12, 24), minimapExpanded ? "白：角色  红：怪物  金：物品  绿：门  紫：商人 · 点击地面前往 · Esc收起" : "白：角色  红：怪  金：物品", smallStyle);
        }
        void DrawExpandedMinimap()
        {
            if (!minimapExpanded || Run == null || ScreenMode != GameScreen.Explore || Panel != GamePanel.None) return;
            Fill(new Rect(0, 0, 1280, 720), "map overlay shade", new Color(.015f, .025f, .04f, .85f));
            DrawMinimap();
        }
        void DrawDirectionWheel()
        {
            if (minimapExpanded || Panel != GamePanel.None || (ScreenMode != GameScreen.Explore && (ScreenMode != GameScreen.Home || TownPage != TownPage.None))) return;
            Vector2 center = WheelCenter;
            if (!guiTextures.TryGetValue("direction wheel disc", out var disc))
            {
                disc = new Texture2D(128, 128);
                for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                {
                    float radius = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(64, 64));
                    disc.SetPixel(x, y, radius > 63 ? Color.clear : radius > 60 ? new Color(.45f, .68f, .76f, .7f) : new Color(.05f, .1f, .15f, .65f));
                }
                disc.Apply(); guiTextures.Add("direction wheel disc", disc);
            }
            GUI.DrawTexture(new Rect(center.x - 64, center.y - 64, 128, 128), disc);
            for (int i = 0; i < 4; i++)
            {
                var direction = MapGenerator.Directions[i];
                var offset = new Vector2(direction.x, -direction.y) * 40;
                var rect = new Rect(center.x + offset.x - 19, center.y + offset.y - 19, 38, 38);
                Fill(rect, "wheel " + (direction == wheelDirection), direction == wheelDirection ? new Color(.18f, .65f, .7f, .85f) : new Color(.08f, .15f, .22f, .72f));
                Label(rect, direction.x > 0 ? "→" : direction.x < 0 ? "←" : direction.y > 0 ? "↑" : "↓", buttonStyle, Color.white);
            }
            Fill(new Rect(center.x - 12, center.y - 12, 24, 24), "wheel center", new Color(.7f, .8f, .85f, .65f));
        }
    }
}
