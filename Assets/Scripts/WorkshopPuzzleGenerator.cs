using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

namespace Monlit
{
    public static class WorkshopPuzzleGenerator
    {
        public sealed class Puzzle
        {
            public Piece[] Pieces { get; }
            public RectInt SourceBounds { get; internal set; }
            public RectInt[] GoalBounds { get; }

            internal Puzzle(WorkshopLevelOptions options)
            {
                Pieces = new Piece[options.Blocks];
                GoalBounds = new RectInt[options.Services];
            }
        }

        public sealed class Piece
        {
            public Vector2Int[] Cells { get; internal set; }
            public Vector2Int SourceOrigin { get; internal set; }
            public int SourceTurns { get; internal set; }
            public Vector2Int GoalOrigin { get; internal set; }
            public int GoalTurns { get; internal set; }
            public int Group { get; internal set; }
            public int PaintGroup { get; internal set; }
            public int TwinIndex { get; internal set; } = -1;
            public int RotationalSymmetry { get; internal set; }
        }

        private static readonly Vector2Int[] Neighbours =
            { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        private static readonly List<int> Cuts = BuildCuts();
        private static class WideTiles
        {
            internal static readonly List<int> Cuts = BuildWideCuts();
        }

        // Positions are centers of unit cells in XZ. Positive quarter turns match Unity's Y rotation.
        public static Vector2Int Rotate(Vector2Int p, int turns)
        {
            switch (turns & 3)
            {
                case 1: return new Vector2Int(p.y, -p.x);
                case 2: return -p;
                case 3: return new Vector2Int(-p.y, p.x);
                default: return p;
            }
        }

        public static Puzzle Generate(int seed) => Generate(seed, WorkshopLevelOptions.Default);

        public static Puzzle Generate(int seed, WorkshopLevelOptions options)
        {
            var random = new Random(seed);
            var puzzle = new Puzzle(options);
            var cuts = new List<int>(Cuts);
            Shuffle(cuts, random);
            var used = new HashSet<int>();
            var source = new HashSet<Vector2Int>();
            var masks = new int[puzzle.Pieces.Length];
            var groups = new List<int>(options.Blocks);
            for (int i = 0; i < options.Blocks; i++) groups.Add(i % options.Services);
            Shuffle(groups, random);
            int pairs = options.Blocks / 2;
            // The square catalogue has 47 independent pairs. Use a second family
            // for large mosaics and keep square profiles available for an odd last part.
            int squarePairs = Math.Min(pairs, 40);
            List<int> wideCuts = null;
            var wideUsed = new HashSet<int>();
            if (pairs > squarePairs)
            {
                wideCuts = new List<int>(WideTiles.Cuts);
                Shuffle(wideCuts, random);
            }
            bool senior = options.Difficulty == WorkshopDifficulty.Senior;
            int firstTile = 0;
            if (senior)
            {
                // Each half of this tile becomes the other after half a turn.
                int twinCut = cuts.FindIndex(mask => RotateMask(RotateMask(mask)) == (0xffff ^ mask));
                if (twinCut < 0) throw new InvalidOperationException("No congruent two-piece cut.");
                Swap(cuts, 0, twinCut);
                if (groups[0] == groups[1])
                {
                    int otherGroup = groups.FindIndex(g => g != groups[0]);
                    Swap(groups, 1, otherGroup);
                }
                firstTile = random.Next(pairs);
            }
            int columns = Mathf.CeilToInt(Mathf.Sqrt(pairs));
            var shifts = new int[(pairs + columns - 1) / columns];
            for (int r = 0; r < shifts.Length; r++) shifts[r] = random.Next(4);
            var tileOrigins = new Vector2Int[pairs];
            int nextX = 0;
            for (int slot = 0; slot < pairs; slot++)
            {
                int pair = (slot - firstTile + pairs) % pairs;
                if (slot % columns == 0) nextX = shifts[slot / columns];
                tileOrigins[pair] = new Vector2Int(nextX, 4 * (slot / columns));
                nextX += pair < squarePairs ? 4 : 5;
            }
            int placed = 0, squareIndex = 0, wideIndex = 0;
            while (placed < 2 * pairs)
            {
                bool wide = placed >= squarePairs * 2;
                int side = wide ? 5 : 4;
                var pool = wide ? wideCuts : cuts;
                int indexInPool = wide ? wideIndex++ : squareIndex++;
                if (indexInPool >= pool.Count) throw new InvalidOperationException("Insufficient distinct cuts for the monolith.");
                int cut = pool[indexInPool];
                int other = ((1 << (4 * side)) - 1) ^ cut;
                int a = Signature(cut, side), b = Signature(other, side);
                bool twins = senior && placed == 0;
                var signatures = wide ? wideUsed : used;
                if ((!twins && a == b) || signatures.Contains(a) || signatures.Contains(b)) continue;
                signatures.Add(a);
                signatures.Add(b);
                var tile = tileOrigins[placed / 2];
                for (int half = 0; half < 2; half++)
                {
                    int index = placed++;
                    int mask = half == 0 ? cut : other;
                    masks[index] = mask;
                    var piece = new Piece { Cells = Cells(mask, side), SourceOrigin = tile, Group = groups[index] };
                    if (twins) piece.TwinIndex = 1 - index;
                    puzzle.Pieces[index] = piece;
                    foreach (var cell in piece.Cells) source.Add(tile + cell);
                }
            }
            if (placed < options.Blocks)
            {
                // An odd final part sits on the exposed top edge. A cut skipped
                // earlier because of its partner may still have an unused first half.
                var top = new Vector2Int(shifts[shifts.Length - 1], shifts.Length * 4);
                for (int i = 0; i < cuts.Count; i++)
                {
                    int cut = cuts[(squareIndex + i) % cuts.Count];
                    int signature = Signature(cut);
                    if (used.Contains(signature)) continue;
                    var cells = Cells(cut);
                    var origin = top - Vector2Int.up * Bounds(cells).yMin;
                    foreach (var cell in cells) source.Add(origin + cell);
                    if (!SimpleOutline(source, Bounds(source)))
                    {
                        foreach (var cell in cells) source.Remove(origin + cell);
                        continue;
                    }
                    used.Add(signature);
                    masks[placed] = cut;
                    puzzle.Pieces[placed] = new Piece { Cells = cells, SourceOrigin = origin, Group = groups[placed] };
                    break;
                }
                if (puzzle.Pieces[placed] == null) throw new InvalidOperationException("No distinct exterior piece for the monolith.");
            }
            // Carve the outside of the mosaic without opening any of its internal seams.
            for (int i = 0; i < masks.Length; i++)
            {
                var piece = puzzle.Pieces[i];
                int mask = masks[i];
                if (piece.TwinIndex >= 0 || (i >= squarePairs * 2 && i < pairs * 2))
                {
                    // Keep the congruent twins and the extra-wide profiles intact.
                    continue;
                }
                var localCells = new List<Vector2Int>(piece.Cells);
                Shuffle(localCells, random);
                int budget = random.Next(1, 4);
                foreach (var cell in localCells)
                {
                    var world = piece.SourceOrigin + cell;
                    bool exposed = false;
                    foreach (var direction in Neighbours)
                        if (!source.Contains(world + direction)) exposed = true;
                    if (!exposed) continue;
                    int trimmed = mask & ~(1 << (4 * cell.y + cell.x));
                    if (!ValidPart(trimmed) || used.Contains(Signature(trimmed))) continue;
                    source.Remove(world);
                    if (!SimpleOutline(source, Bounds(source)))
                    {
                        source.Add(world);
                        continue;
                    }
                    used.Remove(Signature(mask));
                    used.Add(Signature(trimmed));
                    mask = trimmed;
                    if (--budget == 0) break;
                }
                piece.Cells = Cells(mask);
                masks[i] = mask;
            }

            if (senior)
            {
                // Use the exact same local coordinates, including the eyelet and grips.
                // In the source tile, (x,y) maps to (3-x,3-y) for the second half.
                var second = puzzle.Pieces[1];
                second.Cells = puzzle.Pieces[0].Cells;
                second.SourceOrigin += new Vector2Int(3, 3);
                second.SourceTurns = 2;
            }

            int turn = random.Next(4);
            source.Clear();
            for (int i = 0; i < puzzle.Pieces.Length; i++)
            {
                var piece = puzzle.Pieces[i];
                int mask = masks[i];
                int side = i >= squarePairs * 2 && i < pairs * 2 ? 5 : 4;
                int once = RotateMask(mask, side);
                piece.RotationalSymmetry = NormaliseMask(once, side) == NormaliseMask(mask, side) ? 4 :
                    NormaliseMask(RotateMask(once, side), side) == NormaliseMask(mask, side) ? 2 : 1;
                piece.PaintGroup = piece.TwinIndex < 0 ? piece.Group : puzzle.Pieces[piece.TwinIndex].Group;
                piece.SourceTurns = (piece.SourceTurns + turn) & 3;
                piece.SourceOrigin = Rotate(piece.SourceOrigin, turn);
                foreach (var cell in piece.Cells) source.Add(piece.SourceOrigin + Rotate(cell, piece.SourceTurns));
            }
            puzzle.SourceBounds = Bounds(source);
            var previousGoals = new List<HashSet<Vector2Int>>();
            for (int group = 0; group < puzzle.GoalBounds.Length; group++)
            {
                var members = new List<Piece>();
                foreach (var piece in puzzle.Pieces) if (piece.Group == group) members.Add(piece);
                Shuffle(members, random);
                puzzle.GoalBounds[group] = Assemble(members, random, previousGoals);
            }
            return puzzle;
        }

        private static RectInt Assemble(List<Piece> parts, Random random, List<HashSet<Vector2Int>> previousGoals)
        {
            var occupied = new HashSet<Vector2Int>();
            var first = parts[0];
            first.GoalTurns = random.Next(4);
            foreach (var cell in first.Cells) occupied.Add(Rotate(cell, first.GoalTurns));
            RectInt bounds = Bounds(occupied);
            float aspect = 0.8f + (float)random.NextDouble() * 0.5f;
            for (int n = 1; n < parts.Count; n++)
            {
                Piece piece = parts[n];
                float best = float.PositiveInfinity;
                int startTurn = random.Next(4);
                for (int i = 0; i < 4; i++)
                {
                    int turns = (startTurn + i) & 3;
                    var cells = Array.ConvertAll(piece.Cells, p => Rotate(p, turns));
                    var extents = Bounds(cells);
                    for (int x = bounds.xMin - extents.xMax; x <= bounds.xMax - extents.xMin; x++)
                    for (int y = bounds.yMin - extents.yMax; y <= bounds.yMax - extents.yMin; y++)
                    {
                        var offset = new Vector2Int(x, y);
                        int contacts = SeamLength(cells, offset, occupied);
                        if (contacts <= 0) continue;
                        var addedBox = new RectInt(extents.position + offset, extents.size);
                        RectInt box = Union(bounds, addedBox);
                        float longSide = Mathf.Max(box.width / aspect, box.height);
                        float score = box.width * box.height + 0.65f * longSide * longSide
                            - 1.2f * contacts + (float)random.NextDouble() * 1.8f;
                        if (score >= best) continue;
                        // On a large service only vertices touching the new part can
                        // change the winding or introduce a pinched corner.
                        bool localCheck = 2 * addedBox.width * addedBox.height < box.width * box.height;
                        int expected = localCheck ? CornerTurns(occupied, addedBox) : 4;
                        foreach (var p in cells) occupied.Add(p + offset);
                        bool simple = CornerTurns(occupied, localCheck ? addedBox : box) == expected;
                        if (simple && n == parts.Count - 1)
                            simple = !MatchesEarlierGoal(occupied, box.min, previousGoals);
                        foreach (var p in cells) occupied.Remove(p + offset);
                        if (!simple) continue;
                        best = score;
                        piece.GoalTurns = turns;
                        piece.GoalOrigin = offset;
                    }
                }
                if (float.IsPositiveInfinity(best)) throw new InvalidOperationException("No exterior edge to join.");
                foreach (var cell in piece.Cells) occupied.Add(piece.GoalOrigin + Rotate(cell, piece.GoalTurns));
                bounds = Bounds(occupied);
            }
            for (int turns = 0; turns < 4; turns++)
            {
                var rotated = new List<Vector2Int>(occupied.Count);
                foreach (var p in occupied) rotated.Add(Rotate(p, turns));
                Vector2Int min = Bounds(rotated).min;
                var normalized = new HashSet<Vector2Int>();
                foreach (var p in rotated) normalized.Add(p - min);
                previousGoals.Add(normalized);
            }
            return bounds;
        }

        private static bool MatchesEarlierGoal(HashSet<Vector2Int> cells, Vector2Int min, List<HashSet<Vector2Int>> earlier)
        {
            foreach (var outline in earlier)
            {
                if (outline.Count != cells.Count) continue;
                bool same = true;
                foreach (var p in cells)
                    if (!outline.Contains(p - min)) { same = false; break; }
                if (same) return true;
            }
            return false;
        }

        private static int SeamLength(Vector2Int[] cells, Vector2Int offset, HashSet<Vector2Int> occupied)
        {
            int contacts = 0;
            foreach (var cell in cells)
            {
                Vector2Int position = cell + offset;
                if (occupied.Contains(position)) return -1;
                foreach (var step in Neighbours) if (occupied.Contains(position + step)) contacts++;
            }
            return contacts;
        }

        private static RectInt Bounds(IEnumerable<Vector2Int> cells)
        {
            var min = new Vector2Int(int.MaxValue, int.MaxValue);
            var max = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                min = Vector2Int.Min(min, cell);
                max = Vector2Int.Max(max, cell);
            }
            return new RectInt(min, max - min + Vector2Int.one);
        }

