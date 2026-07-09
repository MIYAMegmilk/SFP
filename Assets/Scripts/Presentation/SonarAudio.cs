using UnityEngine;
using SFP.Simulation;

namespace SFP.Presentation
{
    [RequireComponent(typeof(AudioSource))]
    public class SonarAudio : MonoBehaviour
    {
        AudioSource _source;
        SonarState _sonar;
        AudioClip _pingClip;
        AudioClip _returnClip;

        const float PingFrequency = 1500f;
        const float PingDuration = 0.25f;
        const float ReturnFrequency = 2200f;
        const float ReturnDuration = 0.1f;
        const int SampleRate = 44100;

        public void Init(SonarState sonar)
        {
            _sonar = sonar;
            _source = GetComponent<AudioSource>();
            _source.spatialBlend = 0.6f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 3f;
            _source.maxDistance = 30f;
            _source.playOnAwake = false;
            _source.volume = 1f;

            _pingClip = GeneratePing(PingFrequency, PingDuration, 0.9f);
            _returnClip = GeneratePing(ReturnFrequency, ReturnDuration, 0.4f);
        }

        void Update()
        {
            if (_sonar == null) return;

            if (_sonar.PingFired && _sonar.HasPower)
            {
                _sonar.PingFired = false;
                _source.PlayOneShot(_pingClip);

                if (_sonar.Contacts.Count > 0 && !_sonar.IsPassive)
                    _source.PlayOneShot(_returnClip, 0.6f);
            }
        }

        static AudioClip GeneratePing(float freq, float duration, float volume)
        {
            int sampleCount = (int)(SampleRate * duration);
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / SampleRate;
                float normalizedT = t / duration;

                float envelope = Mathf.Exp(-normalizedT * 5f);
                float sweepFreq = freq * (1f - normalizedT * 0.12f);
                float sample = Mathf.Sin(2f * Mathf.PI * sweepFreq * t) * envelope * volume;
                samples[i] = sample;
            }

            var clip = AudioClip.Create("SonarPing", sampleCount, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
