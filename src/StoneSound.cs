using System;
using System.IO;
using System.Media;
using System.Reflection;

namespace GoDesk
{
    public sealed class StoneSound : IDisposable
    {
        private readonly byte[] waveform;
        private readonly object sync = new object();
        private SoundPlayer player, preview;
        private int volume, loadedVolume = -1;
        private bool disposed;

        public StoneSound(int volume)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("audio.stone.wav"))
            {
                if (source == null) throw new InvalidDataException("程序缺少落子音效，请重新构建完整版本。");
                using (var target = new MemoryStream()) { source.CopyTo(target); waveform = target.ToArray(); }
            }
            Volume = volume;
        }

        public int Volume
        {
            get { lock (sync) return volume; }
            set
            {
                ValidateVolume(value);
                lock (sync)
                {
                    if (disposed) throw new ObjectDisposedException("StoneSound");
                    volume = value;
                    if (value == 0 && player != null) player.Stop();
                }
            }
        }

        public void Play()
        {
            lock (sync)
            {
                if (disposed || volume == 0) return;
                try
                {
                    if (player == null || loadedVolume != volume)
                    {
                        if (player != null) player.Dispose();
                        player = CreatePlayer(volume);
                        loadedVolume = volume;
                    }
                    // Volume belongs to this app's PCM samples, not the mixer.
                    player.Play();
                }
                // An unavailable audio device must not turn a successful Go
                // move into an engine error or interrupt the game.
                catch (InvalidOperationException) { }
                catch (TimeoutException) { }
            }
        }

        public void Preview(int previewVolume)
        {
            ValidateVolume(previewVolume);
            lock (sync)
            {
                if (disposed) return;
                if (preview != null) { preview.Stop(); preview.Dispose(); preview = null; }
                if (previewVolume == 0) return;
                try { preview = CreatePlayer(previewVolume); preview.Play(); }
                catch (InvalidOperationException) { }
                catch (TimeoutException) { }
            }
        }

        private SoundPlayer CreatePlayer(int selectedVolume)
        {
            var sound = new SoundPlayer(new MemoryStream(ScaleWaveform(waveform, selectedVolume), false));
            sound.Load();
            return sound;
        }

        internal static byte[] ScaleWaveform(byte[] source, int selectedVolume)
        {
            ValidateVolume(selectedVolume);
            if (source == null || source.Length < 44 || System.Text.Encoding.ASCII.GetString(source, 0, 4) != "RIFF" ||
                System.Text.Encoding.ASCII.GetString(source, 8, 4) != "WAVE") throw new InvalidDataException("落子音效格式无效。");
            byte[] output = (byte[])source.Clone();
            bool pcm16 = false, foundData = false;
            double gain = Math.Pow(selectedVolume / 100.0, 2);
            for (int offset = 12; offset + 8 <= source.Length; )
            {
                string name = System.Text.Encoding.ASCII.GetString(source, offset, 4);
                int length = BitConverter.ToInt32(source, offset + 4);
                int start = offset + 8;
                if (length < 0 || length > source.Length - start) throw new InvalidDataException("落子音效数据不完整。");
                if (name == "fmt ") pcm16 = length >= 16 && BitConverter.ToInt16(source, start) == 1 && BitConverter.ToInt16(source, start + 14) == 16;
                if (name == "data")
                {
                    if (!pcm16 || length % 2 != 0) throw new InvalidDataException("落子音效必须为 16 位 PCM。");
                    for (int i = start; i < start + length; i += 2)
                    {
                        short scaled = (short)Math.Round(BitConverter.ToInt16(source, i) * gain);
                        output[i] = (byte)(scaled & 255);
                        output[i + 1] = (byte)((scaled >> 8) & 255);
                    }
                    foundData = true;
                }
                offset = start + length + (length % 2);
            }
            if (!foundData) throw new InvalidDataException("落子音效没有音频数据。");
            return output;
        }

        private static void ValidateVolume(int value)
        {
            if (value < 0 || value > 100) throw new ArgumentOutOfRangeException("value", "音量必须在 0 到 100 之间。");
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                if (player != null) { player.Stop(); player.Dispose(); player = null; }
                if (preview != null) { preview.Stop(); preview.Dispose(); preview = null; }
            }
        }
    }
}
