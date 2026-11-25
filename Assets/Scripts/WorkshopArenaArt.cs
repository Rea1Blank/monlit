using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monlit
{
    // One static mesh for the workstation, including its engraved markings and hardware.
    internal static class WorkshopArenaArt
    {
        private static readonly Color32[] Planks = { RGB(93, 65, 47), RGB(86, 57, 43), RGB(101, 70, 49), RGB(92, 61, 45) };
        private static readonly Color32 WoodSeam = RGB(62, 43, 34), Grain = RGB(115, 81, 58), Heartwood = RGB(78, 53, 40);
        private static readonly Color32 Rubber = RGB(26, 36, 38), Frame = RGB(74, 90, 88), Steel = RGB(125, 143, 132);
        private static readonly Color32 Mat = RGB(43, 73, 72), Grid = RGB(55, 87, 84), Print = RGB(83, 113, 105);
        private static readonly Color32 Tray = RGB(55, 78, 80), Lip = RGB(91, 112, 107), Source = RGB(53, 67, 69);
        private static readonly Color32 Copper = RGB(160, 111, 66), Brass = RGB(222, 176, 100), Moss = RGB(158, 206, 164);

        private static Color32 RGB(byte r, byte g, byte b) => new Color32(r, g, b, 255);

        internal static Material PaletteMaterial(WorkshopGeneratedSurfaces owner, Material template,
            string name, Color32[] colors)
        {
            var texture = owner.Keep(new Texture2D(colors.Length, 1, TextureFormat.RGBA32, false) {
                name = name + " swatches", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            });
            texture.SetPixels32(colors);
            texture.Apply(false, true);
            var material = owner.Keep(new Material(template) { name = name });
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", .30f);
            return material;
        }

        internal static void Build(WorkshopGeneratedSurfaces owner, Material template, Bounds bounds,
            WorkshopPuzzleArt.Result art, WorkshopPuzzleGenerator.Puzzle puzzle, float cellSize)
        {
            var mesh = new Sheet();
            Vector3 center = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            Vector2 half = new Vector2(bounds.extents.x, bounds.extents.z);
            float detail = Mathf.Clamp(half.magnitude / 13f, 1f, 1.45f);
            DrawBench(mesh, half);

            mesh.Deck(half);
            DrawGrid(mesh, half, detail);
            DrawRim(mesh, half, detail);
            Vector2 Local(Transform t) => new Vector2(t.position.x - center.x, t.position.z - center.z);
            Vector2 dispatch = Local(art.Source);
            Vector2 sourceHalf = (Vector2)puzzle.SourceBounds.size * (cellSize * .5f);
            DrawStation(mesh, dispatch, sourceHalf, detail, true, Brass);
            float leftBank = float.PositiveInfinity, rightBank = float.PositiveInfinity;
            for (int i = 0; i < art.Goals.Length; i++)
            {
                Vector2 station = Local(art.Goals[i]);
                Vector2 stationHalf = (Vector2)puzzle.GoalBounds[i].size * (cellSize * .5f);
                DrawStation(mesh, station, stationHalf, detail, false, WorkshopPuzzleArt.ColorFor(i));
                float edge = Mathf.Abs(station.x - dispatch.x) - stationHalf.x;
                if (station.x < dispatch.x) leftBank = Mathf.Min(leftBank, edge);
                else rightBank = Mathf.Min(rightBank, edge);
            }
            DrawLane(mesh, dispatch, -1f, sourceHalf.x, leftBank);
            DrawLane(mesh, dispatch, 1f, sourceHalf.x, rightBank);
            mesh.Attach(owner, template, center);
        }

        private static void DrawLane(Sheet mesh, Vector2 origin, float side, float start, float end)
        {
            if (float.IsPositiveInfinity(end)) return;
            for (float x = start + 2f; x < end - 1f; x += 3.25f)
            {
                var tip = origin + Vector2.right * (side * x);
                mesh.Line(tip + new Vector2(-side * .28f, .25f), tip, .065f, -.020f, Print);
                mesh.Line(tip + new Vector2(-side * .28f, -.25f), tip, .065f, -.020f, Print);
            }
        }

        private static void DrawBench(Sheet mesh, Vector2 half)
        {
            Vector2 extent = half + new Vector2(42f, 35f);
            const float plankWidth = 2.18f;
            float firstSeam = half.y + .12f;
            var rng = new System.Random(111);
            int first = Mathf.FloorToInt((-extent.y - firstSeam) / plankWidth);
            int last = Mathf.CeilToInt((extent.y - firstSeam) / plankWidth);
            for (int row = first; row < last; row++)
            {
                float z = firstSeam + row * plankWidth;
                mesh.Rect(new Vector2(0, z + plankWidth * .5f), new Vector2(extent.x, plankWidth * .5f),
                    -.55f, Planks[(row - first) % Planks.Length]);
                mesh.Line(new Vector2(-extent.x, z), new Vector2(extent.x, z), .037f, -.546f, WoodSeam);
                mesh.Line(new Vector2(-extent.x, z + .035f), new Vector2(extent.x, z + .035f), .019f, -.544f, Grain);
                for (float x = -extent.x; x < extent.x - 4; x += 5.5f)
                {
                    float grainZ = z + .35f + 1.5f * (float)rng.NextDouble();
                    var p = new Vector2(x + 2f + (float)rng.NextDouble(), grainZ);
                    float distance = Mathf.Max(Mathf.Abs(p.x) - half.x, Mathf.Abs(grainZ) - half.y);
                    if (distance < -2.5f || distance > 6f) continue;
                    float length = 1.5f + (float)rng.NextDouble() * 3f;
                    float bend = ((float)rng.NextDouble() - .45f) * .25f;
                    mesh.GrainLine(p, length, bend, .026f, Grain);
                    if (rng.Next(3) == 0)
                        mesh.GrainLine(p + new Vector2(.27f, -.075f), length * .68f, bend * 1.17f, .016f, Heartwood);
                }
            }
            // Growth rings in the two slats immediately behind the housing.
            DrawKnot(mesh, new Vector2(-half.x * .72f, firstSeam + .96f));
            float lowerRow = Mathf.Floor((-half.y - 1f - firstSeam) / plankWidth);
            DrawKnot(mesh, new Vector2(half.x * .66f, firstSeam + (lowerRow + .52f) * plankWidth));
        }

        private static void DrawKnot(Sheet mesh, Vector2 p)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                mesh.GrainLine(p + new Vector2(-.32f, -.07f * side), 3.35f, .22f * side, .022f, Grain);
                mesh.GrainLine(p + Vector2.down * (.025f * side), 1.78f, .14f * side, .034f, Heartwood);
            }
            mesh.GrainLine(p, .61f, .035f, .063f, Heartwood);
        }

        private static void DrawGrid(Sheet mesh, Vector2 half, float detail)
        {
            Vector2 edge = half - Vector2.one * .50f;
            float step = 1.15f * detail;
            for (int i = -Mathf.FloorToInt(edge.x / step); i <= Mathf.FloorToInt(edge.x / step); i++)
            {
                float x = i * step;
                mesh.Line(new Vector2(x, -edge.y), new Vector2(x, edge.y), .013f * detail, -.029f, Grid);
                for (int j = -Mathf.FloorToInt(edge.y / step); j <= Mathf.FloorToInt(edge.y / step); j++)
                {
                    if (i % 2 != 0 || j % 2 != 0) continue;
                    var p = new Vector2(x, j * step);
                    mesh.Rect(p, new Vector2(.049f, .013f) * detail, -.026f, Print);
                    mesh.Rect(p, new Vector2(.013f, .049f) * detail, -.025f, Print);
                }
            }
            for (int i = -Mathf.FloorToInt(edge.y / step); i <= Mathf.FloorToInt(edge.y / step); i++)
                mesh.Line(new Vector2(-edge.x, i * step), new Vector2(edge.x, i * step), .013f * detail, -.028f, Grid);
        }

        private static void DrawRim(Sheet mesh, Vector2 half, float detail)
        {
            for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var p = new Vector2(x * (half.x - .47f * detail), z * (half.y - .47f * detail));
                mesh.Panel(p, new Vector2(.38f, .35f) * detail, .13f * detail, -.02f, .105f, .045f, Frame, Steel);
                mesh.Disc(p, .16f * detail, .109f, Rubber);
                mesh.Disc(p, .127f * detail, .114f, Brass);
                var offset = new Vector2(.075f, .048f * z * x) * detail;
                mesh.Line(p - offset, p + offset, .032f * detail, .12f, Copper);
                for (int i = 0; i < 4; i++)
                {
                    var a = new Vector2(x * (half.x - (1.12f + i * .21f) * detail), z * (half.y - .13f));
                    mesh.Line(a, a + new Vector2(-x * .10f, -z * .16f), .085f, .053f, Copper);
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                var latch = new Vector2(half.x * side, 0);
                mesh.Panel(latch, new Vector2(.28f, .86f) * detail, .12f, -.20f, .09f, .045f, Rubber, Frame);
                for (int i = -2; i <= 2; i++)
                    mesh.Rect(latch + Vector2.up * (i * .22f * detail), new Vector2(.15f, .044f) * detail, .098f, Rubber);
            }
            DrawMeter(mesh, new Vector2(-half.x * .52f, half.y - .035f), detail);
            // A recessed carry handle on the front edge, with two flanged brass mounts.
            var handle = new Vector2(half.x * .41f, -half.y + .075f);
            mesh.Panel(handle, new Vector2(1.05f, .19f) * detail, .10f, -.055f, .075f, .040f, Rubber, Frame);
            mesh.Panel(handle, new Vector2(.68f, .11f) * detail, .065f, .076f, .138f, .032f, Copper, Rubber);
            for (int side = -1; side <= 1; side += 2)
            {
                mesh.Rect(handle + Vector2.right * (.64f * side * detail), new Vector2(.043f, .107f) * detail, .143f, Copper);
                mesh.Disc(handle + Vector2.right * (.85f * side * detail), .048f * detail, .085f, Brass);
            }
            int ticks = Mathf.FloorToInt((half.x - 2.1f * detail) / (.36f * detail));
            for (int i = -ticks; i <= ticks; i++)
            {
                float x = i * .36f * detail;
                if (Mathf.Abs(x - handle.x) < 1.2f * detail) continue;
                float depth = i % 5 == 0 ? .065f : .038f;
                mesh.Rect(new Vector2(x, -half.y + .14f), new Vector2(.014f * detail, depth), .055f, Frame);
            }
        }

        private static void DrawMeter(Sheet mesh, Vector2 p, float scale)
        {
            Vector2 Offset(float x, float z) => p + new Vector2(x, z) * scale;
            mesh.Panel(p, new Vector2(1.48f, .35f) * scale, .16f, -.16f, .135f, .065f, Rubber, Steel);
            mesh.Panel(p, new Vector2(1.35f, .265f) * scale, .10f, .138f, .153f, .035f, Frame, Rubber);
            // A glass level meter, an amber pilot and an exposed four-finger connector.
            mesh.Panel(Offset(-.56f, 0), new Vector2(.55f, .176f) * scale, .055f, .154f, .164f, .018f, Lip, Source);
            for (int i = 0; i < 5; i++)
            {
                float height = .048f + i * .021f;
                mesh.Rect(Offset(-.95f + .19f * i, height - .125f), new Vector2(.061f, height) * scale,
                    .170f, i < 3 ? Moss : Frame);
            }
            mesh.Disc(Offset(.20f, 0), .118f * scale, .162f, Frame);
            mesh.Disc(Offset(.20f, 0), .075f * scale, .170f, Brass);
            mesh.Panel(Offset(.83f, 0), new Vector2(.385f, .16f) * scale, .045f, .154f, .163f, .018f, Frame, Copper);
            for (int i = 0; i < 4; i++)
                mesh.Rect(Offset(.605f + i * .148f, .015f), new Vector2(.031f, .115f) * scale, .169f, Brass);
        }

        private static void DrawStation(Sheet mesh, Vector2 at, Vector2 footprint, float detail, bool source, Color32 color)
        {
            Vector2 half = footprint + Vector2.one * (.36f * detail);
            mesh.Panel(at, half, .24f * detail, -.024f, -.012f, .052f * detail, Rubber, Lip);
            mesh.Panel(at, half - Vector2.one * .075f, .20f * detail, -.010f, -.005f, .035f,
                Rubber, source ? Source : Tray);
            for (int side = -1; side <= 1; side += 2)
            {
                var p = at + new Vector2(side * (half.x - .22f * detail), side * (half.y - .22f * detail));
                mesh.Disc(p, .063f * detail, -.0008f, Rubber);
                mesh.Disc(p, .031f * detail, .0001f, Steel);
            }
            var contact = at + new Vector2(-half.x + .24f * detail, half.y - .19f * detail);
            float stripe = Mathf.Min(half.x * .65f, 1.2f * detail);
            mesh.Line(contact, contact + Vector2.right * stripe, .075f * detail, -.001f, color);
            for (int i = 0; i < 3; i++)
            {
                var key = at + new Vector2(half.x - (.26f + .12f*i) * detail, -half.y + .20f * detail);
                mesh.Line(key, key + new Vector2(-.045f, -.065f) * detail, .041f * detail, -.001f, source ? Brass : Lip);
            }
        }

        private sealed class Sheet
        {
            private readonly List<Vector3> positions = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<int> triangles = new List<int>(), swatches = new List<int>();
            private readonly Dictionary<Color32, int> lookup = new Dictionary<Color32, int>();
            private readonly List<Color32> colors = new List<Color32>();

            private static Vector3 At(Vector2 p, float y) => new Vector3(p.x, y, p.y);
            private int Swatch(Color32 color)
            {
                if (lookup.TryGetValue(color, out int index)) return index;
                index = colors.Count; colors.Add(color); lookup.Add(color, index);
                return index;
            }

            private void Face(Vector3 a, Vector3 b, Vector3 c, Color32 color, Vector3? d = null)
            {
                var normal = Vector3.Cross(b - a, c - a).normalized;
                int n = positions.Count, swatch = Swatch(color);
                positions.Add(a); positions.Add(b); positions.Add(c);
                triangles.Add(n); triangles.Add(n+1); triangles.Add(n+2);
                if (d.HasValue)
                {
                    positions.Add(d.Value);
                    triangles.Add(n); triangles.Add(n+2); triangles.Add(n+3);
                }
                for (int i = n; i < positions.Count; i++) { normals.Add(normal); swatches.Add(swatch); }
            }

            public void Line(Vector2 a, Vector2 b, float width, float y, Color32 color)
            {
                var side = new Vector2(b.y - a.y, a.x - b.x).normalized * (.5f * width);
                Face(At(a-side,y), At(b-side,y), At(b+side,y), color, At(a+side,y));
            }
            public void GrainLine(Vector2 center, float length, float bend, float width, Color32 color)
            {
                Vector2 point = center - Vector2.right * (.5f * length);
                Vector2 edge = Vector2.up * (.07f * width);
                for (int i = 1; i <= 6; i++)
                {
                    float t = i / 6f;
                    float curve = Mathf.Sin(Mathf.PI * t);
                    var next = center + new Vector2((t - .5f) * length, curve * bend * (1.24f - .48f * t));
                    var nextEdge = Vector2.up * ((.07f + .43f * curve) * width);
                    Face(At(point + edge, -.540f), At(next + nextEdge, -.540f), At(next - nextEdge, -.540f),
                        color, At(point - edge, -.540f));
                    point = next;
                    edge = nextEdge;
                }
            }
            public void Rect(Vector2 p, Vector2 half, float y, Color32 color)
            {
                var a = p - half; var b = p + half;
                Face(At(a,y), At(new Vector2(a.x,b.y),y), At(b,y), color, At(new Vector2(b.x,a.y),y));
            }
            public void Disc(Vector2 p, float radius, float y, Color32 color)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f, b = (i + 1) * Mathf.PI / 6f;
                    Face(At(p,y), At(p + new Vector2(Mathf.Cos(a),-Mathf.Sin(a))*radius,y),
                        At(p + new Vector2(Mathf.Cos(b),-Mathf.Sin(b))*radius,y),color);
                }
            }

            private static Vector2[] Octagon(Vector2 p, Vector2 h, float r) => new[] {
                p+new Vector2(-h.x,-h.y+r), p+new Vector2(-h.x,h.y-r), p+new Vector2(-h.x+r,h.y),
                p+new Vector2(h.x-r,h.y), p+new Vector2(h.x,h.y-r), p+new Vector2(h.x,-h.y+r),
                p+new Vector2(h.x-r,-h.y), p+new Vector2(-h.x+r,-h.y)
            };

            private void Band(Vector2[] outer, float yo, Vector2[] inner, float yi, Color32 color)
            {
                for (int i = 0; i < outer.Length; i++)
                {
                    int next = (i + 1) % outer.Length;
                    Face(At(outer[i],yo), At(outer[next],yo), At(inner[next],yi), color, At(inner[i],yi));
                }
            }

            private void Fill(Vector2 p, Vector2[] loop, float y, Color32 color)
            {
                for (int i = 0; i < loop.Length; i++)
                    Face(At(p,y), At(loop[i],y), At(loop[(i+1)%loop.Length],y),color);
            }

            public void Panel(Vector2 p, Vector2 half, float radius, float bottom, float top, float bevel,
                Color32 edge, Color32 color)
            {
                var outer = Octagon(p,half,radius);
                var inner = Octagon(p,half-Vector2.one*bevel,Mathf.Max(.01f,radius-bevel*.5f));
                Band(outer,bottom,inner,top,edge);
                Fill(p,inner,top,color);
            }

            public void Deck(Vector2 half)
            {
                var plinth = Octagon(Vector2.zero, half + Vector2.one * .12f, .34f);
                var shell = Octagon(Vector2.zero, half, .28f);
                var shoulder = Octagon(Vector2.zero, half - Vector2.one * .075f, .24f);
                var rail = Octagon(Vector2.zero, half - Vector2.one * .19f, .19f);
                var recess = Octagon(Vector2.zero, half - Vector2.one * .27f, .14f);
                var mat = Octagon(Vector2.zero, half - Vector2.one * .325f, .12f);
                Band(plinth, -.52f, shell, -.13f, Rubber);
                Band(shell, -.13f, shoulder, .045f, Frame);
                Band(shoulder, .045f, rail, .045f, Steel);
                Band(rail, .045f, recess, .05f, Frame);
                Band(recess, .05f, mat, -.036f, Rubber);
                Fill(Vector2.zero, mat, -.036f, Mat);
            }

            public void Attach(WorkshopGeneratedSurfaces owner, Material template, Vector3 center)
            {
                var mesh = owner.Keep(new Mesh { name = "Workstation tooling" });
                if (positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                var uv = new List<Vector2>(positions.Count);
                foreach (int index in swatches) uv.Add(new Vector2((index+.5f)/colors.Count, .5f));
                mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0,uv);
                mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                var node = new GameObject("Assembly workbench");
                node.transform.SetParent(owner.transform,false); node.transform.position = center;
                node.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = PaletteMaterial(owner,template,"Bench finishes",colors.ToArray());
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }
}
