using UnityEngine;
using System.Collections.Generic;

namespace Monlit
{
    [DisallowMultipleComponent]
    public sealed class WorkshopFigure : MonoBehaviour
    {
        private const float MaxEdgeNudge = .30f;
        private const float MaxEdgeTurn = 18f;
        private const float InteriorPositionTolerance = .17f;
        private const float InteriorTurnTolerance = 12f;
        private const float MinContactInCells = .35f;
        private const float MinNeighborGap = .015f;
        private const float CellEpsilon = .0001f;

        [SerializeField] private string caption;
        [SerializeField] private WorkshopPiece[] pieces;
        [SerializeField, HideInInspector] private WorkshopPiece[] allPieces;
        [SerializeField, HideInInspector] private Vector2Int gridSize;
        [SerializeField, HideInInspector] private float cellSize;
        [SerializeField, HideInInspector] private bool[] silhouette;
        [SerializeField, HideInInspector] private int area;

        private bool[] covered;
        private int[] indices;
        private Vector2[] alignedCells;
        private readonly List<Vector2> fixedCells = new List<Vector2>();
        private readonly List<float> xCorrections = new List<float>();
        private readonly List<float> zCorrections = new List<float>();

        public string Caption => caption;
        public int PieceCount => pieces?.Length ?? 0;
        public int FilledCount
        {
            get
            {
                if (area > 0) return Measure(out _);
                int filled = 0;
                for (int i = 0; i < PieceCount; i++)
                    if (pieces[i] && pieces[i].IsDelivered)
                        filled++;
                return filled;
            }
        }

        public bool IsComplete
        {
            get
            {
                if (area == 0) return PieceCount > 0 && FilledCount == PieceCount;
                Measure(out int cells);
                return cells == area;
            }
        }

        public int FilledPercent
        {
            get
            {
                if (area == 0) return PieceCount == 0 ? 0 : 100 * FilledCount / PieceCount;
                Measure(out int cells);
                return 100 * cells / area;
            }
        }

        public void Configure(string displayName, WorkshopPiece[] members)
        {
            caption = displayName;
            pieces = members;
        }

        internal void SetGrid(WorkshopPiece[] castings, List<Vector2Int> cells, RectInt bounds, float size)
        {
            allPieces = castings;
            gridSize = bounds.size;
            cellSize = size;
            silhouette = new bool[bounds.width * bounds.height];
            area = cells.Count;
            foreach (var cell in cells)
                silhouette[cell.x - bounds.xMin + (cell.y - bounds.yMin) * bounds.width] = true;
        }

        // A hammer strike may lower the casting, but the player supplies its rotation
        // and horizontal position. Use the same silhouette test as scoring.
        internal bool TryGetNailPose(WorkshopPiece piece, out Vector3 position)
        {
            position = piece.Body.position;
            Vector3 relative = position - transform.position;
            if (area == 0 || !ContainsCenter(relative)) return false;
            float seatY = pieces[0].Target.position.y;
            float maxHeight = piece.IsHeld ? WorkshopPiece.CarryCaptureHeight : .09f;
            if (Mathf.Abs(position.y - seatY) > maxHeight || MapFootprint(piece, relative, .08f) == 0) return false;

            position.y = seatY;
            return IsClearOfCastings(piece, position, piece.Body.rotation);
        }

        internal bool ContainsRestingFootprint(WorkshopPiece piece)
        {
            Vector3 relative = piece.Body.position - transform.position;
            return area > 0 && Mathf.Abs(relative.y) <= .09f && ContainsCenter(relative) &&
                MapRestingFootprint(piece, relative) != 0;
        }

