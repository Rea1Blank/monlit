using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monlit
{
    [DisallowMultipleComponent]
    public sealed class WorkshopMenuView : MonoBehaviour
    {
        private enum Shape { Action, Primary, Back, Minus, Plus, Toggle, Difficulty }
        private enum Symbol { None, Robot, Network, Sliders, Play, Exit }

        private sealed class Control
        {
            public string Id, Label;
            public Rect Bounds;
            public Shape Shape;
            public Symbol Symbol;
            public bool Enabled, Value;
            public Action Click;
        }

        private struct ValueRow
        {
            public Rect Bounds;
            public string Label, Value;
            public string RangeId;
            public int Number, Minimum, Maximum;
            public Action<int> Change;
            public Rect Track => new Rect(17, Bounds.y + 47, Width - 34, 22);
        }

        private static readonly Color Ink = new Color32(248, 244, 226, 255);
        private static readonly Color SoftInk = new Color32(196, 208, 200, 255);
        private static readonly Color Amber = new Color32(248, 185, 91, 255);
        private static readonly Color Mint = new Color32(150, 218, 173, 255);
        private static readonly Color Graphite = new Color32(20, 36, 39, 255);
        private static readonly Color Tile = new Color32(27, 43, 45, 246);
        private static readonly Color HotTile = new Color32(54, 75, 73, 255);
        private static readonly Color Edge = new Color32(82, 102, 97, 255);
        private static readonly Color DisabledInk = new Color32(115, 133, 128, 255);
        private const float Width = 386f;

        private readonly List<Control> controls = new List<Control>();
        private readonly List<ValueRow> rows = new List<ValueRow>();
        private readonly Dictionary<WorkshopScreen, string> focusHistory = new Dictionary<WorkshopScreen, string>();
        private WorkshopGameFlow flow;
        private WorkshopScreen lastScreen;
        private Rect safe;
        private Vector2 origin;
        private float scale, contentHeight;
        private bool modal;
        private int focus = -1;
        private string pressedId;
        private string draggingRange;
        private Vector2 lastPointer;
        private Texture2D background, shade;
        private Font bodyFont, titleFont;
        private GUIStyle text;

        public void Bind(WorkshopGameFlow game)
        {
            flow = game;
            background = Resources.Load<Texture2D>("WorkshopMenu/TitleArt");
            bodyFont = Resources.Load<Font>("WorkshopMenu/Roboto-Regular");
            titleFont = Resources.Load<Font>("WorkshopMenu/Roboto-Black");
            if (!shade)
            {
                shade = new Texture2D(128, 1, TextureFormat.RGBA32, false);
                shade.wrapMode = TextureWrapMode.Clamp;
                shade.hideFlags = HideFlags.HideAndDontSave;
                for (int i = 0; i < shade.width; i++)
                {
                    float opacity = 1f - Mathf.SmoothStep(0f, 1f, (i / 127f - .12f) / .8f);
                    shade.SetPixel(i, 0, new Color(.025f, .065f, .073f, opacity * .77f));
                }
                shade.Apply(false, true);
            }
            Refresh();
        }

        private void OnDestroy()
        {
            if (shade) Destroy(shade);
        }

        private void OnApplicationFocus(bool active)
        {
            if (!active) pressedId = draggingRange = null;
        }

        public void Refresh()
        {
            if (!flow) return;
            if (focus >= 0 && focus < controls.Count) focusHistory[lastScreen] = controls[focus].Id;
            focusHistory.TryGetValue(flow.Screen, out string oldFocus);
            if (lastScreen != flow.Screen)
            {
                pressedId = draggingRange = null;
                if (flow.Screen == WorkshopScreen.ConfirmExit || flow.Screen == WorkshopScreen.MissionComplete ||
                    flow.Screen == WorkshopScreen.RoundComplete ||
                    (flow.Screen == WorkshopScreen.Pause && lastScreen == WorkshopScreen.Playing))
                    oldFocus = null;
            }
            lastScreen = flow.Screen;
            controls.Clear();
            rows.Clear();
            safe = Screen.safeArea;
            if (safe.width < 1f || safe.height < 1f) safe = new Rect(0f, 0f, Screen.width, Screen.height);
            scale = Mathf.Max(.1f, Mathf.Min(safe.height / 760f * flow.UiScale / 100f, safe.width / (Width + 70f)));
            modal = flow.InRound;
            switch (lastScreen)
            {
                case WorkshopScreen.Main:
                    contentHeight = 371f;
                    Button("main.local", "Локальная игра", 178, flow.OpenLocal, Symbol.Robot, true);
                    Button("main.network", "Сетевая игра", 242, flow.OpenNetwork, Symbol.Network);
                    Button("main.settings", "Настройки", 306, flow.OpenSettings, Symbol.Sliders);
                    break;
                case WorkshopScreen.Local:
                    contentHeight = 448f;
                    DifficultyButton("local.junior", WorkshopDifficulty.Junior, 0);
                    DifficultyButton("local.middle", WorkshopDifficulty.Middle, 131);
                    DifficultyButton("local.senior", WorkshopDifficulty.Senior, 262);
                    Range("local.services", "Микросервисы", 211, flow.Services, WorkshopLevelOptions.MinServices,
                        WorkshopLevelOptions.MaxServices, flow.ChangeServices);
                    Range("local.blocks", "Детали", 295, flow.Blocks, WorkshopLevelOptions.MinBlocksFor(flow.Services),
                        WorkshopLevelOptions.MaxBlocks, flow.ChangeBlocks);
                    Button("local.start", "Начать игру", 386, flow.StartLocal, Symbol.Play, true);
                    BackButton();
                    break;
                case WorkshopScreen.Settings:
                    contentHeight = 548f;
                    Toggle("settings.hints", "Подсказки управления", flow.ShowHints, 87, () => flow.SetHints(!flow.ShowHints));
                    Toggle("settings.progress", "Счётчики сборки", flow.ShowProgress, 151, () => flow.SetProgress(!flow.ShowProgress));
                    Toggle("settings.projection", "Проекция детали", flow.ShowPlacementPreview, 215,
                        () => flow.SetPlacementPreview(!flow.ShowPlacementPreview));
                    Toggle("settings.carryAngle", "Переноска без автоповорота", flow.CarryAngleLocked, 279,
                        () => flow.SetCarryAngleLocked(!flow.CarryAngleLocked));
                    Stepper("settings.scale", "Масштаб интерфейса", flow.UiScale + "%", 343, flow.ChangeUiScale,
                        flow.UiScale > 85, flow.UiScale < 115);
                    Toggle("settings.music", "Музыка", flow.MusicOn, 407, () => flow.SetMusic(!flow.MusicOn));
                    Toggle("settings.effects", "Звуковые эффекты", flow.EffectsOn, 471, () => flow.SetEffects(!flow.EffectsOn));
                    BackButton();
                    break;
                case WorkshopScreen.Network:
                    contentHeight = 202f;
                    BackButton();
                    break;
                case WorkshopScreen.Campaign:
                    contentHeight = 347f;
                    Button("campaign.start", flow.CampaignCompleted ? "Пройти ещё раз" : "Начать задание", 282, flow.StartCampaign, Symbol.Play, true);
                    BackButton();
                    break;
                case WorkshopScreen.Pause:
                    contentHeight = 279f;
                    Button("pause.resume", "Продолжить", 76, flow.ContinueGame, Symbol.Play, true);
                    Button("pause.settings", "Настройки", 140, flow.OpenSettings, Symbol.Sliders);
                    Button("pause.exit", "Выход", 204, flow.RequestExit, Symbol.Exit);
                    break;
                case WorkshopScreen.ConfirmExit:
                    contentHeight = 265f;
                    Button("exit.cancel", "Назад к паузе", 120, flow.Back, Symbol.Play, true);
                    Button("exit.confirm", "Выйти в меню", 184, flow.ExitToMenu, Symbol.Exit);
                    break;
                case WorkshopScreen.RoundComplete:
                    contentHeight = 264f;
                    Button("round.replay", "Новый монолит", 118, flow.ReplayLocal, Symbol.Play, true);
                    Button("round.menu", "В главное меню", 182, flow.ExitToMenu, Symbol.Exit);
                    break;
                case WorkshopScreen.MissionComplete:
                    bool last = flow.MissionNumber >= 3;
                    contentHeight = last ? 200f : 264f;
                    if (!last) Button("mission.next", "Следующее задание", 118, flow.NextMission, Symbol.Play, true);
                    Button("mission.menu", "В главное меню", last ? 118 : 182, flow.ExitToMenu, Symbol.Exit, last);
                    break;
            }
            float localWidth = safe.width / scale;
            // On wide screens keep the completed figures visible on the right.
            bool centered = modal && (!ResultScreen || localWidth < 800f);
            float left = centered ? (localWidth - Width) * .5f : Mathf.Clamp(localWidth * .075f, 22f, 112f);
            float top = (safe.height / scale - contentHeight) * .5f + (HasBack() ? 20f : 0f);
            origin = new Vector2(safe.xMin + left * scale, Screen.height - safe.yMax + top * scale);
            focus = controls.FindIndex(c => c.Id == oldFocus);
            if (focus >= 0 && !controls[focus].Enabled)
            {
                float y = controls[focus].Bounds.y;
                focus = controls.FindIndex(c => c.Enabled && c.Bounds.y == y);
            }
            if (focus < 0 && lastScreen == WorkshopScreen.Local)
                focus = controls.FindIndex(c => c.Shape == Shape.Difficulty && c.Value);
            if (focus < 0) focus = controls.FindIndex(c => c.Enabled);
        }

        private bool ResultScreen => lastScreen == WorkshopScreen.RoundComplete || lastScreen == WorkshopScreen.MissionComplete;

        private static string DifficultyName(WorkshopDifficulty difficulty) => difficulty switch
        {
            WorkshopDifficulty.Junior => "Джуниор",
            WorkshopDifficulty.Senior => "Сеньор",
            _ => "Мидл"
        };

        private static string DifficultyDescription(WorkshopDifficulty difficulty) => difficulty switch
        {
            WorkshopDifficulty.Junior => "Уложи свободные блоки в подходящие формы.",
            WorkshopDifficulty.Senior => "Следи за верёвками. Перекрашенные детали скользят, пока их не прибьёшь (R).",
            _ => "Когда верёвка натянется, соседний блок поедет следом."
        };

        private void DifficultyButton(string id, WorkshopDifficulty difficulty, float x)
        {
            controls.Add(new Control { Id = id, Label = DifficultyName(difficulty), Bounds = new Rect(x, 107, 124, 44),
                Shape = Shape.Difficulty, Value = flow.Difficulty == difficulty, Enabled = true,
                Click = () => flow.ChangeDifficulty((int)difficulty - (int)flow.Difficulty) });
        }

        private bool HasBack() => lastScreen == WorkshopScreen.Local || lastScreen == WorkshopScreen.Network ||
            lastScreen == WorkshopScreen.Campaign || lastScreen == WorkshopScreen.Settings;

        private void Button(string id, string label, float y, Action action, Symbol symbol, bool primary = false)
        {
            controls.Add(new Control { Id = id, Label = label, Bounds = new Rect(0, y, Width, 55), Click = action,
                Symbol = symbol, Shape = primary ? Shape.Primary : Shape.Action, Enabled = true });
        }

        private void BackButton()
        {
            controls.Add(new Control { Id = "back", Label = "Назад", Bounds = new Rect(-4, -44, 103, 32),
                Shape = Shape.Back, Click = flow.Back, Enabled = true });
        }

        private void Toggle(string id, string label, bool value, float y, Action action)
        {
            controls.Add(new Control { Id = id, Label = label, Bounds = new Rect(0, y, Width, 55),
                Shape = Shape.Toggle, Click = action, Enabled = true, Value = value });
        }

        private void Stepper(string id, string label, string value, float y, Action<int> change, bool down, bool up)
        {
            rows.Add(new ValueRow { Bounds = new Rect(0, y, Width, 55), Label = label, Value = value });
            StepButtons(id, y, change, down, up);
        }

        private void Range(string id, string label, float y, int value, int minimum, int maximum, Action<int> change)
        {
            rows.Add(new ValueRow { Bounds = new Rect(0, y, Width, 74), Label = label, Value = value.ToString(),
                RangeId = id, Number = value, Minimum = minimum, Maximum = maximum, Change = change });
            StepButtons(id, y, change, value > minimum, value < maximum);
        }

        private void StepButtons(string id, float y, Action<int> change, bool down, bool up)
        {
            controls.Add(new Control { Id = id + ".minus", Bounds = new Rect(Width - 137, y + 7, 34, 41),
                Shape = Shape.Minus, Click = () => change(-1), Enabled = down });
            controls.Add(new Control { Id = id + ".plus", Bounds = new Rect(Width - 46, y + 7, 34, 41),
                Shape = Shape.Plus, Click = () => change(1), Enabled = up });
        }

        // Mouse and touch use bottom-left screen coordinates in the Input System.
        internal Rect GetControlRect(string id)
        {
            var c = controls.Find(item => item.Id == id);
            if (c == null) return Rect.zero;
            var r = c.Bounds;
            return new Rect(origin.x + r.x * scale, Screen.height - origin.y - r.yMax * scale, r.width * scale, r.height * scale);
        }

        private void Update()
        {
            if (!flow || flow.Screen == WorkshopScreen.Playing) return;
            if (Screen.safeArea != safe) Refresh();
            if (controls.Count == 0) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) return; // The game flow owns Back/Escape.
                if (keyboard.tabKey.wasPressedThisFrame) Navigate(keyboard.shiftKey.isPressed ? -1 : 1, false);
                else if (keyboard.downArrowKey.wasPressedThisFrame) Navigate(1, true);
                else if (keyboard.upArrowKey.wasPressedThisFrame) Navigate(-1, true);
                else if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                    Adjust(keyboard.rightArrowKey.wasPressedThisFrame ? Shape.Plus : Shape.Minus);
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
                {
                    if (focus >= 0) Activate(controls[focus]);
                    return;
                }
            }
            var touch = Touchscreen.current?.primaryTouch;
            if (touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame))
                Pointer(touch.position.ReadValue(), touch.press.wasPressedThisFrame, touch.press.wasReleasedThisFrame);
            else if (Mouse.current != null)
                Pointer(Mouse.current.position.ReadValue(), Mouse.current.leftButton.wasPressedThisFrame, Mouse.current.leftButton.wasReleasedThisFrame);
        }

        private void Navigate(int direction, bool skipSameRow)
        {
            int count = controls.Count;
            for (int i = 1; i <= count; i++)
            {
                int index = (focus + count + direction * i) % count;
                if (!controls[index].Enabled) continue;
                if (skipSameRow && focus >= 0 && controls[index].Bounds.y == controls[focus].Bounds.y) continue;
                if (skipSameRow && controls[index].Shape == Shape.Difficulty && !controls[index].Value) continue;
                focus = index;
                return;
            }
        }

        private void Adjust(Shape direction)
        {
            if (focus < 0) return;
            var current = controls[focus];
            if (current.Shape == Shape.Difficulty)
            {
                int next = focus + (direction == Shape.Plus ? 1 : -1);
                if (next >= 0 && next < controls.Count && controls[next].Shape == Shape.Difficulty)
                {
                    focus = next;
                    Activate(controls[next]);
                }
                return;
            }
            if (current.Shape == Shape.Toggle)
            {
                if (current.Value != (direction == Shape.Plus)) Activate(current);
                return;
            }
            if (current.Shape != Shape.Minus && current.Shape != Shape.Plus) return;
            var target = controls.Find(c => c.Shape == direction && c.Bounds.y == current.Bounds.y && c.Enabled);
            if (target != null) Activate(target);
        }

        private void Activate(Control control)
        {
            if (!control.Enabled) return;
            control.Click();
            flow.PlayMenuClick();
        }

        private void Pointer(Vector2 position, bool down, bool up)
        {
            if (!down && !up && lastPointer == position) return;
            lastPointer = position;
            Vector2 local = (new Vector2(position.x, Screen.height - position.y) - origin) / scale;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.RangeId == null) continue;
                var track = row.Track;
                if (draggingRange != row.RangeId && !(down && track.Contains(local))) continue;
                pressedId = null;
                draggingRange = up ? null : row.RangeId;
                int value = Mathf.RoundToInt(Mathf.Lerp(row.Minimum, row.Maximum, Mathf.InverseLerp(track.xMin, track.xMax, local.x)));
                if (value != row.Number)
                {
                    row.Change(value - row.Number);
                    flow.PlayMenuClick();
                }
                return;
            }
            int hit = controls.FindIndex(c => c.Enabled && c.Bounds.Contains(local));
            if (hit >= 0) focus = hit;
            if (down) pressedId = hit >= 0 ? controls[hit].Id : null;
            if (!up) return;
            string press = pressedId;
            pressedId = null;
            if (hit >= 0 && press == controls[hit].Id) Activate(controls[hit]);
        }

        private void OnGUI()
        {
            if (!flow || flow.Screen == WorkshopScreen.Playing || Event.current.type != EventType.Repaint) return;
            if (safe != Screen.safeArea) Refresh();
            if (text == null) text = new GUIStyle(GUI.skin.label) { padding = new RectOffset(), clipping = TextClipping.Clip };
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            int savedDepth = GUI.depth;
            GUI.depth = -50;
            GUI.color = Color.white;
            var viewport = new Rect(0, 0, Screen.width, Screen.height);
            if (modal) RectFill(viewport, new Color(.025f, .06f, .07f, ResultScreen ? .26f : .69f));
            else
            {
                RectFill(viewport, Graphite);
                if (background) DrawBackdrop(viewport);
                GUI.DrawTexture(viewport, shade);
            }
            GUI.matrix = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, scale, 1));
            if (modal)
            {
                float top = HasBack() ? -64 : -35;
                var panel = new Rect(-31, top, Width + 62, contentHeight - top + 14);
                RectFill(panel, Edge, 7);
                RectFill(Pad(panel, 1), Graphite, 6);
                RectFill(new Rect(0, top, 54, 3), ResultScreen ? Mint : Amber);
            }
            DrawPage();
            foreach (var row in rows)
            {
                Frame(row.Bounds, Tile, Edge);
                Label(new Rect(16, row.Bounds.y, Width - 163, 55), row.Label, 18, Ink);
                Label(new Rect(Width - 105, row.Bounds.y, 60, 55), row.Value, 20, Ink, true, TextAnchor.MiddleCenter);
                if (row.RangeId != null)
                {
                    Rect track = row.Track;
                    float t = Mathf.InverseLerp(row.Minimum, row.Maximum, row.Number);
                    float y = track.center.y;
                    RectFill(new Rect(track.x, y - 2, track.width, 4), Edge, 2);
                    RectFill(new Rect(track.x, y - 2, track.width * t, 4), Mint, 2);
                    RectFill(new Rect(track.x + track.width * t - 6, y - 6, 12, 12), Mint, 6);
                }
            }
            for (int i = 0; i < controls.Count; i++) DrawControl(controls[i], i == focus);
            if (!modal && lastScreen == WorkshopScreen.Main && safe.height / scale > 520f)
            {
                float footerY = (safe.height / scale - contentHeight) * .5f + contentHeight - 53f;
                Keycap(new Rect(0, footerY, 44, 24), "↑  ↓");
                Label(new Rect(52, footerY, 71, 24), "Выбор", 14, SoftInk);
                Keycap(new Rect(122, footerY, 51, 24), "Enter");
                Label(new Rect(181, footerY, 90, 24), "Открыть", 14, SoftInk);
            }
            GUI.color = savedColor;
            GUI.matrix = savedMatrix;
            GUI.depth = savedDepth;
        }

        private void DrawBackdrop(Rect viewport)
        {
            float ratio = (viewport.width / viewport.height) / ((float)background.width / background.height);
            // Keep the robot just clear of the menu on narrower desktop windows.
            Rect uv = ratio < 1f ? new Rect((1f - ratio) * .15f, 0f, ratio, 1f) :
                new Rect(0f, (1f - 1f / ratio) * .5f, 1f, 1f / ratio);
            GUI.DrawTextureWithTexCoords(viewport, background, uv, false);
        }

        private void DrawPage()
        {
            switch (lastScreen)
            {
                case WorkshopScreen.Main:
                    // The three pieces form the workshop mark and reuse its casting finishes.
                    RectFill(new Rect(1, 0, 25, 8), new Color32(235,128,91,255));
                    RectFill(new Rect(30, 0, 25, 8), new Color32(85,178,221,255));
                    RectFill(new Rect(59, 0, 25, 8), new Color32(166,207,111,255));
                    var heading = new Rect(-4, 17, Width + 66, 96);
                    Label(heading, "МОНОЛИТ", 81, Ink, true);
                    Label(new Rect(2, 112, Width + 15, 34), "Раздели монолит на микросервисы", 22, SoftInk);
                    break;
                case WorkshopScreen.Local:
                    Heading("Локальная игра");
                    Label(new Rect(0, 70, Width, 26), "Сложность", 18, Ink, true);
                    Label(new Rect(1, 155, Width - 2, 51), DifficultyDescription(flow.Difficulty), 17, SoftInk, wrap:true);
                    break;
                case WorkshopScreen.Settings: Heading("Настройки"); break;
                case WorkshopScreen.Pause: Heading("Пауза"); break;
                case WorkshopScreen.Network:
                    Heading("Сетевая игра");
                    Rule(78);
                    Label(new Rect(0, 94, Width, 72), "Совместная игра по сети появится позже.", 21, SoftInk, wrap:true);
                    break;
                case WorkshopScreen.Campaign:
                    Heading("Кампания");
                    Rule(78);
                    Label(new Rect(0, 98, Width, 25), $"Задание {flow.MissionNumber} из 3   ·   {DifficultyName(flow.MissionDifficulty)}", 16, Amber);
                    Label(new Rect(0, 126, Width, 56), flow.MissionTitle, 31, Ink, true);
                    Label(new Rect(0, 181, Width, 28), $"Блоки   {flow.MissionBlocks}       Микросервисы   {flow.MissionServices}", 18, SoftInk);
                    Label(new Rect(0, 217, Width, 50), DifficultyDescription(flow.MissionDifficulty), 17, SoftInk, wrap:true);
                    break;
                case WorkshopScreen.ConfirmExit:
                    Heading("Выйти в меню?");
                    Label(new Rect(1, 61, Width, 48), "Прогресс этого раунда будет потерян.", 19, SoftInk, wrap:true);
                    break;
                case WorkshopScreen.RoundComplete:
                    Heading("Победа!");
                    Label(new Rect(1, 62, Width, 35), "Все микросервисы собраны.", 22, SoftInk);
                    break;
                case WorkshopScreen.MissionComplete:
                    Heading("Задание выполнено", 38);
                    Label(new Rect(1, 62, Width, 35), flow.MissionTitle, 23, SoftInk);
                    break;
            }
        }

        private void Heading(string title, int size = 43) => Label(new Rect(-2, 0, Width + 6, 58), title, size, Ink, true);
        private static void Rule(float top) => RectFill(new Rect(0, top, Width, 1), Edge);

        private void DrawControl(Control c, bool selected)
        {
            var r = c.Bounds;
            bool hot = selected && c.Enabled;
            bool primary = c.Shape == Shape.Primary;
            Color fg = !c.Enabled ? DisabledInk : primary ? Graphite : Ink;
            if (c.Shape == Shape.Difficulty)
            {
                Frame(r, hot || c.Value ? HotTile : Tile, hot ? Amber : Edge);
                if (c.Value) RectFill(new Rect(r.x + 12, r.yMax - 3, r.width - 24, 3), Amber);
                Label(r, c.Label, 18, c.Value ? Amber : Ink, c.Value, TextAnchor.MiddleCenter);
                return;
            }
            if (c.Shape == Shape.Back)
            {
                if (hot) RectFill(r, HotTile, 4);
                Chevron(new Vector2(r.x + 13, r.center.y), -1, SoftInk);
                Label(new Rect(r.x + 27, r.y, r.width - 30, r.height), c.Label, 16, SoftInk);
                return;
            }
            Color fill = primary ? Amber : hot ? HotTile : Tile;
            if (pressedId == c.Id) fill = Color.Lerp(fill, Graphite, .16f);
            Frame(r, fill, hot ? (primary ? Ink : Amber) : primary ? Amber : Edge);
            if (c.Shape == Shape.Minus || c.Shape == Shape.Plus)
            {
                var p = r.center;
                RectFill(new Rect(p.x - 6, p.y - 1, 12, 2), fg);
                if (c.Shape == Shape.Plus) RectFill(new Rect(p.x - 1, p.y - 6, 2, 12), fg);
                return;
            }
            if (c.Shape == Shape.Toggle)
            {
                Label(new Rect(r.x + 16, r.y, r.width - 95, r.height), c.Label, 18, fg);
                var track = new Rect(r.xMax - 63, r.center.y - 10, 43, 20);
                RectFill(track, c.Value ? Amber : Edge, 10);
                RectFill(new Rect(track.x + (c.Value ? 26 : 3), track.y + 3, 14, 14), c.Value ? Graphite : Ink, 7);
                return;
            }
            DrawSymbol(c.Symbol, new Vector2(r.x + 28, r.center.y), fg);
            Label(new Rect(r.x + 56, r.y, r.width - 98, r.height), c.Label, 21, fg);
            Chevron(new Vector2(r.xMax - 24, r.center.y), 1, primary || hot ? fg : DisabledInk);
        }

        private static void DrawSymbol(Symbol symbol, Vector2 at, Color ink)
        {
            void Stroke(float x1,float y1,float x2,float y2) => Line(at + new Vector2(x1,y1), at + new Vector2(x2,y2), 2, ink);
            void Dot(float x,float y,float w,float h) => RectFill(new Rect(at.x+x,at.y+y,w,h),ink,1);
            switch (symbol)
            {
                case Symbol.Robot:
                    Stroke(-9,-5,9,-5); Stroke(-9,7,9,7); Stroke(-9,-5,-9,7); Stroke(9,-5,9,7);
                    Stroke(0,-9,0,-5); Dot(-5,-1,3,4); Dot(2,-1,3,4); break;
                case Symbol.Network:
                    Stroke(0,-7,0,0); Stroke(-7,0,7,0); Stroke(-7,0,-7,5); Stroke(7,0,7,5);
                    Dot(-3,-10,6,6); Dot(-10,4,6,6); Dot(4,4,6,6); break;
                case Symbol.Sliders:
                    Stroke(-9,-5,9,-5); Stroke(-9,5,9,5); Dot(-5,-8,4,7); Dot(3,2,4,7); break;
                case Symbol.Play:
                    Stroke(-5,-8,7,0); Stroke(7,0,-5,8); Stroke(-5,8,-5,-8); break;
                case Symbol.Exit:
                    Stroke(0,-8,-7,-8); Stroke(-7,-8,-7,8); Stroke(-7,8,0,8); Stroke(-2,0,9,0);
                    Chevron(at + new Vector2(7,0), 1, ink); break;
            }
        }

        private void Keycap(Rect r, string value)
        {
            Frame(r, Tile, Edge);
            Label(r, value, 13, SoftInk, false, TextAnchor.MiddleCenter);
        }

        private void Label(Rect r, string value, int size, Color color, bool bold = false,
            TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            text.font = bold ? titleFont : bodyFont;
            text.fontStyle = bold && !titleFont ? FontStyle.Bold : FontStyle.Normal;
            text.fontSize = size;
            text.wordWrap = wrap;
            text.alignment = align;
            text.normal.textColor = color;
            GUI.Label(r, value, text);
        }

        private static void Chevron(Vector2 at, int direction, Color color)
        {
            Vector2 tip = at + new Vector2(3 * direction,0);
            Line(at + new Vector2(-2 * direction,-5), tip, 2, color);
            Line(tip, at + new Vector2(-2 * direction,5), 2, color);
        }

        private static void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            var matrix = GUI.matrix;
            Vector2 delta = b - a;
            GUI.matrix = matrix * Matrix4x4.TRS(a,
                Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
            RectFill(new Rect(0, -width * .5f, delta.magnitude, width), color);
            GUI.matrix = matrix;
        }

        private static void Frame(Rect rect, Color fill, Color border)
        {
            RectFill(rect, border, 4);
            RectFill(Pad(rect, 1), fill, 3);
        }

        private static Rect Pad(Rect r, float px) => new Rect(r.x + px, r.y + px, r.width - 2 * px, r.height - 2 * px);

        private static void RectFill(Rect bounds, Color color, float radius = 0f) =>
            GUI.DrawTexture(bounds, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
    }
}
