using System;
using System.Collections.Generic;
using UnityEngine;

namespace Monlit
{
    [DefaultExecutionOrder(-200), RequireComponent(typeof(WorkshopPuzzleArt))]
    public sealed class WorkshopMonolith : MonoBehaviour
    {
        private const float CellSize = 0.46f;
        private const float Walkway = 2f;

        [SerializeField] private WorkshopRobot robot;
        [SerializeField] private WorkshopHud hud;
        [SerializeField] private WorkshopCamera workshopCamera;
        [SerializeField] private BoxCollider floor;
        [SerializeField] private Transform deck;
        [SerializeField] private Material cordMaterial;

        private GameObject roundRoot;
        public int Seed { get; private set; }

        public void BeginRound(WorkshopLevelOptions options, int seed)
        {
            EndRound();
            Seed = seed;
            var puzzle = WorkshopPuzzleGenerator.Generate(seed, options);
            ArrangeBoards(puzzle, out var sourceCenter, out var goalCenters);

            roundRoot = new GameObject("Детали и формы");
            roundRoot.transform.SetParent(transform, false);
            roundRoot.SetActive(false);
            var art = GetComponent<WorkshopPuzzleArt>().Build(puzzle, roundRoot.transform, sourceCenter, goalCenters, CellSize);
            var figures = new WorkshopFigure[goalCenters.Length];
            for (int group = 0; group < figures.Length; group++)
            {
                var members = new List<WorkshopPiece>();
                for (int i = 0; i < puzzle.Pieces.Length; i++)
                {
                    var shape = puzzle.Pieces[i];
                    if (shape.Group != group) continue;
                    var part = art.Pieces[i];
                    string prompt = options.Difficulty == WorkshopDifficulty.Senior ? "Взять деталь" :
                        "Взять деталь · " + WorkshopPuzzleArt.NameFor(group).ToLowerInvariant() + " форма";
                    part.Configure(art.Seats[i], shape.RotationalSymmetry, prompt);
                    members.Add(part);
                }
                var figure = art.Goals[group].gameObject.AddComponent<WorkshopFigure>();
                figure.Configure(WorkshopPuzzleArt.NameFor(group), members.ToArray());
                figure.SetGrid(art.Pieces, art.GoalCells[group], puzzle.GoalBounds[group], CellSize);
                figures[group] = figure;
            }
            for (int i = 0; i < art.Pieces.Length; i++)
            {
                var shape = puzzle.Pieces[i];
                int twin = shape.TwinIndex;
                if (twin >= 0) art.Pieces[i].ConfigureAlternate(art.Seats[twin], art.Pieces[twin]);
                art.Pieces[i].SetPlacementFigures(figures,
                    options.Difficulty == WorkshopDifficulty.Senior && shape.PaintGroup != shape.Group);
            }
            if (options.Difficulty != WorkshopDifficulty.Junior)
                ConnectPieces(puzzle, art, roundRoot.transform, options.Difficulty, ref sourceCenter);
            GetComponent<WorkshopPuzzleArt>().FinishArena(art, floor, deck, puzzle, CellSize);
            hud.Configure(robot, figures, options.Difficulty);
            roundRoot.SetActive(true);
            Physics.SyncTransforms();
            StartBesideMonolith(art.Pieces, sourceCenter);
            robot.Configure(art.Pieces, robot.transform.Find("Hold Point"), floor, figures);
            if (!robot.TryGetComponent<WorkshopPlacementPreview>(out var preview))
                preview = robot.gameObject.AddComponent<WorkshopPlacementPreview>();
            preview.Configure(robot, floor, CellSize);
            workshopCamera.FrameDeck(floor.bounds.size.x, floor.bounds.size.z, robot);
            if (robot.TryGetComponent<WorkshopGrips>(out var grips)) grips.Configure(robot);
        }

        public void EndRound()
        {
            robot.ResetRound();
            hud.Configure(robot, Array.Empty<WorkshopFigure>());
            if (!roundRoot) return;
            roundRoot.SetActive(false);
            Destroy(roundRoot);
            roundRoot = null;
        }

