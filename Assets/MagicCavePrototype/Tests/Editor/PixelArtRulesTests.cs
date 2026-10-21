using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class PixelArtRulesTests
{
    [Test]
    public void AllWallNeighborhoodsProduceCompleteOneUnitSprites()
    {
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt"); Assert.That(art, Is.Not.Null); art.Validate();
        using (var tiles = new PixelWallTiles(art))
        {
            for (int mask = 0; mask < 256; mask++)
            {
                var sprite = tiles.Get(mask);
                Assert.That(sprite, Is.Not.Null, "Missing wall neighborhood " + mask);
                Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(16, 16)));
                Assert.That(sprite.bounds.size.x, Is.EqualTo(1).Within(0.001));
                Assert.That(sprite.texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
            Assert.That(tiles.Get(255).name, Does.StartWith("Wall topology"), "A filled 3x3 wall needs a solid center rather than a thin junction.");
            Assert.That(tiles.Get(2), Is.SameAs(art.walls[0]), "Thin horizontal endpoints should keep the original artwork.");
        }
    }

    [TestCase(15 | 32 | 64 | 128, 8, TestName = "ThickWallCorner_OpenNorthEast_ConnectsNorthAndEast")]
    [TestCase(15 | 16 | 64 | 128, 4, TestName = "ThickWallCorner_OpenSouthEast_ConnectsSouthAndEast")]
    [TestCase(15 | 16 | 32 | 128, 5, TestName = "ThickWallCorner_OpenSouthWest_ConnectsSouthAndWest")]
    [TestCase(15 | 16 | 32 | 64, 9, TestName = "ThickWallCorner_OpenNorthWest_ConnectsNorthAndWest")]
    public void ThickWallCornersReuseCompleteConnectionArtwork(int mask, int wallNumber)
    {
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt"); Assert.That(art, Is.Not.Null); art.Validate();
        using (var tiles = new PixelWallTiles(art))
            Assert.That(tiles.Get(mask), Is.SameAs(art.walls[wallNumber - 1]));
    }

    [Test]
    public void TwoByTwoWallBlockMatchesTheTotalReferenceCornerLayout()
    {
        var map = new MapData { size = 4, cells = new int[16], revealed = new bool[16] };
        for (int y = 1; y <= 2; y++) for (int x = 1; x <= 2; x++) map.Set(x, y, Tile.Wall);
        Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 1, 2)), Is.EqualTo(4));
        Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 2, 2)), Is.EqualTo(5));
        Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 1, 1)), Is.EqualTo(8));
        Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 2, 1)), Is.EqualTo(9));
        Assert.That(PixelWallTiles.ExistingTile(15), Is.EqualTo(6), "The isolated cross in the total reference uses tile 6.");
    }

    [Test]
    public void RoomPerimeterUsesContinuousReferenceStripsBetweenTheFourCorners()
    {
        var map = new MapData { size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), revealed = new bool[49] };
        for (int y = 2; y <= 4; y++) for (int x = 2; x <= 4; x++) map.Set(x, y, Tile.Ground);
        var expectedTop = new[] { 4, 2, 2, 2, 5 };
        var expectedBottom = new[] { 8, 2, 2, 2, 9 };
        for (int x = 1; x <= 5; x++)
        {
            Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, x, 5)), Is.EqualTo(expectedTop[x - 1]), "Top perimeter at " + x);
            Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, x, 1)), Is.EqualTo(expectedBottom[x - 1]), "Bottom perimeter at " + x);
        }
        for (int y = 2; y <= 4; y++)
        {
            Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 1, y)), Is.EqualTo(11), "Left perimeter at " + y);
            Assert.That(PixelWallTiles.ExistingTile(PixelWallTiles.Mask(map, 5, y)), Is.EqualTo(11), "Right perimeter at " + y);
        }
    }

    [Test]
    public void DiscoveredFloorAloneDoesNotExposeAnyUndiscoveredWalls()
    {
        var map = new MapData { size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), revealed = new bool[49] };
        map.Set(3, 3, Tile.Ground); map.revealed[map.Index(3, 3)] = true;
        var discovery = (bool[])map.revealed.Clone();
        for (int y = 0; y < map.size; y++) for (int x = 0; x < map.size; x++)
            Assert.That(PixelWallTiles.IsOutlineVisible(map, x, y), Is.False, $"Undiscovered wall at {x},{y}");
        map.Set(2, 2, Tile.Advance); map.Set(4, 4, Tile.Ground);
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 2, 2), Is.False, "Outline completion must not expose a door.");
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 4, 4), Is.False, "Outline completion must not discover diagonal ground.");
        Assert.That(PixelWallTiles.IsOutlineVisible(map, -1, 3), Is.False);
        Assert.That(map.revealed, Is.EqualTo(discovery));
    }

    [TestCase(1, 1)] [TestCase(1, -1)] [TestCase(-1, -1)] [TestCase(-1, 1)]
    public void CornerCompletionRequiresBothDiscoveredWallSegments(int dx, int dy)
    {
        var map = new MapData { size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), revealed = new bool[49] };
        map.Set(3 + dx, 3 + dy, Tile.Ground);
        map.revealed[map.Index(3 + dx, 3 + dy)] = true;
        map.revealed[map.Index(3 + dx, 3)] = true;
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.False, "One known arm cannot discover the other arm or its corner.");
        map.revealed[map.Index(3, 3 + dy)] = true;
        var discovery = (bool[])map.revealed.Clone();
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.True);
        Assert.That(map.Known(3, 3), Is.False);
        Assert.That(map.revealed, Is.EqualTo(discovery));
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3 - dx, 3), Is.False, "The corner must not extend a wall into fog.");
        map.Set(3 + dx, 3, Tile.Ground);
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.False, "A broken arm is no longer a corner connection.");
        map.Set(3 + dx, 3, Tile.Advance);
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.False, "A door is not a wall connection anchor.");
        map.Set(3 + dx, 3, Tile.Wall); map.Set(3, 3, Tile.Advance);
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.False, "Completion must never expose a hidden door.");
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    public void CorridorSideWallsStayHiddenUntilActuallyDiscovered(int turns)
    {
        Vector2Int Rotate(int x, int y)
        {
            var point = new Vector2Int(x - 4, y - 4);
            for (int turn = 0; turn < turns; turn++) point = new Vector2Int(-point.y, point.x);
            return point + new Vector2Int(4, 4);
        }
        var run = MapGenerator.NewRun(12345, false);
        var map = new MapData { size = 9, cells = Enumerable.Repeat((int)Tile.Wall, 81).ToArray(), revealed = new bool[81] };
        for (int y = 4; y <= 6; y++) for (int x = 3; x <= 5; x++)
        { var ground = Rotate(x, y); map.Set(ground.x, ground.y, Tile.Ground); }
        for (int y = 1; y <= 3; y++)
        { var ground = Rotate(4, y); map.Set(ground.x, ground.y, Tile.Ground); }
        run.maps[0] = map;
        foreach (var position in new[] { Rotate(3, 4), Rotate(5, 4), Rotate(4, 4), Rotate(4, 3) })
        { run.x = position.x; run.y = position.y; MapGenerator.Reveal(run); }
        var discovery = (bool[])map.revealed.Clone();
        foreach (var position in new[] { Rotate(3, 2), Rotate(5, 2), Rotate(2, 2), Rotate(6, 2) })
        {
            Assert.That(map.Known(position.x, position.y), Is.False);
            Assert.That(PixelWallTiles.IsOutlineVisible(map, position.x, position.y), Is.False, "Unseen corridor side at " + position);
        }
        foreach (var position in new[] { Rotate(2, 3), Rotate(6, 3) })
        {
            Assert.That(map.Known(position.x, position.y), Is.False);
            Assert.That(PixelWallTiles.IsOutlineVisible(map, position.x, position.y), Is.True, "Missing discovered-wall connector at " + position);
        }
        Assert.That(map.revealed, Is.EqualTo(discovery));
        var next = Rotate(4, 2); run.x = next.x; run.y = next.y; MapGenerator.Reveal(run);
        foreach (var position in new[] { Rotate(3, 2), Rotate(5, 2) })
        {
            Assert.That(map.Known(position.x, position.y), Is.True);
            Assert.That(PixelWallTiles.IsOutlineVisible(map, position.x, position.y), Is.True);
        }
    }

    [Test]
    public void BothEndsOfEverySharedWallEdgeChooseTheSameOutlineConnection()
    {
        for (int axis = 0; axis < 2; axis++)
        {
            var map = new MapData { size = 6, cells = Enumerable.Repeat((int)Tile.Wall, 36).ToArray(), revealed = new bool[36] };
            var first = new Vector2Int(2, 2); var second = first + (axis == 0 ? Vector2Int.right : Vector2Int.up);
            var free = new System.Collections.Generic.List<Vector2Int>();
            for (int y = 1; y <= (axis == 0 ? 3 : 4); y++) for (int x = 1; x <= (axis == 0 ? 4 : 3); x++)
                if (new Vector2Int(x, y) != first && new Vector2Int(x, y) != second) free.Add(new Vector2Int(x, y));
            for (int state = 0; state < 1024; state++)
            {
                for (int bit = 0; bit < free.Count; bit++) map.Set(free[bit].x, free[bit].y, (state & (1 << bit)) == 0 ? Tile.Ground : Tile.Wall);
                int a = PixelWallTiles.OutlineMask(PixelWallTiles.Mask(map, first.x, first.y));
                int b = PixelWallTiles.OutlineMask(PixelWallTiles.Mask(map, second.x, second.y));
                Assert.That((a & (axis == 0 ? 2 : 1)) != 0, Is.EqualTo((b & (axis == 0 ? 8 : 4)) != 0), $"Axis {axis}, neighborhood {state}");
            }
        }
        Assert.That(PixelWallTiles.OutlineMask(255), Is.Zero, "Solid wall interiors have no exposed branches.");
    }

    [TestCase(23)] [TestCase(39)] [TestCase(77)] [TestCase(141)]
    public void ThickThinJunctionKeepsTheFullHorizontalRoofAndFourPixelSideShadow(int mask)
    {
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt"); art.Validate();
        using (var tiles = new PixelWallTiles(art))
        {
            var pixels = tiles.Get(mask).texture.GetPixels32();
            var vertical = art.walls[10].texture.GetPixels32();
            var horizontal = art.walls[1].texture.GetPixels32();
            int start = PixelWallTiles.OutlineMask(mask) == 7 ? 0 : 12;
            int open = start == 0 ? 15 : 0;
            for (int y = 0; y < 16; y++) for (int x = start; x < start + 4; x++)
                Assert.That(pixels[y * 16 + x], Is.EqualTo(vertical[y * 16 + x]), "Missing side outline/shadow column.");
            for (int y = 10; y < 16; y++)
                Assert.That(pixels[y * 16 + open], Is.EqualTo(horizontal[y * 16 + open]), "The horizontal roof must meet the adjoining original strip.");
            var gray = vertical[8 * 16 + 8];
            Assert.That(Enumerable.Range(0, 10).Any(y => !pixels[y * 16 + open].Equals(gray)), Is.True,
                "The open horizontal arm must not be an unoutlined gray slab.");
        }
    }

    [Test]
    public void ScreenshotRegressionJunctionsHaveContinuousRoofsAndExportAPixelPreview()
    {
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt"); art.Validate();
        var map = new MapData { size = 11, cells = Enumerable.Repeat((int)Tile.Wall, 121).ToArray(), revealed = new bool[121] };
        for (int y = 3; y <= 6; y++) for (int x = 4; x <= 8; x++)
        { map.Set(x, y, Tile.Ground); map.revealed[map.Index(x, y)] = true; }
        for (int y = 1; y <= 9; y++) map.Set(2, y, Tile.Ground);
        for (int y = 2; y <= 7; y++) for (int x = 3; x <= 9; x++)
            if (x == 3 || x == 9 || y == 2 || y == 7) map.revealed[map.Index(x, y)] = true;
        Assert.That(PixelWallTiles.Mask(map, 3, 7), Is.EqualTo(23));
        Assert.That(PixelWallTiles.Mask(map, 3, 2), Is.EqualTo(39));
        using (var tiles = new PixelWallTiles(art))
        {
            foreach (int y in new[] { 2, 7 })
            {
                var junction = SpritePixels(tiles.Get(map, 3, y));
                var strip = SpritePixels(tiles.Get(map, 4, y));
                for (int row = 10; row < 16; row++)
                    Assert.That(junction[row * 16 + 15], Is.EqualTo(strip[row * 16]), "The screenshot junction roof must join its next horizontal wall.");
            }
            const int scale = 4; int width = map.size * 16 * scale;
            var fog = new Color32(5, 6, 10, 255);
            var pixels = Enumerable.Repeat(fog, width * width).ToArray();
            var images = new System.Collections.Generic.Dictionary<Sprite, Color32[]>();
            for (int y = 0; y < map.size; y++) for (int x = 0; x < map.size; x++)
            {
                bool wall = map.At(x, y) == Tile.Wall;
                if (!map.Known(x, y) && !PixelWallTiles.IsOutlineVisible(map, x, y)) continue;
                var sprite = wall ? tiles.Get(map, x, y) : art.floors[(x * 17 + y * 31) % 3];
                if (!images.TryGetValue(sprite, out var source)) images.Add(sprite, source = SpritePixels(sprite));
                for (int py = 0; py < 16; py++) for (int px = 0; px < 16; px++)
                    for (int sy = 0; sy < scale; sy++) for (int sx = 0; sx < scale; sx++)
                    {
                        var color = source[py * 16 + px];
                        pixels[((y * 16 + py) * scale + sy) * width + (x * 16 + px) * scale + sx] = color.a == 0 ? fog : color;
                    }
            }
            var image = new Texture2D(width, width, TextureFormat.RGBA32, false);
            try
            {
                image.SetPixels32(pixels); image.Apply(false, false);
                string folder = System.IO.Path.GetFullPath("Captures/MagicCavePrototype/PixelArt"); System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "wall-junction-fix-check.png"), image.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(image); }
        }
    }

    static Color32[] SpritePixels(Sprite sprite)
    {
        if (sprite.texture.isReadable) return sprite.texture.GetPixels32();
        var copy = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        try
        {
            copy.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(sprite.texture)));
            return copy.GetPixels32();
        }
        finally { Object.DestroyImmediate(copy); }
    }

    [Test]
    [TestCase(7, 3, 8)] [TestCase(7, 6, 4)]
    [TestCase(11, 3, 8)] [TestCase(11, 9, 9)]
    [TestCase(13, 9, 9)] [TestCase(13, 12, 5)]
    [TestCase(14, 6, 4)] [TestCase(14, 12, 5)]
    [TestCase(15, 3, 8)] [TestCase(15, 6, 4)]
    [TestCase(15, 9, 9)] [TestCase(15, 12, 5)]
    public void FoggedJunctionCompletesOnlyTheDiscoveredCorner(int topology, int visibleArms, int wallNumber)
    {
        var map = new MapData { size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), revealed = new bool[49] };
        var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        for (int bit = 0; bit < 4; bit++)
        {
            var p = new Vector2Int(3, 3) + directions[bit];
            map.Set(p.x, p.y, (topology & (1 << bit)) != 0 ? Tile.Wall : Tile.Ground);
            map.revealed[map.Index(p.x, p.y)] = (visibleArms & (1 << bit)) != 0;
        }
        for (int dx = -1; dx <= 1; dx += 2) for (int dy = -1; dy <= 1; dy += 2)
        {
            map.Set(3 + dx, 3 + dy, Tile.Ground);
            int pair = (dx > 0 ? 2 : 8) | (dy > 0 ? 1 : 4);
            map.revealed[map.Index(3 + dx, 3 + dy)] = pair == visibleArms;
        }
        var discovery = (bool[])map.revealed.Clone();
        Assert.That(PixelWallTiles.OutlineMask(PixelWallTiles.Mask(map, 3, 3)), Is.EqualTo(topology));
        Assert.That(PixelWallTiles.IsOutlineVisible(map, 3, 3), Is.True, "Do not hide the needed corner with the unknown junction branches.");
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt");
        using (var tiles = new PixelWallTiles(art))
            Assert.That(tiles.Get(map, 3, 3), Is.SameAs(art.walls[wallNumber - 1]), "Render a corner, not the entire hidden T/cross.");
        Assert.That(map.revealed, Is.EqualTo(discovery));
    }

    [Test]
    public void SupplementalJunctionsNeverConnectUndiscoveredWallArms()
    {
        var neighbors = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left,
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1) };
        int corners = 0, complex = 0;
        for (int mask = 0; mask < 256; mask++)
        {
            var map = new MapData { size = 3, cells = Enumerable.Repeat((int)Tile.Wall, 9).ToArray(), revealed = new bool[9] };
            for (int bit = 0; bit < 8; bit++)
            {
                var p = new Vector2Int(1, 1) + neighbors[bit];
                map.Set(p.x, p.y, (mask & (1 << bit)) == 0 ? Tile.Ground : Tile.Wall);
            }
            int outline = PixelWallTiles.OutlineMask(mask);
            for (int discovery = 0; discovery < 256; discovery++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    var p = new Vector2Int(1, 1) + neighbors[bit];
                    map.revealed[map.Index(p.x, p.y)] = (discovery & (1 << bit)) != 0;
                }
                int expected = 0;
                for (int bit = 4; bit < 8; bit++)
                {
                    var offset = neighbors[bit]; int pair = (offset.x > 0 ? 2 : 8) | (offset.y > 0 ? 1 : 4);
                    int requiredDiscovery = pair | (1 << bit);
                    if ((mask & pair) == pair && (mask & (1 << bit)) == 0 &&
                        (discovery & requiredDiscovery) == requiredDiscovery) expected |= pair;
                }
                int actual = PixelWallTiles.SupplementalOutline(map, 1, 1);
                Assert.That(actual, Is.EqualTo(expected), $"Terrain {mask}, discovery {discovery}");
                Assert.That(actual & ~(discovery & mask & 15), Is.Zero, "No undiscovered wall arm may be added.");
                Assert.That(PixelWallTiles.IsOutlineVisible(map, 1, 1), Is.EqualTo(expected != 0));
                if (actual != 0)
                {
                    corners++;
                    if (outline == 7 || outline == 11 || outline == 13 || outline == 14 || outline == 15) complex++;
                }
                Assert.That(map.Known(1, 1), Is.False);
            }
        }
        Assert.That(corners, Is.GreaterThan(0)); Assert.That(complex, Is.GreaterThan(0));
    }

    [Test]
    public void DoorsRemainPartOfTheSurroundingWallTopology()
    {
        var map = new MapData { size = 3, cells = new int[9], revealed = new bool[9] };
        map.Set(1, 1, Tile.Wall); map.Set(2, 1, Tile.Advance); map.Set(0, 1, Tile.Return);
        Assert.That(PixelWallTiles.Mask(map, 1, 1) & 15, Is.EqualTo(2 | 8));
    }

    [Test]
    public void BreakingAWallUpdatesItsNeighborsWithoutChangingDistantTiles()
    {
        var map = new MapData { size = 7, cells = new int[49], revealed = new bool[49] };
        for (int y = 1; y < 6; y++) for (int x = 1; x < 6; x++) map.Set(x, y, Tile.Wall);
        int neighbor = PixelWallTiles.Mask(map, 3, 4), distant = PixelWallTiles.Mask(map, 1, 1);
        map.Set(3, 3, Tile.Ground);
        Assert.That(PixelWallTiles.Mask(map, 3, 4), Is.Not.EqualTo(neighbor));
        Assert.That(PixelWallTiles.Mask(map, 1, 1), Is.EqualTo(distant));
        Assert.That(PixelWallTiles.Mask(map, 3, 4) & 4, Is.Zero, "The wall north of the hole must expose its south face.");
    }

    [Test]
    public void SolidLootBlocksMovementButDroppedStacksDoNotTrapThePlayer()
    {
        var run = MapGenerator.NewRun(12345, false); run.mapIndex = 4; run.Map.enemies.Clear();
        run.x = 5; run.y = 4;
        var loot = MapGenerator.AddLoot(run, run.Map, new Vector2Int(5, 5), ItemKind.Potion, 1);
        Assert.That(run.Map.CanTraverse(5, 5), Is.False);
        Assert.That(run.Map.CanStand(5, 4.2f), Is.False);
        Inventory.Pickup(run, loot);
        Assert.That(run.Map.CanTraverse(5, 5), Is.True);
        Assert.That(loot.IsContainer, Is.True); Assert.That(loot.BlocksMovement, Is.False);
        Assert.That(Rules.Copy(run).Map.loot.Single(l => l.id == loot.id).appearanceKind, Is.EqualTo(ItemKind.Potion));
        int slot = run.inventory.FindIndex(s => s.units.Any());
        var dropped = Inventory.Drop(run, slot, 1);
        Assert.That(dropped.loose, Is.True); Assert.That(run.Map.CanStand(run.x, run.y), Is.True);
        Assert.That(dropped.IsContainer, Is.False); Assert.That(dropped.BlocksMovement, Is.False);
        Assert.That(Rules.Copy(run).Map.loot.Single(l => l.id == dropped.id).loose, Is.True);
    }

    [Test]
    public void EnlargedFootprintFitsOneCellCorridorsAndLegacyContainerPositionsRecover()
    {
        var map = new MapData { size = 5, cells = Enumerable.Repeat((int)Tile.Wall, 25).ToArray(), revealed = new bool[25] };
        for (int y = 1; y < 4; y++) map.Set(2, y, Tile.Ground);
        Assert.That(map.CanStand(2, 2), Is.True); Assert.That(map.CanStand(2.12f, 2), Is.False);
        var run = MapGenerator.NewRun(12345, false); run.mapIndex = 4; run.Map.enemies.Clear(); run.x = 6; run.y = 6;
        MapGenerator.AddLoot(run, run.Map, new Vector2Int(6, 6), ItemKind.Ore, 1);
        Assert.That(MapGenerator.EnsureSafePlayerPosition(run), Is.True);
        Assert.That(run.Map.CanStand(run.x, run.y), Is.True);
    }
}
