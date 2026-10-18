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
        int slot = run.inventory.FindIndex(s => s.units.Any());
        var dropped = Inventory.Drop(run, slot, 1);
        Assert.That(dropped.loose, Is.True); Assert.That(run.Map.CanStand(run.x, run.y), Is.True);
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