        internal bool TryGetInteriorPose(WorkshopPiece piece, out Vector3 position, out Quaternion rotation)
        {
            position = piece.Body.position;
            rotation = GridRotation(piece.Body.rotation);
            Vector3 relative = position - transform.position;
            if (area == 0 || !ContainsCenter(relative) ||
                Mathf.Abs(relative.y - WorkshopPuzzleArt.SeatY) > WorkshopPiece.CarryCaptureHeight ||
                MapFootprint(piece, relative, InteriorPositionTolerance, InteriorTurnTolerance) == 0) return false;

            // A manual placement keeps both horizontal coordinates. Squaring its faces lets it guide the next part.
            position.y = transform.position.y + WorkshopPuzzleArt.SeatY;
            return IsClearOfCastings(piece, position, rotation);
        }

        internal bool TryGetEdgePose(WorkshopPiece piece, out Vector3 position, out Quaternion rotation)
        {
            position = piece.Body.position;
            rotation = piece.Body.rotation;
            Vector3 relative = position - transform.position;
            if (area == 0 || !ContainsCenter(relative, MaxEdgeNudge) ||
                Mathf.Abs(relative.y - WorkshopPuzzleArt.SeatY) > WorkshopPiece.CarryCaptureHeight)
                return false;

            var aligned = GridRotation(rotation);
            float angle = Quaternion.Angle(rotation, aligned);
            if (angle > MaxEdgeTurn) return false;
            var offsets = piece.Footprint;
            if (offsets == null || offsets.Length == 0) return false;
            if (alignedCells == null || alignedCells.Length < offsets.Length) alignedCells = new Vector2[offsets.Length];
            Vector2 center = GridPosition(relative);
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 offset = aligned * new Vector3(offsets[i].x, 0f, offsets[i].y);
                alignedCells[i] = center + new Vector2(offset.x, offset.z);
            }

            CollectGuides(piece);
            float bestTravel = float.PositiveInfinity;
            // If two actual edges meet, choose the nearest fitting corner. A single edge only corrects its normal.
            foreach (float x in xCorrections)
                foreach (float z in zCorrections)
                    ConsiderEdgePose(piece, aligned, new Vector2(x, z), true, true, ref bestTravel, ref position);
            if (float.IsPositiveInfinity(bestTravel))
            {
                foreach (float x in xCorrections)
                    ConsiderEdgePose(piece, aligned, new Vector2(x, 0f), true, false, ref bestTravel, ref position);
                foreach (float z in zCorrections)
                    ConsiderEdgePose(piece, aligned, new Vector2(0f, z), false, true, ref bestTravel, ref position);
            }
            if (float.IsPositiveInfinity(bestTravel)) return false;
            rotation = aligned;
            return true;
        }

        private Vector2 GridPosition(Vector3 relative) =>
            new Vector2(relative.x, relative.z) / cellSize + ((Vector2)gridSize - Vector2.one) * .5f;

        private static Quaternion GridRotation(Quaternion rotation) =>
            Quaternion.Euler(0f, 90f * Mathf.Round(rotation.eulerAngles.y / 90f), 0f);

        private void CollectGuides(WorkshopPiece piece)
        {
            fixedCells.Clear();
            xCorrections.Clear();
            zCorrections.Clear();
            AddGuide(Vector2.zero);
            foreach (var other in allPieces)
            {
                if (!other || other == piece || !(other.IsMagnetized || other.IsNailed)) continue;
                Vector3 pos = other.Body.position;
                Vector3 relative = pos - transform.position;
                if (!ContainsCenter(relative) || Mathf.Abs(relative.y - WorkshopPuzzleArt.SeatY) > .09f) continue;
                Vector3 delta = pos - piece.Body.position;
                float reach = piece.DeckClearance + other.DeckClearance + MaxEdgeNudge;
                if (delta.x * delta.x + delta.z * delta.z > reach * reach) continue;
                Quaternion pose = other.Body.rotation;
                var aligned = GridRotation(pose);
                // A hand-nailed, visibly skewed casting has no parallel guide for an axis-aligned part.
                if (Quaternion.Angle(aligned, pose) > .1f) continue;
                var offsets = other.Footprint;
                if (offsets == null || offsets.Length == 0) continue;
                Vector2 center = GridPosition(relative);
                int first = fixedCells.Count;
                foreach (var offset in offsets)
                {
                    Vector3 turned = aligned * new Vector3(offset.x, 0f, offset.y);
                    fixedCells.Add(center + new Vector2(turned.x, turned.z));
                }
                AddGuide(fixedCells[first]);
            }
        }

