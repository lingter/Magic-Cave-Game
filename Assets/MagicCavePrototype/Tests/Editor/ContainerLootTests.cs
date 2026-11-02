using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class ContainerLootTests
{
    RunData New(int seed = 12345) => MapGenerator.NewRun(seed, AttributeKind.Agility, false);
    static GroundLoot Accessible(RunData run, ContainerKind kind)
    {
        var loot = run.Map.loot.First(l => l.containerKind == kind);
        return loot;
    }

    [Test]
    public void OneRemainingGuardDoesNotBlockSavedContainerPickup()
    {
        var run = New(); var loot = run.Map.loot.First(l => l.guardEnemyId != 0);
        Assert.That(loot.secondGuardEnemyId, Is.Not.Zero.And.Not.EqualTo(loot.guardEnemyId));
        run.Map.enemies.Single(e => e.id == loot.guardEnemyId).defeated = true;
        var copy = Rules.Copy(run); var saved = copy.Map.loot.Single(l => l.id == loot.id);
        Assert.That(copy.Map.LivingGuard(saved).id, Is.EqualTo(loot.secondGuardEnemyId));
        Assert.That(Inventory.Pickup(copy, saved), Is.GreaterThan(0));
        Assert.That(copy.Map.LivingGuard(saved).id, Is.EqualTo(loot.secondGuardEnemyId));
        Assert.That(saved.HasContents, Is.False); Assert.That(saved.BlocksMovement, Is.True);
        Assert.That(copy.Map.CanTraverse(saved.x, saved.y), Is.False);
        PrototypeStore.ValidateRun(copy);
    }

    [Test]
    public void ContainerLayoutsKeepSpacingAndPermanentRoutesAcrossSeeds()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var run = New(seed);
            foreach (var map in run.maps.Take(4))
            {
                Assert.That(map.loot.Count(l => l.guardEnemyId != 0 && l.secondGuardEnemyId != 0), Is.EqualTo(8), "Seed " + seed);
                foreach (var loot in map.loot)
                {
                    Assert.That(loot.permanentObstacle, Is.True);
                    foreach (var other in map.loot.Where(l => l.id != loot.id && l.containerKind == loot.containerKind))
                        Assert.That(MapGenerator.Manhattan(new Vector2Int(loot.x, loot.y), new Vector2Int(other.x, other.y)),
                            Is.GreaterThanOrEqualTo(MapGenerator.MinimumSameContainerDistance), "Seed " + seed);
                    foreach (var guard in map.enemies.Where(e => loot.GuardedBy(e.id)))
                        Assert.That(MapGenerator.Manhattan(new Vector2Int(loot.x, loot.y), new Vector2Int(guard.x, guard.y)), Is.EqualTo(1));
                }
                Assert.That(MapGenerator.ValidateContainerRoutes(map), Is.True, "Seed " + seed);
                foreach (var loot in map.loot) { loot.unopened = false; loot.opened = true; loot.units.Clear(); }
                Assert.That(MapGenerator.ValidateContainerRoutes(map), Is.True);
            }
        }
    }

    [Test]
    public void PermanentContainerAcrossOnlyRouteIsRejected()
    {
        var map = new MapData { size = 7, cells = Enumerable.Repeat((int)Tile.Wall, 49).ToArray(), spawnX = 1, spawnY = 3 };
        for (int x = 1; x < 6; x++) map.Set(x, 3, Tile.Ground);
        map.Set(5, 4, Tile.Advance);
        map.loot.Add(new GroundLoot { x = 3, y = 3, permanentObstacle = true, opened = true });
        Assert.That(MapGenerator.ValidateContainerRoutes(map), Is.False);
        map.Set(2, 2, Tile.Ground); map.Set(3, 2, Tile.Ground); map.Set(4, 2, Tile.Ground);
        Assert.That(MapGenerator.ValidateContainerRoutes(map), Is.True);
    }

    [Test]
    public void BothLivingGuardsAllowDirectOpenAndPickupWithoutReroll()
    {
        var run = New(); var loot = run.Map.loot.First(l => l.guardEnemyId != 0);
        Assert.That(run.Map.enemies.Count(e => loot.GuardedBy(e.id) && !e.defeated), Is.EqualTo(2));
        uint state = run.containerRandom.state;
        Assert.That(ContainerLoot.Open(run, loot), Is.True);
        Assert.That(run.containerRandom.state, Is.Not.EqualTo(state));
        state = run.containerRandom.state;
        Assert.That(Inventory.Pickup(run, loot), Is.GreaterThan(0));
        Assert.That(Inventory.Pickup(run, loot), Is.Zero);
        Assert.That(run.containerRandom.state, Is.EqualTo(state));
        Assert.That(run.Map.enemies.Count(e => loot.GuardedBy(e.id) && !e.defeated), Is.EqualTo(2));
    }

    [Test]
    public void FullInventoryStillOpensOnceAndSavedResultsCannotReroll()
    {
        var run = New(); var loot = Accessible(run, ContainerKind.Equipment);
        for (int slot = 0; slot < 20; slot++)
            run.inventory[slot].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.IronSword });
        uint battle = run.battleRandom.state, cards = run.cardRandom.state, map = run.mapRandom.state;
        Assert.That(Inventory.Pickup(run, loot), Is.Zero);
        Assert.That(loot.opened && !loot.unopened && loot.BlocksMovement, Is.True);
        var result = JsonUtility.ToJson(loot); uint state = run.containerRandom.state;
        Assert.That(Inventory.Pickup(run, loot), Is.Zero); Assert.That(JsonUtility.ToJson(loot), Is.EqualTo(result));
        var copy = Rules.Copy(run); loot = copy.Map.loot.Single(l => l.id == loot.id);
        Assert.That(Inventory.Pickup(copy, loot), Is.Zero); Assert.That(copy.containerRandom.state, Is.EqualTo(state));
        copy.inventory[0].units.Clear(); Assert.That(Inventory.Pickup(copy, loot), Is.EqualTo(1));
        Assert.That(loot.HasContents, Is.False); Assert.That(loot.BlocksMovement, Is.True); Assert.That(Inventory.Pickup(copy, loot), Is.Zero);
        Assert.That(run.battleRandom.state, Is.EqualTo(battle)); Assert.That(run.cardRandom.state, Is.EqualTo(cards)); Assert.That(run.mapRandom.state, Is.EqualTo(map));
    }

    [Test]
    public void PartialAndCompletePickupKeepNewContainersSolid()
    {
        var run = New(); var loot = Accessible(run, ContainerKind.Treasure);
        run.containerRules.treasure = new System.Collections.Generic.List<ContainerDrop> { new ContainerDrop(ItemKind.Ore, 1, 3, 3) };
        for (int slot = 0; slot < 20; slot++) for (int i = 0; i < (slot == 0 ? 19 : 20); i++)
            run.inventory[slot].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.Ore });
        Assert.That(Inventory.Pickup(run, loot), Is.EqualTo(1)); Assert.That(loot.units.Count, Is.EqualTo(2));
        Assert.That(loot.IsOpened && loot.BlocksMovement, Is.True); uint state = run.containerRandom.state;
        run.inventory[1].units.Clear(); Assert.That(Inventory.Pickup(run, loot), Is.EqualTo(2));
        Assert.That(loot.HasContents, Is.False); Assert.That(loot.BlocksMovement, Is.True); Assert.That(run.containerRandom.state, Is.EqualTo(state));
    }

    [Test]
    public void BossRewardKeepsNewLayoutCollisionAndReturnDoorAccess()
    {
        var run = New(); run.mapIndex = 4;
        run.x = run.Map.spawnX; run.y = run.Map.spawnY;
        Growth.FinishVictory(run, run.Map.enemies.Single());
        var reward = run.Map.loot.Single();
        Assert.That(reward.units.Single().kind, Is.EqualTo(ItemKind.Relic));
        Assert.That(Inventory.Pickup(run, reward), Is.EqualTo(1));
        Assert.That(reward.BlocksMovement, Is.True);
        Assert.That(MapGenerator.ValidateContainerRoutes(run.Map), Is.True);
    }

    [Test]
    public void CheckpointKeepsUnopenedOpenedAndNextRollTogether()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "ContainerTests", Guid.NewGuid().ToString("N"));
        try
        {
            var run = New(); var loot = Accessible(run, ContainerKind.Treasure); ContainerLoot.Open(run, loot);
            var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
            var copy = new PrototypeStore(folder).Continue(); Assert.That(JsonUtility.ToJson(copy), Is.EqualTo(JsonUtility.ToJson(run)));
            var next = Accessible(run, ContainerKind.Supplies); var nextCopy = Accessible(copy, ContainerKind.Supplies);
            ContainerLoot.Open(run, next); ContainerLoot.Open(copy, nextCopy);
            Assert.That(JsonUtility.ToJson(nextCopy), Is.EqualTo(JsonUtility.ToJson(next)));
            Assert.That(copy.containerRandom.state, Is.EqualTo(run.containerRandom.state));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void PoolsRespectCategoriesCharacterBindingAndFirstFloorEquipment()
    {
        var seen = new System.Collections.Generic.HashSet<ItemKind>();
        for (int seed = 0; seed < 20; seed++)
        {
            var run = New(seed);
            foreach (var map in run.maps.Take(4))
            {
                run.mapIndex = map.number - 1; run.x = map.spawnX; run.y = map.spawnY;
                foreach (var enemy in map.enemies) enemy.defeated = true;
                foreach (var loot in map.loot)
                {
                    Assert.That(loot.units, Is.Empty); ContainerLoot.Open(run, loot);
                    Assert.That(loot.units.Count, Is.InRange(1, 4));
                    foreach (var item in loot.units)
                    {
                        seen.Add(item.kind); Assert.That(ContainerLoot.Matches(loot.containerKind, item.kind), Is.True);
                        if (map.number == 1 && loot.containerKind == ContainerKind.Equipment)
                            Assert.That(item.kind, Is.InRange(ItemKind.IronSword, ItemKind.InscribedSword));
                        if (item.kind >= ItemKind.IronSword && item.kind <= ItemKind.RefinedSword)
                            Assert.That(run.attributeRules.Find(item.kind).ownerCharacterId, Is.EqualTo(run.character.templateId));
                    }
                }
                PrototypeStore.ValidateRun(run);
            }
        }
        Assert.That(seen.Count, Is.EqualTo(Rules.ItemNames.Length - 1));
        Assert.That(seen.Contains(ItemKind.WoodShield), Is.False, "Independent off-hand shields are retired from new loot.");
    }

    [Test]
    public void LegacyAndDroppedItemsKeepTheirContentsAndDoNotRoll()
    {
        var run = New(); uint state = run.containerRandom.state;
        var loot = MapGenerator.AddLoot(run, run.Map, new Vector2Int(run.CellX, run.CellY), ItemKind.Potion, 1);
        Assert.That(Inventory.Pickup(run, loot), Is.EqualTo(1)); Assert.That(loot.IsOpened, Is.True);
        var drop = Inventory.Drop(run, 0, 1); Assert.That(drop.IsContainer, Is.False);
        Assert.That(Inventory.Pickup(run, drop), Is.EqualTo(1)); Assert.That(run.containerRandom.state, Is.EqualTo(state));
        var old = MapGenerator.NewRun(77, false); var oldLoot = old.Map.loot.First(l => l.guardEnemyId == 0);
        int count = oldLoot.units.Count; Assert.That(Inventory.Pickup(old, oldLoot), Is.EqualTo(count)); PrototypeStore.ValidateRun(old);
    }

    [Test]
    public void ArtUsesThreeDistinctOpenedSpritesAndAllExistingEquipmentIcons()
    {
        var art = Resources.Load<PrototypeArtSet>("PrototypeArt");
        Assert.That(art.openedLoot.Distinct().Count(), Is.EqualTo(3));
        foreach (var kind in new[] { ContainerKind.Equipment, ContainerKind.Treasure, ContainerKind.Supplies })
        {
            var loot = new GroundLoot { containerKind = kind, opened = true };
            Assert.That(art.LootSprite(loot), Is.SameAs(art.openedLoot[(int)kind - 1]));
            Assert.That(art.LootSprite(loot).rect.size, Is.EqualTo(new Vector2(16, 16)));
        }
        foreach (var kind in Enum.GetValues(typeof(ItemKind)).Cast<ItemKind>().Where(kind => Rules.IsEquipment(kind) && kind != ItemKind.WoodShield))
            Assert.That(art.ItemIcon(kind), Is.Not.Null);
    }

    [Test]
    public void InvalidOpenStateIsRejectedWithoutUpgradingOldSaves()
    {
        var run = New(); run.Map.loot[0].unopened = false;
        Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(run));
        run = New(); run.containerRandom.state = 0;
        Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(run));
    }
}
