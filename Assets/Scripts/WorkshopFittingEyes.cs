using System.Collections.Generic;
using UnityEngine;

namespace Monlit
{
    internal static class WorkshopFittingEyes
    {
        internal static Vector3[] InFigure(WorkshopPuzzleGenerator.Piece piece, Vector3 eyeOffset,
            List<Vector2Int> goalCells, RectInt bounds, Vector3 boardCenter, float cellSize, float seatHeight)
        {
            var silhouette = new HashSet<Vector2Int>(goalCells);
            var positions = new List<Vector3>();
            var seen = new HashSet<Vector3>();
            var pivot = Vector2.zero;
            foreach (var cell in piece.Cells) pivot += (Vector2)cell;
            pivot /= piece.Cells.Length;
            var gridCenter = new Vector2(bounds.xMin + (bounds.width - 1) * .5f,
                bounds.yMin + (bounds.height - 1) * .5f);
            var gridOrigin = boardCenter + new Vector3(-gridCenter.x * cellSize, seatHeight, -gridCenter.y * cellSize);
            var rotatedEye = new Vector3(pivot.x * cellSize, 0f, pivot.y * cellSize) + eyeOffset;
            var rotated = new Vector2Int[piece.Cells.Length];

            for (int turns = 0; turns < 4; turns++)
            {
                for (int i = 0; i < rotated.Length; i++)
                    rotated[i] = WorkshopPuzzleGenerator.Rotate(piece.Cells[i], turns);
                var eye = gridOrigin + rotatedEye;

                // In every legal placement, the first cell must cover one of the goal's cells.
                foreach (var cell in goalCells)
                {
                    var offset = cell - rotated[0];
                    int i = 1;
                    while (i < rotated.Length && silhouette.Contains(offset + rotated[i])) i++;
                    if (i != rotated.Length) continue;
                    var position = eye + new Vector3(offset.x * cellSize, 0f, offset.y * cellSize);
                    if (seen.Add(position)) positions.Add(position);
                }
                rotatedEye = new Vector3(rotatedEye.z, rotatedEye.y, -rotatedEye.x);
            }
            return positions.ToArray();
        }
    }
}
