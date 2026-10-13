using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MagicCave.Prototype
{
    public enum GameScreen { Home, Explore, Battle, Rewards, Settlement, Result, Warehouse }
    public enum GamePanel { None, Inventory, Deck, Draw, Discard, Pause, Return, NewRun, Leave }

    public sealed partial class PrototypeGame : MonoBehaviour
    {
        public Material surfaceTemplate;
        public Material unlitTemplate;
        public Material transparentTemplate;
        public string storageOverride;
        public RunData Run { get; private set; }
        public Battle Combat { get; private set; }
        public PrototypeStore Store { get; private set; }
        public GameScreen ScreenMode { get; private set; }
        public GamePanel Panel { get; private set; }
        public bool customSeed;
        public string customSeedText = "12345";
        [Min(0)] public float cameraKeyboardRotationSpeed = 90f;
        [Min(0)] public float cameraDragSensitivity = 0.25f;
        Camera gameCamera;
        Font interfaceFont;
        string message = "", error = "";
        float messageUntil, enemyWait;
        int requestedSeed;
        bool leaveApplication, processedBattle;
        Action queued;
        List<CardKind> rewards;
        PendingSettlement lastSettlement;
        List<ItemUnit> lastCarried;
        readonly List<Vector2Int> route = new List<Vector2Int>();
        Target pendingInteraction, hover, highlighted;
        struct Target { public TargetKind kind; public int id, x, y; }
        partial void BuildWorld(bool snap);
        partial void RefreshVisibility();
        partial void FollowWorld();
        partial void DrawInterface();
        partial void ReadInterfaceInput();
        partial void ReleaseInterfaceInput();

        void Start()
        {
            Application.targetFrameRate = 60;
            gameCamera = Camera.main;
            interfaceFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "Arial" }, 18);
            try
            {
                string folder = string.IsNullOrEmpty(storageOverride) ? Path.Combine(Application.persistentDataPath, "magic-cave-prototype") : storageOverride;
                Store = new PrototypeStore(folder);
                if (Store.Session.pending != null)
                {
                    Run = Store.Session.pending.run; ScreenMode = GameScreen.Settlement;
                    if (Store.Session.pending.confirmed) FinishSettlement();
                }
            }
            catch (Exception exception) { ReportError(exception); }
        }
        void OnGUI() { DrawInterface(); }
        void LateUpdate() { FollowWorld(); }
        void Queue(Action action) { if (queued == null) queued = action; }
        void ReportError(Exception exception)
        {
            error = exception.Message;
            Debug.LogWarning("[MagicCave] " + exception.Message);
        }
        void Notify(string text) { message = text; messageUntil = Time.unscaledTime + 4f; }
        void Update()
        {
            ReadInterfaceInput();
            if (queued != null)
            {
                var action = queued; queued = null;
                try { action(); error = ""; } catch (Exception exception) { ReportError(exception); }
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Run != null && ScreenMode != GameScreen.Result)
            {
                if (Panel == GamePanel.Inventory || Panel == GamePanel.Deck || Panel == GamePanel.Draw || Panel == GamePanel.Discard) Panel = GamePanel.None;
                else if (Panel == GamePanel.Pause) Panel = GamePanel.None;
                else if (Panel == GamePanel.None) { Panel = GamePanel.Pause; CancelRoute(); }
            }
            if (ScreenMode == GameScreen.Battle && Panel == GamePanel.None && Combat != null)
            {
                if (!Combat.playerTurn && Combat.outcome == BattleOutcome.None)
                {
                    enemyWait -= Time.deltaTime;
                    if (enemyWait <= 0) { Combat.ResolveEnemyTurn(); ProcessBattle(); }
                }
            }
            if (Run == null || ScreenMode != GameScreen.Explore) { cameraDragging = false; return; }
            if (keyboard != null && Panel == GamePanel.None)
            {
                if (keyboard.bKey.wasPressedThisFrame) { Panel = GamePanel.Inventory; CancelRoute(); }
                else if (keyboard.tabKey.wasPressedThisFrame) { Panel = GamePanel.Deck; CancelRoute(); }
            }
            if (Panel != GamePanel.None) { cameraDragging = false; return; }
            bool rotatingCamera = ReadCameraRotation(keyboard, Mouse.current);
            hover = PointerTarget(); highlighted = NearbyTarget();
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && highlighted.kind != TargetKind.None)
            {
                CancelRoute(); Interact(highlighted); return;
            }
            var mouse = Mouse.current;
            if (mouse != null && !rotatingCamera && !PointerOverHud())
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    CancelRoute();
                    if (hover.kind == TargetKind.Ground) Navigate(hover, false);
                    else if (hover.kind == TargetKind.Enemy || hover.kind == TargetKind.Loot || hover.kind == TargetKind.Door) Navigate(hover, true);
                }
                else if (mouse.rightButton.wasPressedThisFrame)
                {
                    CancelRoute();
                    if (hover.kind == TargetKind.Wall)
                    {
                        if (Run.Map.Border(hover.x, hover.y)) Notify("边界墙不能破坏");
                        else Navigate(hover, true);
                    }
                }
            }
            Vector2 input = Vector2.zero;
            if (keyboard != null)
            {
                input.x = ((keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1 : 0) - ((keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1 : 0);
                input.y = ((keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1 : 0) - ((keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1 : 0);
            }
            if (input.sqrMagnitude > 0)
            {
                var direction = Quaternion.Euler(0, Run.cameraYaw, 0) * new Vector3(input.x, 0, input.y).normalized;
                CancelRoute(); Move(new Vector2(direction.x, direction.z) * (3f * Mathf.Min(Time.deltaTime, 0.1f)));
            }
            else if (route.Count > 0)
            {
                var target = (Vector2)route[0]; var position = new Vector2(Run.x, Run.y);
                var next = Vector2.MoveTowards(position, target, 3f * Mathf.Min(Time.deltaTime, 0.1f));
                Move(next - position);
                if (Vector2.Distance(new Vector2(Run.x, Run.y), target) < 0.01f)
                {
                    Run.x = target.x; Run.y = target.y; route.RemoveAt(0);
                    if (route.Count == 0 && pendingInteraction.kind != TargetKind.None)
                    { var interaction = pendingInteraction; pendingInteraction = default; Interact(interaction); }
                }
            }
        }
        void Move(Vector2 delta)
        {
            int oldX = Run.CellX, oldY = Run.CellY;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.08f));
            delta /= steps;
            for (int i = 0; i < steps; i++)
            {
                if (Fits(Run.x + delta.x, Run.y)) Run.x += delta.x;
                if (Fits(Run.x, Run.y + delta.y)) Run.y += delta.y;
            }
            if (oldX != Run.CellX || oldY != Run.CellY) { MapGenerator.Reveal(Run); RefreshVisibility(); }
        }
        bool Fits(float x, float y)
        {
            return Run.Map.CanStand(x, y);
        }
        bool InRange(Target target) { return Math.Abs(Run.CellX - target.x) + Math.Abs(Run.CellY - target.y) <= 1; }
        void CancelRoute() { route.Clear(); pendingInteraction = default; }
        void Navigate(Target target, bool interact)
        {
            var path = MapGenerator.Path(Run.Map, new Vector2Int(Run.CellX, Run.CellY), new Vector2Int(target.x, target.y), interact);
            if (path == null) { Notify("无法到达：先探索并揭示通路"); return; }
            if (interact && InRange(target)) { Interact(target); return; }
            if (Vector2.Distance(new Vector2(Run.x, Run.y), new Vector2(Run.CellX, Run.CellY)) > 0.01f)
                route.Add(new Vector2Int(Run.CellX, Run.CellY));
            route.AddRange(path); pendingInteraction = interact ? target : default;
        }
        Target PointerTarget()
        {
            if (gameCamera == null || Mouse.current == null || PointerOverHud()) return default;
            Ray ray = gameCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide)) return default;
            var target = hit.collider.GetComponent<PrototypeTarget>();
            if (target == null || !Run.Map.Known(target.x, target.y)) return default;
            return new Target { kind = target.kind, id = target.id, x = target.x, y = target.y };
        }
        Target NearbyTarget()
        {
            if (hover.kind == TargetKind.Wall && InRange(hover)) return default;
            if ((hover.kind == TargetKind.Enemy || hover.kind == TargetKind.Loot || hover.kind == TargetKind.Door) && InRange(hover)) return hover;
            var options = new List<Target>();
            foreach (var enemy in Run.Map.enemies)
                if (!enemy.defeated && Run.Map.Known(enemy.x, enemy.y)) options.Add(new Target { kind = TargetKind.Enemy, id = enemy.id, x = enemy.x, y = enemy.y });
            foreach (var loot in Run.Map.loot)
                if (loot.units.Count > 0 && Run.Map.Known(loot.x, loot.y)) options.Add(new Target { kind = TargetKind.Loot, id = loot.id, x = loot.x, y = loot.y });
            foreach (var direction in MapGenerator.Directions.Concat(new[] { Vector2Int.zero }))
            {
                int x = Run.CellX + direction.x, y = Run.CellY + direction.y;
                if (Run.Map.Known(x, y) && (Run.Map.At(x, y) == Tile.Advance || Run.Map.At(x, y) == Tile.Return))
                    options.Add(new Target { kind = TargetKind.Door, x = x, y = y });
            }
            return options.Where(InRange).OrderBy(t => (new Vector2(t.x - Run.x, t.y - Run.y)).sqrMagnitude).FirstOrDefault();
        }
        bool PointerOverHud()
        {
            if (Mouse.current == null) return false;
            Vector2 point = GuiMouse;
            return point.y < 94 || point.y > 654 || (!string.IsNullOrEmpty(error) && point.y > 604);
        }
        Vector2 GuiMouse
        {
            get
            {
                float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
                var mouse = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
                return new Vector2((mouse.x - (Screen.width - 1280 * scale) / 2) / scale,
                    (Screen.height - mouse.y - (Screen.height - 720 * scale) / 2) / scale);
            }
        }
        void Interact(Target target)
        {
            if (Run == null || ScreenMode != GameScreen.Explore || !InRange(target)) return;
            CancelRoute();
            switch (target.kind)
            {
                case TargetKind.Wall:
                    if (Run.Map.At(target.x, target.y) != Tile.Wall || Run.Map.Border(target.x, target.y)) return;
                    Run.Map.Set(target.x, target.y, Tile.Ground); MapGenerator.Reveal(Run); BuildWorld(false); Notify("墙体已破坏"); break;
                case TargetKind.Loot:
                    var loot = Run.Map.loot.FirstOrDefault(l => l.id == target.id);
                    if (loot == null || loot.units.Count == 0) return;
                    ItemKind kind = loot.units[0].kind; int count = Inventory.Pickup(Run, loot);
                    Notify(count > 0 ? "拾取 " + Rules.ItemNames[(int)kind] + " ×" + count : "背包已满，物品留在地面"); BuildWorld(false); break;
                case TargetKind.Enemy:
                    var enemy = Run.Map.enemies.FirstOrDefault(e => e.id == target.id);
                    if (enemy != null && !enemy.defeated) BeginBattle(enemy); break;
                case TargetKind.Door:
                    if (Run.Map.At(target.x, target.y) == Tile.Return) Panel = GamePanel.Return;
                    else if (Run.Map.At(target.x, target.y) == Tile.Advance && Run.mapIndex < 4)
                    {
                        Run.mapIndex++; Run.x = Run.Map.spawnX; Run.y = Run.Map.spawnY;
                        MapGenerator.Reveal(Run); BuildWorld(true); Notify("进入地图 " + (Run.mapIndex + 1));
                    }
                    break;
            }
        }

        public void StartNewRun(int seed)
        {
            Store.DiscardCheckpoint(); Run = null; ScreenMode = GameScreen.Home;
            Run = MapGenerator.NewRun(seed); Combat = null; Panel = GamePanel.None;
            cameraDragging = false;
            ScreenMode = GameScreen.Explore; CancelRoute(); BuildWorld(true);
        }
        void RequestNewRun()
        {
            if (customSeed && !int.TryParse(customSeedText, out requestedSeed)) { Notify("请输入32位整数种子，例如12345或-100"); return; }
            if (!customSeed) requestedSeed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
            if (Store.Session.checkpoint != null) Panel = GamePanel.NewRun;
            else StartNewRun(requestedSeed);
        }
        public void ContinueRun()
        {
            cameraDragging = false;
            Panel = GamePanel.None;
            if (Store.Session.pending != null) { Run = Store.Session.pending.run; ScreenMode = GameScreen.Settlement; return; }
            Run = Store.Continue(); ScreenMode = GameScreen.Explore; CancelRoute(); BuildWorld(true);
        }
        void SaveAndLeave()
        {
            Store.SaveCheckpoint(Run); GoHome(); Notify("本局已保存");
        }
        public void BeginBattle(EnemyData enemy)
        {
            CancelRoute(); Combat = new Battle(Run, enemy); processedBattle = false;
            ScreenMode = GameScreen.Battle; Panel = GamePanel.None;
        }
        void PlayCard(int index)
        {
            if (Combat.Play(index)) ProcessBattle();
        }
        void EndTurn() { Combat.EndPlayerTurn(); enemyWait = 0.35f; }
        void ProcessBattle()
        {
            if (Combat.outcome == BattleOutcome.None || processedBattle) return;
            processedBattle = true;
            if (Combat.outcome == BattleOutcome.Failure)
            {
                Run.totalExperience += 5; BeginSettlement(true); return;
            }
            Growth.FinishVictory(Run, Combat.enemy); Combat = null; NextReward();
        }
        void NextReward()
        {
            rewards = Growth.NextUpgrade(Run);
            if (rewards != null) ScreenMode = GameScreen.Rewards;
            else { ScreenMode = GameScreen.Explore; BuildWorld(true); Notify("战斗胜利，已结算经验"); }
        }
        void SelectReward(int index)
        {
            if (index >= 0 && index < rewards.Count) Run.deck.Add(rewards[index]);
            NextReward();
        }
        void BeginSettlement(bool failure)
        {
            Store.BeginSettlement(Run, failure); Run = Store.Session.pending.run;
            ScreenMode = GameScreen.Settlement; Panel = GamePanel.None; CancelRoute();
        }
        void FinishSettlement()
        {
            lastSettlement = Rules.Copy(Store.Session.pending);
            lastCarried = Store.CommitSettlement(); ScreenMode = GameScreen.Result; Panel = GamePanel.None; Combat = null;
        }
        void CancelSettlement()
        {
            Run = Store.CancelSettlement(); ScreenMode = GameScreen.Explore; Panel = GamePanel.None; BuildWorld(true);
        }
        void GoHome() { Run = null; Combat = null; ScreenMode = GameScreen.Home; Panel = GamePanel.None; CancelRoute(); }
        void Leave()
        {
            if (!leaveApplication) GoHome();
            else
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
    }
}
