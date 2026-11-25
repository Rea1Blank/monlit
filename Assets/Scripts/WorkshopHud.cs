using UnityEngine;

namespace Monlit
{
    [DisallowMultipleComponent]
    public sealed class WorkshopHud : MonoBehaviour
    {
        [SerializeField] private WorkshopRobot robot;
        [SerializeField] private WorkshopFigure[] figures;

        private static readonly Color Panel = new Color32(21, 43, 48, 245);
        private static readonly Color KeyBackground = new Color32(52, 77, 81, 255);
        private static readonly Color Ink = new Color32(246, 246, 231, 255);
        private static readonly Color Mint = new Color32(122, 232, 196, 255);
        private static readonly Color Amber = new Color32(255, 196, 98, 255);
        private const string SeniorHint = "Следи за верёвками. Перекрашенные детали держатся на гвоздях.";

        private GUIStyle title;
        private GUIStyle label;
        private GUIStyle key;
        private bool showHints = true;
        private bool showProgress = true;
        private float uiScale = 1f;
        private float seniorHintUntil;
        private int[] progress;
        private string progressLabel;
        private float nextSample;
        private bool assembled;
        private Camera workshopCamera;
        private Renderer[] goalSurfaces;
        private readonly Plane[] cameraPlanes = new Plane[6];
        private WorkshopDifficulty difficulty;

        internal void SetDisplay(bool hints, bool progress, float scale)
        {
            showHints = hints;
            showProgress = progress;
            uiScale = scale;
        }

        public void Configure(WorkshopRobot workshopRobot)
        {
            robot = workshopRobot;
        }

        public void Configure(WorkshopRobot workshopRobot, WorkshopFigure[] goals,
            WorkshopDifficulty difficulty = WorkshopDifficulty.Middle)
        {
            robot = workshopRobot;
            figures = goals;
            this.difficulty = difficulty;
            workshopCamera = Camera.main;
            goalSurfaces = new Renderer[goals.Length];
            for (int i = 0; i < goals.Length; i++)
                goalSurfaces[i] = goals[i].GetComponentInChildren<MeshRenderer>(true);
            progress = new int[goals.Length];
            progressLabel = null;
            seniorHintUntil = difficulty == WorkshopDifficulty.Senior ? Time.time + 8f : 0f;
        }

        private void OnGUI()
        {
            if (robot == null || Event.current.type != EventType.Repaint)
                return;

            if (title == null)
            {
                title = TextStyle(18, FontStyle.Bold, TextAnchor.MiddleLeft);
                label = TextStyle(15, FontStyle.Normal, TextAnchor.MiddleLeft);
                key = TextStyle(14, FontStyle.Bold, TextAnchor.MiddleCenter);
            }

            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0)
                return;

