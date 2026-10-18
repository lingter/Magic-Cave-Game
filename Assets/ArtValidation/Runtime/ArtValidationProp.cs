using UnityEngine;

namespace MagicCave.ArtValidation
{
    public enum ValidationPropKind { Chest, Bag, Merchant, Enemy, Trap, Exit }

    public sealed class ArtValidationProp : MonoBehaviour
    {
        public ValidationPropKind kind;
        public string displayName;
        public bool blocksMovement;
        public Sprite artSprite, whiteboxSprite;
        public Color whiteboxColor = Color.yellow;

        public void ShowWhitebox(bool enabled)
        {
            var visual = GetComponent<SpriteRenderer>();
            visual.sprite = enabled ? whiteboxSprite : artSprite;
            visual.color = enabled ? whiteboxColor : Color.white;
        }
    }
}
