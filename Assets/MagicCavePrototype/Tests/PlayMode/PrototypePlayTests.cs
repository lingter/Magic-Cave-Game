using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[DefaultExecutionOrder(-1000)]
public class PrototypeInputDriver : MonoBehaviour
{
    [NonSerialized] public Mouse mouse;
    [NonSerialized] public Keyboard keyboard;
    [NonSerialized] public MouseState mouseState;
    [NonSerialized] public KeyboardState keyState;
    void Update()
    {
        mouse.MakeCurrent(); keyboard.MakeCurrent();
        InputState.Change(mouse, mouseState); InputState.Change(keyboard, keyState);
    }
}

public class PrototypePlayTests
{
    [UnityTest]
    public IEnumerator QuickBarUsesMouseAndNumberKeysAndAppearsInBag()
    {
        game.StartNewRun(12345); var run = game.Run;
        var loot = new GroundLoot { loose = true };
        foreach (var kind in new[] { ItemKind.Potion, ItemKind.Potion, ItemKind.Torch, ItemKind.AssaultPotion })
            loot.units.Add(new ItemUnit { id = run.nextItemId++, kind = kind });
        Inventory.Pickup(run, loot); run.health = run.MaxHealth - 40;
        yield return null; yield return null;
        var position = new Vector2(run.x, run.y);
        yield return Click(650, 620);
        Assert.That(QuickBar.Count(run, ItemKind.Potion), Is.EqualTo(1));
        Assert.That(new Vector2(run.x, run.y), Is.EqualTo(position), "Quickbar click must not navigate the map.");
        driver.keyState = new KeyboardState(Key.Digit1); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(run.health, Is.EqualTo(run.MaxHealth));
        Assert.That(run.quickSlots[0], Is.EqualTo(-1));
        Inventory.Pickup(run, new GroundLoot { loose = true, units = new System.Collections.Generic.List<ItemUnit> {
            new ItemUnit { id = run.nextItemId++, kind = ItemKind.Potion } } });
        string captures = Path.GetFullPath("Captures/QuickBar"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "exploration.png")); yield return null; yield return null;
        yield return Click(1010, 687);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "inventory.png")); yield return null; yield return null;
        yield return Click(885, 680);
        Assert.That(TorchLight.IsLit(run), Is.True);
        Assert.That(QuickBar.Count(run, ItemKind.Torch), Is.Zero);
        Assert.That(QuickBar.IsBurningTorch(run, 1), Is.True);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "burning-torch.png")); yield return null; yield return null;
        yield return Click(885, 680);
        Assert.That(TorchLight.IsLit(run), Is.False);
        Assert.That(run.quickSlots[1], Is.EqualTo((int)ItemKind.Torch));
        Assert.That(run.torchStepsRemaining, Is.EqualTo(20));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "paused-torch.png")); yield return null; yield return null;
        yield return Click(885, 680);
        Assert.That(TorchLight.IsLit(run), Is.True, "Clicking the same shortcut relights without spare stock.");
        Assert.That(QuickBar.Count(run, ItemKind.Torch), Is.Zero);
        Assert.That(run.torchStepsRemaining, Is.EqualTo(20));
        driver.keyState = new KeyboardState(Key.Escape); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        driver.keyState = new KeyboardState(Key.Digit2); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(TorchLight.IsLit(run), Is.False);
        Assert.That(game.UseQuickSlot(1), Is.True);
        game.BeginBattle(run.Map.enemies.First(e => !e.defeated));
        Assert.That(game.UseQuickSlot(2), Is.False, "Exploration shortcut must not bypass combat consumable rules.");
    }
    PrototypeGame game;
    PrototypeInputDriver driver;
    string folder;
    InputSettings.BackgroundBehavior previousBackgroundBehavior;
    InputSettings.EditorInputBehaviorInPlayMode previousEditorBehavior;
    [UnitySetUp]
    public IEnumerator Setup()
    {
        previousBackgroundBehavior = InputSystem.settings.backgroundBehavior;
        previousEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Other fixtures load their own authored scenes; isolate this fixture's cameras and input.
        var previousScene = SceneManager.GetActiveScene();
        var testScene = SceneManager.CreateScene("Prototype Test " + Guid.NewGuid().ToString("N"));
        SceneManager.SetActiveScene(testScene);
        // Unity 6 keeps the coroutine runner in its initial scene. Preserve it before
        // unloading that scene, otherwise the fixture stops during this setup yield.
        if (previousScene.IsValid() && previousScene.isLoaded)
            foreach (var root in previousScene.GetRootGameObjects())
                if (root.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component != null && component.GetType().Name == "PlaymodeTestsController"))
                    SceneManager.MoveGameObjectToScene(root, testScene);
        if (previousScene.IsValid() && previousScene.isLoaded) yield return SceneManager.UnloadSceneAsync(previousScene);
        folder = Path.Combine(Application.temporaryCachePath, "MagicCavePlayTests", Guid.NewGuid().ToString("N"));
        var camera = new GameObject("Test Camera").AddComponent<Camera>(); camera.tag = "MainCamera";
        game = new GameObject("Prototype Under Test").AddComponent<PrototypeGame>(); game.storageOverride = folder;
        driver = new GameObject("Test Input").AddComponent<PrototypeInputDriver>();
        driver.mouse = InputSystem.AddDevice<Mouse>(); driver.keyboard = InputSystem.AddDevice<Keyboard>();
        yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator SettingsPanelSavesCancelsAndAppliesOnlyToNewRuns()
    {
        string captures = Path.GetFullPath("Captures/Settings"); Directory.CreateDirectory(captures);
        yield return Click(1180, 688); Assert.That(game.Panel, Is.EqualTo(GamePanel.Settings));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "features.png")); yield return null; yield return null;
        yield return Click(800, 230); // Torch off in draft.
        yield return Click(1020, 650); Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        Assert.That(game.Store.Settings.torch, Is.False);
        game.OpenSettings(); yield return null;
        yield return Click(500, 155); // Card page.
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "cards.png")); yield return null; yield return null;
        yield return Click(475, 240); // Strike count +1.
        yield return Click(400, 240); // Focus numeric input.
        driver.keyState = new KeyboardState(Key.LeftCtrl, Key.A); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null; yield return null;
        InputSystem.QueueTextEvent(driver.keyboard, '1'); InputSystem.QueueTextEvent(driver.keyboard, '2'); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.Enter); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null; yield return null;
        yield return Click(1020, 650); Assert.That(game.Store.Settings.cardCounts[0], Is.EqualTo(12));
        game.OpenSettings(); yield return null;
        yield return Click(770, 155);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "monsters.png")); yield return null; yield return null;
        yield return Click(1040, 155);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "drops.png")); yield return null; yield return null;
        yield return Click(578, 251); // Change drop weight then cancel.
        yield return Click(770, 650); Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        Assert.That(game.Store.Settings.drops.firstEquipment[0].weight, Is.EqualTo(1));
        game.StartNewRun(12345); Assert.That(game.Run.deck.Count, Is.EqualTo(17)); Assert.That(PrototypeSettings.For(game.Run).torch, Is.False);
        PrototypeStore.ValidateRun(game.Run);
    }

    [UnityTest]
    public IEnumerator LargeCustomHandsArePagedAndDisabledGrowthDoesNotAwardFailureExperience()
    {
        var settings = new PrototypeSettings { growth = false };
        settings.cardCounts[0] = 30; game.Store.SaveSettings(settings); game.StartNewRun(12345);
        game.BeginBattle(game.Run.Map.enemies[0]); game.Combat.hand.Clear();
        game.Combat.hand.AddRange(Enumerable.Repeat(CardKind.Defend, 14)); yield return null; yield return null;
        yield return Click(790, 625);
        Assert.That((int)typeof(PrototypeGame).GetField("battleHandPage", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(game), Is.EqualTo(1));
        int handCount = game.Combat.hand.Count; yield return Click(150, 500);
        Assert.That(game.Combat.hand.Count, Is.EqualTo(handCount - 1));
        string captures = Path.GetFullPath("Captures/Settings"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "large-hand.png")); yield return null; yield return null;
        game.Run.health = 0; game.Combat.outcome = BattleOutcome.Failure;
        typeof(PrototypeGame).GetMethod("ProcessBattle", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(game, null);
        Assert.That(game.Run.totalExperience, Is.Zero);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        InputSystem.RemoveDevice(driver.mouse); InputSystem.RemoveDevice(driver.keyboard);
        InputSystem.settings.backgroundBehavior = previousBackgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorBehavior;
        UnityEngine.Object.Destroy(driver.gameObject); UnityEngine.Object.Destroy(game.gameObject);
        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>()) UnityEngine.Object.Destroy(camera.gameObject);
        yield return null;
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
    IEnumerator Click(float x, float y)
    {
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        var position = new Vector2((Screen.width - 1280 * scale) / 2 + x * scale, Screen.height - ((Screen.height - 720 * scale) / 2 + y * scale));
        driver.mouseState = new MouseState { position = position, buttons = 1 };
        yield return null; yield return null;
        driver.mouseState = new MouseState { position = position };
        yield return null; yield return null; yield return null;
    }
    void InteractWithLoot(GroundLoot loot)
    {
        // Use the production interaction without requiring a focused Game view in batch mode.
        var type = typeof(PrototypeGame).GetNestedType("Target", BindingFlags.NonPublic);
        var target = Activator.CreateInstance(type);
        type.GetField("kind").SetValue(target, TargetKind.Loot);
        type.GetField("id").SetValue(target, loot.id);
        type.GetField("x").SetValue(target, loot.x);
        type.GetField("y").SetValue(target, loot.y);
        typeof(PrototypeGame).GetMethod("Interact", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new[] { target });
    }

    [UnityTest]
    public IEnumerator TownWalkingTalentPurchaseWarehouseAndDepartureWork()
    {
        string captures = Path.GetFullPath("Captures/WorldMap"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "world-map.png")); yield return null; yield return null;
        // Enter the tent before the mentor, chairman and warehouse become visible.
        yield return Click(184, 233);
        float deadline = Time.time + 8;
        while (game.TownWalking && Time.time < deadline) yield return null;
        Assert.That(game.InGuild, Is.True);
        Assert.That(game.TownPage, Is.EqualTo(TownPage.None));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "guild-interior.png")); yield return null; yield return null;
        yield return Click(584, 281);
        deadline = Time.time + 8;
        while (game.TownWalking && Time.time < deadline) yield return null;
        Assert.That(game.TownPage, Is.EqualTo(TownPage.Mentor));
        Assert.That(Vector2.Distance(game.TownPosition, new Vector2(17, 5)), Is.LessThan(.02f));
        yield return Click(1060, 478);
        Assert.That(game.Store.Warehouse.town.talents[1], Is.EqualTo(1));
        Assert.That(game.Store.Warehouse.town.gold, Is.EqualTo(120));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "knight-talents.png")); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.Escape); yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.TownPage, Is.EqualTo(TownPage.None));
        // WASD cancels a previous click route.
        game.VisitTown(2); driver.keyState = new KeyboardState(Key.A); yield return null; yield return null;
        Assert.That(game.TownWalking, Is.False); driver.keyState = new KeyboardState(); yield return null;
        game.VisitTown(3); deadline = Time.time + 8;
        while (game.TownWalking && Time.time < deadline) yield return null;
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Warehouse));
        yield return Click(350, 574);
        Assert.That(TownRules.Capacity(game.Store.Warehouse), Is.EqualTo(150));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "warehouse.png")); yield return null; yield return null;
        yield return Click(950, 575); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Home));
        game.VisitTown(2); deadline = Time.time + 8;
        while (game.TownWalking && Time.time < deadline) yield return null;
        Assert.That(game.TownPage, Is.EqualTo(TownPage.Chairman));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "guild-upgrade.png")); yield return null; yield return null;
        game.CloseTownPage(); game.StartNewRun(12345); yield return null;
        Assert.That(game.Run.character.talentStrength, Is.EqualTo(1));
        Assert.That(game.Run.MaxHealth, Is.EqualTo(60));
        typeof(PrototypeGame).GetMethod("BeginSettlement", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { false });
        yield return Click(560, 610); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Warehouse));
        driver.keyState = new KeyboardState(Key.Escape); yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Settlement));
        Assert.That(game.Panel, Is.EqualTo(GamePanel.None), "Esc from settlement warehouse must not also open pause.");
    }

    [UnityTest]
    public IEnumerator SeedMenuButtonsAndCardBattleWorkWithNewInputSystem()
    {
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Home));
        game.VisitTown(4);
        float deadline = Time.time + 8;
        while (game.TownWalking && Time.time < deadline) yield return null;
        Assert.That(game.TownPage, Is.EqualTo(TownPage.Expedition));
        yield return Click(1040, 420); Assert.That(game.customSeed, Is.True, "Seed toggle must respond to Input System mouse release");
        yield return Click(1190, 470); Assert.That(game.customSeedText, Is.Empty);
        var character = typeof(PrototypeGame).GetMethod("SeedCharacter", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (char value in "-2026") character.Invoke(game, new object[] { value });
        yield return Click(1050, 292); Assert.That(game.Panel, Is.EqualTo(GamePanel.NewRun));
        yield return Click(840, 612);
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Explore)); Assert.That(game.Run.seed, Is.EqualTo(-2026));
        yield return Click(1010, 687); Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        yield return Click(1090, 136); Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        game.BeginBattle(game.Run.Map.enemies[0]); yield return null;
        var card = game.Combat.hand.FindIndex(c => Rules.Costs[(int)c] <= game.Combat.energy);
        float width = Mathf.Min(170, 1120f / game.Combat.hand.Count - 10);
        float start = (1280 - game.Combat.hand.Count * (width + 10) + 10) / 2;
        int before = game.Combat.energy;
        yield return Click(start + card * (width + 10) + width / 2, 500);
        Assert.That(game.Combat.energy, Is.LessThan(before));
        yield return Click(1100, 669); yield return new WaitForSeconds(0.5f);
        Assert.That(game.Combat.playerTurn, Is.True); Assert.That(game.Combat.enemyTurn, Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator DepartureWithoutSecondaryEquipmentAndDynamicCardTextWork()
    {
        string captures = Path.GetFullPath("Captures/InventoryStars"); Directory.CreateDirectory(captures);
        typeof(PrototypeGame).GetMethod("RequestNewRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.NewRun));
        Assert.That(game.Run, Is.Null);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "departure-wisdom.png")); yield return null; yield return null;
        yield return Click(830, 502);
        Assert.That(game.Run, Is.Null, "Selecting terrain must not start the run.");
        yield return Click(830, 612);
        Assert.That(game.Run.generationMode, Is.EqualTo(MapGenerationMode.Excavated));
        Assert.That(game.Run.maps.Count, Is.EqualTo(5));
        Assert.That(game.Run.maps.Take(4).All(m => m.dugTerrain), Is.True);
        Assert.That(game.Run.character.secondary, Is.EqualTo(AttributeKind.None));
        Assert.That(game.Run.UsesAttributes, Is.True);
        var run = game.Run;
        run.inventory[0].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.InscribedSword, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ });
        run.inventory[1].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.WisdomEssence, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ });
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        yield return Click(630, 225);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "equipment-comparison.png")); yield return null; yield return null;
        yield return Click(1090, 618);
        Assert.That(run.character.equipment.Single().kind, Is.EqualTo(ItemKind.InscribedSword));
        yield return Click(1090, 136);
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        yield return Click(755, 225); yield return Click(650, 604); Assert.That(CharacterAttributes.Calculate(run).wisdom, Is.EqualTo(6));
        yield return Click(1090, 136);
        driver.keyState = new KeyboardState(Key.C); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Character));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "character-wisdom.png")); yield return null; yield return null;
        yield return Click(1090, 136); Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        driver.keyState = new KeyboardState(Key.Tab); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Deck)); yield return Click(880, 622);
        game.BeginBattle(new EnemyData { kind = EnemyKind.Boss }); yield return null;
        game.Combat.hand.Clear(); game.Combat.hand.Add(CardKind.Heavy);
        double damage = CombatNumbers.Damage(run, CardKind.Heavy);
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成13点物理伤害"));
        Assert.That(damage, Is.EqualTo(13));
        game.Combat.spirit = 2;
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成15点物理伤害"));
        game.Combat.enemyBlock = 5;
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成15点物理伤害"));
        Assert.That(game.CardDescription(run, CardKind.Defend, true), Is.EqualTo("获得10点格挡"));
        var defense = run.attributeRules.DefenseCard(CardKind.Defend);
        defense.source = DefenseScalingSource.ActualAttack; defense.multiplier = 10000;
        Assert.That(game.CardDescription(run, CardKind.Defend, true), Is.EqualTo("获得9点格挡"));
        defense.multiplier = 0;
        Assert.That(game.CardDescription(run, CardKind.Defend, true), Is.EqualTo("获得0点格挡"));
        defense.multiplier = 10000; defense.source = DefenseScalingSource.MainAttribute;
        Assert.That(game.EquipInventory(0), Does.Contain("探索")); Assert.That(game.UseInventory(1), Is.False);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "battle-dynamic-damage.png")); yield return null; yield return null;
        game.Store.SaveCheckpoint(run); game.ContinueRun(); yield return null;
        Assert.That(game.Run.character.secondary, Is.EqualTo(AttributeKind.None));
        Assert.That(game.Run.character.equipment.Single().kind, Is.EqualTo(ItemKind.InscribedSword));
    }

    [UnityTest]
    public IEnumerator KeyboardMoveRespectsCollisionAndPause()
    {
        game.StartNewRun(12345); yield return null;
        int x = game.Run.CellX, y = game.Run.CellY;
        var direction = MapGenerator.Directions.First(d => game.Run.Map.CanTraverse(x + d.x, y + d.y));
        Key key = direction.x > 0 ? Key.D : direction.x < 0 ? Key.A : direction.y > 0 ? Key.W : Key.S;
        Vector2 before = new Vector2(game.Run.x, game.Run.y);
        driver.keyState = new KeyboardState(key); yield return new WaitForSeconds(0.15f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(Vector2.Distance(before, new Vector2(game.Run.x, game.Run.y)), Is.GreaterThan(0.1f));
        driver.keyState = new KeyboardState(Key.Escape); yield return null; yield return null;
        driver.keyState = new KeyboardState(key); before = new Vector2(game.Run.x, game.Run.y);
        yield return new WaitForSeconds(0.15f); Assert.That(game.Panel, Is.EqualTo(GamePanel.Pause));
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(before));
    }
    [UnityTest]
    public IEnumerator WallStopsMovementRightClickBreaksAndLeftClickWalksThrough()
    {
        game.StartNewRun(12345); yield return null; yield return null;
        var map = game.Run.Map;
        var walls = Enumerable.Range(0, map.cells.Length).Select(i => new Vector2Int(i % map.size, i / map.size))
            .Where(p => !map.Border(p.x, p.y) && map.At(p.x, p.y) == Tile.Wall)
            .SelectMany(wall => MapGenerator.Directions.Select(d => new { wall, approach = wall + d }))
            .Where(pair => map.CanTraverse(pair.approach.x, pair.approach.y))
            .OrderBy(pair => MapGenerator.Manhattan(pair.approach, new Vector2Int(game.Run.CellX, game.Run.CellY))).ToArray();
        var selected = walls.First(); int wallX = selected.wall.x, wallY = selected.wall.y;
        game.Run.x = selected.approach.x; game.Run.y = selected.approach.y; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null; Physics2D.SyncTransforms();
        Assert.That(game.Run.Map.At(wallX, wallY), Is.EqualTo(Tile.Wall));
        var direction = selected.wall - selected.approach;
        Key key = direction.x > 0 ? Key.D : direction.x < 0 ? Key.A : direction.y > 0 ? Key.W : Key.S;
        driver.keyState = new KeyboardState(key); yield return new WaitForSeconds(0.3f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(Vector2.Distance(new Vector2(game.Run.x, game.Run.y), selected.wall), Is.GreaterThanOrEqualTo(.89f), "The 0.8-unit player must stop before its artwork reaches the wall");
        float half = (game.Run.Map.size - 1) / 2f;
        var point = Camera.main.WorldToScreenPoint(new Vector3(wallX - half, wallY - half, 0));
        var hit = Physics2D.OverlapPoint(new Vector2(wallX - half, wallY - half));
        Assert.That(hit, Is.Not.Null);
        var tag = hit.GetComponent<PrototypeTarget>();
        Assert.That(tag, Is.Not.Null); Assert.That(tag.kind, Is.EqualTo(TargetKind.Wall));
        Assert.That(tag.x, Is.EqualTo(wallX)); Assert.That(tag.y, Is.EqualTo(wallY));
        driver.mouseState = new MouseState { position = point, buttons = 2 };
        yield return null; yield return null;
        driver.mouseState = new MouseState { position = point }; yield return null;
        Assert.That(game.Run.Map.At(wallX, wallY), Is.EqualTo(Tile.Ground));
        point = Camera.main.WorldToScreenPoint(new Vector3(wallX - half, wallY - half, 0));
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        yield return Click((point.x - (Screen.width - 1280 * scale) / 2) / scale, (Screen.height - point.y - (Screen.height - 720 * scale) / 2) / scale);
        yield return new WaitForSeconds(0.5f);
        Assert.That(game.Run.CellX, Is.EqualTo(wallX)); Assert.That(game.Run.CellY, Is.EqualTo(wallY));
    }
    [UnityTest]
    public IEnumerator WallsBesideLegacyDoorsStayUnmarkedAndRespectFog()
    {
        game.StartNewRun(12345); yield return null;
        var map = game.Run.Map;
        int doorIndex = Array.FindIndex(map.cells, tile => tile == (int)Tile.Advance);
        // Cover legacy saves whose ordinary doors were buried in walls.
        map.cells[doorIndex] = (int)Tile.Ground;
        var door = MapGenerator.DoorCandidates(map)[0];
        map.Set(door.x, door.y, Tile.Advance);
        var neighbor = door + MapGenerator.Directions.First(d => MapGenerator.Directions.Any(step => map.Walkable(door.x + d.x + step.x, door.y + d.y + step.y)));
        var approach = neighbor + MapGenerator.Directions.First(d => map.Walkable(neighbor.x + d.x, neighbor.y + d.y));
        Array.Clear(map.revealed, 0, map.revealed.Length);
        Assert.That(map.Known(neighbor.x, neighbor.y), Is.False);
        game.Run.x = approach.x; game.Run.y = approach.y; MapGenerator.Reveal(game.Run);
        Assert.That(map.Known(door.x, door.y), Is.False);
        Assert.That(map.Known(neighbor.x, neighbor.y), Is.True);
        var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.Invoke(game, new object[] { true }); yield return null; yield return null;
        var tags = game.GetComponentsInChildren<PrototypeTarget>(true);
        var wall = tags.First(t => t.kind == TargetKind.Wall && t.x == neighbor.x && t.y == neighbor.y && t.name == "Wall");
        Assert.That(wall.gameObject.activeInHierarchy, Is.True);
        Assert.That(wall.GetComponent<SpriteRenderer>().color, Is.EqualTo(Color.white));
        var hiddenDoor = tags.First(t => t.kind == TargetKind.Door && t.x == door.x && t.y == door.y && t.name == "前进门");
        Assert.That(hiddenDoor.gameObject.activeInHierarchy, Is.False);
        foreach (var direction in MapGenerator.Directions)
        {
            var point = door + direction;
            if (point == neighbor) continue;
            var hiddenWall = tags.First(t => t.kind == TargetKind.Wall && t.x == point.x && t.y == point.y && t.name == "Wall");
            Assert.That(hiddenWall.gameObject.activeInHierarchy, Is.EqualTo(PixelWallTiles.IsOutlineVisible(map, point.x, point.y)));
            Assert.That(hiddenWall.GetComponent<SpriteRenderer>().color,
                Is.EqualTo(Color.white));
        }
        game.Run.maps[game.Run.mapIndex] = Rules.Copy(map); map = game.Run.Map;
        rebuild.Invoke(game, new object[] { true }); yield return null;
        wall = game.GetComponentsInChildren<PrototypeTarget>(true).First(t => t.kind == TargetKind.Wall && t.x == neighbor.x && t.y == neighbor.y && t.name == "Wall");
        Assert.That(wall.GetComponent<SpriteRenderer>().color, Is.EqualTo(Color.white));
        game.Run.x = map.spawnX; game.Run.y = map.spawnY; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; Assert.That(wall.GetComponent<SpriteRenderer>().color, Is.EqualTo(Color.white));
        map.Set(neighbor.x, neighbor.y, Tile.Ground); game.Run.x = neighbor.x; game.Run.y = neighbor.y; MapGenerator.Reveal(game.Run);
        rebuild.Invoke(game, new object[] { true }); yield return null;
        Assert.That(map.At(neighbor.x, neighbor.y), Is.EqualTo(Tile.Ground));
        Assert.That(map.Known(door.x, door.y), Is.True);
    }
    [UnityTest]
    public IEnumerator FormerRotationShortcutsKeepCameraAndExplorationFixed()
    {
        game.StartNewRun(12345); yield return null;
        var fog = (bool[])game.Run.Map.revealed.Clone(); var position = new Vector2(game.Run.x, game.Run.y);
        var rotation = Quaternion.identity;
        foreach (var keys in new[] { new KeyboardState(Key.R), new KeyboardState(Key.LeftShift, Key.R),
            new KeyboardState(Key.RightShift, Key.T), new KeyboardState(Key.LeftShift, Key.R, Key.T) })
        {
            driver.keyState = keys; yield return new WaitForSeconds(0.1f);
            Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
        }
        Assert.That(game.Run.Map.revealed, Is.EqualTo(fog)); Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(position));
        driver.keyState = new KeyboardState(Key.Escape); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.LeftShift, Key.R); yield return new WaitForSeconds(0.1f);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Pause));
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
    }
    [UnityTest]
    public IEnumerator MiddleDragAndLegacySaveKeepFixedCameraAndScreenMovement()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 6; game.Run.y = 6;
        var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.Invoke(game, new object[] { true }); yield return null;
        var position = new Vector2(game.Run.x, game.Run.y);
        driver.mouseState = new MouseState { position = new Vector2(Screen.width / 2f, Screen.height / 2f), buttons = 4 };
        yield return null; yield return null;
        driver.mouseState.delta = new Vector2(20, 800); yield return null; yield return null;
        driver.mouseState.delta = Vector2.zero; yield return null;
        var rotation = Quaternion.identity;
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
        float half = (game.Run.Map.size - 1) / 2f;
        var focus = new Vector3(game.Run.x - half, game.Run.y - half, 0);
        Assert.That(Vector3.Distance(Camera.main.transform.position, focus), Is.EqualTo(10).Within(0.01));
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(position));
        foreach (var label in game.GetComponentsInChildren<TextMesh>(true))
            Assert.That(Quaternion.Angle(label.transform.rotation, Camera.main.transform.rotation), Is.LessThan(0.01));
        driver.mouseState.buttons = 0; driver.mouseState.delta = new Vector2(90, 0);
        yield return null; yield return null;
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
        driver.mouseState.delta = Vector2.zero;
        driver.keyState = new KeyboardState(Key.W); yield return new WaitForSeconds(0.15f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Run.y, Is.GreaterThan(position.y + 0.1f)); Assert.That(game.Run.x, Is.EqualTo(position.x).Within(0.01));
        var point = Camera.main.WorldToScreenPoint(new Vector3(6 - half, 7 - half, 0));
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        yield return Click((point.x - (Screen.width - 1280 * scale) / 2) / scale, (Screen.height - point.y - (Screen.height - 720 * scale) / 2) / scale);
        yield return new WaitForSeconds(0.7f);
        Assert.That(game.Run.CellX, Is.EqualTo(6)); Assert.That(game.Run.CellY, Is.EqualTo(7));
        game.Store.SaveCheckpoint(game.Run); float savedX = game.Run.x;
        // Older saves may contain a rotated camera; the removed field must be ignored.
        string sessionPath = Path.Combine(folder, "session.json");
        string legacyJson = File.ReadAllText(sessionPath).Replace("\"checkpoint\": {", "\"checkpoint\": {\"cameraYaw\": 90,");
        Assert.That(legacyJson, Does.Contain("\"cameraYaw\": 90"));
        File.WriteAllText(sessionPath, legacyJson);
        var persisted = new PrototypeStore(folder);
        typeof(PrototypeGame).GetProperty("Store").GetSetMethod(true).Invoke(game, new object[] { persisted });
        game.ContinueRun(); yield return null;
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
        Assert.That(game.Run.x, Is.EqualTo(savedX)); Assert.That(game.Run.CellY, Is.EqualTo(7));
        game.StartNewRun(12345); yield return null;
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
    }
    [UnityTest]
    public IEnumerator BossVictoryFromReturnDoorSiteAllowsMovementSaveAndWinningSettlement()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 6; game.Run.y = 10;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        game.BeginBattle(game.Run.Map.enemies[0]); game.Combat.enemyHealth = 1;
        int card = game.Combat.hand.FindIndex(c => c == CardKind.Strike); Assert.That(card, Is.GreaterThanOrEqualTo(0));
        float width = Mathf.Min(170, 1120f / game.Combat.hand.Count - 10);
        float start = (1280 - game.Combat.hand.Count * (width + 10) + 10) / 2;
        yield return Click(start + card * (width + 10) + width / 2, 500);
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Rewards));
        yield return Click(610, 600); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Explore));
        Assert.That(game.Run.bossDefeated, Is.True); Assert.That(game.Run.Map.CanStand(game.Run.x, game.Run.y), Is.True);
        PrototypeStore.ValidateRun(game.Run);
        var position = new Vector2(game.Run.x, game.Run.y);
        var direction = MapGenerator.Directions.First(d => game.Run.Map.CanTraverse(game.Run.CellX + d.x, game.Run.CellY + d.y));
        Key key = direction.x > 0 ? Key.D : direction.x < 0 ? Key.A : direction.y > 0 ? Key.W : Key.S;
        driver.keyState = new KeyboardState(key); yield return new WaitForSeconds(0.1f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(Vector2.Distance(position, new Vector2(game.Run.x, game.Run.y)), Is.GreaterThan(0.1f));
        // Finish the current visual segment, then step back before testing door interaction.
        yield return new WaitForSeconds(.4f);
        Key back = direction.x > 0 ? Key.A : direction.x < 0 ? Key.D : direction.y > 0 ? Key.S : Key.W;
        driver.keyState = new KeyboardState(back); yield return new WaitForSeconds(.1f);
        driver.keyState = new KeyboardState(); yield return new WaitForSeconds(.4f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(position));
        driver.mouseState = new MouseState();
        yield return OpenNearbyReturnDoor(); Assert.That(game.Panel, Is.EqualTo(GamePanel.Return));
        yield return Click(640, 290); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Home));
        Assert.That(game.Store.Session.checkpoint, Is.Not.Null);
        game.ContinueRun(); yield return null; PrototypeStore.ValidateRun(game.Run);
        yield return OpenNearbyReturnDoor(); Assert.That(game.Panel, Is.EqualTo(GamePanel.Return));
        yield return Click(640, 360); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Settlement));
        yield return Click(930, 610); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Result));
        Assert.That(game.Store.Session.checkpoint, Is.Null); Assert.That(game.Store.Session.pending, Is.Null);
    }
    IEnumerator OpenNearbyReturnDoor()
    {
        for (int attempt = 0; attempt < 3 && game.Panel == GamePanel.None; attempt++)
        {
            driver.keyState = new KeyboardState(Key.E); yield return null; yield return null;
            driver.keyState = new KeyboardState(); yield return null; yield return null;
        }
    }
    [UnityTest]
    public IEnumerator LivingMonsterHasSolidVolumeStopsWalkingAndBattleStartsFromAdjacentCell()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 6; game.Run.y = 8;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null;
        var body = game.GetComponentsInChildren<PrototypeTarget>().Single(t => t.kind == TargetKind.Enemy);
        Assert.That(body.GetComponent<BoxCollider2D>().isTrigger, Is.False);
        driver.keyState = new KeyboardState(Key.W); yield return new WaitForSeconds(0.4f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Run.y, Is.LessThanOrEqualTo(8.11f)); Assert.That(game.Run.CellY, Is.EqualTo(8));
        Assert.That(game.Run.Map.CanStand(game.Run.x, game.Run.y), Is.True);
        var point = Camera.main.WorldToScreenPoint(body.transform.position);
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        yield return Click((point.x - (Screen.width - 1280 * scale) / 2) / scale, (Screen.height - point.y - (Screen.height - 720 * scale) / 2) / scale);
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Battle)); Assert.That(game.Run.CellY, Is.EqualTo(8));
    }
    [UnityTest]
    public IEnumerator ThreeContainerKindsKeepSolidOpenedArtAfterEmptying()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1;
        string captures = Path.GetFullPath("Captures/ContainerLoot"); Directory.CreateDirectory(captures);
        var map = game.Run.Map; map.enemies.Clear(); map.loot.Clear();
        game.Run.x = 6; game.Run.y = 5;
        for (int i = 0; i < 3; i++)
            map.loot.Add(new GroundLoot { id = game.Run.nextEntityId++, x = 3 + i * 3, y = 6,
                containerKind = (ContainerKind)(i + 1), unopened = true, permanentObstacle = true });
        for (int i = 0; i < 20; i++) game.Run.inventory[i].units.Add(new ItemUnit { id = game.Run.nextItemId++, kind = ItemKind.IronSword });
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "three-containers-closed.png")); yield return null; yield return null;
        foreach (var loot in map.loot)
        {
            var visual = game.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Item " + loot.id);
            Assert.That(visual.sprite, Is.SameAs(game.artSet.loot[ContainerLoot.ArtIndex(loot)]));
            game.Run.x = loot.x; InteractWithLoot(loot); yield return null;
            visual = game.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Item " + loot.id);
            Assert.That(loot.opened && loot.HasContents, Is.True, "A full bag opens the container and preserves its items.");
            Assert.That(visual.sprite, Is.SameAs(game.artSet.openedLoot[(int)loot.containerKind - 1]));
            Assert.That(visual.color.a, Is.EqualTo(1)); Assert.That(visual.GetComponent<BoxCollider2D>().isTrigger, Is.False);
            Assert.That(map.CanTraverse(loot.x, loot.y), Is.False);
        }
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "three-containers-open-with-items.png")); yield return null; yield return null;
        foreach (var slot in game.Run.inventory) slot.units.Clear();
        foreach (var loot in map.loot)
        {
            game.Run.x = loot.x; InteractWithLoot(loot); yield return null;
            var visual = game.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "Item " + loot.id);
            Assert.That(visual.sprite, Is.SameAs(game.artSet.openedLoot[(int)loot.containerKind - 1]));
            Assert.That(visual.color.a, Is.EqualTo(1f)); Assert.That(visual.GetComponent<Collider2D>(), Is.Not.Null);
            Assert.That(visual.GetComponent<PrototypeTarget>(), Is.Null); Assert.That(map.CanTraverse(loot.x, loot.y), Is.False);
        }
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "three-containers-empty.png")); yield return null; yield return null;
        yield return Click(1010, 687);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "random-loot-inventory.png")); yield return null; yield return null;
    }

    [UnityTest]
    public IEnumerator DiscoveredLootRemainsVisibleAndCanBeOpenedWithALivingGuard()
    {
        game.StartNewRun(12345); var map = game.Run.Map; var loot = map.loot[0];
        var defeatedGuard = map.enemies.First(e => e.role == EnemyRole.ItemGuard &&
            MapGenerator.Manhattan(new Vector2Int(e.x, e.y), new Vector2Int(loot.x, loot.y)) == 1);
        defeatedGuard.defeated = true;
        Array.Clear(map.revealed, 0, map.revealed.Length);
        game.Run.x = defeatedGuard.x; game.Run.y = defeatedGuard.y; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true }); yield return null;
        var target = game.GetComponentsInChildren<PrototypeTarget>(true).Single(t => t.kind == TargetKind.Loot && t.id == loot.id);
        var undiscovered = map.loot.First(l => !map.Known(l.x, l.y));
        Assert.That(game.GetComponentsInChildren<PrototypeTarget>(true).Single(t => t.kind == TargetKind.Loot && t.id == undiscovered.id).gameObject.activeInHierarchy, Is.False);
        int far = Enumerable.Range(0, map.cells.Length).First(i => map.CanTraverse(i % map.size, i / map.size) && Math.Abs(i % map.size - loot.x) + Math.Abs(i / map.size - loot.y) > 3);
        game.Run.x = far % map.size; game.Run.y = far / map.size; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null); yield return null;
        Assert.That(MapGenerator.Visible(game.Run, loot.x, loot.y), Is.False); Assert.That(target.gameObject.activeInHierarchy, Is.True);
        float half = (map.size - 1) / 2f;
        var label = game.GetComponentsInChildren<TextMesh>(true).Single(t => t.transform.parent.name == "Item label" && Vector3.Distance(t.transform.parent.localPosition, new Vector3(loot.x - half, loot.y - half, 0)) < 0.01f);
        Assert.That(label.gameObject.activeInHierarchy, Is.True);
        game.Run.x = defeatedGuard.x; game.Run.y = defeatedGuard.y; MapGenerator.Reveal(game.Run);
        InteractWithLoot(loot); yield return null;
        Assert.That(map.enemies.Any(e => !e.defeated && e.role == EnemyRole.ItemGuard &&
            MapGenerator.Manhattan(new Vector2Int(e.x, e.y), new Vector2Int(loot.x, loot.y)) == 1), Is.True);
        Assert.That(loot.guardEnemyId, Is.Zero); Assert.That(loot.secondGuardEnemyId, Is.Zero);
        Assert.That(loot.units.Count, Is.Zero); Assert.That(Inventory.Items(game.Run).Count, Is.GreaterThan(0));
        Assert.That(game.GetComponentsInChildren<PrototypeTarget>(true).Any(t => t.kind == TargetKind.Loot && t.id == loot.id && t.gameObject.activeInHierarchy), Is.False);
        var remnant = game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == "Item " + loot.id && r.gameObject.activeInHierarchy);
        Assert.That(remnant.color.a, Is.EqualTo(1f));
        Assert.That(remnant.GetComponent<Collider2D>(), Is.Not.Null);
        Assert.That(remnant.sprite, Is.EqualTo(game.artSet.openedLoot[ContainerLoot.OpenArtIndex(loot)]));
        Assert.That(map.CanTraverse(loot.x, loot.y), Is.False);
    }

    [UnityTest]
    public IEnumerator DiscoveredRoomCompletesAllFourCornersWithoutDiscoveringHiddenContents()
    {
        game.StartNewRun(12345);
        var map = new MapData { number = 1, size = 11, cells = Enumerable.Repeat((int)Tile.Wall, 121).ToArray(), revealed = new bool[121] };
        for (int y = 3; y <= 7; y++) for (int x = 3; x <= 7; x++)
        { map.Set(x, y, Tile.Ground); map.revealed[map.Index(x, y)] = true; }
        map.Set(9, 5, Tile.Advance);
        map.Set(9, 6, Tile.Ground);
        MapGenerator.AddLoot(game.Run, map, new Vector2Int(9, 6), ItemKind.Potion, 1);
        map.enemies.Add(new EnemyData { id = game.Run.nextEntityId++, x = 9, y = 6, kind = EnemyKind.Bat });
        game.Run.maps[0] = map; game.Run.x = 5; game.Run.y = 5;
        // Walking along the room edges discovers the straight walls, but not the
        // diagonal corner cells. Only those four missing connectors may be added.
        for (int y = 3; y <= 7; y++) for (int x = 3; x <= 7; x++)
        { game.Run.x = x; game.Run.y = y; MapGenerator.Reveal(game.Run); }
        game.Run.x = 5; game.Run.y = 5;
        var discovery = (bool[])map.revealed.Clone();
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null;
        var tags = game.GetComponentsInChildren<PrototypeTarget>(true);
        var corners = new[] { new Vector2Int(2, 8), new Vector2Int(8, 8), new Vector2Int(2, 2), new Vector2Int(8, 2) };
        var cornerArt = new[] { 4, 5, 8, 9 };
        for (int i = 0; i < corners.Length; i++)
        {
            var point = corners[i];
            var wall = tags.Single(t => t.kind == TargetKind.Wall && t.x == point.x && t.y == point.y);
            Assert.That(wall.gameObject.activeInHierarchy, Is.True, "Missing room corner " + point);
            Assert.That(wall.GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[cornerArt[i] - 1]));
            Assert.That(wall.GetComponent<BoxCollider2D>().enabled, Is.False, "Decorative outlines are not discovered interaction targets.");
            Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Fog {point.x},{point.y}").gameObject.activeSelf, Is.False);
            Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Cell {point.x},{point.y}").enabled, Is.False,
                "Transparent corner pixels must not expose an undiscovered floor underneath.");
        }
        Assert.That(tags.Single(t => t.kind == TargetKind.Wall && t.x == 1 && t.y == 1).gameObject.activeInHierarchy, Is.False,
            "Completing the outline must not propagate through walls.");
        Assert.That(tags.Single(t => t.kind == TargetKind.Wall && t.x == 8 && t.y == 5).GetComponent<SpriteRenderer>().color,
            Is.EqualTo(Color.white), "Discovered walls stay unmarked beside hidden doors, including legacy layouts.");
        foreach (var hidden in tags.Where(t => t.kind == TargetKind.Door || t.kind == TargetKind.Enemy || t.kind == TargetKind.Loot))
            Assert.That(hidden.gameObject.activeInHierarchy, Is.False, "Hidden content was exposed by the outline: " + hidden.kind);
        Assert.That(map.revealed, Is.EqualTo(discovery));
        string captures = Path.GetFullPath("Captures/MagicCavePrototype/PixelArt"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "wall-corner-fog-check.png"));
        yield return null; yield return null;
    }

    [UnityTest]
    public IEnumerator UnseenCorridorWallsRemainFoggedUntilThePlayerDiscoversThem()
    {
        game.StartNewRun(12345);
        var map = new MapData { number = 1, size = 9, cells = Enumerable.Repeat((int)Tile.Wall, 81).ToArray(), revealed = new bool[81] };
        for (int y = 4; y <= 6; y++) for (int x = 3; x <= 5; x++) map.Set(x, y, Tile.Ground);
        for (int y = 1; y <= 3; y++) map.Set(4, y, Tile.Ground);
        game.Run.maps[0] = map;
        foreach (var position in new[] { new Vector2Int(3, 4), new Vector2Int(5, 4), new Vector2Int(4, 4) })
        { game.Run.x = position.x; game.Run.y = position.y; MapGenerator.Reveal(game.Run); }
        var discovery = (bool[])map.revealed.Clone();
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null;
        var tags = game.GetComponentsInChildren<PrototypeTarget>(true);
        foreach (int x in new[] { 3, 5 })
        {
            var wall = tags.Single(t => t.kind == TargetKind.Wall && t.x == x && t.y == 2);
            Assert.That(wall.gameObject.activeInHierarchy, Is.False, "The corridor side below the discovered area must remain hidden.");
            Assert.That(wall.GetComponent<BoxCollider2D>().enabled, Is.False);
            Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Fog {x},2").gameObject.activeSelf, Is.True);
        }
        foreach (int x in new[] { 2, 6 })
            Assert.That(tags.Single(t => t.kind == TargetKind.Wall && t.x == x && t.y == 3).gameObject.activeInHierarchy, Is.True,
                "The corner between two discovered wall segments should still be complete.");
        Assert.That(map.revealed, Is.EqualTo(discovery));
        string captures = Path.GetFullPath("Captures/MagicCavePrototype/PixelArt"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "corridor-fog-boundary-check.png"));
        yield return null; yield return null;
        game.Run.x = 4; game.Run.y = 3; MapGenerator.Reveal(game.Run);
        var refresh = typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic);
        refresh.Invoke(game, null); yield return null;
        foreach (int x in new[] { 3, 5 })
            Assert.That(tags.Single(t => t.kind == TargetKind.Wall && t.x == x && t.y == 2).gameObject.activeInHierarchy, Is.False,
                "Moving closer must not reveal walls beyond the current discovery cross.");
        game.Run.y = 2; MapGenerator.Reveal(game.Run); refresh.Invoke(game, null); yield return null;
        foreach (int x in new[] { 3, 5 })
        {
            var wall = tags.Single(t => t.kind == TargetKind.Wall && t.x == x && t.y == 2);
            Assert.That(wall.gameObject.activeInHierarchy, Is.True);
            Assert.That(wall.GetComponent<BoxCollider2D>().enabled, Is.True);
            Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Fog {x},2").gameObject.activeSelf, Is.False);
        }
    }

    [UnityTest]
    public IEnumerator BreakingAWallRebuildsTheActualNeighborConnections()
    {
        game.StartNewRun(12345);
        var map = new MapData { number = 1, size = 9, cells = Enumerable.Repeat((int)Tile.Wall, 81).ToArray(), revealed = new bool[81] };
        for (int y = 3; y <= 5; y++) for (int x = 3; x <= 5; x++) map.Set(x, y, Tile.Ground);
        game.Run.maps[0] = map; game.Run.x = 3; game.Run.y = 3; MapGenerator.Reveal(game.Run);
        // The west corner is already discovered from the earlier room approach.
        map.revealed[map.Index(2, 2)] = true;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null;
        var before = game.GetComponentsInChildren<PrototypeTarget>(true);
        Assert.That(before.Single(t => t.kind == TargetKind.Wall && t.x == 2 && t.y == 2).GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[7]));
        Assert.That(before.Single(t => t.kind == TargetKind.Wall && t.x == 4 && t.y == 2).GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[1]));
        float half = (map.size - 1) * 0.5f;
        var point = Camera.main.WorldToScreenPoint(new Vector3(3 - half, 2 - half, 0));
        driver.mouseState = new MouseState { position = point }; yield return null; yield return null;
        // Batch-mode test coroutines can run against the Editor input buffer rather
        // than Dynamic. Inject into the buffer that the actual input reader uses.
        driver.mouse.MakeCurrent(); InputState.Change(driver.mouse, driver.mouseState, InputState.currentUpdateType);
        var pointer = typeof(PrototypeGame).GetMethod("PointerTarget", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        var inputCamera = (Camera)typeof(PrototypeGame).GetField("gameCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        var mousePosition = Mouse.current == null ? Vector2.zero : Mouse.current.position.ReadValue();
        var inputWorld = inputCamera.ScreenToWorldPoint(mousePosition);
        bool overHud = (bool)typeof(PrototypeGame).GetMethod("PointerOverHud", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        Assert.That(pointer.GetType().GetField("kind").GetValue(pointer), Is.EqualTo(TargetKind.Wall),
            $"Break target {point}, actual mouse {mousePosition}, world {inputWorld}, cameras {Camera.main == inputCamera}, HUD {overHud}, known {map.Known(3, 2)}, size {game.Run.Map.size}, screen {Screen.width}x{Screen.height}.");
        Assert.That(pointer.GetType().GetField("x").GetValue(pointer), Is.EqualTo(3));
        Assert.That(pointer.GetType().GetField("y").GetValue(pointer), Is.EqualTo(2));
        driver.mouseState = new MouseState { position = point, buttons = 2 };
        InputState.Change(driver.mouse, driver.mouseState, InputState.currentUpdateType);
        typeof(PrototypeGame).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; yield return null;
        driver.mouseState = new MouseState { position = point }; yield return null;
        Assert.That(map.At(3, 2), Is.EqualTo(Tile.Ground), "Use the actual wall-breaking input, not only a manual map mutation.");
        var after = game.GetComponentsInChildren<PrototypeTarget>(true);
        Assert.That(after.Single(t => t.kind == TargetKind.Wall && t.x == 2 && t.y == 2).GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[10]),
            "The old bottom-left corner must become a straight vertical wall.");
        Assert.That(after.Single(t => t.kind == TargetKind.Wall && t.x == 4 && t.y == 2).GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[3]),
            "The former horizontal wall must turn into the new corner around the broken cell.");
        Assert.That(after.Any(t => t.kind == TargetKind.Wall && t.x == 3 && t.y == 2), Is.False);
    }

    [UnityTest]
    public IEnumerator UnknownCrossShowsOnlyTheKnownCornerAndRefreshesWhenDiscovered()
    {
        game.StartNewRun(12345);
        var map = new MapData { number = 1, size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), revealed = new bool[49] };
        foreach (var point in new[] { new Vector2Int(2, 2), new Vector2Int(2, 4), new Vector2Int(4, 2), new Vector2Int(4, 4) })
            map.Set(point.x, point.y, Tile.Ground);
        map.revealed[map.Index(4, 4)] = true; map.revealed[map.Index(4, 3)] = true; map.revealed[map.Index(3, 4)] = true;
        game.Run.maps[0] = map; game.Run.x = 4; game.Run.y = 4;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null;
        var cross = game.GetComponentsInChildren<PrototypeTarget>(true).Single(t => t.kind == TargetKind.Wall && t.x == 3 && t.y == 3);
        var fog = game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == "Fog 3,3");
        Assert.That(PixelWallTiles.OutlineMask(PixelWallTiles.Mask(map, 3, 3)), Is.EqualTo(15));
        Assert.That(cross.gameObject.activeInHierarchy, Is.True); Assert.That(fog.gameObject.activeSelf, Is.False);
        Assert.That(cross.GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[7]), "Only the known north/east corner is visible, not the hidden south/west branches.");
        Assert.That(cross.GetComponent<BoxCollider2D>().enabled, Is.False);
        Assert.That(map.Known(3, 3), Is.False);
        map.revealed[map.Index(3, 3)] = true;
        typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null); yield return null;
        Assert.That(cross.gameObject.activeInHierarchy, Is.True); Assert.That(fog.gameObject.activeSelf, Is.False);
        Assert.That(cross.GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[5]), "Refresh must replace the decorative corner with the discovered cross.");
        Assert.That(cross.GetComponent<BoxCollider2D>().enabled, Is.True);
    }

    [UnityTest]
    public IEnumerator ActualWallBreakingCompletesFoggedJunctionCornersInAllFourDirections()
    {
        for (int turns = 0; turns < 4; turns++)
        {
            Vector2Int Rotate(int x, int y)
            {
                var p = new Vector2Int(x - 4, y - 4);
                for (int turn = 0; turn < turns; turn++) p = new Vector2Int(-p.y, p.x);
                return p + new Vector2Int(4, 4);
            }
            game.StartNewRun(12345);
            var map = new MapData { number = 1, size = 9, cells = Enumerable.Repeat((int)Tile.Wall, 81).ToArray(), revealed = new bool[81] };
            var center = Rotate(3, 3); var broken = Rotate(4, 2); var player = Rotate(5, 2);
            var hiddenFloor = Rotate(4, 4);
            map.Set(hiddenFloor.x, hiddenFloor.y, Tile.Ground); map.Set(player.x, player.y, Tile.Ground);
            foreach (var anchor in new[] { Rotate(4, 3), Rotate(3, 2) }) map.revealed[map.Index(anchor.x, anchor.y)] = true;
            game.Run.maps[0] = map; game.Run.x = player.x; game.Run.y = player.y; MapGenerator.Reveal(game.Run);
            typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
            yield return null; yield return null;
            var before = game.GetComponentsInChildren<PrototypeTarget>(true).Single(t => t.kind == TargetKind.Wall && t.x == center.x && t.y == center.y);
            Assert.That(before.gameObject.activeInHierarchy, Is.False, "Before breaking, the diagonal floor needed for this connector does not exist.");
            CaptureWallCamera($"wall-break-corner-before-{turns}.png", center, map.size);
            var discovery = (bool[])map.revealed.Clone();
            float half = (map.size - 1) * 0.5f;
            var camera = (Camera)typeof(PrototypeGame).GetField("gameCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
            var point = camera.WorldToScreenPoint(new Vector3(broken.x - half, broken.y - half, 0));
            driver.mouse.MakeCurrent(); driver.mouseState = new MouseState { position = point };
            InputState.Change(driver.mouse, driver.mouseState, InputState.currentUpdateType);
            var target = typeof(PrototypeGame).GetMethod("PointerTarget", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            Assert.That(target.GetType().GetField("kind").GetValue(target), Is.EqualTo(TargetKind.Wall), "Actual right-click must hit the intended wall.");
            Assert.That(target.GetType().GetField("x").GetValue(target), Is.EqualTo(broken.x));
            Assert.That(target.GetType().GetField("y").GetValue(target), Is.EqualTo(broken.y));
            driver.mouseState = new MouseState { position = point, buttons = 2 };
            InputState.Change(driver.mouse, driver.mouseState, InputState.currentUpdateType);
            typeof(PrototypeGame).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
            driver.mouseState = new MouseState { position = point };
            InputState.Change(driver.mouse, driver.mouseState, InputState.currentUpdateType);
            yield return null; yield return null;
            Assert.That(map.At(broken.x, broken.y), Is.EqualTo(Tile.Ground), "The production wall-breaking input must change terrain.");
            var after = game.GetComponentsInChildren<PrototypeTarget>(true).Single(t => t.kind == TargetKind.Wall && t.x == center.x && t.y == center.y);
            Assert.That(after.gameObject.activeInHierarchy, Is.True, "The newly opened floor must complete the known wall corner immediately.");
            int[] cornerArt = { 4, 8, 9, 5 };
            Assert.That(after.GetComponent<SpriteRenderer>().sprite, Is.SameAs(game.artSet.walls[cornerArt[turns] - 1]), "An unknown T must draw only its proven corner, not its extra arm.");
            Assert.That(after.GetComponent<BoxCollider2D>().enabled, Is.False);
            Assert.That(map.Known(center.x, center.y), Is.False);
            Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Fog {center.x},{center.y}").gameObject.activeSelf, Is.False);
            foreach (var hidden in new[] { Rotate(3, 4), Rotate(2, 3), hiddenFloor })
                Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == $"Fog {hidden.x},{hidden.y}").gameObject.activeSelf, Is.True,
                    "Completing the corner must not expose the unknown branch or floor.");
            Assert.That(map.revealed, Is.EqualTo(discovery), "Visual completion must not expand exploration.");
            CaptureWallCamera($"wall-break-corner-after-{turns}.png", center, map.size);
        }
    }

    [UnityTest]
    public IEnumerator SelectedTerrainFloorsRenderSaveAndAdvanceToFifthFloorBoss()
    {
        foreach (var mode in new[] { MapGenerationMode.RoomsAndCorridors, MapGenerationMode.Excavated })
        {
            game.StartNewRun(12345, mode);
            var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
            var targetType = typeof(PrototypeGame).GetNestedType("Target", BindingFlags.NonPublic);
            for (int index = 0; index < 4; index++)
            {
                game.Run.mapIndex = index;
                var map = game.Run.Map;
                game.Run.x = map.spawnX; game.Run.y = map.spawnY;
                if (map.dugTerrain)
                {
                    for (int i = 0; i < map.revealed.Length; i++) map.revealed[i] = true;
                    rebuild.Invoke(game, new object[] { true }); yield return null; yield return null;
                    CaptureWallCamera("floor-" + map.number + ".png", new Vector2Int(15, 15), map.size, 31, "Captures/NaturalTerrain");
                    game.Store.SaveCheckpoint(game.Run); game.ContinueRun(); yield return null;
                    Assert.That(game.Run.mapIndex, Is.EqualTo(index));
                    Assert.That(game.Run.Map.cells, Is.EqualTo(map.cells));
                    Assert.That(MapGenerator.ValidateContainerRoutes(game.Run.Map), Is.True);
                }
                int door = Array.IndexOf(game.Run.Map.cells, (int)Tile.Advance);
                var point = new Vector2Int(door % map.size, door / map.size);
                var approach = MapGenerator.Directions.Select(d => point + d).First(p => game.Run.Map.Walkable(p.x, p.y) &&
                    !game.Run.Map.loot.Any(l => l.BlocksMovement && l.x == p.x && l.y == p.y));
                foreach (var enemy in game.Run.Map.enemies) enemy.defeated = true;
                game.Run.x = approach.x; game.Run.y = approach.y;
                rebuild.Invoke(game, new object[] { true }); yield return null;
                var target = Activator.CreateInstance(targetType);
                targetType.GetField("kind").SetValue(target, TargetKind.Door);
                targetType.GetField("x").SetValue(target, point.x); targetType.GetField("y").SetValue(target, point.y);
                typeof(PrototypeGame).GetMethod("Interact", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new[] { target });
                yield return null;
                Assert.That(game.Run.mapIndex, Is.EqualTo(index + 1));
            }
            Assert.That(game.Run.Map.number, Is.EqualTo(5));
            Assert.That(game.Run.Map.enemies.Single().kind, Is.EqualTo(EnemyKind.Boss));
        }
    }

    void CaptureWallCamera(string filename, Vector2Int center, int mapSize, float viewCells = 7,
        string directory = "Captures/MagicCavePrototype/PixelArt")
    {
        var camera = (Camera)typeof(PrototypeGame).GetField("gameCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var oldPosition = camera.transform.position; float oldSize = camera.orthographicSize; float oldAspect = camera.aspect;
        int pixels = viewCells > 7 ? 992 : 448;
        var target = RenderTexture.GetTemporary(pixels, pixels, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(pixels, pixels, TextureFormat.RGBA32, false);
        try
        {
            float half = (mapSize - 1) * 0.5f;
            camera.transform.position = new Vector3(center.x - half, center.y - half, -10);
            camera.orthographicSize = viewCells * .5f; camera.aspect = 1; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0); image.Apply();
            string captures = Path.GetFullPath(directory); Directory.CreateDirectory(captures);
            File.WriteAllBytes(Path.Combine(captures, filename), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = oldTarget; camera.transform.position = oldPosition;
            camera.orthographicSize = oldSize; camera.aspect = oldAspect; RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(image);
        }
    }

    [UnityTest]
    public IEnumerator PixelArtRendersRandomTerrainAndBattleAndSolidLootCanBeCollected()
    {
        game.StartNewRun(12345);
        var map = game.Run.Map;
        for (int i = 0; i < map.revealed.Length; i++) map.revealed[i] = true;
        var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.Invoke(game, new object[] { true }); yield return null; yield return null;
        Assert.That(Camera.main.orthographic, Is.True);
        Assert.That(game.GetComponentsInChildren<Collider>(true), Is.Empty, "Pixel exploration must not depend on 3D primitives or raycasts.");
        var actor = game.GetComponentsInChildren<SpriteRenderer>().Single(r => r.name == "玩家");
        Assert.That(actor.sprite, Is.EqualTo(game.artSet.playerDown));
        Assert.That(actor.GetComponent<BoxCollider2D>().size, Is.EqualTo(Vector2.one * 0.8f));
        Assert.That(game.GetComponentsInChildren<SpriteRenderer>(true).Count(r => r.name == "Wall"), Is.EqualTo(map.cells.Count(c => c == (int)Tile.Wall)));
        string captures = Path.GetFullPath("Captures/MagicCavePrototype/PixelArt"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "random-map-art-check.png"));
        yield return null; yield return null;
        game.BeginBattle(map.enemies[0]); yield return null; yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "pixel-battle.png"));
        yield return null; yield return null;
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 5; game.Run.y = 4;
        var loot = MapGenerator.AddLoot(game.Run, game.Run.Map, new Vector2Int(5, 5), ItemKind.Potion, 1);
        rebuild.Invoke(game, new object[] { true }); yield return null;
        driver.keyState = new KeyboardState(Key.UpArrow); yield return new WaitForSeconds(0.3f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Run.y, Is.LessThanOrEqualTo(4.11f));
        InteractWithLoot(loot); yield return null;
        Assert.That(loot.units, Is.Empty); Assert.That(game.Run.Map.CanTraverse(5, 5), Is.True);
        int slot = game.Run.inventory.FindIndex(s => s.units.Count > 0);
        var dropped = Inventory.Drop(game.Run, slot, 1); rebuild.Invoke(game, new object[] { false }); yield return null;
        Assert.That(dropped.loose, Is.True); PrototypeStore.ValidateRun(game.Run);
        game.Store.SaveCheckpoint(game.Run); game.ContinueRun(); yield return null;
        Assert.That(game.Run.Map.loot.Single(l => l.id == dropped.id).loose, Is.True);
        // Capture normal exploration with the unchanged five-cell discovery rule as well.
        game.StartNewRun(12345); yield return null; yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "pixel-exploration.png"));
        yield return null; yield return null;
    }
    Vector2 GuiPoint(float x, float y)
    {
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        return new Vector2((Screen.width - 1280 * scale) / 2 + x * scale, Screen.height - ((Screen.height - 720 * scale) / 2 + y * scale));
    }
    IEnumerator DragItem(float fromX, float fromY, float toX, float toY)
    {
        driver.mouseState = new MouseState { position = GuiPoint(fromX, fromY), buttons = 1 }; yield return null; yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(toX, toY), buttons = 1 }; yield return null; yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(toX, toY) }; yield return null; yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator InventoryDragEquipsUnequipsMovesAndCancels()
    {
        game.StartNewRun(12345); yield return null;
        var run = game.Run;
        var item = new ItemUnit { id = run.nextItemId++, kind = ItemKind.IronSword, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ };
        run.inventory[0].units.Add(item);
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        yield return DragItem(630, 225, 135, 335);
        Assert.That(run.character.equipment.Single(), Is.SameAs(item)); Assert.That(run.inventory[0].units, Is.Empty);
        yield return DragItem(135, 335, 755, 225);
        Assert.That(run.character.equipment, Is.Empty); Assert.That(run.inventory[1].units.Single(), Is.SameAs(item));
        yield return DragItem(755, 225, 875, 225);
        Assert.That(run.inventory[1].units, Is.Empty); Assert.That(run.inventory[2].units.Single(), Is.SameAs(item));
        yield return DragItem(875, 225, 35, 690);
        Assert.That(run.inventory[2].units.Single(), Is.SameAs(item));
        PrototypeStore.ValidateRun(run);
        string captures = Path.GetFullPath("Captures/InventoryStars"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "inventory-grid.png")); yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator SightRendersWallOcclusionAndTorchToggle()
    {
        game.StartNewRun(12345); var run = game.Run; var map = run.Map;
        run.x = 5; run.y = 5;
        Array.Fill(map.cells, (int)Tile.Wall); Array.Clear(map.revealed, 0, map.revealed.Length);
        map.enemies.Clear(); map.loot.Clear(); map.hasMerchant = false;
        for (int y = 2; y <= 8; y++) for (int x = 2; x <= 8; x++) map.Set(x, y, Tile.Ground);
        for (int y = 2; y <= 8; y++) map.Set(6, y, Tile.Wall);
        MapGenerator.Reveal(run);
        var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.Invoke(game, new object[] { true }); yield return null; yield return null;
        Assert.That(map.Known(4, 4), Is.True);
        Assert.That(map.Known(6, 5), Is.True);
        Assert.That(map.Known(7, 5), Is.False);
        CaptureWallCamera("base-wall-occlusion.png", new Vector2Int(5, 5), map.size, 7, "Captures/Sight");
        Inventory.Pickup(run, new GroundLoot { loose = true, units = new System.Collections.Generic.List<ItemUnit> {
            new ItemUnit { id = run.nextItemId++, kind = ItemKind.Torch } } });
        int slot = Array.IndexOf(run.quickSlots, (int)ItemKind.Torch);
        Assert.That(game.UseQuickSlot(slot), Is.True); yield return null;
        Assert.That(map.Known(3, 4), Is.True);
        Assert.That(map.Known(7, 5), Is.False, "Torch cannot reveal the other side of the wall.");
        CaptureWallCamera("torch-wall-occlusion.png", new Vector2Int(5, 5), map.size, 7, "Captures/Sight");
        TorchLight.Step(run);
        Assert.That(game.UseQuickSlot(slot), Is.True);
        Assert.That(TorchLight.IsLit(run), Is.False);
        Assert.That(run.torchStepsRemaining, Is.EqualTo(19));
        Assert.That(game.UseQuickSlot(slot), Is.True);
        Assert.That(TorchLight.IsLit(run), Is.True);
        Assert.That(run.torchStepsRemaining, Is.EqualTo(19));
        Assert.That(QuickBar.Count(run, ItemKind.Torch), Is.Zero);
    }
    [UnityTest]
    public IEnumerator TorchOnlyBurnsForSuccessfulGridSteps()
    {
        game.StartNewRun(12345); var run = game.Run;
        run.mapIndex = run.maps.Count - 1; run.x = 6; run.y = 5;
        run.inventory[0].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.Torch });
        Assert.That(game.UseInventory(0), Is.True);
        yield return new WaitForSeconds(.2f);
        Assert.That(run.torchStepsRemaining, Is.EqualTo(20), "Waiting does not consume steps.");
        var move = typeof(PrototypeGame).GetMethod("MoveGrid", BindingFlags.Instance | BindingFlags.NonPublic);
        run.Map.Set(7, 5, Tile.Wall);
        Assert.That((bool)move.Invoke(game, new object[] { new Vector2Int(7, 5) }), Is.False);
        Assert.That(run.torchStepsRemaining, Is.EqualTo(20));
        for (int i = 0; i < 20; i++)
            Assert.That((bool)move.Invoke(game, new object[] { new Vector2Int(6, i % 2 == 0 ? 6 : 5) }), Is.True);
        Assert.That(run.torchStepsRemaining, Is.Zero);
        Assert.That(game.UseInventory(0), Is.False);
        Assert.That(game.ExtinguishTorch(), Is.False);
    }
    [UnityTest]
    public IEnumerator GridMovementUsesFirstHeldKeyAndNeverMovesDiagonally()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 6; game.Run.y = 5;
        yield return null;
        driver.keyState = new KeyboardState(Key.D); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.D, Key.W); yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.y, Is.EqualTo(5)); Assert.That(game.Run.x, Is.GreaterThan(6));
        Assert.That(game.Run.x, Is.EqualTo(Mathf.Round(game.Run.x)));
        float x = game.Run.x;
        driver.keyState = new KeyboardState(Key.W); yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.x, Is.EqualTo(x)); Assert.That(game.Run.y, Is.GreaterThan(5));
        driver.keyState = new KeyboardState(); yield return null; yield return null;
        var stopped = new Vector2(game.Run.x, game.Run.y); yield return new WaitForSeconds(.4f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped));
        // Two events in one input update must retain actual event order, rather than WASD priority.
        game.Run.x = 6; game.Run.y = 5;
        InputSystem.QueueStateEvent(driver.keyboard, new KeyboardState(Key.D));
        InputSystem.QueueStateEvent(driver.keyboard, new KeyboardState(Key.D, Key.W));
        InputSystem.Update(); driver.keyState = new KeyboardState(Key.D, Key.W);
        yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.y, Is.EqualTo(5)); Assert.That(game.Run.x, Is.GreaterThan(6));
        driver.keyState = new KeyboardState(); yield return null; yield return null;
        game.Run.x = 6; game.Run.y = 5; game.Run.Map.Set(7, 5, Tile.Wall);
        driver.keyState = new KeyboardState(Key.D); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.D, Key.W); yield return new WaitForSeconds(.4f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(new Vector2(6, 5)), "A blocked first direction cannot slide along a later key.");
    }
    void PrepareNavigationMap()
    {
        game.StartNewRun(12345);
        var map = game.Run.Map;
        map.enemies.Clear(); map.loot.Clear(); map.hasMerchant = false;
        for (int y = 1; y < map.size - 1; y++)
        for (int x = 1; x < map.size - 1; x++)
        { map.Set(x, y, Tile.Ground); map.revealed[map.Index(x, y)] = true; }
        game.Run.x = 5; game.Run.y = 5;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
    }
    [UnityTest]
    public IEnumerator WheelDefaultsToBottomRightLongPressRelocatesAndReleaseRestores()
    {
        PrepareNavigationMap(); yield return null;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var center = typeof(PrototypeGame).GetProperty("WheelCenter", flags);
        var active = typeof(PrototypeGame).GetField("wheelActive", flags);
        Assert.That(center.GetValue(game), Is.EqualTo(new Vector2(1184, 588)));
        // The fixed wheel works immediately, with its own center instead of the press point.
        driver.mouseState = new MouseState { position = GuiPoint(1220, 588), buttons = 1 };
        yield return new WaitForSeconds(.15f);
        Assert.That(game.Run.x, Is.GreaterThan(5));
        driver.mouseState.buttons = 0; yield return new WaitForSeconds(.4f);
        var stopped = new Vector2(game.Run.x, game.Run.y);
        driver.mouseState = new MouseState { position = GuiPoint(450, 300), buttons = 1 };
        yield return new WaitForSeconds(.12f);
        Assert.That(active.GetValue(game), Is.False);
        yield return new WaitForSeconds(.2f);
        Assert.That(active.GetValue(game), Is.True, "Long press should summon the wheel without requiring a drag.");
        Assert.That(center.GetValue(game), Is.EqualTo(new Vector2(450, 300)));
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped));
        string captures = Path.GetFullPath("Captures/Navigation"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "long-press-wheel.png")); yield return null; yield return null;
        driver.mouseState.buttons = 0; yield return new WaitForSeconds(.4f);
        Assert.That(center.GetValue(game), Is.EqualTo(new Vector2(1184, 588)));
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped), "Releasing the long press must not create a click route.");
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "default-wheel.png")); yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator ExpandedMinimapBlocksMovementClosesWithEscapeAndRoutesUsingExpandedCoordinates()
    {
        PrepareNavigationMap(); yield return null;
        var expanded = typeof(PrototypeGame).GetField("minimapExpanded", BindingFlags.Instance | BindingFlags.NonPublic);
        yield return Click(1220, 117);
        Assert.That(expanded.GetValue(game), Is.True);
        driver.keyState = new KeyboardState(Key.D); yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.x, Is.EqualTo(5), "Expanded map must own keyboard input.");
        driver.keyState = new KeyboardState(); yield return null;
        yield return Click(100, 400);
        Assert.That(game.Run.x, Is.EqualTo(5), "Overlay clicks must not reach the scene.");
        string captures = Path.GetFullPath("Captures/Navigation"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "expanded-map.png")); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.Escape); yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(expanded.GetValue(game), Is.False);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.None), "Esc should close the map without opening pause.");
        yield return Click(1220, 117);
        Assert.That(expanded.GetValue(game), Is.True);
        yield return Click(910, 108); yield return null;
        Assert.That(expanded.GetValue(game), Is.False, "The collapse button must work independently of the map.");
        yield return Click(1220, 117);
        float step = 492f / game.Run.Map.size;
        yield return Click(394 + 7.5f * step, 128 + (game.Run.Map.size - 5.5f) * step);
        Assert.That(expanded.GetValue(game), Is.False, "Choosing reachable ground closes the map and starts travel.");
        yield return new WaitForSeconds(1);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(new Vector2(7, 5)));
    }
    [UnityTest]
    public IEnumerator WheelYieldsToKeyboardAndCancelsWhenInventoryOpens()
    {
        PrepareNavigationMap(); yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(600, 400), buttons = 1 }; yield return null;
        driver.mouseState.position = GuiPoint(650, 400); yield return new WaitForSeconds(.4f);
        driver.keyState = new KeyboardState(Key.W); yield return new WaitForSeconds(.4f);
        float x = game.Run.x;
        Assert.That(game.Run.y, Is.GreaterThan(5));
        driver.keyState = new KeyboardState(); yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.x, Is.EqualTo(x), "Wheel must not resume while the cancelled press is still held.");
        driver.mouseState.buttons = 0; yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(600, 400), buttons = 1 }; yield return null;
        driver.mouseState.position = GuiPoint(650, 400); yield return null;
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        var stopped = new Vector2(game.Run.x, game.Run.y);
        driver.mouseState.buttons = 0; yield return null;
        driver.keyState = new KeyboardState(Key.Escape); yield return null;
        driver.keyState = new KeyboardState(); yield return new WaitForSeconds(.5f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped));
    }
    [UnityTest]
    public IEnumerator TownWheelMovesAndReleasesWithoutSubmittingTownRoute()
    {
        var start = game.TownPosition;
        driver.mouseState = new MouseState { position = GuiPoint(400, 400), buttons = 1 }; yield return null;
        driver.mouseState.position = GuiPoint(450, 400); yield return new WaitForSeconds(.4f);
        Assert.That(game.TownPosition.x, Is.GreaterThan(start.x)); Assert.That(game.TownPosition.y, Is.EqualTo(start.y));
        driver.mouseState = new MouseState { position = GuiPoint(650, 500) }; yield return new WaitForSeconds(.4f);
        var stopped = game.TownPosition; yield return new WaitForSeconds(.5f);
        Assert.That(game.TownPosition, Is.EqualTo(stopped));
    }
    [UnityTest]
    public IEnumerator DirectionWheelStopsStabilizesAndDoesNotClickOnRelease()
    {
        PrepareNavigationMap(); yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(600, 400), buttons = 1 };
        yield return null; yield return null;
        Assert.That(game.Run.x, Is.EqualTo(5), "Press alone must not start the click route.");
        driver.mouseState.position = GuiPoint(650, 400);
        yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.x, Is.GreaterThan(5)); Assert.That(game.Run.y, Is.EqualTo(5));
        driver.mouseState.position = GuiPoint(650, 345);
        yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.y, Is.EqualTo(5), "Small diagonal boundary changes should retain horizontal direction.");
        driver.mouseState.position = GuiPoint(605, 330);
        yield return new WaitForSeconds(.4f);
        Assert.That(game.Run.y, Is.GreaterThan(5));
        driver.mouseState.position = GuiPoint(600, 400);
        yield return new WaitForSeconds(.4f);
        var stopped = new Vector2(game.Run.x, game.Run.y);
        yield return new WaitForSeconds(.4f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped));
        driver.mouseState = new MouseState { position = GuiPoint(800, 500) };
        yield return new WaitForSeconds(.6f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(stopped), "Releasing a drag must not submit a click route.");
    }
    [UnityTest]
    public IEnumerator MinimapRoutesOnlyToKnownReachableGroundAndDoesNotClickThrough()
    {
        PrepareNavigationMap(); yield return null;
        var map = game.Run.Map;
        float step = 204f / map.size;
        Func<int, int, Vector2> mini = (x, y) => new Vector2(1050 + (x + .5f) * step, 133 + (map.size - y - .5f) * step);
        // Isolate a revealed target behind four walls; an invalid request must leave the player in place.
        map.Set(7, 8, Tile.Wall); map.Set(9, 8, Tile.Wall); map.Set(8, 7, Tile.Wall); map.Set(8, 9, Tile.Wall);
        var point = mini(8, 8); yield return Click(point.x, point.y); yield return new WaitForSeconds(.5f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(new Vector2(5, 5)));
        map.revealed[map.Index(10, 10)] = false;
        point = mini(10, 10); yield return Click(point.x, point.y); yield return new WaitForSeconds(.4f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(new Vector2(5, 5)));
        point = mini(7, 5); yield return Click(point.x, point.y); yield return new WaitForSeconds(1f);
        Assert.That(new Vector2(game.Run.x, game.Run.y), Is.EqualTo(new Vector2(7, 5)));
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Explore));
        string captures = Path.GetFullPath("Captures/Navigation"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "minimap.png")); yield return null; yield return null;
        driver.mouseState = new MouseState { position = GuiPoint(600, 400), buttons = 1 }; yield return null; yield return null;
        driver.mouseState.position = GuiPoint(650, 400); yield return null; yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "direction-wheel.png")); yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator ThreeRegionStarMapCanPurchaseWisdomAndPersist()
    {
        game.VisitTown(1); float deadline = Time.time + 15;
        while (game.TownPage != TownPage.Mentor && Time.time < deadline) yield return null;
        Assert.That(game.TownPage, Is.EqualTo(TownPage.Mentor));
        yield return Click(255, 500); yield return Click(1060, 470);
        Assert.That(game.Store.Warehouse.town.talents[7], Is.EqualTo(1));
        Assert.That(new PrototypeStore(folder).Warehouse.town.talents[7], Is.EqualTo(1));
        Assert.That(game.Store.Warehouse.town.talentPoints, Is.EqualTo(2));
        yield return Click(1060, 520);
        Assert.That(game.Store.Warehouse.town.talents[7], Is.Zero);
        Assert.That(game.Store.Warehouse.town.talentPoints, Is.EqualTo(3));
        yield return Click(1060, 470);
        string captures = Path.GetFullPath("Captures/InventoryStars"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "three-attribute-stars.png")); yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator PaperDollSupportsEightDragTargetsAndThreeIndependentRings()
    {
        game.StartNewRun(12345); yield return null;
        var run = game.Run;
        var kinds = new[] { ItemKind.SwordShield, ItemKind.LeatherHelm, ItemKind.LeatherArmor, ItemKind.LeatherGloves, ItemKind.LeatherBoots,
            ItemKind.CopperRing, ItemKind.CopperRing, ItemKind.CopperRing, ItemKind.CopperRing };
        for (int i = 0; i < kinds.Length; i++) run.inventory[i].units.Add(new ItemUnit { id = run.nextItemId++, kind = kinds[i], acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ });
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        var targets = new[] { new Vector2(135, 330), new Vector2(305, 245), new Vector2(475, 330), new Vector2(135, 445), new Vector2(475, 445),
            new Vector2(135, 550), new Vector2(305, 550), new Vector2(475, 550) };
        for (int i = 0; i < 8; i++) yield return DragItem(630 + i % 5 * 123, 225 + i / 5 * 88, targets[i].x, targets[i].y);
        Assert.That(run.character.equipment.Count, Is.EqualTo(8));
        Assert.That(run.character.equipment.Count(i => run.attributeRules.Find(i.kind).type == EquipmentType.Ring), Is.EqualTo(3));
        yield return DragItem(999, 313, 305, 245); // A ring cannot replace a helmet.
        Assert.That(run.character.equipment.Count, Is.EqualTo(8)); Assert.That(run.inventory[8].units.Count, Is.EqualTo(1));
        PrototypeStore.ValidateRun(run);
        typeof(PrototypeGame).GetMethod("Notify", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { "" });
        yield return Click(145, 255);
        Assert.That((bool)typeof(PrototypeGame).GetField("attributeDetails", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game), Is.True);
        yield return Click(490, 233);
        Assert.That((bool)typeof(PrototypeGame).GetField("attributeDetails", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game), Is.False);
        string captures = Path.GetFullPath("Captures/InventoryStars"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "eight-slot-equipment.png")); yield return null; yield return null;
    }
    [UnityTest]
    public IEnumerator HeldDirectionMovesSmoothlyAcrossCellBoundariesAndFinishesOnRelease()
    {
        game.StartNewRun(12345); game.Run.mapIndex = game.Run.maps.Count - 1; game.Run.x = 3; game.Run.y = 5;
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null; yield return null;
        var visualField = typeof(PrototypeGame).GetField("exploreVisualPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        driver.keyState = new KeyboardState(Key.D);
        float elapsed = 0, travelled = 0; var previous = (Vector2)visualField.GetValue(game);
        while (elapsed < .8f)
        {
            yield return null;
            var current = (Vector2)visualField.GetValue(game);
            float frameDistance = 3f * Mathf.Min(Time.deltaTime, .1f);
            Assert.That(current.y, Is.EqualTo(5));
            Assert.That(current.x - previous.x, Is.EqualTo(frameDistance).Within(.003f), "Crossing a grid center must preserve constant speed without a stall or teleport.");
            travelled += current.x - previous.x; elapsed += Mathf.Min(Time.deltaTime, .1f); previous = current;
        }
        Assert.That(travelled, Is.GreaterThan(2));
        driver.keyState = new KeyboardState(); yield return new WaitForSeconds(.4f);
        var stopped = (Vector2)visualField.GetValue(game);
        Assert.That(stopped, Is.EqualTo(new Vector2(game.Run.x, game.Run.y)));
        Assert.That(stopped.x, Is.EqualTo(Mathf.Round(stopped.x)));
        yield return new WaitForSeconds(.15f);
        Assert.That((Vector2)visualField.GetValue(game), Is.EqualTo(stopped));
        PrototypeStore.ValidateRun(game.Run);
    }
    [UnityTest]
    public IEnumerator ToothMerchantOpensByInteractionBuysThroughUiAndClosesWithEscape()
    {
        game.StartNewRun(12345); var run = game.Run;
        run.mapIndex = run.maps.FindIndex(m => m.hasMerchant); var map = run.Map;
        Assert.That(map.hasMerchant, Is.True);
        var p = MapGenerator.Directions.Select(d => new Vector2Int(map.merchantX, map.merchantY) + d)
            .First(v => map.CanTraverse(v.x, v.y));
        run.x = p.x; run.y = p.y; run.teeth = 20; MapGenerator.Reveal(run);
        typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { true });
        yield return null;
        Assert.That(GameObject.Find("集齿者"), Is.Not.Null);
        driver.keyState = new KeyboardState(Key.E); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Merchant));
        yield return Click(930, 280);
        Assert.That(run.teeth, Is.EqualTo(15));
        Assert.That(run.inventory.SelectMany(s => s.units).Count(i => i.kind == ItemKind.Potion), Is.EqualTo(1));
        string captures = Path.GetFullPath("Captures/ToothMerchant"); Directory.CreateDirectory(captures);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "consumables-shop.png")); yield return null; yield return null;
        driver.keyState = new KeyboardState(Key.Escape); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
        Assert.That(game.BuyFromMerchant(ItemKind.Torch), Is.Not.Empty);
        Assert.That(run.teeth, Is.EqualTo(15));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "merchant-exploration.png")); yield return null; yield return null;
        game.Store.SaveCheckpoint(run); game.ContinueRun();
        Assert.That(game.Run.teeth, Is.EqualTo(15));
        Assert.That(game.Run.inventory.SelectMany(s => s.units).Count(i => i.kind == ItemKind.Potion), Is.EqualTo(1));
    }
    [UnityTest]
    public IEnumerator TownHeldDirectionAlsoMovesSmoothlyAndStopsAtCellCenter()
    {
        var visualField = typeof(PrototypeGame).GetField("townVisualPosition", BindingFlags.Instance | BindingFlags.NonPublic);
        driver.keyState = new KeyboardState(Key.D);
        var previous = (Vector2)visualField.GetValue(game); float elapsed = 0;
        while (elapsed < .65f)
        {
            yield return null;
            var current = (Vector2)visualField.GetValue(game);
            Assert.That(current.x - previous.x, Is.EqualTo(4f * Mathf.Min(Time.deltaTime, .1f)).Within(.003f));
            Assert.That(current.y, Is.EqualTo(previous.y));
            previous = current; elapsed += Mathf.Min(Time.deltaTime, .1f);
        }
        driver.keyState = new KeyboardState(); yield return new WaitForSeconds(.3f);
        Assert.That((Vector2)visualField.GetValue(game), Is.EqualTo(game.TownPosition));
        Assert.That(game.TownPosition.x, Is.EqualTo(Mathf.Round(game.TownPosition.x)));
    }
}
