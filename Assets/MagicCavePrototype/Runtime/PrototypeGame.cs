using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MagicCave.Prototype
{
    public enum GameScreen { Home, Explore, Battle, Rewards, Settlement, Result, Warehouse }
    public enum GamePanel { None, Inventory, Deck, Draw, Discard, Pause, Return, NewRun, Leave, Character }

    public sealed partial class PrototypeGame : MonoBehaviour
    {
        public string storageOverride;
        public RunData Run { get; private set; }
        public Battle Combat { get; private set; }
        public PrototypeStore Store { get; private set; }
        public GameScreen ScreenMode { get; private set; }
        public GamePanel Panel { get; private set; }
        public bool customSeed;
        public string customSeedText = "12345";
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

        void OnEnable() { InputSystem.onEvent += TrackMovementKeys; }
        void OnDisable() { InputSystem.onEvent -= TrackMovementKeys; heldMovementKeys.Clear(); gridDirection = Vector2Int.zero; }
        void TrackMovementKeys(InputEventPtr inputEvent, InputDevice device)
        {
            var keyboard = device as Keyboard;
            if (keyboard == null || (!inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>())) return;
            foreach (var key in movementKeys)
            {
                if (!keyboard[key].ReadValueFromEvent(inputEvent, out float value)) continue;
                if (value < .5f) heldMovementKeys.Remove(key);
                else if (!heldMovementKeys.Contains(key)) heldMovementKeys.Add(key);
            }
        }
        void Start()
        {
            var arguments = Environment.GetCommandLineArgs();
            int storageOption = Array.IndexOf(arguments, "-prototypeStorage");
            if (storageOption >= 0 && storageOption + 1 < arguments.Length) storageOverride = Path.GetFullPath(arguments[storageOption + 1]);
            Application.targetFrameRate = 60;
            gameCamera = Camera.main;
            if (artSet == null) artSet = Resources.Load<PrototypeArtSet>("PrototypeArt");
            interfaceFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Noto Sans SC", "Arial" }, 18);
            try
            {
                string folder = string.IsNullOrEmpty(storageOverride) ? Path.Combine(Application.persistentDataPath, "magic-cave-prototype") : storageOverride;
                Store = new PrototypeStore(folder);
                Debug.Log("[MagicCave] Character prototype ready; rules v" + PrototypeAttributeSet.LoadRules().version + ".");
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
            ReadGridDirection();
            ReadInterfaceInput();
            UpdateTown();
            if (queued != null)
            {
                var action = queued; queued = null;
                try { action(); error = ""; } catch (Exception exception) { ReportError(exception); }
            }
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Panel == GamePanel.NewRun)
            { Panel = GamePanel.None; departurePreview = null; return; }
            if (!townEscapeHandled && keyboard != null && keyboard.escapeKey.wasPressedThisFrame && Run != null && ScreenMode != GameScreen.Result)
            {
                if (Panel == GamePanel.Inventory || Panel == GamePanel.Deck || Panel == GamePanel.Draw || Panel == GamePanel.Discard || Panel == GamePanel.Character) Panel = GamePanel.None;
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
            if (Run == null || ScreenMode != GameScreen.Explore) return;
            if (keyboard != null && Panel == GamePanel.None)
            {
                if (keyboard.bKey.wasPressedThisFrame) { Panel = GamePanel.Inventory; CancelRoute(); }
                else if (keyboard.tabKey.wasPressedThisFrame) { Panel = GamePanel.Deck; CancelRoute(); }
                else if (keyboard.cKey.wasPressedThisFrame) { Panel = GamePanel.Character; CancelRoute(); }
            }
            if (Panel != GamePanel.None) return;
            hover = PointerTarget(); highlighted = NearbyTarget();
            if (keyboard != null && keyboard.eKey.wasPressedThisFrame && highlighted.kind != TargetKind.None)
            {
                CancelRoute(); Interact(highlighted); return;
            }
            var mouse = Mouse.current;
            if (mouse != null && !PointerOverHud())
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
            if (gridDirection != Vector2Int.zero)
            {
                CancelRoute();
                if (Time.time >= nextExploreStep)
                {
                    MoveGrid(new Vector2Int(Run.CellX, Run.CellY) + gridDirection);
                    nextExploreStep = Time.time + 1f / 3f;
                }
            }
            else if (route.Count > 0 && Time.time >= nextExploreStep)
            {
                var target = route[0];
                if (MoveGrid(target))
                {
                    route.RemoveAt(0); nextExploreStep = Time.time + 1f / 3f;
                    if (route.Count == 0 && pendingInteraction.kind != TargetKind.None)
                    { var interaction = pendingInteraction; pendingInteraction = default; Interact(interaction); }
                }
                else CancelRoute();
            }
        }
        // Logical and rendered positions both remain on cell centers, including after key release.
        float nextExploreStep, nextTownStep;
        Vector2Int gridDirection;
        readonly List<Key> heldMovementKeys = new List<Key>();
        static readonly Key[] movementKeys = { Key.W, Key.UpArrow, Key.S, Key.DownArrow, Key.A, Key.LeftArrow, Key.D, Key.RightArrow };
        void ReadGridDirection()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) { heldMovementKeys.Clear(); gridDirection = Vector2Int.zero; return; }
            heldMovementKeys.RemoveAll(key => !keyboard[key].isPressed);
            // Input events preserve press order across frames and within a single frame.
            foreach (var key in movementKeys.Where(key => keyboard[key].isPressed && !heldMovementKeys.Contains(key))) heldMovementKeys.Add(key);
            var previous = gridDirection;
            var first = heldMovementKeys.Count == 0 ? Key.None : heldMovementKeys[0];
            gridDirection = first == Key.W || first == Key.UpArrow ? Vector2Int.up :
                first == Key.S || first == Key.DownArrow ? Vector2Int.down :
                first == Key.A || first == Key.LeftArrow ? Vector2Int.left :
                first == Key.D || first == Key.RightArrow ? Vector2Int.right : Vector2Int.zero;
            if (previous != gridDirection && previous == Vector2Int.zero) { nextExploreStep = 0; nextTownStep = 0; }
        }
        bool MoveGrid(Vector2Int target)
        {
            var origin = new Vector2Int(Run.CellX, Run.CellY);
            if (Math.Abs(target.x - origin.x) + Math.Abs(target.y - origin.y) > 1 || !Run.Map.CanTraverse(target.x, target.y)) return false;
            Run.x = target.x; Run.y = target.y;
            if (origin != target) { facing = target - origin; MapGenerator.Reveal(Run); RefreshVisibility(); }
            return true;
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
            var world = gameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            float half = (Run.Map.size - 1) * 0.5f;
            int x = Mathf.FloorToInt(world.x + half + 0.5f), y = Mathf.FloorToInt(world.y + half + 0.5f);
            if (!Run.Map.Known(x, y)) return default;
            foreach (var enemy in Run.Map.enemies)
                if (!enemy.defeated && enemy.x == x && enemy.y == y) return new Target { kind = TargetKind.Enemy, x = x, y = y, id = enemy.id };
            foreach (var loot in Run.Map.loot)
                if (loot.HasContents && loot.x == x && loot.y == y) return new Target { kind = TargetKind.Loot, x = x, y = y, id = loot.id };
            Tile tile = Run.Map.At(x, y);
            return new Target { kind = tile == Tile.Wall ? TargetKind.Wall : tile == Tile.Ground ? TargetKind.Ground : TargetKind.Door, x = x, y = y };
        }
        Target NearbyTarget()
        {
            if (hover.kind == TargetKind.Wall && InRange(hover)) return default;
            if ((hover.kind == TargetKind.Enemy || hover.kind == TargetKind.Loot || hover.kind == TargetKind.Door) && InRange(hover)) return hover;
            var options = new List<Target>();
            foreach (var enemy in Run.Map.enemies)
                if (!enemy.defeated && Run.Map.Known(enemy.x, enemy.y)) options.Add(new Target { kind = TargetKind.Enemy, id = enemy.id, x = enemy.x, y = enemy.y });
            foreach (var loot in Run.Map.loot)
                if (loot.HasContents && Run.Map.Known(loot.x, loot.y)) options.Add(new Target { kind = TargetKind.Loot, id = loot.id, x = loot.x, y = loot.y });
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
                    if (loot == null || !loot.HasContents) return;
                    ContainerLoot.Open(Run, loot);
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
            => StartNewRun(seed, AttributeKind.None);

        public void StartNewRun(int seed, AttributeKind secondary)
        {
            var next = MapGenerator.NewRun(seed, AttributeKind.None);
            TownRules.Apply(Store.Warehouse.town, next); PrototypeStore.ValidateRun(next);
            Store.DiscardCheckpoint(); Run = next; Combat = null; Panel = GamePanel.None;
            facing = Vector2.down; nextExploreStep = 0; attributeDetails = false;
            equipmentPreviewSlot = selectedBagSlot = dropSlot = -1; ClearItemDrag();
            message = ""; messageUntil = 0; hover = default; highlighted = default;
            ScreenMode = GameScreen.Explore; CancelRoute(); BuildWorld(true);
        }
        void RequestNewRun()
        {
            if (customSeed && !int.TryParse(customSeedText, out requestedSeed)) { Notify("请输入32位整数种子，例如12345或-100"); return; }
            if (!customSeed) requestedSeed = BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0);
            departurePreview = null; Panel = GamePanel.NewRun;
        }
        public void ContinueRun()
        {
            Panel = GamePanel.None;
            if (Store.Session.pending != null) { Run = Store.Session.pending.run; ScreenMode = GameScreen.Settlement; return; }
            Run = Store.Continue(); Run.x = Run.CellX; Run.y = Run.CellY; nextExploreStep = 0;
            equipmentPreviewSlot = selectedBagSlot = dropSlot = -1; ClearItemDrag(); ScreenMode = GameScreen.Explore; CancelRoute(); BuildWorld(true);
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
        public string EquipInventory(int slot, EquipmentSlot? target = null)
        {
            if (ScreenMode != GameScreen.Explore || Run == null) return "只能在探索时更换装备";
            return Inventory.Equip(Run, slot, target);
        }
        public string UnequipSlot(EquipmentSlot slot)
        {
            if (ScreenMode != GameScreen.Explore || Run == null) return "只能在探索时更换装备";
            return Inventory.Unequip(Run, slot);
        }
        public bool UseInventory(int slot)
        {
            return ScreenMode == GameScreen.Explore && Run != null && Inventory.Use(Run, slot);
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
        void GoHome() { Run = null; Combat = null; ScreenMode = GameScreen.Home; Panel = GamePanel.None; ResetTownLocation(); CancelRoute(); }
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
