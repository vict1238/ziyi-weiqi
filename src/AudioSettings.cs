using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GoDesk
{
    public static class AudioSettings
    {
        public const int DefaultVolume = 65;
        private static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "audio-settings.json"); }
        }

        public static int LoadVolume()
        {
            try { return File.Exists(SettingsFile) ? ReadVolume(File.ReadAllText(SettingsFile, Encoding.UTF8)) : DefaultVolume; }
            catch (IOException) { return DefaultVolume; }
            catch (UnauthorizedAccessException) { return DefaultVolume; }
        }

        internal static int ReadVolume(string json)
        {
            try
            {
                var values = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                object value;
                if (values == null || !values.TryGetValue("Volume", out value) || !(value is int)) return DefaultVolume;
                int volume = (int)value;
                return volume >= 0 && volume <= 100 ? volume : DefaultVolume;
            }
            catch (ArgumentException) { return DefaultVolume; }
            catch (InvalidOperationException) { return DefaultVolume; }
        }

        public static void SaveVolume(int volume)
        {
            if (volume < 0 || volume > 100) throw new ArgumentOutOfRangeException("volume");
            string directory = Path.GetDirectoryName(SettingsFile);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "audio-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, "{\"Volume\":" + volume + "}", new UTF8Encoding(false));
                if (File.Exists(SettingsFile)) File.Replace(temporary, SettingsFile, null);
                else File.Move(temporary, SettingsFile);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
