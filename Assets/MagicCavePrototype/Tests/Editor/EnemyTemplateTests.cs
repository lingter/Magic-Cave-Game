using System;
using System.IO;
using System.Linq;
using MagicCave.Prototype;
using NUnit.Framework;
using UnityEngine;

public sealed class EnemyTemplateTests
{
    static RunData New() => MapGenerator.NewRun(12345, MapGenerationMode.RoomsAndCorridors, false);
    static CardEffectEntry Effect(CardEffectKind kind, CardValueSource source, int amount, int repeat = 1) => new CardEffectEntry
    {
        kind = kind, target = kind == CardEffectKind.Damage ? CardEffectTarget.Enemy : CardEffectTarget.Self, repeat = repeat,
        terms = new System.Collections.Generic.List<CardFormulaTerm> { new CardFormulaTerm { source = source, amount = amount } }
    };
    [TestCase(EnemyKind.Bat)]
    [TestCase(EnemyKind.Beetle)]
    [TestCase(EnemyKind.Boss)]
    public void LoadingExistingTemplatesPreservesDefaultTurnCycle(EnemyKind kind)
    {
        var settings = new PrototypeSettings(); var original = New(); settings.Apply(original);
        settings.enemyTemplates.Add(EnemyTemplates.FromExisting(settings, kind));
        var custom = New(); settings.Apply(custom);
        for (int turn = 0; turn < 6; turn++)
        {
            var oldRun = Rules.Copy(original); var newRun = Rules.Copy(custom);
            var oldBattle = new Battle(oldRun, new EnemyData { kind = kind, level = 4 }) { enemyTurn = turn, playerBlock = 7, enemyBlock = 4 };
            var newBattle = new Battle(newRun, new EnemyData { kind = kind, level = 4, templateId = (int)kind + 1 }) { enemyTurn = turn, playerBlock = 7, enemyBlock = 4 };
            Assert.That(newBattle.enemyHealth, Is.EqualTo(oldBattle.enemyHealth));
            Assert.That(newBattle.IntentAttack, Is.EqualTo(oldBattle.IntentAttack));
            Assert.That(newBattle.ExpectedHealthLoss, Is.EqualTo(Math.Min(oldRun.health, oldBattle.ExpectedHealthLoss)));
            oldBattle.EndPlayerTurn(); newBattle.EndPlayerTurn(); oldBattle.ResolveEnemyTurn(); newBattle.ResolveEnemyTurn();
            Assert.That(newRun.health, Is.EqualTo(oldRun.health)); Assert.That(newBattle.enemyBlock, Is.EqualTo(oldBattle.enemyBlock));
            Assert.That(newBattle.outcome, Is.EqualTo(oldBattle.outcome));
        }
    }
    [Test]
    public void CustomEnemyGenerationIsSeededAndKeepsGuardLayoutAndSaveSnapshot()
    {
        var settings = new PrototypeSettings();
        var baseline = EnemyTemplates.FromExisting(settings, EnemyKind.Bat); baseline.weight = 0;
        var custom = EnemyTemplates.FromExisting(settings, EnemyKind.Bat); custom.id = 4; custom.name = "自定义蝠"; custom.defense = 3; custom.health = 77;
        settings.enemyTemplates.Add(baseline); settings.enemyTemplates.Add(custom);
        var run = New(); var original = Rules.Copy(run); settings.Apply(run); var repeated = New(); settings.Apply(repeated);
        Assert.That(run.maps.SelectMany(m => m.enemies).Where(e => e.kind == EnemyKind.Bat).All(e => e.templateId == 4 && e.physicalDefense == 3), Is.True);
        CollectionAssert.AreEqual(run.maps.SelectMany(m => m.enemies).Select(e => e.templateId), repeated.maps.SelectMany(m => m.enemies).Select(e => e.templateId));
        CollectionAssert.AreEqual(original.maps.SelectMany(m => m.enemies).Select(e => new { e.id, e.x, e.y, e.kind, e.role }), run.maps.SelectMany(m => m.enemies).Select(e => new { e.id, e.x, e.y, e.kind, e.role }));
        Assert.That(run.maps.Take(4).All(MapGenerator.ValidateCombatApproaches), Is.True);
        custom.name = "新名字"; custom.health = 1;
        var enemy = run.Map.enemies.First(e => e.templateId == 4);
        Assert.That(EnemyTemplates.Label(run, enemy), Does.StartWith("自定义蝠")); Assert.That(PrototypeSettings.Health(run, enemy), Is.EqualTo(77));
        string folder = Path.Combine(Application.temporaryCachePath, "EnemyTemplates-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PrototypeStore(folder); store.SaveCheckpoint(run); var loaded = new PrototypeStore(folder).Continue();
            Assert.That(loaded.Map.enemies.First(e => e.templateId == 4).physicalDefense, Is.EqualTo(3));
            loaded.Map.enemies.First().templateId = 999;
            Assert.Throws<InvalidDataException>(() => PrototypeStore.ValidateRun(loaded));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    [Test]
    public void EnemyMultiEffectPreviewMatchesSegmentDefenseAndNextTurnCycle()
    {
        var settings = new PrototypeSettings(); var template = EnemyTemplates.FromExisting(settings, EnemyKind.Bat); template.attack = 10;
        template.turns[0].effects = new[] { Effect(CardEffectKind.Damage, CardValueSource.ActualAttack, 100, 2), Effect(CardEffectKind.Block, CardValueSource.Fixed, 5) }.ToList();
        template.turns.Add(new EnemyTurnDefinition { effects = new[] { Effect(CardEffectKind.Spirit, CardValueSource.Fixed, 2), Effect(CardEffectKind.Damage, CardValueSource.CurrentBlock, 100) }.ToList() });
        settings.enemyTemplates.Add(template); var run = New(); settings.Apply(run);
        run.character.equipment.Add(new ItemUnit { id = run.nextItemId++, kind = ItemKind.IronArmor });
        var enemy = run.Map.enemies.First(e => e.kind == EnemyKind.Bat);
        var battle = new Battle(run, enemy) { playerBlock = 3 };
        int defense = CharacterAttributes.Calculate(run, true).defense;
        double expected = Math.Min(run.health, Math.Max(0, 2 * Math.Max(0, 10 - defense) - 3));
        Assert.That(battle.ExpectedHealthLoss, Is.EqualTo(expected)); Assert.That(battle.Intent, Does.Contain("自身格挡5"));
        double health = run.health; battle.EndPlayerTurn(); battle.ResolveEnemyTurn();
        Assert.That(run.health, Is.EqualTo(health - expected)); Assert.That(battle.enemyBlock, Is.EqualTo(5));
        Assert.That(battle.enemyTurn, Is.EqualTo(1));
        Assert.That(battle.IntentAttack, Is.EqualTo(2), "Old enemy block expires at enemy-turn start.");
    }
    [Test]
    public void TemplatesRejectMissingPoolUnsupportedEffectsAndDuplicateIds()
    {
        var settings = new PrototypeSettings(); var template = EnemyTemplates.FromExisting(settings, EnemyKind.Bat);
        settings.enemyTemplates.Add(template); template.weight = 0;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        template.weight = 1; template.turns[0].effects[0].kind = CardEffectKind.Draw;
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        template.turns[0].effects[0] = Effect(CardEffectKind.Damage, CardValueSource.Wisdom, 100);
        Assert.Throws<InvalidOperationException>(() => settings.Validate());
        template.turns[0].effects[0] = Effect(CardEffectKind.Damage, CardValueSource.ActualAttack, 100);
        settings.enemyTemplates.Add(Rules.Copy(template)); Assert.Throws<InvalidOperationException>(() => settings.Validate());
        settings.enemyTemplates = null; settings.Validate(); Assert.That(settings.enemyTemplates, Is.Empty);
    }
}