            if (progressLabel == null || Time.unscaledTime >= nextSample) SampleProgress();
            string goal = progressLabel;
            bool delivered = assembled;
            float goalWidth = Mathf.Ceil(title.CalcSize(new GUIContent(goal)).x);
            if (progress != null) goalWidth = Mathf.Max(goalWidth, progress.Length * 13f);
            bool slipped = !robot.IsCarrying && Time.time - robot.GripLostAt < 1.4f;
            string action = slipped ? "Верёвка натянулась! Деталь выскользнула" : InteractionLabel();
            float actionWidth = Mathf.Ceil(label.CalcSize(new GUIContent(action)).x);
            string nailAction = null;
            string hint = Time.time < seniorHintUntil ? SeniorHint : null;
            if (showHints && difficulty == WorkshopDifficulty.Senior)
            {
                if (robot.NailCandidate) nailAction = "Прибить";
                else if (robot.NailedCandidate) nailAction = "Вынуть гвоздь";
                if (robot.HeldPiece && robot.HeldPiece.RequiresNail)
                    hint = nailAction == null ? "Совмести края детали со свободным местом в форме." : null;
            }
            float nailWidth = nailAction == null ? 0f : label.CalcSize(new GUIContent(nailAction)).x + 43f;
            bool drawGoal = showProgress || delivered;
            bool canTurnPiece = robot.HeldPiece && robot.HeldPiece.IsHeld;
            float interactionKeyX = canTurnPiece ? 574f : 392f;
            float controlsWidth = interactionKeyX + actionWidth + nailWidth + 166f;
            float requiredWidth = Mathf.Max(drawGoal ? goalWidth + 73f : 1f, showHints ? controlsWidth : 1f);
            bool drawSeniorHint = showHints && hint != null;
            float hintWidth = drawSeniorHint ? Mathf.Ceil(label.CalcSize(new GUIContent(hint)).x) : 0f;
            requiredWidth = Mathf.Max(requiredWidth, hintWidth + 60f);
            float scale = Mathf.Min(Mathf.Clamp(safe.height / 760f, 0.75f, 1.5f) * uiScale, safe.width / requiredWidth);
            var origin = new Vector3(safe.xMin, Screen.height - safe.yMax, 0f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1f));
            Rect goalPanel = drawGoal ? DrawGoal(goal, goalWidth) : Rect.zero;
            if (drawSeniorHint)
            {
                float y = safe.height / scale - 90f;
                Box(new Rect(18f, y, hintWidth + 24f, 29f), Panel, 5f);
                GUI.Label(new Rect(30f, y, hintWidth + 2f, 29f), hint, label);
            }
            if (showHints)
            {
                float controlsTop = safe.height / scale - 56f;
                Rect controls = DrawControls(controlsTop, canTurnPiece, interactionKeyX, action, actionWidth, slipped, nailAction, nailWidth);
                if (TryFindOffscreenGoal(out var screenPoint, out var color))
                {
                    var target = new Vector2((screenPoint.x - safe.xMin) / scale, (safe.yMax - screenPoint.y) / scale);
                    var size = safe.size / scale;
                    var position = MarkerPosition(target, size, goalPanel, controls);
                    DrawMarker(position, target - position, color);
                }
            }
            GUI.matrix = previousMatrix;
        }

        private bool TryFindOffscreenGoal(out Vector2 screenPoint, out Color color)
        {
            screenPoint = default;
            color = default;
            var held = robot.HeldPiece;
            if (difficulty == WorkshopDifficulty.Senior || !held || !held.IsHeld ||
                !held.Target || !workshopCamera || goalSurfaces == null) return false;

            Transform station = held.Target.parent;
            for (int i = 0; i < figures.Length; i++)
            {
                if (figures[i].transform != station) continue;
                var surface = goalSurfaces[i];
                if (!surface) return false;
                GeometryUtility.CalculateFrustumPlanes(workshopCamera, cameraPlanes);
                if (GeometryUtility.TestPlanesAABB(cameraPlanes, surface.bounds)) return false;
                screenPoint = workshopCamera.WorldToScreenPoint(surface.bounds.center);
                color = WorkshopPuzzleArt.ColorFor(i);
                return true;
            }
            return false;
        }

        private static Vector2 MarkerPosition(Vector2 target, Vector2 size, Rect goal, Rect controls)
        {
            const float inset = 23f;
            Vector2 center = size * .5f;
            Vector2 direction = target - center;
            float crossX = (center.x - inset) / Mathf.Max(.001f, Mathf.Abs(direction.x));
            float crossY = (center.y - inset) / Mathf.Max(.001f, Mathf.Abs(direction.y));
            Vector2 edge = center + direction * Mathf.Min(crossX, crossY);
            // Leave a radius and a small gap around either panel where it meets the edge.
            if (goal.width > 0f && edge.x < goal.xMax + inset)
                edge.y = Mathf.Max(edge.y, goal.yMax + inset);
            if (edge.x < controls.xMax + inset)
                edge.y = Mathf.Min(edge.y, controls.yMin - inset);
            return edge;
        }

        private static void DrawMarker(Vector2 position, Vector2 direction, Color color)
        {
            Box(new Rect(position.x - 15f, position.y - 15f, 30f, 30f), Panel, 15f);
            Vector2 tip = position + direction.normalized * 5f;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var previous = GUI.matrix;
            for (int side = -1; side <= 1; side += 2)
            {
                GUI.matrix = previous * Matrix4x4.TRS(tip, Quaternion.Euler(0f, 0f, angle + side * 45f), Vector3.one);
                Box(new Rect(-12f, -2f, 13f, 4f), color, 2f);
            }
            GUI.matrix = previous;
        }

        private void SampleProgress()
        {
            nextSample = Time.unscaledTime + .12f;
            int total = figures?.Length ?? 0;
            if (progress == null || progress.Length != total) progress = new int[total];
            int filled = 0;
            for (int i = 0; i < total; i++)
            {
                progress[i] = figures[i].FilledPercent;
                if (progress[i] == 100) filled++;
            }
            assembled = total > 0 && filled == total;
            progressLabel = assembled ? "Все фигуры собраны" : $"Микросервисы   {filled} / {total}";
        }

        private Rect DrawGoal(string message, float width)
        {
            var panel = new Rect(18f, 18f, width + 24f, 51f);
            Box(panel, Panel, 6f);
            GUI.Label(new Rect(30f, 20f, width + 2f, 30f), message, title);
            float step = (width + 3f) / Mathf.Max(1, progress.Length);
            for (int i = 0; i < progress.Length; i++)
            {
                float x = 30f + i * step;
                float w = step - 3f;
                Color color = WorkshopPuzzleArt.ColorFor(i);
                Box(new Rect(x, 53f, w, 6f), Color.Lerp(Panel, color, .38f), 2f);
                if (progress[i] > 0) Box(new Rect(x, 53f, w * progress[i] / 100f, 6f), color, 2f);
            }
            return panel;
        }

        private Rect DrawControls(float top, bool canTurnPiece, float interactionKeyX, string action, float width, bool slipped, string nailAction, float nailWidth)
        {
            float actionX = interactionKeyX + (slipped ? 0f : 29f);
            float nailX = actionX + width + 16f;
            float pauseX = nailX + nailWidth;
            var panel = new Rect(18f, top, pauseX + 80f, 38f);
            Box(panel, Panel, 6f);
            if (robot.PrecisionSteering)
            {
                DrawKey("W / S", new Rect(27f, top + 7f, 50f, 24f));
                GUI.Label(new Rect(84f, top, 120f, 38f), "Вперёд / назад", label);
                DrawKey("A / D", new Rect(209f, top + 7f, 46f, 24f));
                GUI.Label(new Rect(262f, top, 120f, 38f), "Поворот робота", label);
            }
            else
            {
                DrawKey("WASD / стрелки", new Rect(27f, top + 7f, 114f, 24f));
                string movement = robot.CarryAngleLocked && robot.IsCarrying ? "Сдвиг" : "Ходьба";
                GUI.Label(new Rect(148f, top, 60f, 38f), movement, label);
                DrawKey("Shift", new Rect(209f, top + 7f, 41f, 24f));
                GUI.Label(new Rect(257f, top, 93f, 38f), "Точный ход", label);
            }
            if (canTurnPiece)
            {
                DrawKey("Q / E", new Rect(387f, top + 7f, 46f, 24f));
                GUI.Label(new Rect(440f, top, 124f, 38f), "Поворот детали", label);
            }
            Box(new Rect(interactionKeyX - 9f, top + 10f, 1f, 18f), KeyBackground, 0f);
            if (!slipped) DrawKey("F", new Rect(interactionKeyX, top + 7f, 22f, 24f));
            GUI.Label(new Rect(actionX, top, width + 2f, 38f), action, label);
            if (nailAction != null)
            {
                DrawKey("R", new Rect(nailX, top + 7f, 22f, 24f));
                GUI.Label(new Rect(nailX + 29f, top, nailWidth - 29f, 38f), nailAction, label);
            }
            DrawKey("Esc", new Rect(pauseX, top + 7f, 35f, 24f));
            GUI.Label(new Rect(pauseX + 41f, top, 54f, 38f), "Пауза", label);
            return panel;
        }

        private string InteractionLabel()
        {
            if (robot.IsSettingDown)
                return "Опускает деталь";

            if (robot.IsCarrying)
            {
                if (!robot.CanSeatHeld) return "Опустить деталь";
                return robot.HeldPiece.RequiresNail ? "Выровнять по краю" : "Примагнитить";
            }

            var candidate = robot.PickupCandidate;
            return candidate ? candidate.PickupPrompt : "Взять (подойди ближе)";
        }

        private void DrawKey(string caption, Rect bounds)
        {
            Box(bounds, KeyBackground, 4f);
            GUI.Label(bounds, caption, key);
        }

        private static GUIStyle TextStyle(int size, FontStyle weight, TextAnchor alignment)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = weight,
                alignment = alignment,
                padding = new RectOffset(),
                wordWrap = false
            };
            style.normal.textColor = Ink;
            return style;
        }

        private static void Box(Rect bounds, Color color, float radius)
        {
            GUI.DrawTexture(bounds, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                true, 0f, color, 0f, radius);
        }
    }
}
