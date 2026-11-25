using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Monlit
{
    [DisallowMultipleComponent]
    public sealed class WorkshopPlacementPreview : MonoBehaviour
    {
        // The tallest painted lip on a station is .018 above the tabletop.
        private const float MarkHeight = .022f;
        private static readonly Color32 Wash = new Color32(154, 239, 222, 58);
        private static readonly Color32 Chalk = new Color32(233, 255, 242, 250);
        private static readonly Color32 Ink = new Color32(22, 49, 53, 230);
        private static readonly Vector2Int[] Corners =
            { new Vector2Int(-1, -1), new Vector2Int(-1, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) };
        private static readonly Vector2Int[] Sides =
            { Vector2Int.left, Vector2Int.up, Vector2Int.right, Vector2Int.down };

        private readonly HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> floorFaces = new List<int>();
        private readonly List<int> obscuredEdges = new List<int>();
        private readonly Vector3[] inner = new Vector3[4], middle = new Vector3[4], outer = new Vector3[4];
        private WorkshopRobot robot;
        private WorkshopPiece meshPiece;
        private BoxCollider floor;
        private float cellSize;
        private bool display = true;
        private Mesh mesh;
        private MeshRenderer mark;
        private Material floorMaterial, obscuredMaterial;

        public void Configure(WorkshopRobot subject, BoxCollider tabletop, float size)
        {
            robot = subject;
            floor = tabletop;
            cellSize = size;
            meshPiece = null;
            Hide();
        }

        public void SetDisplay(bool value)
        {
            display = value;
            if (!value) Hide();
        }

        private void LateUpdate()
        {
            var piece = robot ? robot.HeldPiece : null;
            if (!display || !floor || !piece || !piece.IsHeld || piece.Footprint == null || piece.Footprint.Length == 0)
            {
                Hide();
                return;
            }

            if (!mark) CreateMark();
            if (meshPiece != piece)
            {
                BuildOutline(piece.Footprint);
                meshPiece = piece;
            }
            var body = piece.Body;
            var position = body.position;
            position.y = floor.bounds.max.y + MarkHeight;
            mark.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, body.rotation.eulerAngles.y, 0f));
            mark.enabled = true;
        }

        private void CreateMark()
        {
            var shader = Resources.Load<Shader>("WorkshopPlacementMark");
            floorMaterial = new Material(shader) { name = "Footprint on table" };
            obscuredMaterial = new Material(shader) { name = "Hidden footprint edge" };
            obscuredMaterial.SetInt("_DepthTest", (int)CompareFunction.Greater);
            obscuredMaterial.SetFloat("_Obscured", 1f);
            mesh = new Mesh { name = "Carried casting footprint" };
            mesh.MarkDynamic();
            var node = new GameObject("Placement projection");
            node.transform.SetParent(transform, false);
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            mark = node.AddComponent<MeshRenderer>();
            mark.sharedMaterials = new[] { floorMaterial, obscuredMaterial };
            mark.shadowCastingMode = ShadowCastingMode.Off;
            mark.receiveShadows = false;
            mark.lightProbeUsage = LightProbeUsage.Off;
            mark.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void BuildOutline(Vector2[] footprint)
        {
            cells.Clear();
            vertices.Clear();
            colors.Clear();
            floorFaces.Clear();
            obscuredEdges.Clear();
            // Offsets in Footprint are relative to the centroid of its cells.
            // Use an actual cell as the grid origin to retain that fractional pivot.
            Vector2 origin = footprint[0];
            foreach (var offset in footprint)
                cells.Add(Vector2Int.RoundToInt(offset - origin));

            foreach (var cell in cells)
            {
                for (int i = 0; i < 4; i++)
                {
                    var corner = Corners[i];
                    int acrossX = cells.Contains(cell + new Vector2Int(corner.x, 0)) ? 1 : 0;
                    int acrossZ = cells.Contains(cell + new Vector2Int(0, corner.y)) ? 1 : 0;
                    int diagonal = cells.Contains(cell + corner) ? 1 : 0;
                    // The cells at an elbow must share the same miter, including its inner corner.
                    int dx = acrossX + acrossZ == 0 ? 1 : System.Math.Sign(1 - acrossX + acrossZ - diagonal);
                    int dz = acrossX + acrossZ == 0 ? 1 : System.Math.Sign(1 + acrossX - acrossZ - diagonal);
                    var outward = new Vector3(corner.x * dx, 0f, corner.y * dz);
                    Vector2 point = (origin + (Vector2)cell + (Vector2)corner * .5f) * cellSize;
                    var edge = new Vector3(point.x, 0f, point.y) - outward * WorkshopPuzzleArt.CastingInset;
                    inner[i] = edge - outward * (cellSize * .045f);
                    middle[i] = edge + outward * (cellSize * .045f);
                    outer[i] = edge + outward * (cellSize * .10f);
                }
                Quad(inner[0], inner[1], inner[2], inner[3], Wash);
                for (int i = 0; i < 4; i++)
                {
                    if (cells.Contains(cell + Sides[i])) continue;
                    int next = (i + 1) % 4;
                    Quad(inner[i], inner[next], middle[next], middle[i], Chalk, true);
                    Quad(middle[i], middle[next], outer[next], outer[i], Ink);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(floorFaces, 0, false);
            mesh.SetTriangles(obscuredEdges, 1, false);
            mesh.RecalculateBounds();
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color, bool showThrough = false)
        {
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            for (int i = 0; i < 4; i++) colors.Add(color);
            AddFace(floorFaces, start);
            if (showThrough) AddFace(obscuredEdges, start);
        }

        private static void AddFace(List<int> faces, int start)
        {
            faces.Add(start);
            faces.Add(start + 1);
            faces.Add(start + 2);
            faces.Add(start);
            faces.Add(start + 2);
            faces.Add(start + 3);
        }

        private void Hide()
        {
            if (mark) mark.enabled = false;
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (mark) Destroy(mark.gameObject);
            if (mesh) Destroy(mesh);
            if (floorMaterial) Destroy(floorMaterial);
            if (obscuredMaterial) Destroy(obscuredMaterial);
        }
    }
}
