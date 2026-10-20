using System;
using System.IO;
using System.Reflection;

namespace GoDesk
{
    public static class StoneSoundTests
    {
        public static string Run()
        {
            int checks = 0;
            Action<bool, string> check = (condition, message) => { checks++; if (!condition) throw new Exception("落子音效检查失败：" + message); };
            byte[] source;
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("audio.stone.wav"))
            using (var memory = new MemoryStream()) { input.CopyTo(memory); source = memory.ToArray(); }
            byte[] muted = StoneSound.ScaleWaveform(source, 0);
            byte[] full = StoneSound.ScaleWaveform(source, 100);
            byte[] half = StoneSound.ScaleWaveform(source, 50);
            bool silent = true, equal = true, reduced = true;
            int peak = 0;
            for (int i = 44; i + 1 < source.Length; i += 2)
            {
                short sample = BitConverter.ToInt16(source, i);
                peak = Math.Max(peak, Math.Abs((int)sample));
                silent &= BitConverter.ToInt16(muted, i) == 0;
                equal &= BitConverter.ToInt16(full, i) == sample;
                reduced &= Math.Abs(BitConverter.ToInt16(half, i) - sample * .25) <= .5;
            }
            check(peak > 1000, "原始音效非空");
            check(silent, "0% 输出静音 PCM");
            check(equal, "100% 保持原始波形");
            check(reduced, "50% 音量按增益衰减，不改系统音量");
            check(BitConverter.ToInt32(source, 24) == 22050 && source.Length < 10000, "短促原生 PCM 资源");
            foreach (int invalid in new int[] { -1, 101 })
            {
                bool rejected = false;
                try { StoneSound.ScaleWaveform(source, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                check(rejected, "拒绝非法音量");
            }
            check(AudioSettings.ReadVolume("{\"Volume\":0}") == 0, "静音设置读回");
            check(AudioSettings.ReadVolume("{\"Volume\":100}") == 100, "最大音量读回");
            foreach (string invalid in new string[] { "{", "null", "{}", "{\"Volume\":101}", "{\"Volume\":-1}", "{\"Volume\":22.5}", "{\"Volume\":\"80\"}" })
                check(AudioSettings.ReadVolume(invalid) == AudioSettings.DefaultVolume, "损坏设置使用默认音量");
            using (var sound = new StoneSound(0)) { sound.Play(); sound.Preview(0); check(sound.Volume == 0, "静音播放不改当前音量"); }
            return "落子音效检查通过：" + checks + " 项断言（不播放可听音、不写偏好）。";
        }
    }
}