        private void AddGuide(Vector2 guideCell)
        {
            // All cells in one casting have the same fractional offset. A previous placement can have
            // its own offset along the board edge, so read the guide from its actual resting position.
            Vector2 difference = guideCell - alignedCells[0];
            AddCorrections(xCorrections, difference.x);
            AddCorrections(zCorrections, difference.y);
        }

        private void AddCorrections(List<float> corrections, float difference)
        {
            // Past half a cell the nearest grid line may be empty while the next one is a real edge.
            float reach = MaxEdgeNudge / cellSize + CellEpsilon;
            int last = Mathf.FloorToInt(difference + reach);
            for (int line = Mathf.CeilToInt(difference - reach); line <= last; line++)
            {
                float correction = difference - line;
                bool duplicate = false;
                foreach (float existing in corrections)
                {
                    if (Mathf.Abs(existing - correction) > CellEpsilon) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) corrections.Add(correction);
            }
        }

        private void ConsiderEdgePose(WorkshopPiece piece, Quaternion rotation, Vector2 nudge, bool alignX,
            bool alignZ, ref float bestTravel, ref Vector3 result)
        {
            float travel = nudge.magnitude * cellSize;
            if (travel >= bestTravel || travel > MaxEdgeNudge + cellSize * CellEpsilon) return;
            // A second, slightly offset neighbour may use part of the seam. Keep air between the actual sidewalls.
            float minCellSpacing = 1f - (2f * WorkshopPuzzleArt.CastingInset - MinNeighborGap) / cellSize;
            bool touchesX = false, touchesZ = false;
            for (int i = 0; i < piece.Footprint.Length; i++)
            {
                Vector2 cell = alignedCells[i] + nudge;
                // Between rows a square straddles both, allowing continuous travel along a straight edge.
                int left = Mathf.FloorToInt(cell.x + CellEpsilon), right = Mathf.CeilToInt(cell.x - CellEpsilon);
                int bottom = Mathf.FloorToInt(cell.y + CellEpsilon), top = Mathf.CeilToInt(cell.y - CellEpsilon);
                for (int z = bottom; z <= top; z++)
                {
                    for (int x = left; x <= right; x++)
                    {
                        if (!ContainsCell(x, z)) return;
                        // An absent neighbour leaves this section of both the silhouette and casting exposed.
                        if (alignX && left == right && 1f - Mathf.Abs(cell.y - z) >= MinContactInCells &&
                            (!ContainsCell(x - 1, z) || !ContainsCell(x + 1, z))) touchesX = true;
                        if (alignZ && bottom == top && 1f - Mathf.Abs(cell.x - x) >= MinContactInCells &&
                            (!ContainsCell(x, z - 1) || !ContainsCell(x, z + 1))) touchesZ = true;
                    }
                }
                // The collider inset leaves a seam when nominal cells share a face one cell apart.
                foreach (var fixedCell in fixedCells)
                {
                    float dx = Mathf.Abs(cell.x - fixedCell.x), dz = Mathf.Abs(cell.y - fixedCell.y);
                    if (dx < minCellSpacing - CellEpsilon && dz < minCellSpacing - CellEpsilon) return;
                    if (alignX && Mathf.Abs(dx - 1f) <= CellEpsilon && 1f - dz >= MinContactInCells) touchesX = true;
                    if (alignZ && Mathf.Abs(dz - 1f) <= CellEpsilon && 1f - dx >= MinContactInCells) touchesZ = true;
                }
            }
            if ((alignX && !touchesX) || (alignZ && !touchesZ)) return;
            Vector3 candidate = piece.Body.position + new Vector3(nudge.x * cellSize, 0f, nudge.y * cellSize);
            candidate.y = transform.position.y + WorkshopPuzzleArt.SeatY;
            if (!IsClearOfCastings(piece, candidate, rotation)) return;
            result = candidate;
            bestTravel = travel;
        }

