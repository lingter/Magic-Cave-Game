using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public class CharacterAttributeTests
{
    string folder;
    RunData New(AttributeKind secondary = AttributeKind.Wisdom, int seed = 12345) => MapGenerator.NewRun(seed, secondary, false);
    ItemUnit Give(RunData run, ItemKind kind, int slot = 0)
    {
        var item = new ItemUnit { id = run.nextItemId++, kind = kind, acquiredTicks = 100, acquisitionOrder = run.nextAcquisitionOrder++ };
        run.inventory[slot].units.Add(item); return item;
    }
    Battle Hand(RunData run, params CardKind[] cards)
    {
        var battle = new Battle(run, new EnemyData { kind = EnemyKind.Boss });
        battle.hand.Clear(); battle.hand.AddRange(cards); return battle;
    }
    [SetUp] public void Setup() { folder = Path.Combine(Application.temporaryCachePath, "AttributeTests", Guid.NewGuid().ToString("N")); }
    [TearDown] public void Cleanup() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Test]
    public void BirthStatsCardAmountsAndSnapshotsAreIndependent()
    {
        var a = New(); var b = New(); var stats = CharacterAttributes.Calculate(a);
        Assert.That(new[] { stats.strength, stats.agility, stats.wisdom, stats.maxHealth }, Is.EqualTo(new[] { 10, 5, 5, 60 }));
        Assert.That(stats.attack, Is.EqualTo(6)); Assert.That(stats.secondaryMultiplier, Is.EqualTo(1.05m));
        Assert.That(CombatNumbers.Damage(a, CardKind.Strike), Is.EqualTo(4));
        Assert.That(CombatNumbers.Damage(a, CardKind.Heavy), Is.EqualTo(9));
        Assert.That(CombatNumbers.Damage(a, CardKind.Double), Is.EqualTo(2));
        Assert.That(CombatNumbers.Block(a, CardKind.Defend), Is.EqualTo(10));
        a.attributeRules.template.wisdom = 20;
        Assert.That(b.attributeRules.template.wisdom, Is.EqualTo(5));
        Assert.That(PrototypeAttributeSet.LoadRules().template.wisdom, Is.EqualTo(5));
        Assert.That(Resources.Load<PrototypeAttributeSet>("CharacterRules").rules.secondaryPerPoint, Is.EqualTo(100));
    }
    [TestCase(AttributeKind.Strength)] [TestCase(AttributeKind.None)] [TestCase((AttributeKind)99)]
    public void MainAttributeCannotBeChosenAsSecondary(AttributeKind secondary)
    {
        Assert.Throws<InvalidOperationException>(() => CharacterAttributes.Initialize(new RunData(), secondary));
    }
    [TestCase(0, 38)] [TestCase(5, 39)] [TestCase(10, 41)]
    public void ConfirmedScalingAndFinalMultiplierExamples(int wisdom, double expected)
    {
        var run = New(); run.attributeRules.template.wisdom = wisdom;
        var weapon = run.attributeRules.Weapon("training-sword"); weapon.baseAttack = 10; weapon.strengthScaling = 15000;
        Assert.That(CharacterAttributes.Calculate(run).attack, Is.EqualTo(25));
        Assert.That(CombatNumbers.Damage(run, CardKind.Heavy), Is.EqualTo(expected));
        if (wisdom == 10) Assert.That(CombatNumbers.Damage(run, CardKind.Heavy, 2), Is.EqualTo(43));
    }
    [Test]
    public void CardPreviewUsesIdenticalDefenseBlockAndSegmentRounding()
    {
        var run = New(); var weapon = run.attributeRules.Weapon("training-sword");
        weapon.baseAttack = 10; weapon.strengthScaling = 15000; run.attributeRules.template.wisdom = 10;
        var battle = Hand(run, CardKind.Heavy); battle.enemy.physicalDefense = 1; battle.enemyBlock = 5;
        Assert.That(battle.Preview(CardKind.Heavy), Is.EqualTo(35));
        battle.Play(0); Assert.That(battle.enemyHealth, Is.EqualTo(45)); Assert.That(battle.enemyBlock, Is.Zero);
        var doubleBattle = Hand(New(), CardKind.Double); doubleBattle.enemyBlock = 2; doubleBattle.enemy.physicalDefense = 1;
        double expected = doubleBattle.Preview(CardKind.Double); double before = doubleBattle.enemyHealth;
        doubleBattle.Play(0); Assert.That(before - doubleBattle.enemyHealth, Is.EqualTo(expected).Within(.00001));
    }
    [Test]
    public void WisdomCanContributeToBothWeaponAndSecondaryWithoutThirdAttributeBonus()
    {
        var wisdom = New(); Give(wisdom, ItemKind.InscribedSword); Inventory.Equip(wisdom, 0);
        wisdom.attributeRules.template.wisdom = 10;
        var stats = CharacterAttributes.Calculate(wisdom);
        Assert.That(stats.attack, Is.EqualTo(11)); Assert.That(stats.secondaryMultiplier, Is.EqualTo(1.1m));
        Assert.That(CombatNumbers.Damage(wisdom, CardKind.Heavy), Is.EqualTo(18));
        wisdom.character.secondary = AttributeKind.Agility;
        Assert.That(CharacterAttributes.Calculate(wisdom).attack, Is.EqualTo(11));
        Assert.That(CombatNumbers.Damage(wisdom, CardKind.Heavy), Is.EqualTo(17));
    }
    [Test]
    public void ShieldReceivesFinalMultiplierButDoesNotReadWeaponAttack()
    {
        var run = New(); var battle = Hand(run, CardKind.Shield); battle.playerBlock = 10;
        run.attributeRules.Weapon("training-sword").baseAttack = 1000;
        Assert.That(battle.Preview(CardKind.Shield), Is.EqualTo(11)); battle.Play(0);
        Assert.That(battle.enemyHealth, Is.EqualTo(69)); Assert.That(battle.playerBlock, Is.EqualTo(10));
    }
    [TestCase(7417, 4)] [TestCase(7499, 4)] [TestCase(7500, 5)]
    public void DamageRoundsOnceToIntegerAfterAllMultipliers(int strengthScaling, int expected)
    {
        var run = New(); run.attributeRules.template.wisdom = 0;
        var weapon = run.attributeRules.Weapon("training-sword"); weapon.baseAttack = 0; weapon.strengthScaling = strengthScaling;
        // 7499 yields 4.4994. Rounding first to two decimals would incorrectly produce 5.
        Assert.That(CombatNumbers.Damage(run, CardKind.Strike), Is.EqualTo(expected));
    }
    [Test]
    public void TemplateFilteredLootDoesNotDependOnSecondaryChoice()
    {
        var a = New(AttributeKind.Agility); var b = New(AttributeKind.Wisdom);
        for (int i = 0; i < 5; i++) Assert.That(JsonUtility.ToJson(a.maps[i]), Is.EqualTo(JsonUtility.ToJson(b.maps[i])));
        foreach (var run in new[] { a, b }) foreach (var map in run.maps.Take(4))
        {
            Assert.That(map.loot.Count, Is.EqualTo(10));
            Assert.That(map.loot.Count(l => l.units.Any(i => Rules.IsEquipment(i.kind))), Is.EqualTo(1));
            Assert.That(map.loot.Count(l => l.units.Any(i => i.kind >= ItemKind.StrengthEssence)), Is.EqualTo(1));
            foreach (var item in map.loot.SelectMany(l => l.units).Where(i => i.kind >= ItemKind.IronSword && i.kind <= ItemKind.RefinedSword))
                Assert.That(run.attributeRules.Find(item.kind).ownerCharacterId, Is.EqualTo(run.character.templateId));
            foreach (var guard in map.enemies.Where(e => e.role == EnemyRole.ItemGuard))
                Assert.That(guard.Level, Is.EqualTo(map.loot.Where(l => l.guardEnemyId == guard.id).Max(l => Rules.Rarity(l.units[0].kind)) + 1));
        }
        a.attributeRules.Find(ItemKind.SwiftSword).ownerCharacterId = "other";
        for (int i = 0; i < 100; i++) Assert.That(MapGenerator.DrawWeapon(a), Is.Not.EqualTo(ItemKind.SwiftSword));
        Give(a, ItemKind.SwiftSword); Assert.That(Inventory.Equip(a, 0), Does.Contain("其他角色"));
    }
    [Test]
    public void EquipmentSwapsInFullInventoryAndHealthNeverIncreases()
    {
        var run = New(); var first = Give(run, ItemKind.LeatherArmor); Inventory.Equip(run, 0);
        Assert.That(run.MaxHealth, Is.EqualTo(66)); Assert.That(run.health, Is.EqualTo(60));
        run.health = 66;
        for (int i = 0; i < 20; i++) Give(run, i == 0 ? ItemKind.InsightArmor : ItemKind.Ore, i);
        Assert.That(Inventory.Unequip(run, EquipmentSlot.Armor), Does.Contain("已满"));
        Assert.That(Inventory.Equip(run, 0), Is.Empty);
        Assert.That(run.inventory[0].units[0].id, Is.EqualTo(first.id));
        Assert.That(run.MaxHealth, Is.EqualTo(64)); Assert.That(run.health, Is.EqualTo(64));
        run.inventory[1].units.Clear(); Inventory.Unequip(run, EquipmentSlot.Armor);
        Assert.That(run.health, Is.EqualTo(60)); Assert.That(run.MaxHealth, Is.EqualTo(60));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void UpgradeGrowsSelectedWisdomAndDoesNotHeal()
    {
        var run = New(); run.health = 20; Growth.Award(run, 55);
        Growth.NextUpgrade(run); Growth.NextUpgrade(run);
        var stats = CharacterAttributes.Calculate(run);
        Assert.That(new[] { stats.strength, stats.agility, stats.wisdom, stats.maxHealth }, Is.EqualTo(new[] { 11, 5, 6, 68 }));
        Assert.That(run.health, Is.EqualTo(20)); Assert.That(run.experience, Is.EqualTo(5));
        Assert.That(run.deck.Count, Is.EqualTo(10));
    }
    [TestCase(24.84)] [TestCase(23.17)] [TestCase(31.95)]
    public void HealingKeepsQuantizedHealthValidForPersistence(double health)
    {
        var run = New(); run.health = health; Give(run, ItemKind.Potion);
        Assert.That(Inventory.Use(run, 0), Is.True);
        Assert.That(run.health, Is.EqualTo(CharacterAttributes.Quantize((decimal)health + 20)));
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
        Assert.That(new PrototypeStore(folder).Continue().health, Is.EqualTo(run.health));
    }
    [Test]
    public void EssencesAndTwoBattlePotionPersistAndExpireExactlyOnce()
    {
        var run = New(); Give(run, ItemKind.WisdomEssence); Assert.That(Inventory.Use(run, 0), Is.True);
        Give(run, ItemKind.AssaultPotion); Give(run, ItemKind.AssaultPotion);
        Assert.That(Inventory.Use(run, 0), Is.True); Assert.That(Inventory.Use(run, 0), Is.False);
        Assert.That(CharacterAttributes.Calculate(run).strength, Is.EqualTo(10));
        Assert.That(CharacterAttributes.Calculate(run, true).strength, Is.EqualTo(12));
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run); run = new PrototypeStore(folder).Continue();
        Assert.That(run.character.secondary, Is.EqualTo(AttributeKind.Wisdom));
        Assert.That(CharacterAttributes.Calculate(run).wisdom, Is.EqualTo(6));
        var first = run.Map.enemies[0]; Growth.FinishVictory(run, first); Growth.FinishVictory(run, first);
        Assert.That(run.character.modifiers.Single(m => m.source == "assault-potion").remainingBattles, Is.EqualTo(1));
        Growth.FinishVictory(run, run.Map.enemies[1]);
        Assert.That(run.character.modifiers.Any(m => m.source == "assault-potion"), Is.False);
        Assert.That(CharacterAttributes.Calculate(run, true).strength, Is.EqualTo(10));
        Assert.That(run.character.modifiers.Single().remainingBattles, Is.EqualTo(-1));
    }
    [Test]
    public void EquippedItemsParticipateInSettlementAndWarehouseLedger()
    {
        var run = New(); var item = Give(run, ItemKind.InscribedSword); Inventory.Equip(run, 0);
        Assert.That(Inventory.Items(run).Single().id, Is.EqualTo(item.id));
        var store = new PrototypeStore(folder); store.BeginSettlement(run, false); store.SaveSelection(new[] { item.id });
        var cancelled = store.CancelSettlement(); Assert.That(cancelled.character.equipment.Single().id, Is.EqualTo(item.id));
        store.BeginSettlement(cancelled, false); store.SaveSelection(new[] { item.id });
        var interrupted = Rules.Copy(store.Session); interrupted.pending.confirmed = true;
        store.CommitSettlement(); Assert.That(TownRules.Used(store.Warehouse), Is.EqualTo(1));
        Assert.That(store.Warehouse.equipment.Single().definition.wisdomScaling, Is.EqualTo(6000));
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonUtility.ToJson(interrupted));
        var reloaded = new PrototypeStore(folder); reloaded.CommitSettlement();
        Assert.That(reloaded.Warehouse.equipment.Count, Is.EqualTo(1));
        Assert.That(reloaded.Warehouse.counts[(int)ItemKind.InscribedSword], Is.Zero);
    }
    [Test]
    public void RuleSnapshotRoundTripsAndLegacySaveKeepsLegacyDamage()
    {
        var run = New(); run.attributeRules.Find(ItemKind.IronSword).strengthScaling = 8500;
        Give(run, ItemKind.IronSword); Inventory.Equip(run, 0); run.health = 32.75;
        var store = new PrototypeStore(folder); store.SaveCheckpoint(run);
        var loaded = new PrototypeStore(folder).Continue();
        Assert.That(JsonUtility.ToJson(loaded), Is.EqualTo(JsonUtility.ToJson(run)));
        Assert.That(loaded.health, Is.EqualTo(32.75));
        Assert.That(CharacterAttributes.Calculate(loaded).attack, Is.EqualTo(10.5m));
        var legacy = MapGenerator.NewRun(12345, false);
        File.WriteAllText(Path.Combine(folder, "session.json"), JsonUtility.ToJson(new SessionData { version = 1, checkpoint = legacy }));
        store = new PrototypeStore(folder); loaded = store.Continue();
        Assert.That(loaded.UsesAttributes, Is.False); Assert.That(CombatNumbers.Damage(loaded, CardKind.Strike), Is.EqualTo(6));
        Growth.Award(loaded, 20); Growth.NextUpgrade(loaded); Assert.That(loaded.MaxHealth, Is.EqualTo(60));
        store.SaveCheckpoint(loaded); Assert.That(File.Exists(Path.Combine(folder, "session.json.pre-attributes.bak")), Is.True);
    }
    [TestCase(false)] [TestCase(true)]
    public void ExistingAttributeSavesAdoptOnePercentWithoutLosingProgress(bool pending)
    {
        var run = New(); Give(run, ItemKind.InscribedSword); Inventory.Equip(run, 0); run.health = 32.75;
        var store = new PrototypeStore(folder);
        if (pending) store.BeginSettlement(run, false); else store.SaveCheckpoint(run);
        var oldSession = Rules.Copy(store.Session);
        var oldRun = pending ? oldSession.pending.run : oldSession.checkpoint;
        oldRun.attributeRules.version = 1; oldRun.attributeRules.secondaryPerPoint = 1000;
        string original = JsonUtility.ToJson(oldSession);
        File.WriteAllText(Path.Combine(folder, "session.json"), original);
        var loaded = new PrototypeStore(folder);
        var restored = pending ? loaded.Session.pending.run : loaded.Continue();
        Assert.That(restored.attributeRules.version, Is.EqualTo(2));
        Assert.That(restored.attributeRules.secondaryPerPoint, Is.EqualTo(100));
        Assert.That(CharacterAttributes.Calculate(restored).secondaryMultiplier, Is.EqualTo(1.05m));
        Assert.That(restored.health, Is.EqualTo(32.75)); Assert.That(restored.character.secondary, Is.EqualTo(AttributeKind.Wisdom));
        Assert.That(restored.character.equipment.Single().kind, Is.EqualTo(ItemKind.InscribedSword));
        Assert.That(JsonUtility.ToJson(restored.maps), Is.EqualTo(JsonUtility.ToJson(run.maps)));
        Assert.That(File.ReadAllText(Path.Combine(folder, "session.json.pre-secondary-1pct.bak")), Is.EqualTo(original));
        var expected = Rules.Copy(run);
        Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(JsonUtility.ToJson(expected)));
        if (pending) restored = loaded.CancelSettlement();
        loaded.SaveCheckpoint(restored);
        Assert.That(new PrototypeStore(folder).Continue().attributeRules.secondaryPerPoint, Is.EqualTo(100));
    }
    [Test]
    public void InvalidSecondaryDuplicateEquipmentAndModifiersAreRejected()
    {
        var run = New(); run.character.secondary = AttributeKind.Strength;
        Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(run));
        run = New(); var item = Give(run, ItemKind.IronSword); Inventory.Equip(run, 0); run.inventory[0].units.Add(item);
        Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(run));
        run = New(); run.character.modifiers.Add(new AttributeModifier { source = "assault-potion", value = 2, attribute = AttributeKind.Strength, remainingBattles = 3 });
        Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(run));
    }

    [Test]
    public void NewRuleMapsAndGuardLevelsValidateAcrossOneHundredSeeds()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var run = New(AttributeKind.Wisdom, seed); PrototypeStore.ValidateRun(run);
            foreach (var map in run.maps.Take(4))
            {
                Assert.That(MapGenerator.ValidateTerrain(map, out _, out _), Is.True, "Seed " + seed);
                Assert.That(map.loot.Count, Is.EqualTo(10)); Assert.That(map.enemies.Count, Is.EqualTo(10));
                foreach (var guard in map.enemies.Where(e => e.role == EnemyRole.ItemGuard))
                    Assert.That(guard.Level, Is.EqualTo(map.loot.Where(l => l.guardEnemyId == guard.id).Max(l => Rules.Rarity(l.units[0].kind)) + 1));
            }
        }
    }

    [TestCase(AttributeKind.Agility)] [TestCase(AttributeKind.Wisdom)]
    public void BothSecondaryRoutesCompleteFiveMapsWithSaveAndSettlement(AttributeKind secondary)
    {
        var run = New(secondary); var store = new PrototypeStore(folder);
        for (int floor = 0; floor < 5; floor++)
        {
            run.mapIndex = floor; run.x = run.Map.spawnX; run.y = run.Map.spawnY;
            foreach (var enemy in run.Map.enemies.OrderBy(e => e.Level).ThenBy(e => e.kind).Take(2))
            {
                foreach (var loot in run.Map.loot) Inventory.Pickup(run, loot);
                for (int i = 0; i < run.inventory.Count; i++)
                {
                    if (run.inventory[i].units.Count == 0) continue;
                    var kind = run.inventory[i].units[0].kind;
                    if (Rules.IsEquipment(kind)) Inventory.Equip(run, i);
                    else if (kind == ItemKind.Potion && run.MaxHealth - run.health >= 15 || kind >= ItemKind.StrengthEssence)
                        Inventory.Use(run, i);
                }
                var battle = new Battle(run, enemy);
                for (int turns = 0; turns < 100 && battle.outcome == BattleOutcome.None; turns++)
                {
                    for (int plays = 0; plays < 20 && battle.outcome == BattleOutcome.None; plays++)
                    {
                        int chosen = -1; double best = -1;
                        for (int i = 0; i < battle.hand.Count; i++)
                        {
                            var card = battle.hand[i]; if (Rules.Costs[(int)card] > battle.energy) continue;
                            double damage = battle.Preview(card), block = CombatNumbers.Block(run, card);
                            double score = damage / Rules.Costs[(int)card];
                            if (damage >= battle.enemyHealth && damage > 0) score = 1000;
                            else if (block > 0 && battle.ExpectedHealthLoss > 0) score = 40 + Math.Min(block, battle.ExpectedHealthLoss);
                            else if (card == CardKind.Prepare || card == CardKind.Steady) score = battle.energy >= 2 ? 20 : 1;
                            else if (card == CardKind.Spirit) score = battle.energy >= 2 ? 15 : 0;
                            if (score > best) { best = score; chosen = i; }
                        }
                        if (chosen < 0) break; battle.Play(chosen);
                    }
                    if (battle.outcome == BattleOutcome.None) { battle.EndPlayerTurn(); battle.ResolveEnemyTurn(); }
                }
                Assert.That(battle.outcome, Is.EqualTo(BattleOutcome.Victory), secondary + " floor " + (floor + 1) + " HP " + run.health);
                Growth.FinishVictory(run, enemy);
                foreach (var loot in run.Map.loot) Inventory.Pickup(run, loot);
                System.Collections.Generic.List<CardKind> choices;
                while ((choices = Growth.NextUpgrade(run)) != null)
                    run.deck.Add(choices.OrderByDescending(c => c == CardKind.Shield ? 10 : c == CardKind.Heavy ? 9 : c == CardKind.Quick ? 8 : c == CardKind.Fortify ? 7 : 1).First());
            }
            if (floor == 2)
            {
                store.SaveCheckpoint(run); store = new PrototypeStore(folder); run = store.Continue();
                Assert.That(run.character.secondary, Is.EqualTo(secondary));
            }
        }
        Assert.That(run.bossDefeated, Is.True); Assert.That(run.level, Is.EqualTo(4));
        PrototypeStore.ValidateRun(run); store.BeginSettlement(run, false);
        store.SaveSelection(Inventory.Items(run).OrderByDescending(i => Rules.Rarity(i.kind)).Take(10).Select(i => i.id).ToList());
        int selected = store.Session.pending.selectedIds.Count; store.CommitSettlement();
        Assert.That(TownRules.Used(store.Warehouse), Is.EqualTo(selected));
        Assert.That(new PrototypeStore(folder).Session.checkpoint, Is.Null);
    }
}