        private void ArrangeBoards(WorkshopPuzzleGenerator.Puzzle puzzle, out Vector3 source, out Vector3[] goals,
            float stationGap = Walkway)
        {
            Vector2 sourceSize = (Vector2)puzzle.SourceBounds.size * CellSize;
            var sizes = new Vector2[puzzle.GoalBounds.Length];
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = (Vector2)puzzle.GoalBounds[i].size * CellSize;
            goals = new Vector3[sizes.Length];
            float floorY = floor.bounds.max.y;
            float sourceEdge = sourceSize.x * .5f;
            float left = sourceEdge;
            float depth = sourceSize.y;
            bool twoBanks = sizes.Length > 4;
            int columns = twoBanks ? (sizes.Length + 7) / 8 : (sizes.Length + 1) / 2;
            float firstStationEdge = sourceEdge + stationGap;
            float right = PlaceBank(sizes, goals, 0, twoBanks ? 2 : 1, columns, 1f, firstStationEdge, stationGap, floorY, ref depth);
            if (twoBanks)
                left = PlaceBank(sizes, goals, 1, 2, columns, -1f, firstStationEdge, stationGap, floorY, ref depth);
            float midpoint = (right - left) * .5f;
            source = new Vector3(-midpoint, floorY, 0f);
            for (int i = 0; i < goals.Length; i++) goals[i].x -= midpoint;
            float deckWidth = Mathf.Max(20f, left + right + 4.4f);
            float deckDepth = Mathf.Max(13.4f, depth + 4.4f);
            floor.transform.localScale = new Vector3(deckWidth, floor.transform.localScale.y, deckDepth);
            deck.localScale = new Vector3(deckWidth / 16f, 1f, deckDepth / 10f);
        }

        // Size each row from its actual silhouettes so long shapes still leave a walkable aisle.
        private static float PlaceBank(Vector2[] sizes, Vector3[] goals, int first, int stride, int columns,
            float side, float firstStationEdge, float gap, float y, ref float depth)
        {
            int count = (sizes.Length - first + stride - 1) / stride;
            var heights = new float[(count + columns - 1) / columns];
            var widths = new float[columns];
            for (int n = 0; n < count; n++)
            {
                Vector2 size = sizes[first + n * stride];
                heights[n / columns] = Mathf.Max(heights[n / columns], size.y);
                widths[n % columns] = Mathf.Max(widths[n % columns], size.x);
            }
            float bankDepth = (heights.Length - 1) * gap;
            foreach (float h in heights) bankDepth += h;
            depth = Mathf.Max(depth, bankDepth);
            float top = bankDepth * .5f;
            float outerEdge = firstStationEdge;
            for (int row = 0; row < heights.Length; row++)
            {
                float edge = firstStationEdge;
                int usedColumns = Mathf.Min(columns, count - row * columns);
                for (int c = usedColumns; c < columns; c++) edge += (widths[c] + gap) * .5f;
                for (int column = 0; column < columns; column++)
                {
                    int n = row * columns + column;
                    if (n == count) break;
                    goals[first + n * stride] = new Vector3(side * (edge + widths[column] * .5f), y, top - heights[row] * .5f);
                    edge += widths[column];
                    outerEdge = Mathf.Max(outerEdge, edge);
                    edge += gap;
                }
                top -= heights[row] + gap;
            }
            return outerEdge;
        }

        private void ConnectPieces(WorkshopPuzzleGenerator.Puzzle puzzle, WorkshopPuzzleArt.Result art, Transform parent,
            WorkshopDifficulty difficulty, ref Vector3 source)
        {
            var anchors = new Transform[art.Pieces.Length];
            for (int i = 0; i < anchors.Length; i++) anchors[i] = art.Pieces[i].transform.Find("Rope Anchor");
            bool senior = difficulty == WorkshopDifficulty.Senior;
            var eyes = GetEyes(puzzle, art, anchors, senior);
            WorkshopCordLayout.Link[] links;
            if (!senior) links = WorkshopCordLayout.Plan(eyes);
            else
            {
                int twin = Array.FindIndex(puzzle.Pieces, p => p.TwinIndex >= 0);
                float gap = Walkway;
                // Beyond either board's diagonal even an in-service tether cannot bridge
                // the gap. Leave enough room to lift and turn a casting during placement.
                float largestBoard = ((Vector2)puzzle.SourceBounds.size).magnitude;
                foreach (var bounds in puzzle.GoalBounds)
                    largestBoard = Mathf.Max(largestBoard, ((Vector2)bounds.size).magnitude);
                float maxGap = largestBoard * CellSize + 4f;
                while (!WorkshopCordLayout.TryPlanSenior(eyes, twin, puzzle.Pieces[twin].TwinIndex, out links))
                {
                    // Separate the boards until each twin can reach its own seat and cannot
                    // complete the colour-matched arrangement. Each service has a spare partner.
                    if (gap >= maxGap) throw new InvalidOperationException("Cannot separate the senior puzzle's twin placements.");
                    gap = Mathf.Min(maxGap, gap + 2f * CellSize);
                    ArrangeBoards(puzzle, out source, out var centers, gap);
                    Vector3 shift = source - art.Source.position;
                    art.Source.position = source;
                    foreach (var part in art.Pieces) part.transform.position += shift;
                    for (int i = 0; i < eyes.Length; i++)
                    {
                        int group = eyes[i].Group;
                        Vector3 delta = centers[group] - art.Goals[group].position;
                        eyes[i].Start += shift;
                        eyes[i].SeatCenter += delta;
                        for (int j = 0; j < eyes[i].Docked.Length; j++) eyes[i].Docked[j] += delta;
                        for (int j = 0; j < eyes[i].Fitting.Length; j++) eyes[i].Fitting[j] += delta;
                    }
                    for (int g = 0; g < centers.Length; g++) art.Goals[g].position = centers[g];
                }
            }
            var supports = new WorkshopRope.SurfaceMotion(art.Pieces, floor);
            foreach (var link in links)
            {
                var cord = new GameObject("Верёвка");
                cord.transform.SetParent(parent, false);
                cord.AddComponent<WorkshopRope>().Configure(anchors[link.A], anchors[link.B], cordMaterial, link.Length, supports);
            }
        }

