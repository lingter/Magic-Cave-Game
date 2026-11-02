using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class ToothMerchantTests
{
    static RunData New() => MapGenerator.NewRun(12345, AttributeKind.None, false);
    static void Approach(RunData run)
    {
        run.mapIndex = run.maps.FindIndex(m => m.hasMerchant);
        var map = run.Map;
        var p = MapGenerator.Directions.Select(d => new Vector2Int(map.merchantX, map.merchantY) + d)
            .First(v => map.CanTraverse(v.x, v.y));
        run.x = p.x; run.y = p.y; MapGenerator.Reveal(run);
    }

    [Test]
    public void VictoryAwardsOnceWithFullBagAndBossGivesTen()
    {
        var run = New();
        foreach (var slot in run.inventory) for (int i = 0; i < 20; i++)
            slot.units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.Ore });
        var enemy = run.Map.enemies[0]; Growth.FinishVictory(run, enemy); Growth.FinishVictory(run, enemy);
        Assert.That(run.teeth, Is.EqualTo(3));
        Assert.That(Inventory.UsedSlots(run), Is.EqualTo(20));
        run.mapIndex = run.maps.Count - 1;
        run.x = run.Map.spawnX; run.y = run.Map.spawnY;
        Growth.FinishVictory(run, run.Map.enemies.Single());
        Assert.That(run.teeth, Is.EqualTo(13));
    }

    [Test]
    public void PurchasesAreAtomicStackedAndRequireRealMerchantProximity()
    {
        var run = New(); Approach(run); run.teeth = 30;
        Assert.That(ToothMerchant.Buy(run, ItemKind.Potion), Is.Empty);
        Assert.That(run.teeth, Is.EqualTo(25));
        Assert.That(ToothMerchant.Buy(run, ItemKind.Potion), Is.Empty);
        Assert.That(run.inventory[0].units.Count, Is.EqualTo(2));
        Assert.That(run.inventory[0].units.Select(i => i.id).Distinct().Count(), Is.EqualTo(2));
        foreach (var slot in run.inventory) while (slot.units.Count < 20)
            slot.units.Add(new ItemUnit { id = run.nextItemId++, kind = slot == run.inventory[0] ? ItemKind.Potion : ItemKind.Ore });
        string before = JsonUtility.ToJson(run);
        Assert.That(ToothMerchant.Buy(run, ItemKind.Potion), Is.EqualTo("背包已满"));
        Assert.That(JsonUtility.ToJson(run), Is.EqualTo(before));
        run.inventory[0].units.RemoveAt(0);
        Assert.That(ToothMerchant.Buy(run, ItemKind.Potion), Is.Empty, "A full bag can still accept an existing partial stack");
        run.teeth = 0; before = JsonUtility.ToJson(run);
        Assert.That(ToothMerchant.Buy(run, ItemKind.Torch), Is.EqualTo("牙齿不足"));
        Assert.That(JsonUtility.ToJson(run), Is.EqualTo(before));
        run.teeth = 10; before = JsonUtility.ToJson(run);
        Assert.That(ToothMerchant.Buy(run, ItemKind.Relic), Is.EqualTo("商品暂未出售"));
        Assert.That(JsonUtility.ToJson(run), Is.EqualTo(before));
        run.x = run.Map.merchantX; run.y = run.Map.merchantY;
        Assert.That(ToothMerchant.Buy(run, ItemKind.Torch), Is.EqualTo("请先靠近集齿者"));
    }

    [Test]
    public void RandomMerchantsAvoidAllEightEnemyNeighborsAndPreserveRoutesAcrossSeeds()
    {
        var locations = new System.Collections.Generic.HashSet<string>();
        for (int seed = 0; seed < 100; seed++)
        {
            var run = MapGenerator.NewRun(seed, AttributeKind.None, false);
            foreach (var map in run.maps.Take(run.maps.Count - 1))
            {
                string context = "seed=" + seed + " map=" + map.number;
                if (!map.hasMerchant) continue;
                Assert.That(map.enemies.All(e => Math.Abs(e.x - map.merchantX) > 1 || Math.Abs(e.y - map.merchantY) > 1), Is.True, context);
                Assert.That(map.CanTraverse(map.merchantX, map.merchantY), Is.False, context);
                Assert.That(MapGenerator.ValidateContainerRoutes(map), Is.True, context);
                Assert.That(MapGenerator.ValidateCombatApproaches(map), Is.True, context);
            }
            Assert.That(run.maps.Last().hasMerchant, Is.False);
            for (int start = 0; start <= run.maps.Count - 5; start++)
                Assert.That(run.maps.Skip(start).Take(5).Count(m => m.hasMerchant), Is.GreaterThanOrEqualTo(2),
                    "seed=" + seed + " window=" + start);
            PrototypeStore.ValidateRun(run);
            foreach (var map in run.maps.Where(m => m.hasMerchant)) locations.Add(map.merchantX + "," + map.merchantY);
        }
        Assert.That(locations.Count, Is.GreaterThan(10));
        Assert.That(JsonUtility.ToJson(New().Map), Is.EqualTo(JsonUtility.ToJson(New().Map)));
    }

    [Test]
    public void EverySlidingFiveFloorWindowHasTwoMerchantsIncludingBossWindow()
    {
        var patterns = new System.Collections.Generic.HashSet<string>();
        foreach (int floors in new[] { 5, 8, 15, 60 })
            for (int seed = -500; seed < 500; seed++)
            {
                var plan = ToothMerchant.PlanFloors(seed, floors);
                Assert.That(plan.Length, Is.EqualTo(floors));
                Assert.That(plan.Last(), Is.False);
                Assert.That(plan, Is.EqualTo(ToothMerchant.PlanFloors(seed, floors)));
                for (int start = 0; start <= floors - 5; start++)
                    Assert.That(plan.Skip(start).Take(5).Count(p => p), Is.GreaterThanOrEqualTo(2),
                        "seed=" + seed + " floors=" + floors + " window=" + start);
                if (floors == 8) patterns.Add(string.Concat(plan.Select(p => p ? "1" : "0")));
            }
        Assert.That(patterns.Count, Is.GreaterThan(10));
        Assert.That(patterns.Any(p => p.Take(7).Contains('0')), Is.True, "Ordinary floors may have no merchant");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CheckpointPreservesCurrencyPurchasesAndMerchantButSettlementDoesNotExportCurrency(bool failure)
    {
        string folder = Path.Combine(Application.temporaryCachePath, "ToothMerchantTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PrototypeStore(folder); var run = New(); Approach(run); run.teeth = 20;
            Assert.That(ToothMerchant.Buy(run, ItemKind.Torch), Is.Empty);
            store.SaveCheckpoint(run);
            var loaded = new PrototypeStore(folder).Continue();
            Assert.That(loaded.teeth, Is.EqualTo(17));
            Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(JsonUtility.ToJson(run)));
            if (failure) loaded.health = 0;
            store.BeginSettlement(loaded, failure);
            store.CommitSettlement();
            Assert.That(store.Session.checkpoint, Is.Null);
            Assert.That(store.Session.pending, Is.Null);
            Assert.That(New().teeth, Is.Zero);
            Assert.That(store.Warehouse.counts.Length, Is.EqualTo(Rules.ItemNames.Length));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public void OldSavesKeepZeroBalanceWithoutMovingObjectsOrRegrantingDefeatedRewards()
    {
        var run = New();
        foreach (var map in run.maps) map.hasMerchant = false;
        run.Map.enemies[0].defeated = true;
        string oldJson = JsonUtility.ToJson(run).Replace("\"teeth\":0,", "");
        var old = JsonUtility.FromJson<RunData>(oldJson); PrototypeStore.ValidateRun(old);
        Assert.That(old.teeth, Is.Zero);
        Growth.FinishVictory(old, old.Map.enemies[0]); Assert.That(old.teeth, Is.Zero);
        Growth.FinishVictory(old, old.Map.enemies[1]); Assert.That(old.teeth, Is.EqualTo(3));
        Assert.That(old.maps.All(m => !m.hasMerchant), Is.True);
    }
}
