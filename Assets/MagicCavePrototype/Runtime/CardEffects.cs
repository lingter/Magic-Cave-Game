using System;
using System.Collections.Generic;
using System.Linq;

namespace MagicCave.Prototype
{
    public enum CardEffectKind { Damage, Block, Draw, Spirit }
    public enum CardEffectTarget { Enemy, Self }
    public enum CardValueSource { ActualAttack, Strength, Agility, Wisdom, CurrentBlock, Defense, Fixed, HeavyAttack }

    [Serializable]
    public sealed class CardFormulaTerm
    {
        public CardValueSource source;
        public int amount = 100;
    }
    [Serializable]
    public sealed class CardEffectEntry
    {
        public CardEffectKind kind;
        public CardEffectTarget target;
        public int repeat = 1;
        public List<CardFormulaTerm> terms = new List<CardFormulaTerm> { new CardFormulaTerm() };
    }
    public sealed class CardEffectState
    {
        public double enemyHealth, enemyBlock, playerHealth, playerBlock;
        public int spirit;
    }
    public static class CardEffects
    {
        public const int MaxEffects = 8, MaxTerms = 4;
        public static readonly string[] KindNames = { "造成物理伤害", "获得格挡", "抽牌", "获得本回合战意" };
        public static readonly string[] SourceNames = { "实际攻击力", "力量", "敏捷", "智慧", "当前格挡", "防御值", "固定数值", "重击攻击力" };
        public static string TargetName(CardEffectTarget target) => target == CardEffectTarget.Enemy ? "指定敌人" : "自身";
        public static void Validate(CustomCardDefinition card, bool monster = false)
        {
            if (card.effectsVersion == 0)
            {
                if (card.effects != null && card.effects.Count != 0) throw new InvalidOperationException("旧模板卡不应包含词条。");
                return;
            }
            if (card.effectsVersion != 1 || card.effects == null || card.effects.Count < 1 || card.effects.Count > MaxEffects ||
                card.effects.Any(e => e == null || !Enum.IsDefined(typeof(CardEffectKind), e.kind) || monster && e.kind == CardEffectKind.Draw ||
                    !Enum.IsDefined(typeof(CardEffectTarget), e.target) || e.repeat < 1 || e.repeat > 10 ||
                    e.kind != CardEffectKind.Damage && e.repeat != 1 ||
                    (e.kind == CardEffectKind.Draw || e.kind == CardEffectKind.Spirit) && e.target != CardEffectTarget.Self ||
                    e.terms == null || e.terms.Count < 1 || e.terms.Count > MaxTerms ||
                    e.terms.Any(t => t == null || !Enum.IsDefined(typeof(CardValueSource), t.source) || (!monster && t.source == CardValueSource.HeavyAttack || monster && !EnemyTemplates.Sources.Contains(t.source)) || t.amount < 0 || t.amount > 1000)))
                throw new InvalidOperationException("词条配置无效：每牌1～8条效果，每条1～4个数值项；倍率／固定值0～1000，伤害1～10段。抽牌与战意仅作用于自身。");
        }
        public static decimal Formula(RunData run, CardEffectEntry effect, double playerBlock, bool inBattle, Func<CardValueSource, decimal> sourceValues = null)
        {
            var stats = sourceValues == null && run.UsesAttributes ? CharacterAttributes.Calculate(run, inBattle) : null;
            decimal total = 0;
            foreach (var term in effect.terms)
            {
                decimal basis;
                if (term.source == CardValueSource.Fixed) { total += term.amount; continue; }
                if (sourceValues != null && term.source != CardValueSource.CurrentBlock)
                { total += sourceValues(term.source) * term.amount / 100m; continue; }
                switch (term.source)
                {
                    case CardValueSource.ActualAttack: basis = stats == null ? 6 + run.attackBonus : stats.attack; break;
                    case CardValueSource.Strength: basis = stats?.strength ?? 0; break;
                    case CardValueSource.Agility: basis = stats?.agility ?? 0; break;
                    case CardValueSource.Wisdom: basis = stats?.wisdom ?? 0; break;
                    case CardValueSource.CurrentBlock: basis = (decimal)playerBlock; break;
                    case CardValueSource.Defense: basis = stats?.defense ?? 0; break;
                    default: total += term.amount; continue;
                }
                total += basis * term.amount / 100m;
            }
            return total;
        }
        public static double Amount(RunData run, CardEffectEntry effect, double playerBlock, int spirit, bool inBattle, Func<CardValueSource, decimal> sourceValues = null, decimal? outgoingMultiplier = null)
        {
            decimal value = Formula(run, effect, playerBlock, inBattle, sourceValues);
            if (effect.kind == CardEffectKind.Damage)
                value = (value + spirit) * (outgoingMultiplier ?? (run.UsesAttributes ? CharacterAttributes.Calculate(run, inBattle).secondaryMultiplier : 1m));
            if (effect.kind == CardEffectKind.Draw) value = Math.Min(20m, value);
            if (effect.kind == CardEffectKind.Spirit) value = Math.Min(100m, value);
            return CombatNumbers.RoundDamage(Math.Max(0m, value));
        }
        // The battle and its preview use exactly the same ordered evaluator. Preview passes a detached state.
        public static void Resolve(RunData run, CustomCardDefinition card, int enemyDefense, int playerDefense, CardEffectState state, Action<int> draw = null, bool inBattle = true, List<List<double>> trace = null, Func<CardValueSource, decimal> sourceValues = null, decimal? outgoingMultiplier = null)
        {
            if (trace != null) foreach (var entry in card.effects) trace.Add(new List<double>());
            for (int index = 0; index < card.effects.Count; index++)
            {
                var effect = card.effects[index];
                for (int hit = 0; hit < effect.repeat; hit++)
                {
                    if (state.enemyHealth <= 0 || state.playerHealth <= 0) return;
                    double amount = Amount(run, effect, state.playerBlock, state.spirit, inBattle, sourceValues, outgoingMultiplier);
                    trace?[index].Add(amount);
                    switch (effect.kind)
                    {
                        case CardEffectKind.Damage:
                            if (effect.target == CardEffectTarget.Enemy)
                                CombatNumbers.Apply(amount, enemyDefense, ref state.enemyBlock, ref state.enemyHealth);
                            else CombatNumbers.Apply(amount, playerDefense, ref state.playerBlock, ref state.playerHealth);
                            break;
                        case CardEffectKind.Block:
                            if (effect.target == CardEffectTarget.Enemy) state.enemyBlock += amount;
                            else state.playerBlock += amount;
                            break;
                        case CardEffectKind.Draw: draw?.Invoke((int)amount); break;
                        case CardEffectKind.Spirit: state.spirit += (int)amount; break;
                    }
                }
            }
        }
        public static string FormulaText(CardEffectEntry effect) => string.Join("＋", effect.terms.Select(t =>
            t.source == CardValueSource.Fixed ? t.amount.ToString() : t.amount + "%" + SourceNames[(int)t.source]));
        public static string Sentence(CardEffectEntry effect)
        {
            string value = "（" + FormulaText(effect) + "）";
            switch (effect.kind)
            {
                case CardEffectKind.Damage: return "对" + TargetName(effect.target) + "造成" + value + "物理伤害" + (effect.repeat > 1 ? "，共" + effect.repeat + "段" : "");
                case CardEffectKind.Block: return "使" + TargetName(effect.target) + "获得" + value + "格挡";
                case CardEffectKind.Draw: return "自身抽" + value + "张牌（每条最多20张）";
                default: return "自身获得" + value + "本回合战意（每条最多100）";
            }
        }
        public static string Description(RunData run, CustomCardDefinition card, bool inBattle, CardEffectState state = null, int enemyDefense = 0)
        {
            state = state ?? new CardEffectState { playerHealth = run.health, enemyHealth = 1000000000 };
            var trace = new List<List<double>>();
            Resolve(run, card, enemyDefense, run.UsesAttributes ? CharacterAttributes.Calculate(run, inBattle).defense : 0,
                state, inBattle: inBattle, trace: trace);
            var lines = new List<string>();
            for (int index = 0; index < card.effects.Count; index++)
            {
                var effect = card.effects[index]; var amounts = trace[index];
                if (amounts.Count == 0) { lines.Add("战斗结束后停止"); break; }
                string number = CharacterAttributes.Number(amounts[0]);
                bool same = amounts.All(v => v == amounts[0]);
                if (!same) number = string.Join("/", amounts.Select(CharacterAttributes.Number));
                switch (effect.kind)
                {
                    case CardEffectKind.Damage: lines.Add((effect.target == CardEffectTarget.Enemy ? "敌" : "自身") + "：" + number + "物伤" + (same && effect.repeat > 1 ? "×" + effect.repeat : "")); break;
                    case CardEffectKind.Block: lines.Add((effect.target == CardEffectTarget.Enemy ? "敌" : "自身") + "：+" + number + "格挡"); break;
                    case CardEffectKind.Draw: lines.Add("抽" + number + "张牌"); break;
                    case CardEffectKind.Spirit: lines.Add("本回合战意+" + number); break;
                }
            }
            return string.Join("\n", lines);
        }
    }
}
