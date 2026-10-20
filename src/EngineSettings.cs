using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GoDesk
{
    public sealed class EngineSettings
    {
        public string Backend { get; set; }
        public string EnginePath { get; set; }
        public string ModelPath { get; set; }

        public EngineSettings() { Backend = "cpu"; EnginePath = ""; ModelPath = ""; }

        private static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "engine-settings.json"); }
        }

        private static string GpuDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu"); }
        }

        // This detects packaged files only. Driver/device support is checked by KataGo
        // when starting; a selected GPU profile never silently falls back to the CPU.
        public static bool GpuAvailable
        {
            get { return File.Exists(Path.Combine(GpuDirectory, "katago.exe")) && File.Exists(Path.Combine(GpuDirectory, "model.bin.gz")); }
        }

        public static EngineSettings CreateCpu() { return new EngineSettings(); }

        public static EngineSettings CreateGpu()
        {
            return new EngineSettings { Backend = "gpu", EnginePath = Path.Combine(GpuDirectory, "katago.exe"),
                ModelPath = Path.Combine(GpuDirectory, "model.bin.gz") };
        }

        public EngineSettings Clone()
        {
            return new EngineSettings { Backend = Backend, EnginePath = EnginePath, ModelPath = ModelPath };
        }

        internal string NormalizedBackend
        {
            get
            {
                string value = (Backend ?? "").Trim().ToLowerInvariant();
                if (value != "cpu" && value != "gpu" && value != "custom")
                    throw new ArgumentException("引擎类型必须是 CPU、GPU 或自定义。");
                return value;
            }
        }

        internal string ResolvedEnginePath
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(EnginePath)) return ResolvePath(EnginePath);
                return NormalizedBackend == "gpu" ? Path.Combine(GpuDirectory, "katago.exe") : "";
            }
        }

        internal string ResolvedModelPath
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(ModelPath)) return ResolvePath(ModelPath);
                return NormalizedBackend == "gpu" ? Path.Combine(GpuDirectory, "model.bin.gz") : "";
            }
        }

        public string ModelDescription
        {
            get
            {
                string path;
                try { path = ResolvedModelPath; }
                catch (ArgumentException) { return "模型路径待检查"; }
                catch (NotSupportedException) { return "模型路径待检查"; }
                catch (IOException) { return "模型路径待检查"; }
                if (String.IsNullOrEmpty(path)) return NormalizedBackend == "cpu" ? "内置 b10 轻量模型" : "模型待选择";
                if (NormalizedBackend == "gpu" && String.Equals(path, Path.Combine(GpuDirectory, "model.bin.gz"), StringComparison.OrdinalIgnoreCase))
                {
                    // Manifest metadata is used ONLY as a label for the default
                    // bundled model. It never selects files or executable paths.
                    try
                    {
                        string manifestPath = Path.Combine(GpuDirectory, "runtime.json");
                        if (File.Exists(manifestPath) && new FileInfo(manifestPath).Length <= 65536)
                        {
                            var manifest = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(manifestPath, Encoding.UTF8)) as Dictionary<string, object>;
                            object value;
                            if (manifest != null && manifest.TryGetValue("modelName", out value) && value is string)
                            {
                                string label = ((string)value).Replace("\r", " ").Replace("\n", " ").Trim();
                                if (label.Length > 0 && label.Length <= 160) return label;
                            }
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                return Path.GetFileName(path);
            }
        }

        public string Description
        {
            get
            {
                string backend = NormalizedBackend;
                return (backend == "cpu" ? "CPU" : backend == "gpu" ? "GPU · OpenCL" : "自定义 KataGo") + " · " + ModelDescription;
            }
        }

        public void Validate()
        {
            string backend = NormalizedBackend;
            string engine = ResolvedEnginePath;
            string model = ResolvedModelPath;
            if (backend == "custom" && String.IsNullOrEmpty(engine)) throw new ArgumentException("请选择自定义 KataGo 程序（katago.exe）。");
            if (backend == "custom" && String.IsNullOrEmpty(model)) throw new ArgumentException("请选择自定义 KataGo 模型（.bin.gz）。");
            if (!String.IsNullOrEmpty(engine) && !File.Exists(engine))
                throw new FileNotFoundException((backend == "gpu" ? "GPU 引擎文件不存在，请解压完整 GPU 便携包或重新选择程序：" : "KataGo 程序不存在：") + engine, engine);
            if (!String.IsNullOrEmpty(model) && !File.Exists(model))
                throw new FileNotFoundException((backend == "gpu" ? "GPU 模型文件不存在，请解压完整 GPU 便携包或重新选择模型：" : "KataGo 模型不存在：") + model, model);
            if (!String.IsNullOrEmpty(engine) && !String.Equals(Path.GetExtension(engine), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 Windows KataGo 程序（.exe）。");
        }

        public static EngineSettings Load()
        {
            if (!File.Exists(SettingsFile)) return GpuAvailable ? CreateGpu() : CreateCpu();
            try
            {
                return ParseSettings(File.ReadAllText(SettingsFile, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)) throw;
                throw new InvalidDataException("无法读取 AI 引擎设置，请在“AI 引擎设置”中重新选择。设置文件：" + SettingsFile, ex);
            }
        }

        internal static EngineSettings ParseSettings(string json)
        {
            var content = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (content == null) throw new FormatException("内容不是设置对象。");
            var settings = new EngineSettings { Backend = ReadString(content, "Backend", "cpu"),
                EnginePath = ReadString(content, "EnginePath", ""), ModelPath = ReadString(content, "ModelPath", "") };
            settings.Backend = settings.NormalizedBackend;
            // Preserve missing-file profiles so the UI can open and repair them.
            // File existence is checked by Validate()/engine startup, not Load().
            return settings;
        }

        public void Save()
        {
            Validate();
            var content = new Dictionary<string, object> { { "Version", 1 }, { "Backend", NormalizedBackend },
                { "EnginePath", ResolvedEnginePath }, { "ModelPath", ResolvedModelPath } };
            string directory = Path.GetDirectoryName(SettingsFile);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "engine-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(content));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(SettingsFile)) File.Replace(temporary, SettingsFile, null);
                else
                {
                    try { File.Move(temporary, SettingsFile); }
                    catch (IOException) { if (File.Exists(SettingsFile)) File.Replace(temporary, SettingsFile, null); else throw; }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string ResolvePath(string value)
        {
            string path = value.Trim();
            if (path.IndexOf('"') >= 0 || path.IndexOf('\0') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
                throw new ArgumentException("引擎和模型路径不能包含引号或换行。");
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
        }

        private static string ReadString(Dictionary<string, object> content, string key, string fallback)
        {
            object value;
            if (!content.TryGetValue(key, out value) || value == null) return fallback;
            if (!(value is string)) throw new FormatException("设置字段格式错误：" + key);
            return (string)value;
        }
    }
}
