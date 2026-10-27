using UnityEngine;

namespace MagicCave.Prototype
{
    [CreateAssetMenu(menuName = "Magic Cave/Character and Equipment Rules")]
    public sealed class PrototypeAttributeSet : ScriptableObject
    {
        public AttributeRules rules = AttributeRules.Defaults();
        public static AttributeRules LoadRules()
        {
            var asset = Resources.Load<PrototypeAttributeSet>("CharacterRules");
            return asset != null ? asset.rules : AttributeRules.Defaults();
        }
    }
}
