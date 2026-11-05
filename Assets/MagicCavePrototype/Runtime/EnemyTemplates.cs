using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCave.Prototype
{
    [Serializable]
    public sealed class EnemyTurnDefinition
    {
        public List<CardEffectEntry> effects = new List<CardEffectEntry> { new CardEffectEntry() };
    }
    [Serializable]
    public sealed class EnemyTemplateDefinition
    {
        public int id;
        public string name = "自定义怪物";
        public EnemyKind appearance;
        public int health = 18, attack = 5, heavy = 14, defense, healthGrowth = 4, attackGrowth = 1, weight = 1;
        public List<EnemyTurnDefinition> turns = new List<EnemyTurnDefinition> { new EnemyTurnDefinition() };
    }
    public static class EnemyTemplates
    {
        public static readonly CardValueSource[] Sources = { CardValueSource.ActualAttack, CardValueSource.CurrentBlock, CardValueSource.Defense, CardValueSource.Fixed, CardValueSource.HeavyAttack };
        public static EnemyTemplateDefinition Find(RunData run, EnemyData enemy) => enemy.templateId == 0 ? null :
            PrototypeSettings.For(run)?.enemyTemplates?.FirstOrDefault(t => t.id == enemy.templateId && t.appearance == enemy.kind);
        public static string Label(RunData run, EnemyData enemy) => (Find(run, enemy)?.name ?? Rules.EnemyNames[(int)enemy.kind]) + " Lv." + enemy.Level;
        public static void Validate(List<EnemyTemplateDefinition> templates)
        {
            if (templates == null || templates.Count > 100 || templates.Any(t => t == null || t.id < 1 || t.id > 1000000 ||
                !Enum.IsDefined(typeof(EnemyKind), t.appearance) || t.id <= 3 && (int)t.appearance != t.id - 1 ||
                string.IsNullOrWhiteSpace(t.name) || t.name.Length > 16 || t.name.Any(char.IsControl) ||
                t.health < 1 || t.health > 10000 || t.attack < 0 || t.attack > 1000 || t.heavy < 0 || t.heavy > 1000 ||
                t.defense < 0 || t.defense > 1000 || t.healthGrowth < 0 || t.healthGrowth > 1000 || t.attackGrowth < 0 || t.attackGrowth > 1000 ||
                t.weight < 0 || t.weight > 10000 || t.turns == null || t.turns.Count < 1 || t.turns.Count > 8) ||
                templates.Select(t => t.id).Distinct().Count() != templates.Count)
                throw new InvalidOperationException("怪物模板配置无效：名称1～16字，最多100种；每种1～8个循环行动。");
            foreach (var template in templates)
                foreach (var turn in template.turns)
                {
                    if (turn == null) throw new InvalidOperationException("怪物行动不能为空。");
                    CardEffects.Validate(new CustomCardDefinition { effectsVersion = 1, effects = turn.effects }, true);
                }
            for (int kind = 0; kind < 3; kind++)
            {
                var baseline = templates.FirstOrDefault(t => t.id == kind + 1);
                if (baseline != null && baseline.weight == 0 && !templates.Any(t => (int)t.appearance == kind && t.weight > 0))
                    throw new InvalidOperationException("每类出现位置至少需要一个权重大于0的怪物模板。");
            }
        }
        public static EnemyTemplateDefinition FromExisting(PrototypeSettings settings, EnemyKind kind)
        {
            int n = (int)kind;
            var template = new EnemyTemplateDefinition { id = n + 1, name = Rules.EnemyNames[n], appearance = kind,
                health = settings.enemyHealth[n], attack = settings.enemyAttack[n], heavy = settings.enemyHeavy[n],
                defense = Math.Max(0, settings.enemyDefense[n]), turns = new List<EnemyTurnDefinition>() };
            template.turns.Add(Turn(kind == EnemyKind.Beetle ? CardEffectKind.Block : CardEffectKind.Damage,
                kind == EnemyKind.Beetle ? CardValueSource.Fixed : CardValueSource.ActualAttack,
                kind == EnemyKind.Beetle ? settings.enemyBlock[n] : 100));
            if (kind == EnemyKind.Beetle) template.turns.Add(Turn(CardEffectKind.Damage, CardValueSource.ActualAttack, 100));
            if (kind == EnemyKind.Boss)
            {
                template.turns.Add(Turn(CardEffectKind.Block, CardValueSource.Fixed, settings.enemyBlock[n]));
                template.turns.Add(Turn(CardEffectKind.Damage, CardValueSource.HeavyAttack, 100));
            }
            return template;
        }
        static EnemyTurnDefinition Turn(CardEffectKind kind, CardValueSource source, int amount) => new EnemyTurnDefinition
        {
            effects = new List<CardEffectEntry> { new CardEffectEntry { kind = kind, target = kind == CardEffectKind.Damage ? CardEffectTarget.Enemy : CardEffectTarget.Self,
                terms = new List<CardFormulaTerm> { new CardFormulaTerm { source = source, amount = amount } } } }
        };
        public static void Apply(RunData run, List<EnemyTemplateDefinition> templates)
        {
            if (templates.Count == 0) return;
            var random = new RandomStream(RandomStream.Derive(run.seed, 0x454E454D));
            foreach (var enemy in run.maps.SelectMany(m => m.enemies))
            {
                var pool = templates.Where(t => t.appearance == enemy.kind).OrderBy(t => t.id).ToList();
                var baseline = pool.FirstOrDefault(t => t.id == (int)enemy.kind + 1);
                int baselineWeight = baseline == null ? 1 : 0;
                int total = pool.Sum(t => t.weight) + baselineWeight;
                int roll = random.Range(0, total);
                enemy.templateId = 0;
                if (roll < baselineWeight) continue;
                roll -= baselineWeight;
                foreach (var template in pool)
                {
                    if (roll < template.weight) { enemy.templateId = template.id; enemy.physicalDefense = template.defense; break; }
                    roll -= template.weight;
                }
            }
        }
        public static CardEffectState Preview(Battle battle, List<List<double>> trace = null)
        {
            var template = Find(battle.run, battle.enemy);
            var state = new CardEffectState { playerHealth = battle.enemyHealth, playerBlock = 0,
                enemyHealth = battle.run.health, enemyBlock = battle.playerBlock };
            Resolve(battle.run, battle.enemy, template, battle.enemyTurn, state, trace);
            return state;
        }
        public static void Resolve(RunData run, EnemyData enemy, EnemyTemplateDefinition template, int turnIndex, CardEffectState state, List<List<double>> trace = null)
        {
            var effects = template.turns[turnIndex % template.turns.Count].effects;
            decimal growth = template.attackGrowth * (enemy.Level - 1m);
            CardEffects.Resolve(run, new CustomCardDefinition { effectsVersion = 1, effects = effects },
                run.UsesAttributes ? CharacterAttributes.Calculate(run, true).defense : 0, enemy.physicalDefense, state,
                trace: trace, sourceValues: source => source == CardValueSource.ActualAttack ? template.attack + growth :
                    source == CardValueSource.HeavyAttack ? template.heavy + growth : source == CardValueSource.Defense ? enemy.physicalDefense : 0,
                outgoingMultiplier: 1m);
        }
        public static int IntentAmount(Battle battle, CardEffectKind kind, CardEffectTarget target)
        {
            var trace = new List<List<double>>(); Preview(battle, trace);
            var template = Find(battle.run, battle.enemy);
            var effects = template.turns[battle.enemyTurn % template.turns.Count].effects;
            double value = 0;
            for (int i = 0; i < effects.Count; i++) if (effects[i].kind == kind && effects[i].target == target) value += trace[i].Sum();
            return (int)Math.Min(int.MaxValue, value);
        }
        public static string Intent(Battle battle)
        {
            var template = Find(battle.run, battle.enemy);
            int damage = IntentAmount(battle, CardEffectKind.Damage, CardEffectTarget.Enemy);
            int block = IntentAmount(battle, CardEffectKind.Block, CardEffectTarget.Self);
            int selfDamage = IntentAmount(battle, CardEffectKind.Damage, CardEffectTarget.Self);
            int playerBlock = IntentAmount(battle, CardEffectKind.Block, CardEffectTarget.Enemy);
            int spirit = IntentAmount(battle, CardEffectKind.Spirit, CardEffectTarget.Self);
            var labels = new List<string>();
            if (damage > 0) labels.Add("攻击" + damage);
            if (block > 0) labels.Add("自身格挡" + block);
            if (selfDamage > 0) labels.Add("自伤" + selfDamage);
            if (playerBlock > 0) labels.Add("玩家格挡" + playerBlock);
            if (spirit > 0) labels.Add("本回合战意+" + spirit);
            return "行动" + (battle.enemyTurn % template.turns.Count + 1) + "/" + template.turns.Count + "：" + (labels.Count == 0 ? "等待" : string.Join("／", labels));
        }
    }
}
