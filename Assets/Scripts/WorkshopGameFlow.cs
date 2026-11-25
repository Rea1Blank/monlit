using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Monlit
{
    public enum WorkshopScreen { Main, Local, Network, Campaign, Settings, Playing, Pause, ConfirmExit, MissionComplete, RoundComplete }

    [DefaultExecutionOrder(-300), DisallowMultipleComponent, RequireComponent(typeof(WorkshopMonolith))]
    public sealed class WorkshopGameFlow : MonoBehaviour
    {
        private const string Pref = "Monlit.Workshop.";
        private static readonly int[] InterfaceScales = { 85, 100, 115 };

        private readonly struct Mission
        {
            public readonly string Title;
            public readonly WorkshopLevelOptions Level;
            public readonly int Seed;

            public Mission(string title, int blocks, int services, WorkshopDifficulty difficulty, int seed)
            {
                Title = title;
                Level = new WorkshopLevelOptions(blocks, services, difficulty);
                Seed = seed;
            }
        }

        private static readonly Mission[] Missions = {
            new Mission("Первый распил", 8, 2, WorkshopDifficulty.Junior, 1727),
            new Mission("Общие зависимости", 14, 3, WorkshopDifficulty.Middle, 4109),
            new Mission("Большая миграция", 20, 4, WorkshopDifficulty.Senior, 8903)
        };

        [SerializeField] private WorkshopRobot robot;
        [SerializeField] private GameObject deck;
        [SerializeField] private GameObject floor;
        private WorkshopMonolith monolith;
        private WorkshopHud hud;
        private WorkshopPlacementPreview placementPreview;
        private WorkshopMenuView menu;
        private WorkshopAudio sound;
        private WorkshopScreen settingsOrigin;
        private WorkshopLevelOptions localLevel;
        private int scaleIndex;
        private int missionIndex;
        private int completedMissions;
        private bool campaignRound;
        private float assembledFor;
        private bool clockPaused;
        private float previousTimeScale;
        private bool previousAudioPause;

        public WorkshopScreen Screen { get; private set; }
        public bool InRound { get; private set; }
        public int Blocks => localLevel.Blocks;
        public int Services => localLevel.Services;
        public WorkshopDifficulty Difficulty => localLevel.Difficulty;
        public bool ShowHints { get; private set; }
        public bool ShowProgress { get; private set; }
        public bool ShowPlacementPreview { get; private set; }
        public bool CarryAngleLocked { get; private set; }
        public bool MusicOn { get; private set; }
        public bool EffectsOn { get; private set; }
        public int UiScale => InterfaceScales[scaleIndex];
        public int MissionNumber => missionIndex + 1;
        public string MissionTitle => Missions[missionIndex].Title;
        public int MissionBlocks => Missions[missionIndex].Level.Blocks;
        public int MissionServices => Missions[missionIndex].Level.Services;
        public WorkshopDifficulty MissionDifficulty => Missions[missionIndex].Level.Difficulty;
        public bool CampaignCompleted => completedMissions == Missions.Length;

        private void Awake()
        {
            monolith = GetComponent<WorkshopMonolith>();
            hud = robot.GetComponent<WorkshopHud>();
            if (!robot.TryGetComponent(out placementPreview))
                placementPreview = robot.gameObject.AddComponent<WorkshopPlacementPreview>();
            var defaults = WorkshopLevelOptions.Default;
            localLevel = new WorkshopLevelOptions(PlayerPrefs.GetInt(Pref + "Blocks", defaults.Blocks),
                PlayerPrefs.GetInt(Pref + "Services", defaults.Services),
                (WorkshopDifficulty)PlayerPrefs.GetInt(Pref + "Difficulty", (int)defaults.Difficulty));
            scaleIndex = Mathf.Clamp(PlayerPrefs.GetInt(Pref + "Scale", 1), 0, InterfaceScales.Length - 1);
            ShowHints = PlayerPrefs.GetInt(Pref + "Hints", 1) != 0;
            ShowProgress = PlayerPrefs.GetInt(Pref + "Progress", 1) != 0;
            ShowPlacementPreview = PlayerPrefs.GetInt(Pref + "PlacementPreview", 1) != 0;
            placementPreview.SetDisplay(ShowPlacementPreview);
            CarryAngleLocked = PlayerPrefs.GetInt(Pref + "CarryAngleLocked", 0) != 0;
            robot.SetCarryAngleLocked(CarryAngleLocked);
            MusicOn = PlayerPrefs.GetInt(Pref + "Music", 1) != 0;
            EffectsOn = PlayerPrefs.GetInt(Pref + "Effects", 1) != 0;
            sound = gameObject.AddComponent<WorkshopAudio>();
            sound.SetPreferences(MusicOn, EffectsOn);
            completedMissions = Mathf.Clamp(PlayerPrefs.GetInt(Pref + "Missions", 0), 0, Missions.Length);
            ApplyHudPreferences();
            SetWorkshopVisible(false);
            menu = gameObject.AddComponent<WorkshopMenuView>();
            menu.Bind(this);
            Show(WorkshopScreen.Main);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (Screen != WorkshopScreen.Main) sound.Click();
                if (Screen == WorkshopScreen.Playing) Show(WorkshopScreen.Pause);
                else Back();
                return;
            }
            if (Screen == WorkshopScreen.Playing)
            {
                // Let the free bodies settle after the last piece is lowered.
                assembledFor = robot.IsDelivered ? assembledFor + Time.deltaTime : 0f;
                if (assembledFor < .22f) return;
                if (campaignRound && completedMissions < MissionNumber)
                {
                    completedMissions = MissionNumber;
                    Save("Missions", completedMissions);
                }
                var camera = Camera.main;
                if (camera && camera.TryGetComponent<WorkshopCamera>(out var workshopCamera)) workshopCamera.ShowOverview();
                Show(campaignRound ? WorkshopScreen.MissionComplete : WorkshopScreen.RoundComplete);
                sound.FinishRound();
            }
        }

        public void OpenLocal() { if (Screen == WorkshopScreen.Main) Show(WorkshopScreen.Local); }
        public void OpenNetwork() { if (Screen == WorkshopScreen.Main) Show(WorkshopScreen.Network); }

        public void OpenCampaign()
        {
            if (Screen != WorkshopScreen.Main) return;
            missionIndex = CampaignCompleted ? 0 : completedMissions;
            Show(WorkshopScreen.Campaign);
        }

        public void OpenSettings()
        {
            if (Screen != WorkshopScreen.Main && Screen != WorkshopScreen.Pause) return;
            settingsOrigin = Screen;
            Show(WorkshopScreen.Settings);
        }

        public void Back()
        {
            switch (Screen)
            {
                case WorkshopScreen.Local:
                case WorkshopScreen.Network:
                case WorkshopScreen.Campaign: Show(WorkshopScreen.Main); break;
                case WorkshopScreen.Settings: Show(settingsOrigin); break;
                case WorkshopScreen.Pause: ContinueGame(); break;
                case WorkshopScreen.ConfirmExit: Show(WorkshopScreen.Pause); break;
                case WorkshopScreen.MissionComplete:
                case WorkshopScreen.RoundComplete: ExitToMenu(); break;
            }
        }

        public void StartLocal()
        {
            if (Screen == WorkshopScreen.Local) Begin(localLevel, Guid.NewGuid().GetHashCode(), false);
        }

        public void ReplayLocal()
        {
            if (Screen == WorkshopScreen.RoundComplete) Begin(localLevel, Guid.NewGuid().GetHashCode(), false);
        }

        public void StartCampaign()
        {
            if (Screen == WorkshopScreen.Campaign) Begin(Missions[missionIndex].Level, Missions[missionIndex].Seed, true);
        }

        public void NextMission()
        {
            if (Screen != WorkshopScreen.MissionComplete || MissionNumber >= Missions.Length) return;
            missionIndex++;
            Begin(Missions[missionIndex].Level, Missions[missionIndex].Seed, true);
        }

        private void Begin(WorkshopLevelOptions level, int seed, bool campaign)
        {
            SetWorkshopVisible(true);
            try
            {
                monolith.BeginRound(level, seed);
                InRound = true;
                campaignRound = campaign;
                assembledFor = 0f;
                Show(WorkshopScreen.Playing);
                sound.StartRound();
            }
            catch
            {
                ReturnToTitle();
                throw;
            }
        }

        public void ContinueGame() { if (Screen == WorkshopScreen.Pause) Show(WorkshopScreen.Playing); }
        public void RequestExit() { if (Screen == WorkshopScreen.Pause) Show(WorkshopScreen.ConfirmExit); }
        public void ExitToMenu()
        {
            if (Screen == WorkshopScreen.ConfirmExit || Screen == WorkshopScreen.MissionComplete ||
                Screen == WorkshopScreen.RoundComplete) ReturnToTitle();
        }

        private void ReturnToTitle()
        {
            sound.StopRound();
            monolith.EndRound();
            InRound = false;
            campaignRound = false;
            SetWorkshopVisible(false);
            Show(WorkshopScreen.Main);
        }

        private void SetWorkshopVisible(bool active)
        {
            floor.SetActive(active);
            deck.SetActive(active);
            robot.gameObject.SetActive(active);
        }

        private void Show(WorkshopScreen next)
        {
            if (Screen == WorkshopScreen.Local && next != WorkshopScreen.Local) PlayerPrefs.Save();
            Screen = next;
            bool playing = next == WorkshopScreen.Playing;
            if (playing) robot.SkipMenuInputFrame();
            robot.enabled = playing;
            hud.enabled = playing;
            if (InRound && !playing)
            {
                if (!clockPaused)
                {
                    previousTimeScale = Time.timeScale;
                    previousAudioPause = AudioListener.pause;
                    clockPaused = true;
                    Time.timeScale = 0f;
                    AudioListener.pause = true;
                }
            }
            else RestoreClock();
            menu.Refresh();
        }

        private void RestoreClock()
        {
            if (!clockPaused) return;
            Time.timeScale = previousTimeScale;
            AudioListener.pause = previousAudioPause;
            clockPaused = false;
        }

        private void OnDestroy() => RestoreClock();

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && Screen == WorkshopScreen.Local) PlayerPrefs.Save();
        }

        public void ChangeBlocks(int delta) => ChangeLevel(Blocks + delta, Services, Difficulty);
        public void ChangeServices(int delta)
        {
            int services = Mathf.Clamp(Services + delta, WorkshopLevelOptions.MinServices, WorkshopLevelOptions.MaxServices);
            int blocks = Blocks == Services * 6 ? services * 6 : Blocks;
            ChangeLevel(blocks, services, Difficulty);
        }
        public void ChangeDifficulty(int delta) => ChangeLevel(Blocks, Services, (WorkshopDifficulty)((int)Difficulty + delta));

        private void ChangeLevel(int blocks, int services, WorkshopDifficulty difficulty)
        {
            if (Screen != WorkshopScreen.Local) return;
            localLevel = new WorkshopLevelOptions(blocks, services, difficulty);
            PlayerPrefs.SetInt(Pref + "Blocks", Blocks);
            PlayerPrefs.SetInt(Pref + "Difficulty", (int)localLevel.Difficulty);
            PlayerPrefs.SetInt(Pref + "Services", Services);
            menu.Refresh();
        }

        public void SetHints(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            ShowHints = value;
            Save("Hints", value ? 1 : 0);
            RefreshPreferences();
        }

        public void SetProgress(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            ShowProgress = value;
            Save("Progress", value ? 1 : 0);
            RefreshPreferences();
        }

        public void SetPlacementPreview(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            ShowPlacementPreview = value;
            placementPreview.SetDisplay(value);
            Save("PlacementPreview", value ? 1 : 0);
            menu.Refresh();
        }

        public void SetCarryAngleLocked(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            CarryAngleLocked = value;
            robot.SetCarryAngleLocked(value);
            Save("CarryAngleLocked", value ? 1 : 0);
            menu.Refresh();
        }

        internal void PlayMenuClick() => sound.Click();

        public void SetMusic(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            MusicOn = value;
            Save("Music", value ? 1 : 0);
            sound.SetPreferences(MusicOn, EffectsOn);
            menu.Refresh();
        }

        public void SetEffects(bool value)
        {
            if (Screen != WorkshopScreen.Settings) return;
            EffectsOn = value;
            Save("Effects", value ? 1 : 0);
            sound.SetPreferences(MusicOn, EffectsOn);
            menu.Refresh();
        }

        public void ChangeUiScale(int delta)
        {
            if (Screen != WorkshopScreen.Settings) return;
            scaleIndex = Mathf.Clamp(scaleIndex + delta, 0, InterfaceScales.Length - 1);
            Save("Scale", scaleIndex);
            RefreshPreferences();
        }

        private static void Save(string key, int value)
        {
            PlayerPrefs.SetInt(Pref + key, value);
            PlayerPrefs.Save();
        }

        private void RefreshPreferences()
        {
            ApplyHudPreferences();
            menu.Refresh();
        }

        private void ApplyHudPreferences() => hud.SetDisplay(ShowHints, ShowProgress, UiScale / 100f);
    }
}
