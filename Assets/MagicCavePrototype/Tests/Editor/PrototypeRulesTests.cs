using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public class PrototypeRulesTests
{
    string folder;
    RunData New(int seed = 12345) { return MapGenerator.NewRun(seed, false); }
    [SetUp] public void Setup() { folder = Path.Combine(Application.temporaryCachePath, "MagicCaveTests", Guid.NewGuid().ToString("N")); }
    [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [TestCase(0)] [TestCase(-1)] [TestCase(int.MinValue)] [TestCase(int.MaxValue)] [TestCase(12345)]
    public void SeedReproducesMapsObjectsAndAllRandomStreams(int seed)
    {
        var a = New(seed); var b = New(seed);
        for (int i = 0; i < 5; i++) Assert.That(JsonUtility.ToJson(a.maps[i]), Is.EqualTo(JsonUtility.ToJson(b.maps[i])));
        Assert.That(a.battleRandom.Next(), Is.EqualTo(b.battleRandom.Next()));
        Assert.That(a.cardRandom.Next(), Is.EqualTo(b.cardRandom.Next()));
        Assert.That(a.mapRandom.Next(), Is.EqualTo(b.mapRandom.Next()));
    }
    [Test]
    public void FourHundredMapsMeetTerrainDoorAndObjectRules()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var run = New(seed); PrototypeStore.ValidateRun(run);
            for (int index = 0; index < 4; index++)
            {
                var map = run.maps[index];
                var doors = Enumerable.Range(0, map.cells.Length).Where(i => map.cells[i] >= 2).ToList();
                Assert.That(doors.Count, Is.EqualTo(index == 2 ? 2 : 1), "Seed " + seed);
                var terrain = Rules.Copy(map);
                foreach (int door in doors) terrain.cells[door] = (int)Tile.Ground;
                Assert.That(MapGenerator.ValidateTerrain(terrain, out int groups, out int thick), Is.True);
                Assert.That(groups, Is.GreaterThanOrEqualTo(4)); Assert.That(thick, Is.GreaterThanOrEqualTo(2));
                foreach (int door in doors) Assert.That(MapGenerator.Directions.Any(d => map.Walkable(door % map.size + d.x, door / map.size + d.y)), Is.True);
                if (doors.Count == 2) Assert.That(Math.Abs(doors[0] % 25 - doors[1] % 25) + Math.Abs(doors[0] / 25 - doors[1] / 25), Is.GreaterThanOrEqualTo(3));
                var occupied = new HashSet<int> { map.Index(map.spawnX, map.spawnY) };
                foreach (var enemy in map.enemies) Assert.That(occupied.Add(map.Index(enemy.x, enemy.y)), Is.True);
                foreach (var loot in map.loot) Assert.That(occupied.Add(map.Index(loot.x, loot.y)), Is.True);
                Assert.That(map.enemies.Count, Is.EqualTo(10));
                Assert.That(map.loot.Count, Is.EqualTo(10));
                Assert.That(map.loot.Count(l => l.units[0].kind == ItemKind.Ore), Is.InRange(4, 5));
                Assert.That(map.loot.Count(l => l.units[0].kind == ItemKind.Crystal), Is.EqualTo(3));
                Assert.That(map.loot.Count(l => l.units[0].kind == ItemKind.Potion), Is.EqualTo(2));
                Assert.That(map.enemies.Count(e => e.role == EnemyRole.ItemGuard), Is.EqualTo(7));
                Assert.That(map.enemies.Count(e => e.role == EnemyRole.CorridorGuard), Is.EqualTo(1));
                Assert.That(map.enemies.Count(e => e.role == EnemyRole.Roaming), Is.EqualTo(2));
                Assert.That(map.loot.Count(l => l.guardEnemyId != 0), Is.EqualTo(8));
                foreach (var loot in map.loot)
                {
                    var guard = map.LivingGuard(loot);
                    Assert.That(MapGenerator.IsLootCorner(map, new Vector2Int(loot.x, loot.y)), Is.EqualTo(guard != null));
                    if (guard == null) continue;
                    Assert.That(MapGenerator.Manhattan(new Vector2Int(loot.x, loot.y), new Vector2Int(guard.x, guard.y)), Is.EqualTo(1));
                    Assert.That(guard.Level, Is.GreaterThanOrEqualTo(Rules.Rarity(loot.units[0].kind) + 1));
                }
                foreach (var guard in map.enemies.Where(e => e.role == EnemyRole.CorridorGuard))
                    Assert.That(MapGenerator.IsCorridor(map, new Vector2Int(guard.x, guard.y)), Is.True);
                foreach (var enemy in map.enemies)
                    Assert.That(MapGenerator.Manhattan(new Vector2Int(map.spawnX, map.spawnY), new Vector2Int(enemy.x, enemy.y)), Is.GreaterThanOrEqualTo(3));
            }
        }
    }
    [Test]
    public void GuardMustBeDefeatedEvenAfterWallsAreBrokenAndSurvivesCheckpoint()
    {
        var run = New(); var loot = run.Map.loot.First(l => l.guardEnemyId != 0);
        var guard = run.Map.LivingGuard(loot);
        foreach (var d in MapGenerator.Directions)
        {
            int x = loot.x + d.x, y = loot.y + d.y;
            if (!run.Map.Border(x, y) && run.Map.At(x, y) == Tile.Wall) run.Map.Set(x, y, Tile.Ground);
        }
        Assert.That(Inventory.Pickup(run, loot), Is.Zero);
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
        run = new PrototypeStore(folder).Continue(); loot = run.Map.loot.Single(l => l.id == loot.id);
        Assert.That(run.Map.LivingGuard(loot).id, Is.EqualTo(guard.id));
        Assert.That(Inventory.Pickup(run, loot), Is.Zero);
        Growth.FinishVictory(run, run.Map.LivingGuard(loot));
        Assert.That(Inventory.Pickup(run, loot), Is.GreaterThan(0));
        PrototypeStore.ValidateRun(run);
    }

    [Test]
    public void GuardLevelsChangeCombatAndLegacyEnemiesKeepBaseStats()
    {
        var run = New();
        var legacy = new EnemyData { kind = EnemyKind.Bat, level = 0 };
        var stronger = new EnemyData { kind = EnemyKind.Bat, level = 3 };
        var basic = new Battle(run, legacy); var high = new Battle(run, stronger);
        Assert.That(basic.enemyHealth, Is.EqualTo(18)); Assert.That(basic.IntentAttack, Is.EqualTo(5));
        Assert.That(high.enemyHealth, Is.EqualTo(26)); Assert.That(high.IntentAttack, Is.EqualTo(7));
    }
    [Test]
    public void BattleAndChoicesDoNotPerturbMapRandomness()
    {
        var a = New(); var b = New();
        new Battle(a, a.Map.enemies[0]); Growth.Award(a, 20); Growth.NextUpgrade(a);
        Assert.That(a.mapRandom.state, Is.EqualTo(b.mapRandom.state));
        Assert.That(a.mapRandom.Next(), Is.EqualTo(b.mapRandom.Next()));
    }
    [Test]
    public void PartialPickupDropAndRepickPreserveFirstAcquisition()
    {
        var run = New();
        for (int s = 0; s < 20; s++) for (int i = 0; i < (s == 0 ? 19 : 20); i++)
            run.inventory[s].units.Add(new ItemUnit { id = run.nextItemId++, kind = s == 0 ? ItemKind.Ore : ItemKind.Crystal, acquiredTicks = 10, acquisitionOrder = run.nextAcquisitionOrder++ });
        var loot = MapGenerator.AddLoot(run, run.Map, new Vector2Int(run.CellX, run.CellY), ItemKind.Ore, 3);
        // This inventory fixture places a loose pile at the player's feet, rather than spawning a solid chest there.
        loot.loose = true;
        Assert.That(Inventory.Pickup(run, loot), Is.EqualTo(1)); Assert.That(loot.units.Count, Is.EqualTo(2));
        var first = run.inventory[0].units[0]; long ticks = first.acquiredTicks; int order = first.acquisitionOrder;
        var dropped = Inventory.Drop(run, 0, 1); Assert.That(Inventory.Pickup(run, dropped), Is.EqualTo(1));
        Assert.That(first.acquiredTicks, Is.EqualTo(ticks)); Assert.That(first.acquisitionOrder, Is.EqualTo(order));
        Assert.That(Inventory.UsedSlots(run), Is.EqualTo(20)); PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void PotionCapsHealthAndDoesNotConsumeAtFullHealth()
    {
        var run = New(); var loot = MapGenerator.AddLoot(run, run.Map, new Vector2Int(run.CellX, run.CellY), ItemKind.Potion, 2);
        Inventory.Pickup(run, loot); Assert.That(Inventory.Heal(run, 0), Is.False);
        run.health = 51; Assert.That(Inventory.Heal(run, 0), Is.True); Assert.That(run.health, Is.EqualTo(60));
        Assert.That(run.inventory[0].units.Count, Is.EqualTo(1));
    }
    Battle ControlledBattle(params CardKind[] hand)
    {
        var run = New(); var battle = new Battle(run, new EnemyData { kind = EnemyKind.Boss });
        battle.draw.Clear(); battle.hand.Clear(); battle.discard.Clear(); battle.hand.AddRange(hand); return battle;
    }
    [Test]
    public void DrawCardCannotRedrawItselfWhileResolving()
    {
        var battle = ControlledBattle(CardKind.Prepare); battle.discard.Add(CardKind.Strike);
        Assert.That(battle.Play(0), Is.True);
        Assert.That(battle.hand, Is.EqualTo(new[] { CardKind.Strike }));
        Assert.That(battle.discard, Is.EqualTo(new[] { CardKind.Prepare }));
    }
    [Test]
    public void SpiritAppliesPerHitAndBlockAbsorbsSegments()
    {
        var battle = ControlledBattle(CardKind.Spirit, CardKind.Double); battle.enemyBlock = 7;
        battle.Play(0); battle.Play(0);
        Assert.That(battle.enemyHealth, Is.EqualTo(75)); Assert.That(battle.enemyBlock, Is.Zero);
        battle.EndPlayerTurn(); Assert.That(battle.spirit, Is.Zero);
    }
    [Test]
    public void ShieldKeepsBlockAndOwnerBlockExpiresAtNextTurn()
    {
        var battle = ControlledBattle(CardKind.Defend, CardKind.Shield);
        battle.Play(0); battle.Play(0); Assert.That(battle.playerBlock, Is.EqualTo(5)); Assert.That(battle.enemyHealth, Is.EqualTo(75));
        battle.EndPlayerTurn(); battle.ResolveEnemyTurn();
        Assert.That(battle.run.health, Is.EqualTo(57)); Assert.That(battle.playerBlock, Is.Zero);
        Assert.That(battle.energy, Is.EqualTo(3)); Assert.That(battle.hand.Count, Is.EqualTo(2));
    }
    [Test]
    public void EnemyBlockPersistsForPlayerTurnAndClearsOnEnemyTurn()
    {
        var run = New(); var battle = new Battle(run, new EnemyData { kind = EnemyKind.Beetle });
        battle.EndPlayerTurn(); battle.ResolveEnemyTurn(); Assert.That(battle.enemyBlock, Is.EqualTo(6));
        battle.EndPlayerTurn(); battle.ResolveEnemyTurn(); Assert.That(battle.enemyBlock, Is.Zero); Assert.That(run.health, Is.EqualTo(52));
    }
    [Test]
    public void InsufficientEnergyDoesNotChangeHandOrHealth()
    {
        var battle = ControlledBattle(CardKind.Heavy); battle.energy = 1;
        Assert.That(battle.Play(0), Is.False); Assert.That(battle.hand.Count, Is.EqualTo(1));
        Assert.That(battle.enemyHealth, Is.EqualTo(80)); Assert.That(battle.energy, Is.EqualTo(1));
    }
    [Test]
    public void LethalAttackStopsAndLethalEnemyTurnEndsRun()
    {
        var battle = ControlledBattle(CardKind.Double); battle.enemyHealth = 3; battle.Play(0);
        Assert.That(battle.outcome, Is.EqualTo(BattleOutcome.Victory)); Assert.That(battle.playerTurn, Is.False);
        var dying = ControlledBattle(); dying.run.health = 1; dying.EndPlayerTurn(); dying.ResolveEnemyTurn();
        Assert.That(dying.run.health, Is.Zero); Assert.That(dying.outcome, Is.EqualTo(BattleOutcome.Failure));
    }
    [Test]
    public void MultipleUpgradesCarryExperienceAndDoNotHeal()
    {
        var run = New(); run.health = 24; Growth.Award(run, 55);
        var first = Growth.NextUpgrade(run); Assert.That(first.Distinct().Count(), Is.EqualTo(3)); Assert.That(run.level, Is.EqualTo(2));
        Assert.That(Growth.NextUpgrade(run).Count, Is.EqualTo(3)); Assert.That(run.level, Is.EqualTo(3));
        Assert.That(Growth.NextUpgrade(run), Is.Null); Assert.That(run.experience, Is.EqualTo(5));
        Assert.That(run.health, Is.EqualTo(24)); Assert.That(run.deck.Count, Is.EqualTo(10));
    }
    [Test]
    public void SavedStateRestoresWallsFogItemsAndNextRandomResults()
    {
        var run = New(); int wall = Enumerable.Range(0, 625).First(i => !run.Map.Border(i % 25, i / 25) && run.Map.cells[i] == (int)Tile.Wall);
        run.Map.cells[wall] = (int)Tile.Ground; run.Map.revealed[wall] = true; run.health = 37;
        Inventory.Pickup(run, run.Map.loot.First(l => l.guardEnemyId == 0)); new Battle(run, run.Map.enemies[0]);
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
        var loaded = new PrototypeStore(folder).Continue();
        Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(JsonUtility.ToJson(run)));
        Assert.That(loaded.battleRandom.Next(), Is.EqualTo(run.battleRandom.Next())); Assert.That(loaded.cardRandom.Next(), Is.EqualTo(run.cardRandom.Next()));
    }
    [Test]
    public void ActiveSettlementRestoresSelectionAndCancelReturnsLiveState()
    {
        var run = New(); var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
        Inventory.Pickup(run, run.Map.loot.First(l => l.guardEnemyId == 0)); run.health = 31; store.BeginSettlement(run, false);
        int id = Inventory.Items(run)[0].id; store.SaveSelection(new[] { id });
        store = new PrototypeStore(folder); Assert.That(store.Session.pending.selectedIds, Does.Contain(id));
        var restored = store.CancelSettlement(); Assert.That(restored.health, Is.EqualTo(31));
        Assert.That(Inventory.Items(restored).Count, Is.GreaterThan(0)); Assert.That(store.Session.checkpoint.health, Is.EqualTo(60));
    }
    [Test]
    public void FailureSelectionUsesRarityThenFirstTimeThenOrderAndInvalidatesCheckpoint()
    {
        var run = New(); var kinds = new[] { ItemKind.Ore, ItemKind.Relic, ItemKind.Crystal, ItemKind.Relic, ItemKind.Potion, ItemKind.Ore, ItemKind.Crystal };
        for (int i = 0; i < kinds.Length; i++) run.inventory[i].units.Add(new ItemUnit { id = run.nextItemId++, kind = kinds[i], acquiredTicks = i < 4 ? 100 : 200, acquisitionOrder = i });
        var selected = Inventory.FailureSelection(run);
        Assert.That(selected.Select(i => i.kind), Is.EqualTo(new[] { ItemKind.Relic, ItemKind.Relic, ItemKind.Crystal, ItemKind.Crystal, ItemKind.Ore }));
        Assert.That(selected[0].acquisitionOrder, Is.EqualTo(3));
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run); run.health = 0; store.BeginSettlement(run, true);
        Assert.That(store.Session.checkpoint, Is.Null); Assert.That(store.Session.pending.selectedIds.Count, Is.EqualTo(5));
        Assert.Throws<InvalidOperationException>(() => store.CancelSettlement());
    }
    [Test]
    public void InterruptedWarehouseCommitIsNotCreditedTwice()
    {
        var run = New(); Inventory.Pickup(run, run.Map.loot.First(l => l.guardEnemyId == 0)); var store = new PrototypeStore(folder);
        store.BeginSettlement(run, false); store.SaveSelection(Inventory.Items(run).Select(i => i.id).Take(2).ToList());
        var interrupted = Rules.Copy(store.Session); interrupted.pending.confirmed = true; interrupted.checkpoint = null;
        store.CommitSettlement(); long total = store.Warehouse.counts.Sum(); Assert.That(total, Is.EqualTo(2));
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonUtility.ToJson(interrupted));
        store = new PrototypeStore(folder); store.CommitSettlement(); Assert.That(store.Warehouse.counts.Sum(), Is.EqualTo(total));
        Assert.That(store.Session.pending, Is.Null); Assert.That(store.Session.checkpoint, Is.Null);
    }
    [Test]
    public void BossVictoryCreatesReturnAndRewardExactlyOnce()
    {
        var run = New(); run.mapIndex = 4; run.x = 6; run.y = 2; var boss = run.Map.enemies[0];
        Assert.That(run.Map.cells.Count(i => i == (int)Tile.Return), Is.Zero);
        Growth.FinishVictory(run, boss); Growth.FinishVictory(run, boss);
        Assert.That(run.bossDefeated, Is.True); Assert.That(run.totalExperience, Is.EqualTo(30));
        Assert.That(run.Map.At(6, 10), Is.EqualTo(Tile.Return)); Assert.That(run.Map.loot.Count, Is.EqualTo(1));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void PathCannotUseUnrevealedGroundAndRemainsCardinal()
    {
        var run = New(); var map = run.maps[4]; Array.Clear(map.revealed, 0, map.revealed.Length);
        map.revealed[map.Index(6, 2)] = true;
        Assert.That(MapGenerator.Path(map, new Vector2Int(6, 2), new Vector2Int(6, 4), false), Is.Null);
        map.revealed[map.Index(6, 3)] = true; map.revealed[map.Index(6, 4)] = true;
        Assert.That(MapGenerator.Path(map, new Vector2Int(6, 2), new Vector2Int(6, 4), false), Is.EqualTo(new[] { new Vector2Int(6, 3), new Vector2Int(6, 4) }));
    }

    [Test]
    public void FiveMapRunCanWinWithNormalDeckRewardsPotionsAndCheckpoint()
    {
        var run = New(); var store = new PrototypeStore(folder);
        for (int mapIndex = 0; mapIndex < 5; mapIndex++)
        {
            run.mapIndex = mapIndex; run.x = run.Map.spawnX; run.y = run.Map.spawnY;
            foreach (var loot in run.Map.loot.ToArray()) Inventory.Pickup(run, loot);
            // Ordinary fights are optional; take two low-level encounters per floor.
            foreach (var enemy in run.Map.enemies.OrderBy(e => e.Level).ThenBy(e => e.kind).Take(2))
            {
                UsePotions(run);
                var battle = new Battle(run, enemy);
                for (int turns = 0; turns < 100 && battle.outcome == BattleOutcome.None; turns++)
                {
                    for (int plays = 0; plays < 12 && battle.outcome == BattleOutcome.None; plays++)
                    {
                        int chosen = -1; float best = -1;
                        for (int i = 0; i < battle.hand.Count; i++)
                        {
                            var card = battle.hand[i]; if (Rules.Costs[(int)card] > battle.energy) continue;
                            float score = CardScore(battle, card);
                            if (score > best) { best = score; chosen = i; }
                        }
                        if (chosen < 0) break; battle.Play(chosen);
                    }
                    if (battle.outcome == BattleOutcome.None) { battle.EndPlayerTurn(); battle.ResolveEnemyTurn(); }
                }
                Assert.That(battle.outcome, Is.EqualTo(BattleOutcome.Victory), "Map " + (mapIndex + 1) + ", enemy " + enemy.kind + ", HP " + run.health);
                Growth.FinishVictory(run, enemy);
                foreach (var loot in run.Map.loot.ToArray()) Inventory.Pickup(run, loot);
                List<CardKind> choices;
                while ((choices = Growth.NextUpgrade(run)) != null)
                    run.deck.Add(choices.OrderByDescending(c => c == CardKind.Quick ? 8 : c == CardKind.Heavy ? 7 : c == CardKind.Double ? 6 : c == CardKind.Fortify ? 5 : c == CardKind.Steady ? 4 : c == CardKind.Shield ? 2 : 1).First());
            }
            if (mapIndex == 2)
            {
                store.SaveCheckpoint(run); store = new PrototypeStore(folder); run = store.Continue();
                Assert.That(run.Map.number, Is.EqualTo(3));
            }
        }
        Assert.That(run.bossDefeated, Is.True); Assert.That(run.level, Is.EqualTo(4)); Assert.That(run.totalExperience, Is.EqualTo(110));
        Inventory.Pickup(run, run.Map.loot.Single());
        store.BeginSettlement(run, false);
        store.SaveSelection(Inventory.Items(run).OrderByDescending(i => Rules.Rarity(i.kind)).Take(10).Select(i => i.id).ToList());
        store.CommitSettlement(); Assert.That(store.Warehouse.counts.Sum(), Is.EqualTo(10));
        Assert.That(new PrototypeStore(folder).Session.checkpoint, Is.Null);
    }
    static void UsePotions(RunData run)
    {
        while (run.health <= 40)
        {
            int slot = run.inventory.FindIndex(s => s.units.Count > 0 && s.units[0].kind == ItemKind.Potion);
            if (slot < 0) break; Inventory.Heal(run, slot);
        }
    }
    [TestCase(6f, 10f)] [TestCase(6.55f, 10f)] [TestCase(6f, 9.45f)]
    public void BossReturnDoorNeverTrapsOrOverlapsPlayer(float x, float y)
    {
        var run = New(); run.mapIndex = 4; run.x = x; run.y = y;
        Growth.FinishVictory(run, run.Map.enemies[0]);
        Assert.That(run.Map.At(6, 10), Is.EqualTo(Tile.Return));
        Assert.That(run.Map.CanStand(run.x, run.y), Is.True);
        PrototypeStore.ValidateRun(run);
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run); store.BeginSettlement(run, false);
        Assert.That(store.CancelSettlement().Map.CanStand(run.x, run.y), Is.True);
    }
    [Test]
    public void PathsCannotEnterLivingEnemyCellsAndCanApproachOrPassAfterVictory()
    {
        var run = New(); run.mapIndex = 4; var map = run.Map;
        Assert.That(map.CanTraverse(6, 9), Is.False);
        Assert.That(MapGenerator.Path(map, new Vector2Int(6, 8), new Vector2Int(6, 9), false), Is.Null);
        Assert.That(MapGenerator.Path(map, new Vector2Int(6, 7), new Vector2Int(6, 9), true), Is.EqualTo(new[] { new Vector2Int(6, 8) }));
        Assert.That(map.CanStand(6, 8.4f), Is.False);
        map.enemies[0].defeated = true;
        Assert.That(map.CanTraverse(6, 9), Is.True);
        Assert.That(MapGenerator.Path(map, new Vector2Int(6, 8), new Vector2Int(6, 9), false), Is.Not.Null);
    }
    [TestCase(true)] [TestCase(false)]
    public void LegacyOverlappingCheckpointRecoversWithoutOverwritingSource(bool bossDoor)
    {
        var run = New(); run.mapIndex = 4; run.x = 6; run.y = 2;
        if (bossDoor) Growth.FinishVictory(run, run.Map.enemies[0]);
        run.x = 6; run.y = bossDoor ? 10 : 9;
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "session.json");
        string original = JsonUtility.ToJson(new SessionData { checkpoint = run }); File.WriteAllText(path, original);
        var recovered = new PrototypeStore(folder).Continue();
        Assert.That(recovered.Map.CanStand(recovered.x, recovered.y), Is.True);
        Assert.That(recovered.seed, Is.EqualTo(run.seed)); Assert.That(recovered.health, Is.EqualTo(run.health));
        Assert.That(File.ReadAllText(path), Is.EqualTo(original)); PrototypeStore.ValidateRun(recovered);
    }
    static float CardScore(Battle battle, CardKind card)
    {
        double damage = card == CardKind.Strike ? 6 : card == CardKind.Heavy ? 15 : card == CardKind.Double ? 8 + battle.spirit : card == CardKind.Quick ? 5 : card == CardKind.Shield ? battle.playerBlock : 0;
        int block = card == CardKind.Defend ? 5 : card == CardKind.Fortify ? 12 : card == CardKind.Steady ? 4 : 0;
        if (damage > 0 && damage + battle.spirit >= battle.enemyHealth + battle.enemyBlock) return 100;
        if (block > 0 && battle.playerBlock < battle.IntentAttack) return (float)(20 + Math.Min(block, battle.IntentAttack - battle.playerBlock));
        if (card == CardKind.Prepare || card == CardKind.Steady) return battle.energy >= 2 ? 15 : 0;
        if (card == CardKind.Spirit) return battle.energy >= 2 ? 14 : 0;
        return damage > 0 ? (float)((damage + battle.spirit) / Rules.Costs[(int)card]) : 0;
    }
}
