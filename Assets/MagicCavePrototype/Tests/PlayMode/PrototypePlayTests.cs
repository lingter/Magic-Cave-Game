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
        Assert.That(game.Store.Warehouse.town.gold, Is.EqualTo(90));
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
        yield return Click(430, 320); yield return Click(840, 582);
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Explore)); Assert.That(game.Run.seed, Is.EqualTo(-2026));
        yield return Click(1010, 687); Assert.That(game.Panel, Is.EqualTo(GamePanel.Inventory));
        yield return Click(1080, 609); Assert.That(game.Panel, Is.EqualTo(GamePanel.None));
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
    public IEnumerator WisdomDepartureCharacterEquipmentAndDynamicCardTextWork()
    {
        string captures = Path.GetFullPath("Captures/CharacterAttributes"); Directory.CreateDirectory(captures);
        typeof(PrototypeGame).GetMethod("RequestNewRun", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; yield return null;
        Assert.That(game.Panel, Is.EqualTo(GamePanel.NewRun));
        yield return Click(830, 583); Assert.That(game.Run, Is.Null, "Departure requires an explicit secondary choice.");
        yield return Click(825, 320);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "departure-wisdom.png")); yield return null; yield return null;
        yield return Click(830, 583);
        Assert.That(game.Run.character.secondary, Is.EqualTo(AttributeKind.Wisdom));
        Assert.That(game.Run.UsesAttributes, Is.True);
        var run = game.Run;
        run.inventory[0].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.InscribedSword, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ });
        run.inventory[1].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.WisdomEssence, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ });
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        yield return Click(141, 256);
        Assert.That(game.Panel, Is.EqualTo(GamePanel.Character));
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "equipment-comparison.png")); yield return null; yield return null;
        yield return Click(1090, 598);
        Assert.That(run.character.equipment.Single().kind, Is.EqualTo(ItemKind.InscribedSword));
        yield return Click(1090, 136);
        driver.keyState = new KeyboardState(Key.B); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        yield return Click(362, 256); Assert.That(CharacterAttributes.Calculate(run).wisdom, Is.EqualTo(6));
        yield return Click(1080, 609);
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
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成14点物理伤害"));
        Assert.That(damage, Is.EqualTo(14));
        game.Combat.spirit = 2;
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成16点物理伤害"));
        game.Combat.enemyBlock = 5;
        Assert.That(game.CardDescription(run, CardKind.Heavy, true), Is.EqualTo("造成16点物理伤害"));
        Assert.That(game.EquipInventory(0), Does.Contain("探索")); Assert.That(game.UseInventory(1), Is.False);
        ScreenCapture.CaptureScreenshot(Path.Combine(captures, "battle-dynamic-damage.png")); yield return null; yield return null;
        game.Store.SaveCheckpoint(run); game.ContinueRun(); yield return null;
        Assert.That(game.Run.character.secondary, Is.EqualTo(AttributeKind.Wisdom));
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
    public IEnumerator DoorCluesRespectFogPersistAndDisappearWhenWallBreaks()
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
        Assert.That(MapGenerator.IsDoorHintWall(map, neighbor.x, neighbor.y), Is.False);
        game.Run.x = approach.x; game.Run.y = approach.y; MapGenerator.Reveal(game.Run);
        Assert.That(map.Known(door.x, door.y), Is.False, "The clue must appear before the door is revealed");
        Assert.That(MapGenerator.IsDoorHintWall(map, neighbor.x, neighbor.y), Is.True);
        var rebuild = typeof(PrototypeGame).GetMethod("BuildWorld", BindingFlags.Instance | BindingFlags.NonPublic);
        rebuild.Invoke(game, new object[] { true }); yield return null; yield return null;
        var tags = game.GetComponentsInChildren<PrototypeTarget>(true);
        var wall = tags.First(t => t.kind == TargetKind.Wall && t.x == neighbor.x && t.y == neighbor.y && t.name == "Wall");
        Assert.That(wall.gameObject.activeInHierarchy, Is.True);
        Assert.That(wall.GetComponent<SpriteRenderer>().color, Is.EqualTo(new Color(1, 0.77f, 0.25f)));
        var hiddenDoor = tags.First(t => t.kind == TargetKind.Door && t.x == door.x && t.y == door.y && t.name == "前进门");
        Assert.That(hiddenDoor.gameObject.activeInHierarchy, Is.False);
        foreach (var direction in MapGenerator.Directions)
        {
            var point = door + direction;
            if (point == neighbor) continue;
            Assert.That(MapGenerator.IsDoorHintWall(map, point.x, point.y), Is.EqualTo(map.Known(point.x, point.y)));
            var hiddenWall = tags.First(t => t.kind == TargetKind.Wall && t.x == point.x && t.y == point.y && t.name == "Wall");
            Assert.That(hiddenWall.gameObject.activeInHierarchy, Is.EqualTo(PixelWallTiles.IsOutlineVisible(map, point.x, point.y)));
            Assert.That(hiddenWall.GetComponent<SpriteRenderer>().color,
                Is.EqualTo(map.Known(point.x, point.y) ? new Color(1, 0.77f, 0.25f) : Color.white));
        }
        var copy = Rules.Copy(game.Run); Assert.That(MapGenerator.IsDoorHintWall(copy.Map, neighbor.x, neighbor.y), Is.True);
        game.Run.x = map.spawnX; game.Run.y = map.spawnY; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; Assert.That(wall.GetComponent<SpriteRenderer>().color, Is.EqualTo(new Color(1, 0.77f, 0.25f)));
        map.Set(neighbor.x, neighbor.y, Tile.Ground); game.Run.x = neighbor.x; game.Run.y = neighbor.y; MapGenerator.Reveal(game.Run);
        rebuild.Invoke(game, new object[] { true }); yield return null;
        Assert.That(MapGenerator.IsDoorHintWall(map, neighbor.x, neighbor.y), Is.False);
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
        game.StartNewRun(12345); game.Run.mapIndex = 4; game.Run.x = 6; game.Run.y = 6;
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
        game.StartNewRun(12345); game.Run.mapIndex = 4; game.Run.x = 6; game.Run.y = 10;
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
        game.StartNewRun(12345); game.Run.mapIndex = 4; game.Run.x = 6; game.Run.y = 8;
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
    public IEnumerator DiscoveredLootAndLabelRemainVisibleAfterLeavingViewUntilPickup()
    {
        game.StartNewRun(12345); var map = game.Run.Map; var loot = map.loot[0];
        var guard = map.LivingGuard(loot);
        if (guard != null) Growth.FinishVictory(game.Run, guard);
        Array.Clear(map.revealed, 0, map.revealed.Length);
        game.Run.x = loot.x; game.Run.y = loot.y; MapGenerator.Reveal(game.Run);
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
        game.Run.x = loot.x; game.Run.y = loot.y; MapGenerator.Reveal(game.Run);
        InteractWithLoot(loot); yield return null;
        Assert.That(loot.units.Count, Is.Zero); Assert.That(Inventory.Items(game.Run).Count, Is.GreaterThan(0));
        Assert.That(game.GetComponentsInChildren<PrototypeTarget>(true).Any(t => t.kind == TargetKind.Loot && t.id == loot.id && t.gameObject.activeInHierarchy), Is.False);
        var remnant = game.GetComponentsInChildren<SpriteRenderer>(true).Single(r => r.name == "Item " + loot.id && r.gameObject.activeInHierarchy);
        Assert.That(remnant.color.a, Is.EqualTo(0.25f));
        Assert.That(remnant.GetComponent<Collider2D>(), Is.Null);
        Assert.That(remnant.sprite, Is.EqualTo(game.artSet.loot[(int)loot.appearanceKind]));
        Assert.That(map.CanTraverse(loot.x, loot.y), Is.True);
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
            Is.EqualTo(new Color(1, 0.77f, 0.25f)), "Actually discovered walls retain their existing door clues.");
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

    void CaptureWallCamera(string filename, Vector2Int center, int mapSize)
    {
        var camera = (Camera)typeof(PrototypeGame).GetField("gameCamera", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game);
        var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
        var oldPosition = camera.transform.position; float oldSize = camera.orthographicSize; float oldAspect = camera.aspect;
        var target = RenderTexture.GetTemporary(448, 448, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(448, 448, TextureFormat.RGBA32, false);
        try
        {
            float half = (mapSize - 1) * 0.5f;
            camera.transform.position = new Vector3(center.x - half, center.y - half, -10);
            camera.orthographicSize = 3.5f; camera.aspect = 1; camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 448, 448), 0, 0); image.Apply();
            string captures = Path.GetFullPath("Captures/MagicCavePrototype/PixelArt"); Directory.CreateDirectory(captures);
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
        game.StartNewRun(12345); game.Run.mapIndex = 4; game.Run.x = 5; game.Run.y = 4;
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
}
