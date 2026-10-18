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
    [UnitySetUp]
    public IEnumerator Setup()
    {
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
    [UnityTest]
    public IEnumerator SeedMenuButtonsAndCardBattleWorkWithNewInputSystem()
    {
        Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Home));
        yield return Click(910, 424); Assert.That(game.customSeed, Is.True, "Seed toggle must respond to Input System mouse release");
        yield return Click(1090, 482); Assert.That(game.customSeedText, Is.Empty);
        var character = typeof(PrototypeGame).GetMethod("SeedCharacter", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (char value in "-2026") character.Invoke(game, new object[] { value });
        yield return Click(920, 222); Assert.That(game.ScreenMode, Is.EqualTo(GameScreen.Explore)); Assert.That(game.Run.seed, Is.EqualTo(-2026));
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
    public IEnumerator KeyboardMoveRespectsCollisionAndPause()
    {
        game.StartNewRun(12345); yield return null;
        int x = game.Run.CellX, y = game.Run.CellY;
        var direction = MapGenerator.Directions.First(d => game.Run.Map.Walkable(x + d.x, y + d.y));
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
        int wallX = 1, wallY = 13;
        Assert.That(game.Run.Map.At(wallX, wallY), Is.EqualTo(Tile.Wall));
        driver.keyState = new KeyboardState(Key.A); yield return new WaitForSeconds(0.3f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Run.x, Is.GreaterThanOrEqualTo(1.68f), "Wall collision must stop the capsule before crossing into the wall cell");
        float half = (game.Run.Map.size - 1) / 2f;
        var point = Camera.main.WorldToScreenPoint(new Vector3(wallX - half, 0.53f, wallY - half));
        Assert.That(Physics.Raycast(Camera.main.ScreenPointToRay(point), out RaycastHit hit), Is.True);
        var tag = hit.collider.GetComponent<PrototypeTarget>();
        Assert.That(tag, Is.Not.Null); Assert.That(tag.kind, Is.EqualTo(TargetKind.Wall));
        Assert.That(tag.x, Is.EqualTo(wallX)); Assert.That(tag.y, Is.EqualTo(wallY));
        driver.mouseState = new MouseState { position = point, buttons = 2 };
        yield return null; yield return null;
        driver.mouseState = new MouseState { position = point }; yield return null;
        Assert.That(game.Run.Map.At(wallX, wallY), Is.EqualTo(Tile.Ground));
        point = Camera.main.WorldToScreenPoint(new Vector3(wallX - half, 0, wallY - half));
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
        var door = new Vector2Int(doorIndex % map.size, doorIndex / map.size);
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
        Assert.That(wall.GetComponent<Renderer>().sharedMaterial.name, Does.Contain("door hint wall"));
        var hiddenDoor = tags.First(t => t.kind == TargetKind.Door && t.x == door.x && t.y == door.y && t.name == "前进门");
        Assert.That(hiddenDoor.gameObject.activeInHierarchy, Is.False);
        foreach (var direction in MapGenerator.Directions)
        {
            var point = door + direction;
            if (point == neighbor) continue;
            Assert.That(MapGenerator.IsDoorHintWall(map, point.x, point.y), Is.EqualTo(map.Known(point.x, point.y)));
            var hiddenWall = tags.First(t => t.kind == TargetKind.Wall && t.x == point.x && t.y == point.y && t.name == "Wall");
            Assert.That(hiddenWall.gameObject.activeInHierarchy, Is.EqualTo(map.Known(point.x, point.y)));
        }
        var copy = Rules.Copy(game.Run); Assert.That(MapGenerator.IsDoorHintWall(copy.Map, neighbor.x, neighbor.y), Is.True);
        game.Run.x = map.spawnX; game.Run.y = map.spawnY; MapGenerator.Reveal(game.Run);
        typeof(PrototypeGame).GetMethod("RefreshVisibility", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, null);
        yield return null; Assert.That(wall.GetComponent<Renderer>().sharedMaterial.name, Does.Contain("door hint wall"));
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
        var rotation = Quaternion.Euler(50, 0, 0);
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
        var rotation = Quaternion.Euler(50, 0, 0);
        Assert.That(Quaternion.Angle(Camera.main.transform.rotation, rotation), Is.LessThan(0.01));
        float half = (game.Run.Map.size - 1) / 2f;
        var focus = new Vector3(game.Run.x - half, 0.52f, game.Run.y - half);
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
        var point = Camera.main.WorldToScreenPoint(new Vector3(6 - half, 0, 7 - half));
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
        Assert.That(body.GetComponent<Collider>().isTrigger, Is.False);
        driver.keyState = new KeyboardState(Key.W); yield return new WaitForSeconds(0.4f);
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(game.Run.y, Is.LessThanOrEqualTo(8.32f)); Assert.That(game.Run.CellY, Is.EqualTo(8));
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
        var label = game.GetComponentsInChildren<TextMesh>(true).Single(t => t.transform.parent.name == "Item label" && Vector3.Distance(t.transform.parent.localPosition, new Vector3(loot.x - half, 0, loot.y - half)) < 0.01f);
        Assert.That(label.gameObject.activeInHierarchy, Is.True);
        game.Run.x = loot.x; game.Run.y = loot.y; MapGenerator.Reveal(game.Run);
        driver.mouseState = new MouseState(); driver.keyState = new KeyboardState(Key.E); yield return null; yield return null;
        driver.keyState = new KeyboardState(); yield return null;
        Assert.That(loot.units.Count, Is.Zero); Assert.That(Inventory.Items(game.Run).Count, Is.GreaterThan(0));
        Assert.That(game.GetComponentsInChildren<PrototypeTarget>(true).Any(t => t.kind == TargetKind.Loot && t.id == loot.id && t.gameObject.activeInHierarchy), Is.False);
    }
}
