using UnityEngine;

namespace MagicCave.Arknights
{
    /// <summary>0-1 coordinates: row 0 is the south edge, column 0 the west edge.</summary>
    public sealed class ArknightsMapLayout : MonoBehaviour
    {
        public enum TileKind { Forbidden, Road, Highland, Entry, Goal }

        [System.Serializable]
        public struct Tile
        {
            public int row;
            public int column;
            public TileKind kind;
            public Transform surface;
        }

        public float tileSize = 2f;
        public int rows = 6;
        public int columns = 9;
        public Tile[] tiles;
        public Transform[] routeWaypoints;
        [TextArea] public string reference;

        public Vector3 CellToWorld(int row, int column)
        {
            return transform.TransformPoint(new Vector3((column - 4) * tileSize, 0, (row - 2.5f) * tileSize));
        }

        public bool CanDeploy(int row, int column, bool ranged)
        {
            if (tiles == null) return false;
            foreach (var tile in tiles)
                if (tile.row == row && tile.column == column)
                    return tile.kind == (ranged ? TileKind.Highland : TileKind.Road);
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            if (routeWaypoints == null) return;
            Gizmos.color = new Color(1f, 0.3f, 0.15f, 0.9f);
            for (int i = 0; i < routeWaypoints.Length; i++)
            {
                if (!routeWaypoints[i]) continue;
                Gizmos.DrawWireSphere(routeWaypoints[i].position, 0.13f);
                if (i > 0 && routeWaypoints[i - 1])
                    Gizmos.DrawLine(routeWaypoints[i - 1].position, routeWaypoints[i].position);
            }
        }
    }
}