        private bool ContainsCell(int x, int z) => x >= 0 && z >= 0 && x < gridSize.x && z < gridSize.y &&
            silhouette[x + z * gridSize.x];

        internal bool IsClearOfCastings(WorkshopPiece piece, Vector3 position, Quaternion rotation)
        {
            foreach (var other in allPieces)
            {
                if (!other || other == piece) continue;
                Vector3 delta = other.Body.position - position;
                float reach = other.DeckClearance + piece.DeckClearance;
                if (delta.x * delta.x + delta.z * delta.z > reach * reach) continue;
                // Generated box colliders share their rigidbody's transform and
                // preserve concave cutouts. Neighbouring lids can touch at the seam.
                foreach (var a in piece.CollisionShapes)
                    foreach (var b in other.CollisionShapes)
                        if (Physics.ComputePenetration(a, position, rotation,
                            b, other.Body.position, other.Body.rotation, out _, out float depth) && depth > .003f)
                            return false;
            }
            return true;
        }

        private bool ContainsCenter(Vector3 relative, float margin = 0f)
        {
            return Mathf.Abs(relative.x) <= gridSize.x * cellSize * .5f + margin &&
                Mathf.Abs(relative.z) <= gridSize.y * cellSize * .5f + margin;
        }

        // A latched edge placement has already passed the silhouette and collision checks.
        // Sliding along a straight edge can leave its centre halfway between grid lines.
        private int MapRestingFootprint(WorkshopPiece piece, Vector3 relative) =>
            MapFootprint(piece, relative, piece.IsMagnetized ? float.PositiveInfinity : .12f);

        private int MapFootprint(WorkshopPiece piece, Vector3 relative, float tolerance, float turnTolerance = 3f)
        {
            Quaternion pose = piece.Body.rotation;
            var rotation = GridRotation(pose);
            if (Quaternion.Angle(rotation, pose) > turnTolerance) return 0;
            var offsets = piece.Footprint;
            if (offsets == null || offsets.Length == 0) return 0;
            if (indices == null || indices.Length < offsets.Length) indices = new int[offsets.Length];
            Vector3 firstOffset = rotation * new Vector3(offsets[0].x, 0f, offsets[0].y);
            Vector2 firstCell = GridPosition(relative) + new Vector2(firstOffset.x, firstOffset.z);
            // Use the same side of a half-cell line for adjoining pieces despite floating-point drift.
            var origin = Vector2Int.FloorToInt(firstCell + Vector2.one * (.5f + CellEpsilon));
            if ((firstCell - (Vector2)origin).sqrMagnitude * cellSize * cellSize > tolerance * tolerance) return 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2 delta = offsets[i] - offsets[0];
                Vector3 rotated = rotation * new Vector3(delta.x, 0f, delta.y);
                var nearest = origin + new Vector2Int(Mathf.RoundToInt(rotated.x), Mathf.RoundToInt(rotated.z));
                if (!ContainsCell(nearest.x, nearest.y)) return 0;
                int index = nearest.x + nearest.y * gridSize.x;
                indices[i] = index;
            }
            return offsets.Length;
        }

        private int Measure(out int filledCells)
        {
            if (covered == null || covered.Length != silhouette.Length) covered = new bool[silhouette.Length];
            System.Array.Clear(covered, 0, covered.Length);
            filledCells = 0;
            int count = 0;
            Vector3 center = transform.position;
            foreach (var piece in allPieces)
            {
                if (!piece) continue;
                Vector3 relative = piece.Body.position - center;
                if (Mathf.Abs(relative.y) > .09f || !ContainsCenter(relative) || !piece.IsResting) continue;
                int n = MapRestingFootprint(piece, relative);
                if (n == 0) continue;
                int free = 0;
                while (free < n && !covered[indices[free]]) free++;
                if (free != n) continue;
                for (int i = 0; i < n; i++) covered[indices[i]] = true;
                filledCells += n;
                count++;
            }
            return count;
        }
    }
}