        private static RectInt Union(RectInt a, RectInt b)
        {
            var min = Vector2Int.Min(a.min, b.min);
            return new RectInt(min, Vector2Int.Max(a.max, b.max) - min);
        }

        // A connected orthogonal polygon has four more outside turns than inside turns.
        // Checkerboard contacts would pinch the outline to a point, so reject them too.
        private static bool SimpleOutline(HashSet<Vector2Int> cells, RectInt box) =>
            CornerTurns(cells, box) == 4;

        private static int CornerTurns(HashSet<Vector2Int> cells, RectInt box)
        {
            int winding = 0;
            for (int x = box.xMin; x <= box.xMax; x++)
            for (int y = box.yMin; y <= box.yMax; y++)
            {
                bool a = cells.Contains(new Vector2Int(x,y)), b = cells.Contains(new Vector2Int(x-1,y));
                bool c = cells.Contains(new Vector2Int(x-1,y-1)), d = cells.Contains(new Vector2Int(x,y-1));
                int count = (a?1:0) + (b?1:0) + (c?1:0) + (d?1:0);
                if (count == 2 && a == c) return int.MinValue;
                if (count == 1) winding++;
                if (count == 3) winding--;
            }
            return winding;
        }

        // Enumerate true two-piece dissections of a 4x4 tile once. Each half is connected,
        // contains a re-entrant corner, and has enough material for a robot to handle.
        private static List<int> BuildCuts()
        {
            var result = new List<int>();
            for (int mask = 1; mask < 0xffff; mask++)
                if (ValidPart(mask) && ValidPart(mask ^ 0xffff)) result.Add(mask);
            return result;
        }

