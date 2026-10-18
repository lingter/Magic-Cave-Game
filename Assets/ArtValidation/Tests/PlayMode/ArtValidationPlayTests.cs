#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using MagicCave.ArtValidation;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

[DefaultExecutionOrder(-1000)]
public sealed class ValidationInputDriver : MonoBehaviour
{
    [NonSerialized] public Keyboard keyboard;
    [NonSerialized] public Mouse mouse;
    [NonSerialized] public KeyboardState keys;
    [NonSerialized] public MouseState pointer;
    void Update()
    {
        keyboard.MakeCurrent(); mouse.MakeCurrent();
        InputState.Change(keyboard, keys); InputState.Change(mouse, pointer);
    }
}

public sealed class ArtValidationPlayTests
{
    ArtValidationRoom room;
    ValidationInputDriver input;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/ArtValidation10x10.unity", new LoadSceneParameters(LoadSceneMode.Single));
        // LoadSceneInPlayMode finishes on the following frame; create the input driver afterwards.
        yield return null; yield return null;
        input = new GameObject("Validation Test Input").AddComponent<ValidationInputDriver>();
        input.keyboard = InputSystem.AddDevice<Keyboard>(); input.mouse = InputSystem.AddDevice<Mouse>();
        yield return null; yield return null;
        room = UnityEngine.Object.FindFirstObjectByType<ArtValidationRoom>();
        Assert.That(room, Is.Not.Null);
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        InputSystem.RemoveDevice(input.keyboard); InputSystem.RemoveDevice(input.mouse);
        UnityEngine.Object.Destroy(input.gameObject); yield return null;
    }

    [UnityTest]
    public IEnumerator FixedLayoutCameraAndDisplayModesShareTheSameCollision()
    {
        Assert.That(room.wallTiles, Is.EqualTo(ArtValidationRoom.CreateLayout()));
        Assert.That(room.wallTiles.Length, Is.EqualTo(100));
        Assert.That(room.artRoot.transform.Find("Floors - 100 Cells").childCount, Is.EqualTo(100));
        Assert.That(room.roomCamera.orthographic, Is.True);
        Assert.That(room.props.Length, Is.EqualTo(9));
        var open = new bool[100];
        for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++) open[y * 10 + x] = room.CanStand(new Vector2(x, y));
        input.keys = new KeyboardState(Key.V, Key.G); yield return null; yield return null;
        Assert.That(room.WhiteboxVisible, Is.True); Assert.That(room.GridVisible, Is.True);
        Assert.That(room.artRoot.activeSelf, Is.False); Assert.That(room.whiteboxRoot.activeSelf, Is.True);
        for (int y = 0; y < 10; y++) for (int x = 0; x < 10; x++)
            Assert.That(room.CanStand(new Vector2(x, y)), Is.EqualTo(open[y * 10 + x]));
        input.keys = new KeyboardState(); yield return null;
        input.pointer = new MouseState { buttons = 4, delta = new Vector2(100, 50) };
        yield return null; yield return null;
        Assert.That(Quaternion.Angle(room.roomCamera.transform.rotation, Quaternion.identity), Is.LessThan(0.01));
        room.ResetRoom(); yield return null;
        Assert.That(room.player.sprite, Is.EqualTo(room.playerDown));
        Assert.That(room.player.transform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(room.player.GetComponent<BoxCollider2D>().bounds.size.x, Is.EqualTo(0.8f).Within(0.001));
        Assert.That(room.WhiteboxVisible || room.GridVisible, Is.False);
    }

    [UnityTest]
    public IEnumerator KeyboardCollisionAndNavigationReachTheFixedExit()
    {
        input.keys = new KeyboardState(Key.RightArrow); yield return new WaitForSeconds(0.35f);
        Assert.That(room.PlayerPosition.x, Is.InRange(1.05f, 1.101f), "The enlarged player must stop before its art intersects the wall at (2,1).");
        input.keys = new KeyboardState(Key.UpArrow); yield return new WaitForSeconds(0.2f);
        Assert.That(room.PlayerPosition.y, Is.GreaterThan(1.2f));
        input.keys = new KeyboardState(); yield return null;
        room.ResetRoom();
        Vector2 screenTarget = room.roomCamera.WorldToScreenPoint(new Vector3(8, 1, 0));
        input.pointer = new MouseState { position = screenTarget, buttons = 1 };
        yield return null; yield return null;
        input.pointer = new MouseState { position = screenTarget };
        float deadline = Time.time + 10;
        while (Vector2.Distance(room.PlayerPosition, new Vector2(8, 1)) > 0.02f && Time.time < deadline) yield return null;
        Assert.That(room.PlayerPosition.x, Is.EqualTo(8).Within(0.02));
        Assert.That(room.PlayerPosition.y, Is.EqualTo(1).Within(0.02));
        Assert.That(room.CanStand(new Vector2(0, 1)), Is.False);
        Assert.That(room.CanStand(new Vector2(6, 3)), Is.False, "Enemy must occupy its cell.");
    }

    [UnityTest]
    public IEnumerator ArrowKeysStopVisiblePlayerBeforePropsFromAllEightDirections()
    {
        var bag = room.props.First(p => p.kind == ValidationPropKind.Bag);
        Assert.That(room.CanStand(bag.transform.position), Is.False, "The supply bag must block movement until collected.");
        var chest = room.props.First(p => p.displayName == "宝箱");
        var originalPosition = chest.transform.position;
        // This clear area allows the same prop to be approached from every direction.
        chest.transform.position = new Vector3(4, 3, 0);
        try
        {
            foreach (var from in new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left,
                new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(-1, -1) })
            {
                Vector2 start = new Vector2(4, 3) + from;
                Assert.That(room.CanStand(start), Is.True);
                room.player.transform.position = start;
                var keys = new System.Collections.Generic.List<Key>();
                if (from.x > 0) keys.Add(Key.LeftArrow);
                if (from.x < 0) keys.Add(Key.RightArrow);
                if (from.y > 0) keys.Add(Key.DownArrow);
                if (from.y < 0) keys.Add(Key.UpArrow);
                input.keys = new KeyboardState(keys.ToArray()); yield return new WaitForSeconds(0.5f);
                input.keys = new KeyboardState(); yield return null;
                Assert.That(room.CanStand(room.PlayerPosition), Is.True);
                // The current direction sprites all have a 12x12 opaque footprint on a 16px canvas.
                var visiblePlayer = new Rect(room.PlayerPosition - Vector2.one * 0.375f, Vector2.one * 0.75f);
                Assert.That(visiblePlayer.Overlaps(new Rect(3.5f, 2.5f, 1, 1)), Is.False,
                    "Visible player art overlaps the chest when approaching from " + from);
            }
        }
        finally { chest.transform.position = originalPosition; room.ResetRoom(); }
        // The bag can be collected from the adjacent tile and then reset like a chest.
        room.player.transform.position = new Vector3(5, 2, 0);
        input.keys = new KeyboardState(Key.E); yield return null; yield return null;
        Assert.That(bag.gameObject.activeSelf, Is.False);
        Assert.That(room.CanStand(new Vector2(5, 1)), Is.True);
        room.ResetRoom();
        Assert.That(room.CanStand(new Vector2(5, 1)), Is.False);
    }

    [UnityTest]
    public IEnumerator InteractionsResetAndScreenshotsWorkInTheAuthoredScene()
    {
        var chest = room.props.First(p => p.displayName == "宝箱");
        room.player.transform.position = new Vector3(3, 7, 0);
        input.keys = new KeyboardState(Key.UpArrow); yield return new WaitForSeconds(0.3f);
        input.keys = new KeyboardState(); yield return null;
        Assert.That(room.PlayerPosition.y + 0.375f, Is.LessThanOrEqualTo(7.5f), "The player's visible edge must stop below the chest.");
        string directory = Path.GetFullPath("Captures/ArtValidation10x10"); Directory.CreateDirectory(directory);
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "playmode-contact.png"));
        yield return null; yield return null;
        input.keys = new KeyboardState(Key.E); yield return null; yield return null;
        Assert.That(chest.gameObject.activeSelf, Is.False);
        Assert.That(room.CanStand(new Vector2(3, 8)), Is.True);
        input.keys = new KeyboardState(); yield return null;
        input.keys = new KeyboardState(Key.R); yield return null; yield return null;
        Assert.That(chest.gameObject.activeSelf, Is.True);
        Assert.That(room.PlayerPosition, Is.EqualTo(new Vector2(1, 1)));
        input.keys = new KeyboardState(); yield return null;
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "playmode-art.png"));
        yield return null; yield return null;
        room.SetWhitebox(true); room.SetGrid(true);
        yield return null;
        ScreenCapture.CaptureScreenshot(Path.Combine(directory, "playmode-whitebox.png"));
        yield return null; yield return null;
        Assert.That(File.Exists(Path.Combine(directory, "playmode-art.png")), Is.True);
        room.ResetRoom();
    }
}
#endif
