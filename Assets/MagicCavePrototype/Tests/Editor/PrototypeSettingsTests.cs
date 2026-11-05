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
    [TestCase(CardKind.Strike)]
    [TestCase(CardKind.Defend)]
    [TestCase(CardKind.Prepare)]
    [TestCase(CardKind.Heavy)]
    [TestCase(CardKind.Double)]
    [TestCase(CardKind.Shield)]
    [TestCase(CardKind.Fortify)]
    [TestCase(CardKind.Quick)]
    [TestCase(CardKind.Spirit)]
    [TestCase(CardKind.Steady)]
    public void CustomCardsResolveTemplateWithIndependentNumbers(CardKind template)
    {
        var run = New(); var settings = new PrototypeSettings { cardCounts = new int[10] };
        settings.customCards.Add(new CustomCardDefinition { id = 42, name = "试作牌", template = template, count = 7, cost = 0, power = 200, draw = 1, spirit = 7 });
        settings.enemyHealth[0] = 10000; settings.Apply(run);
        var card = (CardKind)42;
        Assert.That(run.deck, Has.Count.EqualTo(7));
        Assert.That(run.deck.All(c => c == card), Is.True);
        var enemy = new EnemyData { kind = EnemyKind.Bat, physicalDefense = 2 };
        var battle = new Battle(run, enemy) { playerBlock = 8 };
        double before = battle.enemyHealth, preview = battle.Preview(card);
        double expectedBlock = CombatNumbers.Block(run, card);
        Assert.That(battle.Play(0), Is.True);
        Assert.That(battle.energy, Is.EqualTo(3));
        Assert.That(battle.hand.Count, Is.EqualTo(5));
        Assert.That(battle.discard.Single(), Is.EqualTo(card));
        Assert.That(before - battle.enemyHealth, Is.EqualTo(preview));
        Assert.That(battle.playerBlock, Is.EqualTo(8 + expectedBlock));
        Assert.That(battle.spirit, Is.EqualTo(template == CardKind.Spirit ? 7 : 0));
        Assert.That(PrototypeSettings.Cost(run, template), Is.EqualTo(Rules.Costs[(int)template]));
        Assert.That(PrototypeSettings.Name(run, card), Is.EqualTo("试作牌"));
        if (template == CardKind.Shield) Assert.That(preview, Is.EqualTo(14));
        if (template == CardKind.Double)
        {
            var stats = CharacterAttributes.Calculate(run);
            double each = CombatNumbers.RoundDamage(stats.attack * run.attributeRules.cardMultipliers[(int)template] / 10000m * 2m);
            Assert.That(preview, Is.EqualTo(2 * Math.Max(0, each - 2)));
        }
        PrototypeStore.ValidateRun(run);
    }
    [Test]
    public void CustomDefinitionsSurviveSaveAndLaterDeletionFromSettings()
    {
        string folder = Path.Combine(Application.temporaryCachePath, "CustomCards-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PrototypeStore(folder); var settings = new PrototypeSettings();
            settings.customCards.Add(new CustomCardDefinition { id = 20, name = "旧局专属", template = CardKind.Double, count = 3, power = 180 });
            store.SaveSettings(settings);
            var run = New(); store.Settings.Apply(run); store.SaveCheckpoint(run);
            settings.customCards.Clear(); store.SaveSettings(settings);
            var loaded = new PrototypeStore(folder); var continued = loaded.Continue();
            Assert.That(loaded.Settings.customCards, Is.Empty);
            Assert.That(continued.deck.Count(c => (int)c == 20), Is.EqualTo(3));
            Assert.That(PrototypeSettings.Name(continued, (CardKind)20), Is.EqualTo("旧局专属"));
            Assert.That(PrototypeSettings.Power(continued, (CardKind)20), Is.EqualTo(1.8m));
            continued.deck.Add((CardKind)21);
            Assert.Throws<System.IO.InvalidDataException>(() => PrototypeStore.ValidateRun(continued));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test]
    public void CustomConfigurationRejectsDuplicateIdsUnknownTemplatesAndOversizedDeck()
    {
        var settings = new PrototypeSettings();
        var card = new CustomCardDefinition { id = 10 };
        settings.customCards.Add(card); settings.customCards.Add(Rules.Copy(card));
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        settings.customCards.RemoveAt(1); card.template = (CardKind)10;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        card.template = CardKind.Strike; card.name = " ";
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        card.name = "测试"; settings.cardCounts = Enumerable.Repeat(10, 10).ToArray();
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        settings.customCards = null; settings.Validate();
        Assert.That(settings.customCards, Is.Empty);
    }

    static CardEffectEntry Effect(CardEffectKind kind, CardEffectTarget target, CardValueSource source, int amount, int repeat = 1)
        => new CardEffectEntry { kind = kind, target = target, repeat = repeat,
            terms = new System.Collections.Generic.List<CardFormulaTerm> { new CardFormulaTerm { source = source, amount = amount } } };
    static CustomCardDefinition Composed(params CardEffectEntry[] effects) => new CustomCardDefinition
        { id = 30, name = "拼接词条牌", effectsVersion = 1, count = 7, effects = effects.ToList() };
    [Test]
    public void ComposedCardAddsAttributesAndExecutesMultipleEffectsWithMatchingPreview()
    {
        var damage = Effect(CardEffectKind.Damage, CardEffectTarget.Enemy, CardValueSource.ActualAttack, 100, 2);
        damage.terms.Add(new CardFormulaTerm { source = CardValueSource.Strength, amount = 50 });
        var card = Composed(damage,
            Effect(CardEffectKind.Block, CardEffectTarget.Self, CardValueSource.Agility, 50),
            Effect(CardEffectKind.Draw, CardEffectTarget.Self, CardValueSource.Fixed, 1),
            Effect(CardEffectKind.Spirit, CardEffectTarget.Self, CardValueSource.Fixed, 2));
        card.effects[1].terms.Add(new CardFormulaTerm { source = CardValueSource.Wisdom, amount = 50 });
        var settings = new PrototypeSettings { cardCounts = new int[10] }; settings.customCards.Add(card);
        var run = New(); settings.Apply(run);
        var battle = new Battle(run, new EnemyData { kind = EnemyKind.Bat, physicalDefense = 2 }) { enemyHealth = 100, enemyBlock = 3 };
        var stats = CharacterAttributes.Calculate(run, true);
        double each = CombatNumbers.RoundDamage(stats.attack + stats.strength * .5m);
        double expected = Math.Max(0, 2 * Math.Max(0, each - 2) - 3);
        Assert.That(battle.Preview((CardKind)30), Is.EqualTo(expected));
        Assert.That(battle.enemyHealth, Is.EqualTo(100)); Assert.That(battle.enemyBlock, Is.EqualTo(3));
        Assert.That(CardEffects.Sentence(damage), Does.Contain("100%实际攻击力＋50%力量"));
        Assert.That(battle.Play(0), Is.True);
        Assert.That(battle.enemyHealth, Is.EqualTo(100 - expected)); Assert.That(battle.energy, Is.EqualTo(2));
        Assert.That(battle.playerBlock, Is.EqualTo(CombatNumbers.RoundDamage((stats.agility + stats.wisdom) * .5m)));
        Assert.That(battle.hand.Count, Is.EqualTo(5)); Assert.That(battle.spirit, Is.EqualTo(2));
        PrototypeStore.ValidateRun(Rules.Copy(run));
    }
    [Test]
    public void EffectOrderChangesLaterBlockScalingAndSpiritDamage()
    {
        var run = New();
        var card = Composed(Effect(CardEffectKind.Block, CardEffectTarget.Self, CardValueSource.Fixed, 10),
            Effect(CardEffectKind.Spirit, CardEffectTarget.Self, CardValueSource.Fixed, 3),
            Effect(CardEffectKind.Damage, CardEffectTarget.Enemy, CardValueSource.CurrentBlock, 100));
        var before = new CardEffectState { playerHealth = 60, enemyHealth = 100 };
        CardEffects.Resolve(run, card, 0, 0, before);
        Assert.That(before.enemyHealth, Is.EqualTo(87)); Assert.That(before.playerBlock, Is.EqualTo(10));
        Assert.That(CardEffects.Description(run, card, false), Does.Contain("敌：13物伤"));
        card.effects.Reverse();
        var after = new CardEffectState { playerHealth = 60, enemyHealth = 100 };
        CardEffects.Resolve(run, card, 0, 0, after);
        Assert.That(after.enemyHealth, Is.EqualTo(100));
        Assert.That(after.playerBlock, Is.EqualTo(10)); Assert.That(after.spirit, Is.EqualTo(3));
    }
    [Test]
    public void FormulaRoundsOnceAfterAddingTermsAndCapsDrawAndSpirit()
    {
        var run = New();
        var effect = Effect(CardEffectKind.Block, CardEffectTarget.Self, CardValueSource.Fixed, 0);
        effect.terms.Clear();
        effect.terms.Add(new CardFormulaTerm { source = CardValueSource.Wisdom, amount = 10 });
        effect.terms.Add(new CardFormulaTerm { source = CardValueSource.Wisdom, amount = 10 });
        Assert.That(CardEffects.Amount(run, effect, 0, 0, false), Is.EqualTo(1));
        var card = Composed(Effect(CardEffectKind.Draw, CardEffectTarget.Self, CardValueSource.Fixed, 1000),
            Effect(CardEffectKind.Spirit, CardEffectTarget.Self, CardValueSource.Fixed, 1000));
        int drawn = 0; var state = new CardEffectState { playerHealth = 60, enemyHealth = 100 };
        CardEffects.Resolve(run, card, 0, 0, state, n => drawn += n);
        Assert.That(drawn, Is.EqualTo(20)); Assert.That(state.spirit, Is.EqualTo(100));
    }
    [Test]
    public void SelfDamageAndEnemyBlockUseTheirOwnTargetsAndStopAfterDeath()
    {
        var run = New();
        var card = Composed(Effect(CardEffectKind.Block, CardEffectTarget.Enemy, CardValueSource.Fixed, 4),
            Effect(CardEffectKind.Damage, CardEffectTarget.Self, CardValueSource.Fixed, 10, 2),
            Effect(CardEffectKind.Draw, CardEffectTarget.Self, CardValueSource.Fixed, 1));
        var state = new CardEffectState { playerHealth = 10, playerBlock = 3, enemyHealth = 100 };
        int drawn = 0; CardEffects.Resolve(run, card, 2, 1, state, n => drawn += n);
        Assert.That(state.playerHealth, Is.Zero); Assert.That(state.enemyHealth, Is.EqualTo(100));
        Assert.That(state.enemyBlock, Is.EqualTo(4)); Assert.That(drawn, Is.Zero);
        card.effects = new[] { Effect(CardEffectKind.Damage, CardEffectTarget.Enemy, CardValueSource.Fixed, 20),
            Effect(CardEffectKind.Spirit, CardEffectTarget.Self, CardValueSource.Fixed, 8) }.ToList();
        state = new CardEffectState { playerHealth = 10, enemyHealth = 5 };
        CardEffects.Resolve(run, card, 0, 0, state);
        Assert.That(state.enemyHealth, Is.Zero); Assert.That(state.spirit, Is.Zero);
    }
    [Test]
    public void ComposedSnapshotsRemainIndependentAndRejectInvalidEffects()
    {
        var settings = new PrototypeSettings();
        var card = Composed(Effect(CardEffectKind.Damage, CardEffectTarget.Enemy, CardValueSource.ActualAttack, 100));
        settings.customCards.Add(card); var run = New(); settings.Apply(run);
        card.effects[0].terms[0].amount = 200;
        Assert.That(PrototypeSettings.Custom(Rules.Copy(run), (CardKind)30).effects[0].terms[0].amount, Is.EqualTo(100));
        card.effects[0].kind = CardEffectKind.Draw;
        Assert.Throws<InvalidOperationException>(() => settings.Validate()); // Enemy cannot draw player's cards.
        card.effects[0].target = CardEffectTarget.Self; card.effects[0].repeat = 2;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        card.effects[0].repeat = 1; card.effects[0].terms.Clear();
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        card.effects.Clear(); Assert.Throws<InvalidOperationException>(() => settings.Validate());
        card.effectsVersion = 2; Assert.Throws<InvalidOperationException>(() => settings.Validate());
    }

}