        // Extend a valid square cut by one column. Requiring both partners to span
        // five columns keeps all 29 congruence classes of pairs disjoint from square cuts.
        private static List<int> BuildWideCuts()
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            const int left = 0x8421, right = left << 4; // x=0 and x=4 in four 5-bit rows
            foreach (int square in Cuts)
            for (int shift = 0; shift <= 1; shift++)
            {
                int core = 0;
                for (int y = 0; y < 4; y++)
                    core |= ((square >> (4 * y)) & 15) << (5 * y + shift);
                int edgeX = shift == 0 ? 4 : 0;
                for (int edge = 0; edge < 16; edge++)
                {
                    int mask = core;
                    for (int y = 0; y < 4; y++)
                        mask |= ((edge >> y) & 1) << (5 * y + edgeX);
                    int other = 0xfffff ^ mask;
                    if ((mask & left) == 0 || (mask & right) == 0 ||
                        (other & left) == 0 || (other & right) == 0 || !seen.Add(mask)) continue;
                    if (ValidPart(mask, 5) && ValidPart(other, 5)) result.Add(mask);
                }
            }
            return result;
        }

        private static bool ValidPart(int mask, int side = 4)
        {
            int size = 0;
            for (int bits = mask; bits != 0; bits &= bits - 1) size++;
            if (size < 6 || size > 10) return false;
            int reach = mask & -mask;
            int left = side == 4 ? 0x1111 : 0x108421;
            int right = left << (side - 1);
            while (true)
            {
                int next = (reach | (reach & ~right) << 1 | (reach & ~left) >> 1 | reach << side | reach >> side) & mask;
                if (next == reach) break;
                reach = next;
            }
            if (reach != mask) return false;
            int outer = 0, inner = 0;
            for (int x = 0; x <= side; x++)
            for (int y = 0; y <= side; y++)
            {
                int a = Bit(mask,x,y,side), b = Bit(mask,x-1,y,side), c = Bit(mask,x-1,y-1,side), d = Bit(mask,x,y-1,side);
                int count = a+b+c+d;
                if (count == 2 && a == c) return false;
                if (count == 1) outer++;
                if (count == 3) inner++;
            }
            return outer - inner == 4 && inner >= 2;
        }

