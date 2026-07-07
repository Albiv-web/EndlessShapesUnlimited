using System;
using System.Reflection;
using UnityEngine;

namespace DecoLimitLifter.AutomationEditMode
{
    internal static class AutomationUiSound
    {
        private const int SampleRate = 22050;
        private const float Volume = 0.055f;
        private static object s_addClip;
        private static object s_snapClip;

        internal static void PlayAdd()
        {
            Play(ref s_addClip, "esu-block-add", 420f, 620f, 0.045f);
        }

        internal static void PlaySnap()
        {
            Play(ref s_snapClip, "esu-block-snap", 690f, 360f, 0.065f);
        }

        private static void Play(
            ref object clip,
            string name,
            float startHz,
            float endHz,
            float seconds)
        {
            try
            {
                if (clip == null)
                    clip = CreateClip(name, startHz, endHz, seconds);
                if (clip == null)
                    return;

                Vector3 position = Vector3.zero;
                Camera camera = Camera.main;
                if (camera != null)
                    position = camera.transform.position;
                Type audioSourceType = AudioType("UnityEngine.AudioSource");
                MethodInfo playClipAtPoint = audioSourceType?.GetMethod(
                    "PlayClipAtPoint",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { clip.GetType(), typeof(Vector3), typeof(float) },
                    null);
                playClipAtPoint?.Invoke(null, new object[] { clip, position, Volume });
            }
            catch
            {
                // Audio is non-essential UI feedback; never block editor input.
            }
        }

        private static object CreateClip(
            string name,
            float startHz,
            float endHz,
            float seconds)
        {
            Type clipType = AudioType("UnityEngine.AudioClip");
            if (clipType == null)
                return null;

            int sampleCount = Math.Max(1, Mathf.RoundToInt(SampleRate * Mathf.Max(0.01f, seconds)));
            var samples = new float[sampleCount];
            double phase = 0d;
            for (int index = 0; index < samples.Length; index++)
            {
                float t = index / (float)Math.Max(1, samples.Length - 1);
                float frequency = Mathf.Lerp(startHz, endHz, t);
                phase += Math.PI * 2d * frequency / SampleRate;
                float envelope = Mathf.Sin(Mathf.PI * t);
                samples[index] = Mathf.Sin((float)phase) * envelope;
            }

            MethodInfo create = clipType.GetMethod(
                "Create",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(bool) },
                null);
            object clip = create?.Invoke(null, new object[] { name, sampleCount, 1, SampleRate, false });
            MethodInfo setData = clipType.GetMethod(
                "SetData",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float[]), typeof(int) },
                null);
            setData?.Invoke(clip, new object[] { samples, 0 });
            return clip;
        }

        private static Type AudioType(string typeName) =>
            Type.GetType(typeName + ", UnityEngine.AudioModule") ??
            Type.GetType(typeName + ", UnityEngine") ??
            Type.GetType(typeName);
    }
}
