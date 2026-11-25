using UnityEngine;

namespace Monlit
{
    [DisallowMultipleComponent]
    public sealed class WorkshopAudio : MonoBehaviour
    {
        public enum Effect { Clamp, Drop, Seat, Tension, Slide, Slip, Hammer }

        private sealed class Variations
        {
            private readonly AudioClip first, second;
            private bool alternate;
            public float NextAllowed;

            public Variations(Effect effect)
            {
                string path = "Audio/Workshop/" + effect.ToString().ToLowerInvariant();
                first = Resources.Load<AudioClip>(path + "_01");
                second = Resources.Load<AudioClip>(path + "_02");
            }

            public AudioClip Next()
            {
                alternate = !alternate;
                return alternate ? first : second;
            }
        }

        private static WorkshopAudio active;
        private readonly AudioSource[] voices = new AudioSource[8];
        private Variations[] effects;
        private AudioSource music, victory, menuClick;
        private int nextVoice;
        private float nextClick;
        private bool inRound;

        private void Awake()
        {
            active = this;
            music = MakeSource("Round music");
            music.clip = Resources.Load<AudioClip>("Audio/Music/workshop-loop");
            music.loop = true;
            music.volume = .24f;
            victory = MakeSource("Victory music");
            victory.clip = Resources.Load<AudioClip>("Audio/Music/victory");
            victory.volume = .43f;
            victory.ignoreListenerPause = true;
            menuClick = MakeSource("Menu click");
            menuClick.clip = Resources.Load<AudioClip>("Audio/Workshop/ui_01");
            menuClick.volume = .32f;
            menuClick.ignoreListenerPause = true;

            var kinds = (Effect[])System.Enum.GetValues(typeof(Effect));
            effects = new Variations[kinds.Length];
            foreach (var kind in kinds) effects[(int)kind] = new Variations(kind);
            for (int i = 0; i < voices.Length; i++)
            {
                var voice = MakeSource("Workshop voice");
                voice.spatialBlend = 1f;
                voice.dopplerLevel = 0f;
                // The orthographic camera backs away on large boards. Keep the far end of a cord audible.
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 12f;
                voice.maxDistance = 130f;
                voices[i] = voice;
            }
        }

        private AudioSource MakeSource(string sourceName)
        {
            var child = new GameObject(sourceName);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }

        internal void SetPreferences(bool musicOn, bool effectsOn)
        {
            music.mute = victory.mute = !musicOn;
            menuClick.mute = !effectsOn;
            foreach (var voice in voices) voice.mute = !effectsOn;
        }

        internal void StartRound()
        {
            StopRound();
            foreach (var sound in effects) sound.NextAllowed = 0f;
            inRound = true;
            music.Play();
        }

        internal void StopRound()
        {
            inRound = false;
            music.Stop();
            victory.Stop();
            foreach (var voice in voices) voice.Stop();
        }

        internal void FinishRound()
        {
            StopRound();
            victory.Play();
        }

        internal void Click()
        {
            if (Time.unscaledTime < nextClick || menuClick.mute) return;
            nextClick = Time.unscaledTime + .055f;
            menuClick.Play();
        }

        public static void PlayTension(Vector3 position) => Play(Effect.Tension, position, .66f);
        public static void PlaySlide(Vector3 position, float intensity) =>
            Play(Effect.Slide, position, Mathf.Lerp(.24f, .60f, Mathf.Clamp01(intensity)));
        public static void PlaySlip(Vector3 position) => Play(Effect.Slip, position, .83f);

        public static void Play(Effect effect, Vector3 position, float volume = .78f)
        {
            if (!active || !active.inRound || AudioListener.pause) return;
            var sound = active.effects[(int)effect];
            if (Time.time < sound.NextAllowed) return;
            sound.NextAllowed = Time.time + .07f;
            var voice = active.voices[active.nextVoice];
            active.nextVoice = (active.nextVoice + 1) % active.voices.Length;
            voice.transform.position = position;
            voice.clip = sound.Next();
            voice.volume = volume;
            voice.Play();
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
        }
    }
}
