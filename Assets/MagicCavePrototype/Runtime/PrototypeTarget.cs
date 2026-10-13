using UnityEngine;
namespace MagicCave.Prototype
{
    public enum TargetKind { None, Ground, Wall, Door, Enemy, Loot }
    public sealed class PrototypeTarget : MonoBehaviour
    {
        public TargetKind kind;
        public int id, x, y;
    }
}
