using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class PrototypeSettingsTests
{
    static RunData New() => MapGenerator.NewRun(12345, MapGenerationMode.RoomsAndCorridors, false);
    [Test]
    public void DefaultsPreserveGeneratedContentAndCombat()
    {
        var run = New(); var original = Rules.Copy(run); new PrototypeSettings().Apply(run);
        CollectionAssert.AreEqual(original.deck, run.deck);
        for (int i = 0; i < run.maps.Count; i++) CollectionAssert.AreEqual(original.maps[i].enemies.Select(e => e.id), run.maps[i].enemies.Select(e => e.id));
        foreach (CardKind card in Enum.GetValues(typeof(CardKind)))
        {
            Assert.That(CombatNumbers.Damage(run, card), Is.EqualTo(CombatNumbers.Damage(original, card)));
            Assert.That(CombatNumbers.Block(run, card), Is.EqualTo(CombatNumbers.Block(original, card)));
            Assert.That(PrototypeSettings.Cost(run, card), Is.EqualTo(Rules.Costs[(int)card]));
        }
        PrototypeStore.ValidateRun(run);
    }
    [TestCase(MapGenerationMode.RoomsAndCorridors)]
    [TestCase(MapGenerationMode.Excavated)]
    public void CustomCountsPreserveGuardsMerchantSpacingAndSaveValidity(MapGenerationMode mode)
    {
        var run = MapGenerator.NewRun(321, mode, false);
        var settings = new PrototypeSettings { roamingCounts = new[] { 7, 4 } };
        var guardIds = run.maps.SelectMany(m => m.enemies).Where(e => e.role == EnemyRole.ItemGuard).Select(e => e.id).ToArray();
        settings.Apply(run);
        foreach (var map in run.maps.Take(4))
        {
            Assert.That(map.enemies.Count(e => e.role == EnemyRole.Roaming && e.kind == EnemyKind.Bat), Is.EqualTo(7));
            Assert.That(map.enemies.Count(e => e.role == EnemyRole.Roaming && e.kind == EnemyKind.Beetle), Is.EqualTo(4));
            Assert.That(MapGenerator.ValidateCombatApproaches(map), Is.True);
        }
        CollectionAssert.AreEqual(guardIds, run.maps.SelectMany(m => m.enemies).Where(e => e.role == EnemyRole.ItemGuard).Select(e => e.id));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void DisabledSystemsHaveActualEffects()
    {
        var run = New();
        var settings = new PrototypeSettings { fog = false, merchant = false, teeth = false, growth = false, itemGuards = false, corridorGuards = false, roamingCounts = new[] { 0, 0 }, consumables = false, equipment = false };
        settings.Apply(run);
        Assert.That(run.maps.All(m => !m.hasMerchant && m.revealed.All(v => v)), Is.True);
        Assert.That(run.maps.Take(4).All(m => m.enemies.Count == 0), Is.True);
        Assert.That(run.maps.SelectMany(m => m.loot).All(l => !l.combatApproach), Is.True);
        Growth.Award(run, 100); Assert.That(run.experience, Is.Zero);
        var enemy = new EnemyData { kind = EnemyKind.Bat }; Growth.FinishVictory(run, enemy); Assert.That(run.teeth, Is.Zero);
        run.inventory[0].units.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.Potion }); run.health--;
        Assert.That(Inventory.Use(run, 0), Is.False);
        var box = run.Map.loot.First(l => l.containerKind == ContainerKind.Equipment); ContainerLoot.Open(run, box);
        Assert.That(box.units, Is.Empty); Assert.That(box.BlocksMovement, Is.True);
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void ZeroWeightsOpenEmptyAndSinglePositiveWeightIsCertain()
    {
        var run = New(); var settings = new PrototypeSettings();
        foreach (var d in settings.drops.treasure) d.weight = 0;
        settings.Apply(run);
        var box = run.Map.loot.First(l => l.containerKind == ContainerKind.Treasure);
        Assert.That(ContainerLoot.Open(run, box), Is.True); Assert.That(box.units, Is.Empty);
        Assert.That(box.HasContents, Is.False); Assert.That(box.BlocksMovement, Is.True);
        foreach (var d in settings.drops.supplies) d.weight = d.kind == ItemKind.Torch ? 1 : 0;
        run.containerRules = Rules.Copy(settings.drops);
        var bag = run.Map.loot.First(l => l.containerKind == ContainerKind.Supplies); ContainerLoot.Open(run, bag);
        Assert.That(bag.units.Single().kind, Is.EqualTo(ItemKind.Torch));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void CardCostPowerDrawAndEnemyNumbersAffectResolution()
    {
        var run = New(); var settings = new PrototypeSettings();
        settings.cardCounts = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; settings.cardCosts[0] = 0; settings.cardPower[0] = 200; settings.cardDraw[0] = 1;
        settings.enemyHealth[0] = 200; settings.enemyAttack[0] = 17; settings.enemyDefense[0] = 2;
        settings.Apply(run);
        var enemy = run.Map.enemies.First(e => e.kind == EnemyKind.Bat); var battle = new Battle(run, enemy);
        Assert.That(battle.enemyHealth, Is.EqualTo(200 + 4 * (enemy.Level - 1)));
        Assert.That(battle.IntentAttack, Is.EqualTo(17 + enemy.Level - 1));
        Assert.That(CombatNumbers.Damage(run, CardKind.Strike), Is.EqualTo(CombatNumbers.RoundDamage(CharacterAttributes.Calculate(run).attack * run.attributeRules.cardMultipliers[0] / 10000m * 2m)));
        Assert.That(battle.Play(0), Is.True); Assert.That(battle.energy, Is.EqualTo(3));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void SettingsAndRunSnapshotsPersistIndependently()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "SettingsTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PrototypeStore(folder); var settings = new PrototypeSettings { torch = false }; store.SaveSettings(settings);
            var run = New(); store.Settings.Apply(run); store.SaveCheckpoint(run);
            settings.torch = true; store.SaveSettings(settings);
            var loaded = new PrototypeStore(folder);
            Assert.That(loaded.Settings.torch, Is.True); Assert.That(PrototypeSettings.For(loaded.Continue()).torch, Is.False);
            Assert.That(loaded.Settings.drops, Is.Not.SameAs(run.containerRules));
            Assert.That(PrototypeSettings.For(New()), Is.Null);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [TestCase(0)]
    [TestCase(100)]
    public void BossRelicChanceDoesNotPreventReturnOrConsumeContainerRandomness(int chance)
    {
        var run = New(); new PrototypeSettings { bossRelicChance = chance }.Apply(run);
        run.mapIndex = 4; run.x = run.Map.spawnX; run.y = run.Map.spawnY;
        uint random = run.containerRandom.state;
        Growth.FinishVictory(run, run.Map.enemies.Single());
        Assert.That(run.Map.loot.Count, Is.EqualTo(chance == 100 ? 1 : 0));
        Assert.That(run.Map.At(6, 10), Is.EqualTo(Tile.Return)); Assert.That(run.containerRandom.state, Is.EqualTo(random));
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void DamagedSettingsDoNotBlockSessionAndAreNotOverwrittenOnRead()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "SettingsTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PrototypeStore(folder); store.SaveCheckpoint(New());
            string path = Path.Combine(folder, "settings.json"); File.WriteAllText(path, "invalid-json");
            var loaded = new PrototypeStore(folder); Assert.That(loaded.SettingsWarning, Is.Not.Null);
            Assert.That(loaded.Continue(), Is.Not.Null); Assert.That(File.ReadAllText(path), Is.EqualTo("invalid-json"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test]
    public void InvalidConfigurationCannotReplaceSavedSettings()
    {
        var settings = new PrototypeSettings(); settings.cardCounts = new int[10]; Assert.Throws<InvalidOperationException>(() => settings.Validate());
        settings = new PrototypeSettings(); settings.drops.supplies[0].minimum = 3; settings.drops.supplies[0].maximum = 2;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }
    [Test]
    public void DisabledCardRewardsStillProcessAllEarnedLevels()
    {
        var run = New(); new PrototypeSettings { cardRewards = false }.Apply(run);
        Growth.Award(run, 50); Assert.That(Growth.NextUpgrade(run), Is.Null); Assert.That(run.level, Is.EqualTo(3)); Assert.That(run.deck.Count, Is.EqualTo(10));
    }
}
