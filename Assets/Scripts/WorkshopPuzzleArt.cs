using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monlit
{
    public sealed class WorkshopPuzzleArt : MonoBehaviour
    {
        [SerializeField] private Material finishTemplate;
        [SerializeField] private PhysicsMaterial skid;

        public sealed class Result
        {
            public WorkshopPiece[] Pieces;
            public Transform[] Seats;
            public Transform Source;
            public Transform[] Goals;
            public List<Vector2Int>[] GoalCells;
        }

        private const float BodyHeight = .37f;
        internal const float SeatY = .010f;
        internal const float CastingInset = .038f;
        private static readonly Vector2Int[] Neighbours =
            { Vector2Int.left, Vector2Int.up, Vector2Int.right, Vector2Int.down };
        private static readonly Vector2Int[] Corners =
            { new Vector2Int(-1,-1), new Vector2Int(-1,1), new Vector2Int(1,1), new Vector2Int(1,-1) };
        private static readonly Color32[] GroupColors =
        {
            new Color32(235,128,91,255), new Color32(85,178,221,255), new Color32(166,207,111,255),
            new Color32(185,145,220,255), new Color32(246,205,76,255), new Color32(48,179,152,255),
            new Color32(229,112,168,255), new Color32(81,109,202,255), new Color32(243,226,192,255),
            new Color32(168,95,66,255), new Color32(79,151,81,255), new Color32(133,79,154,255),
            new Color32(246,159,58,255), new Color32(172,225,221,255), new Color32(209,70,76,255),
            new Color32(153,164,190,255)
        };
        private static readonly string[] GroupNames =
        {
            "Коралловая", "Голубая", "Фисташковая", "Сиреневая", "Золотая", "Бирюзовая",
            "Розовая", "Кобальтовая", "Кремовая", "Медная", "Травяная", "Сливовая",
            "Янтарная", "Ледяная", "Карминовая", "Стальная"
        };

        public static Color32 ColorFor(int group) => GroupColors[group];
        public static string NameFor(int group) => GroupNames[group];

        public void FinishArena(Result art, BoxCollider floor, Transform deck,
            WorkshopPuzzleGenerator.Puzzle puzzle, float cellSize)
        {
            // The authored deck also carries the playable perimeter colliders.
            foreach (var trim in deck.GetComponentsInChildren<MeshRenderer>(true)) trim.enabled = false;
            var floorRenderer = floor.GetComponent<MeshRenderer>();
            if (floorRenderer != null) floorRenderer.enabled = false;
            var owner = art.Source.parent.GetComponent<WorkshopGeneratedSurfaces>();
            // Read the newly resized, axis-aligned tabletop before the physics scene syncs the round.
            var bounds = new Bounds(floor.transform.TransformPoint(floor.center),
                Vector3.Scale(floor.size, floor.transform.lossyScale));
            WorkshopArenaArt.Build(owner, finishTemplate, bounds, art, puzzle, cellSize);
        }

        private sealed class Finishes
        {
            public readonly Material Dark, Metal, Ivory;
            public readonly Material[] Paint, Border;
            public readonly WorkshopGeneratedSurfaces Owner;
            public readonly Material Hardware;

            public Finishes(Material template, WorkshopGeneratedSurfaces owner, int groups)
            {
                Owner = owner;
                Material Coat(string label, Color c, float smoothness)
                {
                    var mat = Owner.Keep(new Material(template) { name = label });
                    mat.SetColor("_BaseColor", c);
                    mat.SetFloat("_Smoothness", smoothness);
                    return mat;
                }
                var steel = new Color32(69,87,96,255);
                Dark = Coat("Casting gasket", new Color32(39,53,62,255), .26f);
                Metal = Coat("Machined alloy", steel, .42f);
                Ivory = Coat("Workshop ivory", new Color32(244,230,183,255), .30f);
                Hardware = WorkshopArenaArt.PaletteMaterial(owner, template, "Machined fittings",
                    new Color32[] { new Color32(39,53,62,255), steel, new Color32(244,230,183,255) });
                Paint = new Material[groups];
                Border = new Material[groups];
                for (int group = 0; group < groups; group++)
                {
                    Color color = GroupColors[group];
                    Paint[group] = Coat("Assembly enamel " + (group + 1), color, .33f);
                    Border[group] = Coat("Assembly border " + (group + 1), Color.Lerp(Color.black,color,.66f), .24f);
                }
            }
        }

        // Batch a complete casting or workplate into one mesh, with a submesh for each finish.
        private sealed class Surface
        {
            private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            private readonly Dictionary<Material,List<int>> faces = new Dictionary<Material,List<int>>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly Finishes hardware;

            public Surface(Finishes hardware = null) { this.hardware = hardware; }

            public void Quad(Material finish, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                var normal = Vector3.Cross(b-a,c-a).normalized;
                if (hardware != null)
                {
                    int swatch = finish == hardware.Dark ? 0 : finish == hardware.Metal ? 1 : finish == hardware.Ivory ? 2 : -1;
                    if (swatch >= 0) finish = hardware.Hardware;
                    var sample = new Vector2(swatch >= 0 ? (swatch + .5f) / 3f : .5f, .5f);
                    for (int k = 0; k < 4; k++) uv.Add(sample);
                }
                if (!faces.TryGetValue(finish, out var tris))
                    faces.Add(finish, tris = new List<int>());
                int n = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                for (int k=0;k<4;k++) normals.Add(normal);
                tris.Add(n); tris.Add(n+1); tris.Add(n+2);
                tris.Add(n); tris.Add(n+2); tris.Add(n+3);
            }

            public void Attach(Transform parent, string name, Finishes kit, bool shadows)
            {
                var mesh = kit.Owner.Keep(new Mesh { name = name, subMeshCount = faces.Count });
                if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(vertices); mesh.SetNormals(normals);
                if (uv.Count > 0) mesh.SetUVs(0, uv);
                var materials = new List<Material>();
                foreach (var part in faces)
                {
                    mesh.SetTriangles(part.Value, materials.Count, false);
                    materials.Add(part.Key);
                }
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                var node = Child(parent,name);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials.ToArray();
                renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        // All tile corners are displaced from the true outline. Adjacent cell faces share their
        // corner positions, including the inside elbow of a notch, so a piece has one continuous lid.
        private sealed class Grid
        {
            public readonly HashSet<Vector2Int> Cells;
            public readonly Vector2 Pivot;
            private readonly float size;

            public Grid(IEnumerable<Vector2Int> cells, Vector2 pivot, float size)
            {
                Cells = new HashSet<Vector2Int>(cells);
                Pivot = pivot;
                this.size = size;
            }

            public Vector3 Center(Vector2 cell, float y) => XZ((cell - Pivot) * size, y);

            private Vector3 Corner(Vector2Int cell, int index, float inset, float y)
            {
                var k = Corners[index];
                var side = new Vector2Int(k.x,0);
                var end = new Vector2Int(0,k.y);
                bool a = Cells.Contains(cell+side), b = Cells.Contains(cell+end), d = Cells.Contains(cell+k);
                int nx = Math.Sign(-1 + (a?1:0) - (b?1:0) + (d?1:0));
                int nz = Math.Sign(-1 - (a?1:0) + (b?1:0) + (d?1:0));
                if (!a && !b) { nx = -1; nz = -1; } // distinct corners where only two diagonals touch
                var pos = Center((Vector2)cell + (Vector2)k * .5f, y);
                return pos + new Vector3(k.x * nx * inset, 0, k.y * nz * inset);
            }

            public void Fill(Surface mesh, Material finish, float y, float inset)
            {
                foreach (var cell in Cells)
                    mesh.Quad(finish, Corner(cell,0,inset,y), Corner(cell,1,inset,y),
                        Corner(cell,2,inset,y), Corner(cell,3,inset,y));
            }

            public void Band(Surface mesh, Material finish, float yFrom, float insetFrom, float yTo, float insetTo)
            {
                foreach (var cell in Cells)
                    for (int k=0;k<4;k++)
                    {
                        if (Cells.Contains(cell+Neighbours[k])) continue;
                        int next = (k+1)%4;
                        mesh.Quad(finish,Corner(cell,k,insetFrom,yFrom),Corner(cell,next,insetFrom,yFrom),
                            Corner(cell,next,insetTo,yTo),Corner(cell,k,insetTo,yTo));
                    }
            }
        }

        public Result Build(WorkshopPuzzleGenerator.Puzzle puzzle, Transform generatedRoot, Vector3 sourceCenter,
            Vector3[] goalCenters, float cellSize)
        {
            var owner = generatedRoot.gameObject.AddComponent<WorkshopGeneratedSurfaces>();
            var kit = new Finishes(finishTemplate,owner,goalCenters.Length);
            var result = new Result
            {
                Pieces = new WorkshopPiece[puzzle.Pieces.Length], Seats = new Transform[puzzle.Pieces.Length],
                Source = Child(generatedRoot,"Monolith bed"), Goals = new Transform[goalCenters.Length]
            };
            result.Source.position = sourceCenter;
            var sourceCells = new List<Vector2Int>();
            var goalCells = new List<Vector2Int>[goalCenters.Length];
            result.GoalCells = goalCells;
            for (int g=0;g<goalCenters.Length;g++)
            {
                result.Goals[g] = Child(generatedRoot,"Assembly "+(g+1));
                result.Goals[g].position = goalCenters[g];
                goalCells[g] = new List<Vector2Int>();
            }
            for (int i=0;i<puzzle.Pieces.Length;i++)
            {
                var piece = puzzle.Pieces[i];
                var pivot = Vector2.zero;
                foreach (var cell in piece.Cells) pivot += (Vector2)cell;
                pivot /= piece.Cells.Length;
                var shape = new Grid(piece.Cells,pivot,cellSize);
                var root = Child(generatedRoot,"Casting "+(i+1));
                SetGridPose(root, sourceCenter, puzzle.SourceBounds, piece.SourceOrigin, piece.SourceTurns, pivot, cellSize);
                var seat = Child(result.Goals[piece.Group],"Seat "+(i+1));
                SetGridPose(seat,goalCenters[piece.Group],puzzle.GoalBounds[piece.Group],piece.GoalOrigin,piece.GoalTurns,pivot,cellSize);
                DrawCasting(root,shape,cellSize,piece.PaintGroup,kit);
                AddCollision(root,shape,cellSize);
                result.Seats[i] = seat;
                result.Pieces[i] = root.gameObject.AddComponent<WorkshopPiece>();
                result.Pieces[i].SetFootprint(piece.Cells, pivot);
                sourceCells.AddRange(piece.Cells.Select(c=>piece.SourceOrigin+WorkshopPuzzleGenerator.Rotate(c,piece.SourceTurns)));
                goalCells[piece.Group].AddRange(piece.Cells.Select(c=>piece.GoalOrigin+WorkshopPuzzleGenerator.Rotate(c,piece.GoalTurns)));
            }
            var source = new Grid(sourceCells,BoundsCenter(puzzle.SourceBounds),cellSize);
            var bed = new Surface();
            source.Fill(bed,kit.Metal,.006f,-.065f);
            source.Band(bed,kit.Ivory,.012f,-.06f,.012f,-.036f);
            bed.Attach(result.Source,"Monolith outline",kit,false);
            for (int group=0;group<goalCells.Length;group++)
            {
                var pivot = BoundsCenter(puzzle.GoalBounds[group]);
                var union = new Grid(goalCells[group],pivot,cellSize);
                var surface = new Surface();
                union.Fill(surface,kit.Border[group],.005f,-.09f);
                union.Fill(surface,kit.Paint[group],.010f,0f);
                union.Band(surface,kit.Paint[group],.018f,-.070f,.018f,-.036f);
                surface.Attach(result.Goals[group],"Shared cutout",kit,false);
            }
            return result;
        }

        private static Vector2 BoundsCenter(RectInt b) =>
            new Vector2(b.xMin+(b.width-1)*.5f,b.yMin+(b.height-1)*.5f);

        private static Vector3 XZ(Vector2 p,float y) => new Vector3(p.x,y,p.y);

        private static Transform Child(Transform parent,string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent,false);
            return node;
        }

        private static void SetGridPose(Transform node, Vector3 center, RectInt bounds,
            Vector2Int origin, int turns, Vector2 pivot, float size)
        {
            var rotation = Quaternion.Euler(0,90f*turns,0);
            node.SetPositionAndRotation(center + XZ(((Vector2)origin-BoundsCenter(bounds))*size,SeatY)
                + rotation*XZ(pivot*size,0),rotation);
        }

        private void AddCollision(Transform root, Grid grid, float size)
        {
            bool ContainsLine(Vector2Int start, Vector2Int step, int length)
            {
                for (int i = 0; i < length; i++)
                    if (!grid.Cells.Contains(start + step * i)) return false;
                return true;
            }

            // Inset maximal rectangles of filled cells. Their overlap keeps internal seams solid
            // while the outside faces follow the same inset as the casting, including its notches.
            foreach (var start in grid.Cells.OrderBy(c=>c.y).ThenBy(c=>c.x))
            {
                for (int width = 1; grid.Cells.Contains(start + Vector2Int.right * (width - 1)); width++)
                {
                    if (ContainsLine(start + Vector2Int.down, Vector2Int.right, width)) continue;
                    int depth = 1;
                    while (ContainsLine(start + Vector2Int.up * depth, Vector2Int.right, width)) depth++;
                    if (ContainsLine(start + Vector2Int.left, Vector2Int.up, depth) ||
                        ContainsLine(start + Vector2Int.right * width, Vector2Int.up, depth)) continue;

                    var box = root.gameObject.AddComponent<BoxCollider>();
                    box.center = grid.Center((Vector2)start + new Vector2((width - 1) * .5f, (depth - 1) * .5f), BodyHeight * .5f);
                    box.size = new Vector3(width * size - 2f * CastingInset, BodyHeight, depth * size - 2f * CastingInset);
                    box.contactOffset = .004f;
                    box.sharedMaterial = skid;
                }
            }
        }

        private static void DrawCasting(Transform root, Grid grid, float size,int group,Finishes kit)
        {
            var mesh = new Surface(kit);
            var color = kit.Paint[group];
            const float lidInset = CastingInset + .021f;
            grid.Band(mesh,kit.Dark,.003f,CastingInset + .007f,.060f,CastingInset);
            grid.Band(mesh,color,.060f,CastingInset,.281f,CastingInset);
            grid.Band(mesh,kit.Dark,.281f,CastingInset,.300f,CastingInset);
            grid.Band(mesh,color,.300f,CastingInset,BodyHeight-.025f,CastingInset);
            grid.Band(mesh,color,BodyHeight-.025f,CastingInset,BodyHeight,lidInset);
            grid.Fill(mesh,color,BodyHeight,lidInset);
            var cells = grid.Cells.OrderBy(c=>((Vector2)c-grid.Pivot).sqrMagnitude).ThenBy(c=>c.x).ThenBy(c=>c.y).ToArray();
            var eye = grid.Center(cells[cells.Length>1 ? 1 : 0],BodyHeight+.016f);
            var anchor = Child(root,"Rope Anchor");
            anchor.localPosition = eye;
            // An open machined eye around the actual end of a cord, kept entirely on its own cell.
            for (int n=0;n<10;n++)
            {
                float a=n*Mathf.PI*.2f,b=(n+1)*Mathf.PI*.2f;
                var dirA = new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                var dirB = new Vector3(Mathf.Cos(b),0,Mathf.Sin(b));
                mesh.Quad(kit.Ivory,eye+dirA*size*.09f,eye+dirB*size*.09f,
                    eye+dirB*size*.18f,eye+dirA*size*.18f);
            }
            AddGrips(root,grid,size,mesh,kit,false);
            AddGrips(root,grid,size,mesh,kit,true);
            mesh.Attach(root,"Casting",kit,true);
        }

        private static void AddGrips(Transform root, Grid grid,float size,Surface mesh,Finishes kit,bool back)
        {
            // Choose an uninterrupted cross section close to the robot-facing edge.
            float desired = grid.Pivot.y + (back ? .55f : -.55f);
            float best = float.PositiveInfinity;
            Vector2Int left = default, right = default;
            foreach (var cell in grid.Cells)
            {
                if (grid.Cells.Contains(cell+Vector2Int.left)) continue;
                var end = cell;
                while (grid.Cells.Contains(end+Vector2Int.right)) end += Vector2Int.right;
                float score = Mathf.Abs(cell.y-desired) + Mathf.Abs((cell.x+end.x)*.5f-grid.Pivot.x)*.42f
                    + Mathf.Max(0,(end.x-cell.x+1)*size-1.15f)*1.5f;
                if (score >= best) continue;
                best=score;left=cell;right=end;
            }
            for(int j=0;j<2;j++)
            {
                bool positive = (j==1) != back;
                var center = grid.Center(positive ? right : left,.215f);
                var outwards = positive ? Vector3.right : Vector3.left;
                var contact = center + outwards * (size * .5f - CastingInset + .003f);
                var marker = Child(root,(back?"Back Grip ":"Grip ")+(j==0?"Left":"Right"));
                marker.localPosition = contact;
                marker.localRotation = Quaternion.LookRotation(-outwards);
                var a = Vector3.forward*.063f;
                var b = Vector3.up*.048f;
                var direction = positive ? -a : a;
                mesh.Quad(kit.Metal,contact-direction-b,contact+direction-b,contact+direction+b,contact-direction+b);
            }
        }
    }
}
