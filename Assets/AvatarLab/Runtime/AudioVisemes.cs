using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarLab
{
    [Serializable] public sealed class VowelTemplate { public string vowel; public float[] features; }
    [Serializable] public sealed class VowelProfile { public List<VowelTemplate> templates = new List<VowelTemplate>(); }
    public static class VoiceFeatures
    {
        public const int Size = 1024;
        public static float[] Extract(float[] samples, int sampleRate, out float rms)
        {
            if (samples == null || samples.Length < Size || sampleRate < 8000) throw new ArgumentException("Janela de áudio inválida.");
            var real = new double[Size]; var imag = new double[Size]; double sum = 0;
            for (int i = 0; i < Size; i++)
            {
                float sample = float.IsNaN(samples[i]) || float.IsInfinity(samples[i]) ? 0 : samples[i]; sum += sample * sample;
                real[i] = (sample - (i > 0 ? .97 * samples[i - 1] : 0)) * (.54 - .46 * Math.Cos(2 * Math.PI * i / (Size - 1)));
            }
            rms = (float)Math.Sqrt(sum / Size);
            for (int i = 1, j = 0; i < Size; i++)
            {
                int bit = Size >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { double swap = real[i]; real[i] = real[j]; real[j] = swap; }
            }
            for (int len = 2; len <= Size; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                for (int start = 0; start < Size; start += len)
                {
                    double wr = 1, wi = 0, stepR = Math.Cos(angle), stepI = Math.Sin(angle);
                    for (int j = 0; j < len / 2; j++)
                    {
                        int a = start + j, b = a + len / 2; double tr = wr * real[b] - wi * imag[b], ti = wr * imag[b] + wi * real[b];
                        real[b] = real[a] - tr; imag[b] = imag[a] - ti; real[a] += tr; imag[a] += ti;
                        double next = wr * stepR - wi * stepI; wi = wr * stepI + wi * stepR; wr = next;
                    }
                }
            }
            const int filters = 26, coefficients = 12; var energies = new double[filters];
            double minMel = Mel(80), maxMel = Mel(Math.Min(8000, sampleRate * .48));
            var bins = new int[filters + 2];
            for (int i = 0; i < bins.Length; i++) bins[i] = Math.Min(Size / 2, Math.Max(0, (int)((Size + 1) * Hz(minMel + (maxMel - minMel) * i / (filters + 1)) / sampleRate)));
            for (int m = 1; m <= filters; m++)
            {
                double energy = 0;
                for (int k = bins[m - 1]; k < bins[m]; k++) energy += (real[k] * real[k] + imag[k] * imag[k]) * (k - bins[m - 1]) / Math.Max(1, bins[m] - bins[m - 1]);
                for (int k = bins[m]; k < bins[m + 1]; k++) energy += (real[k] * real[k] + imag[k] * imag[k]) * (bins[m + 1] - k) / Math.Max(1, bins[m + 1] - bins[m]);
                energies[m - 1] = Math.Log(Math.Max(1e-12, energy));
            }
            var output = new float[coefficients];
            for (int c = 1; c <= coefficients; c++) for (int m = 0; m < filters; m++) output[c - 1] += (float)(energies[m] * Math.Cos(Math.PI * c * (m + .5) / filters));
            return output;
        }
        static double Mel(double hz) => 2595 * Math.Log10(1 + hz / 700);
        static double Hz(double mel) => 700 * (Math.Pow(10, mel / 2595) - 1);
        public static int Classify(float[] features, VowelProfile profile)
        {
            int winner = -1; double best = double.MaxValue;
            for (int i = 0; i < profile.templates.Count; i++)
            {
                var target = profile.templates[i].features; if (target == null || target.Length != features.Length) continue;
                double distance = 0; for (int k = 0; k < features.Length; k++) { double d = features[k] - target[k]; distance += d * d / (1 + k * .15); }
                if (distance < best) { best = distance; winner = i; }
            }
            return winner;
        }
    }
    public sealed class AudioVisemes : IDisposable
    {
        public VowelProfile profile = new VowelProfile();
        public readonly float[] vowels = new float[5];
        public float threshold = .008f, gain = 12, openness;
        public string Device, Status = "Microfone desligado. Calibre A/E/I/O/U com sua voz.";
        public bool Running => clip != null;
        AudioClip clip;
        float[] interleaved;
        readonly float[] samples = new float[VoiceFeatures.Size];
        float next, calibrationEnd;
        string calibrating;
        float[] sum;
        int count;
        public void Start(string device)
        {
            Dispose(); Device = string.IsNullOrEmpty(device) ? null : device;
            try { clip = Microphone.Start(Device, true, 2, 48000); if (clip == null) throw new Exception("Microfone indisponível."); interleaved = new float[VoiceFeatures.Size * clip.channels]; Status = "Microfone ativo. Calibre as vogais."; }
            catch (Exception ex) { Status = ex.Message; clip = null; }
        }
        public void Calibrate(string vowel)
        {
            if (!Running) { Status = "Inicie o microfone primeiro."; return; }
            calibrating = vowel; calibrationEnd = Time.unscaledTime + 2; sum = new float[12]; count = 0; Status = "Diga e sustente “" + vowel + "” por 2 segundos.";
        }
        public void Pump()
        {
            if (clip == null || Time.unscaledTime < next) return; next = Time.unscaledTime + 1f / 30;
            int position = Microphone.GetPosition(Device); if (position < VoiceFeatures.Size) return;
            if (!clip.GetData(interleaved, (position - VoiceFeatures.Size + clip.samples) % clip.samples)) return;
            for (int i = 0; i < samples.Length; i++) { float v = 0; for (int c = 0; c < clip.channels; c++) v += interleaved[i * clip.channels + c]; samples[i] = v / clip.channels; }
            Analyze(samples, clip.frequency);
        }
        public void Analyze(float[] audio, int sampleRate)
        {
            var features = VoiceFeatures.Extract(audio, sampleRate, out float rms);
            float target = Mathf.Clamp01((rms - threshold) * gain); openness = Mathf.Lerp(openness, target, .6f);
            if (calibrating != null)
            {
                if (rms > threshold) { for (int i = 0; i < sum.Length; i++) sum[i] += features[i]; count++; }
                if (Time.unscaledTime >= calibrationEnd)
                {
                    if (count > 3)
                    {
                        for (int i = 0; i < sum.Length; i++) sum[i] /= count;
                        profile.templates.RemoveAll(t => t.vowel == calibrating); profile.templates.Add(new VowelTemplate { vowel = calibrating, features = sum });
                        Status = "Vogal “" + calibrating + "” calibrada. Salve o perfil facial para guardar a calibração.";
                    }
                    else Status = "Sem voz suficiente; ajuste o limiar e repita a calibração.";
                    calibrating = null;
                }
            }
            int winner = profile.templates.Count >= 5 && target > 0 ? VoiceFeatures.Classify(features, profile) : -1;
            for (int i = 0; i < vowels.Length; i++)
            {
                string vowel = "AEIOU"[i].ToString(); float value = winner >= 0 && profile.templates[winner].vowel == vowel ? openness : 0;
                vowels[i] = Mathf.Lerp(vowels[i], value, .65f);
            }
        }
        public void Dispose()
        {
            if (clip != null) { Microphone.End(Device); UnityEngine.Object.Destroy(clip); clip = null; }
            openness = 0; Array.Clear(vowels, 0, vowels.Length);
        }
    }
}