        private static WorkshopCordLayout.Eye[] GetEyes(WorkshopPuzzleGenerator.Puzzle puzzle,
            WorkshopPuzzleArt.Result art, Transform[] anchors, bool senior)
        {
            var eyes = new WorkshopCordLayout.Eye[anchors.Length];
            for (int i = 0; i < eyes.Length; i++)
            {
                var part = art.Pieces[i].transform;
                var shape = puzzle.Pieces[i];
                Vector3 offset = part.InverseTransformPoint(anchors[i].position);
                // A symmetric piece may seat in several rotations around its centroid.
                var docked = new Vector3[shape.RotationalSymmetry];
                for (int j = 0; j < docked.Length; j++)
                    docked[j] = art.Seats[i].TransformPoint(Quaternion.Euler(0f, 360f * j / docked.Length, 0f) * offset);
                eyes[i] = new WorkshopCordLayout.Eye
                {
                    Group = shape.Group, Start = anchors[i].position,
                    SeatCenter = art.Seats[i].position, Docked = docked,
                    Fitting = senior ? WorkshopFittingEyes.InFigure(shape, offset, art.GoalCells[shape.Group],
                        puzzle.GoalBounds[shape.Group], art.Goals[shape.Group].position, CellSize,
                        art.Seats[i].position.y - art.Goals[shape.Group].position.y) : null
                };
            }
            return eyes;
        }

        private void StartBesideMonolith(WorkshopPiece[] pieces, Vector3 center)
        {
            var extents = new Bounds(pieces[0].transform.position, Vector3.zero);
            foreach (var piece in pieces)
                foreach (var shape in piece.GetComponentsInChildren<Collider>())
                    extents.Encapsulate(shape.bounds);
            var controller = robot.GetComponent<CharacterController>();
            float clearance = controller.radius + controller.skinWidth + .02f;
            float front = extents.min.z - clearance, back = extents.max.z + clearance;
            float left = extents.min.x - clearance, right = extents.max.x + clearance;
            float outOfReachPenalty = floor.bounds.size.x + floor.bounds.size.z;
            Vector3 start = center;
            Vector3 facing = Vector3.forward;
            float best = float.PositiveInfinity;
            void TrySide(Vector3 position, Vector3 direction, float distance, float preference)
            {
                float cost = distance + preference;
                if (distance > robot.InteractionRange) cost += outOfReachPenalty;
                if (cost >= best) return;
                best = cost;
                start = position;
                facing = direction;
            }
            foreach (var piece in pieces)
            {
                Vector3 at = piece.transform.position;
                TrySide(new Vector3(at.x, center.y, front), Vector3.forward, at.z - front, .1f * Mathf.Abs(at.x - center.x));
                // Prefer the usual front start. On a jagged cut another side can have the nearest exposed grip.
                TrySide(new Vector3(left, center.y, at.z), Vector3.right, at.x - left, 5f);
                TrySide(new Vector3(right, center.y, at.z), Vector3.left, right - at.x, 5f);
                TrySide(new Vector3(at.x, center.y, back), Vector3.back, back - at.z, 5f);
            }
            start.y = floor.bounds.max.y + .07f;
            controller.enabled = false;
            robot.transform.SetPositionAndRotation(start, Quaternion.LookRotation(facing));
            controller.enabled = true;
        }
    }
}
