using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public class TownProgressionTests
{
    string folder;
    PrototypeStore store;
    [SetUp] public void Setup()
    {
        folder = Path.Combine(Application.temporaryCachePath, "TownTests", Guid.NewGuid().ToString("N"));
        store = new PrototypeStore(folder);
    }
    [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    RunData New() => MapGenerator.NewRun(12345, false);

    [Test]
    public void TalentActivationAndResetPersistExactPointsAndRanks()
    {
        store.LearnTalent(1); store.LearnTalent(1);
        var loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.gold, Is.EqualTo(120));
        Assert.That(loaded.Warehouse.town.talents[1], Is.EqualTo(2));
        Assert.That(loaded.Warehouse.town.talentPoints, Is.EqualTo(1));
        loaded.ResetTalents(); loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.gold, Is.EqualTo(120));
        Assert.That(loaded.Warehouse.town.talents, Is.EqualTo(new[] { 1, 0, 0, 0, 0, 0, 0, 0 }));
    }
    [Test]
    public void LockedTalentAndGuildUpgradeNeverSpendCurrency()
    {
        Assert.Throws<InvalidOperationException>(() => store.LearnTalent(4));
        Assert.Throws<InvalidOperationException>(() => store.UpgradeGuild());
        Assert.That(store.Warehouse.town.gold, Is.EqualTo(120));
        store.Warehouse.town.gold = 1000; store.Warehouse.town.reputation = 35;
        store.UpgradeGuild(); store.UpgradeGuild();
        Assert.That(store.Warehouse.town.guildLevel, Is.EqualTo(3));
        Assert.That(store.Warehouse.town.gold, Is.EqualTo(650));
        Assert.That(store.Warehouse.town.reputation, Is.EqualTo(35));
        Assert.Throws<InvalidOperationException>(() => store.UpgradeGuild());
    }
    [Test]
    public void PausedRunKeepsItsTalentSnapshotWhenTownChanges()
    {
        store.LearnTalent(2);
        var run = New(); TownRules.Apply(store.Warehouse.town, run); store.SaveCheckpoint(run);
        store.LearnTalent(1); store.ResetTalents();
        var restored = new PrototypeStore(folder).Continue();
        Assert.That(restored.MaxHealth, Is.EqualTo(65)); Assert.That(restored.health, Is.EqualTo(65));
        Assert.That(restored.attackBonus, Is.Zero);
        var next = New(); TownRules.Apply(store.Warehouse.town, next);
        Assert.That(next.MaxHealth, Is.EqualTo(60));
    }
    [Test]
    public void KnightMechanicsApplyToCombatAndVictoryOnlyOnce()
    {
        var town = new TownProgress { talents = new[] { 1, 3, 3, 3, 1, 1, 1 } };
        var run = New(); TownRules.Apply(town, run); PrototypeStore.ValidateRun(run);
        var enemy = run.Map.enemies[0]; var battle = new Battle(run, enemy);
        Assert.That(battle.energy, Is.EqualTo(4)); Assert.That(battle.playerBlock, Is.EqualTo(6));
        battle.hand.Clear(); battle.hand.Add(CardKind.Strike); double before = battle.enemyHealth;
        Assert.That(battle.Play(0), Is.True); Assert.That(battle.enemyHealth, Is.EqualTo(before - 9));
        battle.hand.Add(CardKind.Defend); battle.Play(0); Assert.That(battle.playerBlock, Is.EqualTo(14));
        battle.EndPlayerTurn(); battle.ResolveEnemyTurn();
        Assert.That(battle.energy, Is.EqualTo(3)); Assert.That(battle.playerBlock, Is.Zero);
        run.health = 65; Growth.FinishVictory(run, enemy); Growth.FinishVictory(run, enemy);
        Assert.That(run.health, Is.EqualTo(68));
        run.inventory[0].units.Add(new ItemUnit { id = 999999, kind = ItemKind.Potion });
        Assert.That(Inventory.Heal(run, 0), Is.True); Assert.That(run.health, Is.EqualTo(75));
    }
    [Test]
    public void ShieldDamageDoesNotReceiveWeaponTalentBonus()
    {
        var run = New(); run.attackBonus = 3;
        var battle = new Battle(run, run.Map.enemies[0]); battle.hand.Clear(); battle.hand.Add(CardKind.Shield);
        battle.playerBlock = 5; double before = battle.enemyHealth; battle.Play(0);
        Assert.That(battle.enemyHealth, Is.EqualTo(before - 5));
    }
    [Test]
    public void CurrencyAndItemsUseSameInterruptedSettlementLedger()
    {
        var run = New(); var enemy = run.Map.enemies[0]; enemy.defeated = true;
        store.BeginSettlement(run, false);
        var pending = store.Session.pending;
        pending.confirmed = true;
        var committed = Rules.Copy(store.Warehouse);
        committed.committedRuns.Add(run.id); committed.town.gold += 8; committed.town.reputation += 2;
        File.WriteAllText(Path.Combine(folder, "warehouse.json"), JsonUtility.ToJson(committed));
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonUtility.ToJson(store.Session));
        var recovered = new PrototypeStore(folder); recovered.CommitSettlement();
        recovered = new PrototypeStore(folder);
        Assert.That(recovered.Warehouse.town.gold, Is.EqualTo(128));
        Assert.That(recovered.Warehouse.town.reputation, Is.EqualTo(2));
        Assert.That(recovered.Warehouse.committedRuns.Count, Is.EqualTo(1)); Assert.That(recovered.Session.pending, Is.Null);
    }
    [Test]
    public void FullWarehouseRetainsSettlementUntilSpaceIsMade()
    {
        store.Warehouse.counts[0] = 100;
        var run = New(); Inventory.Pickup(run, run.Map.loot.First(l => l.guardEnemyId == 0));
        store.BeginSettlement(run, false); store.SaveSelection(Inventory.Items(run).Take(1).Select(i => i.id).ToList());
        Assert.Throws<InvalidOperationException>(() => store.CommitSettlement());
        Assert.That(store.Session.pending.confirmed, Is.False); Assert.That(store.Warehouse.counts.Sum(), Is.EqualTo(100));
        store.SellWarehouse(ItemKind.Ore, 1); store.CommitSettlement();
        Assert.That(new PrototypeStore(folder).Warehouse.counts.Sum(), Is.EqualTo(100));
    }
    [Test]
    public void SellingDonationAndExpansionPersistAndRejectOverspending()
    {
        store.Warehouse.counts[(int)ItemKind.Relic] = 2;
        store.DonateRelic(); store.SellWarehouse(ItemKind.Relic, 1); store.ExpandWarehouse();
        var loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.gold, Is.EqualTo(95));
        Assert.That(loaded.Warehouse.town.reputation, Is.EqualTo(10));
        Assert.That(TownRules.Capacity(loaded.Warehouse), Is.EqualTo(150));
        Assert.Throws<InvalidOperationException>(() => loaded.DonateRelic());
        Assert.Throws<InvalidOperationException>(() => loaded.SellWarehouse(ItemKind.Relic, -1));
        Assert.Throws<InvalidOperationException>(() => loaded.SellWarehouse(ItemKind.Relic, 1));
        Assert.Throws<InvalidOperationException>(() => loaded.ExpandWarehouse());
    }
    [Test]
    public void LegacyUnlimitedWarehouseMigratesWithoutLosingStockOrLedger()
    {
        string json = "{\"version\":1,\"counts\":[200,5,1,0],\"committedRuns\":[\"old-run\"]}";
        File.WriteAllText(Path.Combine(folder, "warehouse.json"), json);
        var loaded = new PrototypeStore(folder);
        Assert.That(TownRules.Used(loaded.Warehouse), Is.EqualTo(206));
        Assert.That(TownRules.Capacity(loaded.Warehouse), Is.GreaterThanOrEqualTo(206));
        Assert.That(loaded.Warehouse.committedRuns.Single(), Is.EqualTo("old-run"));
        Assert.That(File.ReadAllText(Path.Combine(folder, "warehouse.json")), Is.EqualTo(json));
        loaded.SellWarehouse(ItemKind.Ore, 1); loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.version, Is.EqualTo(5)); Assert.That(TownRules.Used(loaded.Warehouse), Is.EqualTo(205));
    }
    [Test]
    public void EveryTownServiceHasAConnectedWalkableEntrance()
    {
        var visited = new System.Collections.Generic.HashSet<Vector2Int>(); var queue = new System.Collections.Generic.Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(12, 11)); visited.Add(new Vector2Int(12, 11));
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in MapGenerator.Directions)
            {
                var next = cell + direction;
                if (PrototypeGame.TownWalkable(next.x, next.y) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        foreach (var stop in new[] { new Vector2Int(5, 5), new Vector2Int(13, 5) })
            Assert.That(visited.Contains(stop), Is.True, stop.ToString());
        Assert.That(PrototypeGame.TownWalkable(5, 3), Is.False); Assert.That(PrototypeGame.TownWalkable(25, 7), Is.False);
        visited.Clear(); queue.Enqueue(new Vector2Int(12, 11)); visited.Add(new Vector2Int(12, 11));
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var direction in MapGenerator.Directions)
            {
                var next = cell + direction;
                if (PrototypeGame.TownWalkable(next.x, next.y, true) && visited.Add(next)) queue.Enqueue(next);
            }
        }
        foreach (var stop in new[] { new Vector2Int(7, 5), new Vector2Int(17, 5), new Vector2Int(12, 9), new Vector2Int(12, 13) })
            Assert.That(visited.Contains(stop), Is.True, stop.ToString());
        Assert.That(PrototypeGame.TownWalkable(7, 4, true), Is.False);
    }
    [Test]
    public void WisdomStarPersistsAndAffectsOnlyNextRunSnapshot()
    {
        var existing = MapGenerator.NewRun(12345, AttributeKind.None, false);
        TownRules.Apply(store.Warehouse.town, existing); store.SaveCheckpoint(existing);
        store.LearnTalent(7);
        var loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.talents[7], Is.EqualTo(1));
        Assert.That(CharacterAttributes.Calculate(loaded.Continue()).wisdom, Is.EqualTo(5));
        var next = MapGenerator.NewRun(12345, AttributeKind.None, false); TownRules.Apply(loaded.Warehouse.town, next);
        Assert.That(CharacterAttributes.Calculate(next).wisdom, Is.EqualTo(6));
        Assert.That(next.character.secondary, Is.EqualTo(AttributeKind.None));
        loaded.ResetTalents(); Assert.That(loaded.Warehouse.town.gold, Is.EqualTo(120));
    }
    [Test]
    public void SevenNodeTownMigratesWithoutChangingRanksOrCurrency()
    {
        var old = Rules.Copy(store.Warehouse); old.version = 3; old.town.talents = new[] { 1, 1, 0, 0, 0, 0, 0 }; old.town.gold = 90;
        var path = Path.Combine(folder, "warehouse.json"); var json = JsonUtility.ToJson(old); File.WriteAllText(path, json);
        var loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.talents, Is.EqualTo(new[] { 1, 1, 0, 0, 0, 0, 0, 0 }));
        Assert.That(loaded.Warehouse.town.gold, Is.EqualTo(90));
        Assert.That(File.ReadAllText(path + ".pre-star-talents.bak"), Is.EqualTo(json));
    }
    [Test]
    public void PointsDepleteRetractAndRefundWithoutChangingGold()
    {
        store.Warehouse.town.gold = 0;
        store.LearnTalent(1); store.LearnTalent(1); store.LearnTalent(7);
        Assert.That(store.Warehouse.town.talentPoints, Is.Zero);
        Assert.That(store.Warehouse.town.gold, Is.Zero);
        Assert.Throws<InvalidOperationException>(() => store.LearnTalent(2));
        store.RetractTalent(7);
        Assert.That(store.Warehouse.town.talentPoints, Is.EqualTo(1));
        Assert.That(store.Warehouse.town.talents[7], Is.Zero);
        store.ResetTalents();
        var loaded = new PrototypeStore(folder);
        Assert.That(loaded.Warehouse.town.talentPoints, Is.EqualTo(3)); Assert.That(loaded.Warehouse.town.gold, Is.Zero);
        Assert.Throws<InvalidOperationException>(() => loaded.RetractTalent(7));
        Assert.Throws<InvalidOperationException>(() => loaded.RetractTalent(0));
    }
    [Test]
    public void ActiveChildPreventsRetractingRequiredParent()
    {
        Assert.Throws<InvalidOperationException>(() => store.LearnTalent(3));
        store.LearnTalent(2); store.LearnTalent(3);
        Assert.Throws<InvalidOperationException>(() => store.RetractTalent(2));
        Assert.That(store.Warehouse.town.talents[2], Is.EqualTo(1));
        Assert.That(store.Warehouse.town.talentPoints, Is.EqualTo(1));
        store.RetractTalent(3); store.RetractTalent(2);
        Assert.That(store.Warehouse.town.talentPoints, Is.EqualTo(3));
    }
}