        private static int Bit(int mask,int x,int y,int side) =>
            (uint)x < side && (uint)y < side ? (mask >> (side*y+x)) & 1 : 0;

        private static Vector2Int[] Cells(int mask, int side = 4)
        {
            var result = new List<Vector2Int>(10);
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++) if (Bit(mask,x,y,side) != 0) result.Add(new Vector2Int(x,y));
            return result.ToArray();
        }

        private static int RotateMask(int mask, int side = 4)
        {
            int result = 0;
            for (int x = 0; x < side; x++)
            for (int y = 0; y < side; y++) result |= Bit(mask,x,y,side) << (side*(side-1-x)+y);
            return result;
        }

        private static int NormaliseMask(int mask, int side = 4)
        {
            int bottom = (1 << side) - 1;
            int left = side == 4 ? 0x1111 : 0x108421;
            while ((mask & bottom) == 0) mask >>= side;
            while ((mask & left) == 0) mask >>= 1;
            return mask;
        }

        private static int Signature(int mask, int side = 4)
        {
            int least = NormaliseMask(mask, side);
            for (int i = 1; i < 4; i++)
            {
                mask = RotateMask(mask, side);
                least = Math.Min(least, NormaliseMask(mask, side));
            }
            return least;
        }

        private static void Shuffle<T>(List<T> values, Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
                Swap(values, i, random.Next(i + 1));
        }

        private static void Swap<T>(List<T> values, int a, int b)
        {
            T previous = values[a];
            values[a] = values[b];
            values[b] = previous;
        }
    }
}
