using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace GoDesk
{
    public sealed class EngineSettings
    {
        public string Backend { get; set; }
        public string EnginePath { get; set; }
        public string ModelPath { get; set; }
        public string ModelId { get; set; }
        public string MigrationNotice { get; private set; }

        private static readonly string PackagedEnginePath = Path.Combine("gpu", "katago.exe");
        private static readonly string PackagedCudaEnginePath = Path.Combine("cuda", "katago.exe");
        private static readonly Lazy<bool> nvidiaAvailable = new Lazy<bool>(DetectNvidiaGpu);

        public EngineSettings()
        {
            Backend = "cpu"; EnginePath = ""; ModelPath = "";
            ModelId = ModelCatalog.LegacyCpuId; MigrationNotice = "";
        }

        private static string SettingsFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoDesk", "engine-settings.json"); }
        }

        private static string GpuDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu"); }
        }

        private static string CudaDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cuda"); }
        }

        // Package detection is independent of driver support. A missing model
        // must produce a repairable error instead of silently using weaker play.
        public static bool GpuAvailable { get { return File.Exists(Path.Combine(GpuDirectory, "katago.exe")); } }
        public static bool CudaAvailable { get { return File.Exists(Path.Combine(CudaDirectory, "katago.exe")); } }
        public static bool NvidiaGpuAvailable { get { return nvidiaAvailable.Value; } }
        public bool IsGpu { get { string backend = NormalizedBackend; return backend == "gpu" || backend == "cuda"; } }

        public static string GpuBackendDescription
        {
            get { string name = ReadBackendLabel(GpuDirectory); return name.Length == 0 ? "GPU · OpenCL" : "GPU · " + name; }
        }

        public static string CudaBackendDescription
        {
            get { string name = ReadBackendLabel(CudaDirectory); return name.Length == 0 ? "GPU · CUDA" : "GPU · " + name; }
        }

        public static EngineSettings CreateCpu() { return new EngineSettings(); }
        public static EngineSettings CreateGpu() { return CreateGpu(ModelCatalog.StrongestId); }
        public static EngineSettings CreateCuda() { return CreateCuda(ModelCatalog.StrongestId); }

        public static EngineSettings CreateDefault() { return CreateDefault(GpuAvailable, CudaAvailable, NvidiaGpuAvailable); }

        internal static EngineSettings CreateDefault(bool gpuAvailable, bool cudaAvailable, bool nvidiaGpuAvailable)
        {
            if (cudaAvailable && nvidiaGpuAvailable) return CreateCuda();
            return gpuAvailable ? CreateGpu() : CreateCpu();
        }

        public static EngineSettings CreateGpu(string modelId)
        { return CreatePackagedGpu("gpu", modelId); }

        public static EngineSettings CreateCuda(string modelId)
        { return CreatePackagedGpu("cuda", modelId); }

        private static EngineSettings CreatePackagedGpu(string backend, string modelId)
        {
            ModelCatalogEntry entry = ModelCatalog.Find(modelId);
            if (entry == null || entry.Embedded) throw new ArgumentException("请选择目录中的 GPU 模型。", "modelId");
            return new EngineSettings { Backend = backend, EnginePath = backend == "cuda" ? PackagedCudaEnginePath : PackagedEnginePath,
                ModelId = entry.Id, ModelPath = entry.RelativePath };
        }

        public EngineSettings Clone()
        {
            return new EngineSettings { Backend = Backend, EnginePath = EnginePath, ModelPath = ModelPath,
                ModelId = ModelId, MigrationNotice = MigrationNotice };
        }

        internal string NormalizedBackend
        {
            get
            {
                string value = (Backend ?? "").Trim().ToLowerInvariant();
                if (value != "cpu" && value != "gpu" && value != "cuda" && value != "custom")
                    throw new ArgumentException("引擎类型必须是 CPU、OpenCL GPU、CUDA GPU 或自定义。");
                return value;
            }
        }

        internal string ResolvedEnginePath
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(EnginePath)) return ResolvePath(EnginePath);
                string backend = NormalizedBackend;
                return backend == "gpu" ? Path.Combine(GpuDirectory, "katago.exe") :
                    backend == "cuda" ? Path.Combine(CudaDirectory, "katago.exe") : "";
            }
        }

        internal string ResolvedModelPath
        {
            get
            {
                if ((IsGpu || NormalizedBackend == "custom") && !String.IsNullOrEmpty(ModelId))
                {
                    ModelCatalogEntry entry = ModelCatalog.Find(ModelId);
                    if (entry == null || entry.Embedded) throw new ArgumentException("已保存的 GPU 模型不在目录中，请重新选择模型。");
                    return ResolvePath(entry.RelativePath);
                }
                if (!String.IsNullOrWhiteSpace(ModelPath)) return ResolvePath(ModelPath);
                return "";
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
                ModelCatalogEntry entry = ModelCatalog.Find(ModelId);
                if (NormalizedBackend != "cpu" && entry != null && !entry.Embedded) return entry.Label;
                if (NormalizedBackend == "cpu" && String.IsNullOrEmpty(path)) return ModelCatalog.Find(ModelCatalog.LegacyCpuId).Label;
                return String.IsNullOrEmpty(path) ? "模型待选择" : Path.GetFileName(path);
            }
        }

        public string Description
        {
            get
            {
                string backend = NormalizedBackend;
                return (backend == "cpu" ? "CPU" : backend == "gpu" ? GpuBackendDescription :
                    backend == "cuda" ? CudaBackendDescription : "自定义 KataGo") + " · " + ModelDescription;
            }
        }

        public void Validate()
        {
            string backend = NormalizedBackend;
            string engine = ResolvedEnginePath;
            string model = ResolvedModelPath;
            if (backend != "cpu" && String.IsNullOrEmpty(engine)) throw new ArgumentException("请选择 KataGo 程序（katago.exe）。");
            if (backend != "cpu" && String.IsNullOrEmpty(model)) throw new ArgumentException("请选择 KataGo 模型（.bin.gz）。");
            if (!String.IsNullOrEmpty(engine) && !File.Exists(engine))
                throw new FileNotFoundException((IsGpu ? "GPU 引擎文件不存在，请解压完整 GPU 便携包或重新选择程序：" : "KataGo 程序不存在：") + engine, engine);
            if (!String.IsNullOrEmpty(model) && !File.Exists(model))
                throw new FileNotFoundException((IsGpu ? "所选 GPU 模型文件不存在，请解压完整 GPU 便携包或重新选择模型：" : "KataGo 模型不存在：") + model, model);
            if (!String.IsNullOrEmpty(engine) && !String.Equals(Path.GetExtension(engine), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 Windows KataGo 程序（.exe）。");
            if (backend == "cuda" && IsPackagedEnginePath(EnginePath, backend))
            {
                foreach (string library in new string[] { "cudart64_12.dll", "cublas64_12.dll", "cublasLt64_12.dll", "cudnn64_9.dll" })
                {
                    string path = Path.Combine(Path.GetDirectoryName(engine), library);
                    if (!File.Exists(path)) throw new FileNotFoundException("CUDA 运行库不完整，请完整解压 GPU 包，保留 cuda 文件夹内的全部 DLL；也可在设置中选择 OpenCL。缺少：" + library, path);
                }
            }
        }

        public static EngineSettings Load()
        {
            if (!File.Exists(SettingsFile)) return CreateDefault();
            try { return ParseSettings(File.ReadAllText(SettingsFile, Encoding.UTF8)); }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)) throw;
                throw new InvalidDataException("无法读取 AI 引擎设置，请在“AI 引擎设置”中重新选择。设置文件：" + SettingsFile, ex);
            }
        }

        internal static EngineSettings ParseSettings(string json) { return ParseSettings(json, GpuAvailable, CudaAvailable, NvidiaGpuAvailable); }

        // The explicit package flag lets migration checks run without touching
        // user preferences or depending on this machine's installed GPU files.
        internal static EngineSettings ParseSettings(string json, bool gpuAvailable)
        { return ParseSettings(json, gpuAvailable, false, false); }

        internal static EngineSettings ParseSettings(string json, bool gpuAvailable, bool cudaAvailable, bool nvidiaGpuAvailable)
        {
            var content = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (content == null) throw new FormatException("内容不是设置对象。");
            int version = ReadVersion(content);
            var settings = new EngineSettings { Backend = ReadString(content, "Backend", "cpu"),
                EnginePath = ReadString(content, "EnginePath", ""), ModelPath = ReadString(content, "ModelPath", ""),
                ModelId = ReadString(content, "ModelId", "") };
            settings.Backend = settings.NormalizedBackend;
            if (version == 1 && IsLegacyBundledProfile(settings))
            {
                if (gpuAvailable || (cudaAvailable && nvidiaGpuAvailable))
                {
                    settings = CreateDefault(gpuAvailable, cudaAvailable, nvidiaGpuAvailable);
                    settings.MigrationNotice = settings.Backend == "cuda" ?
                        "已升级为 CUDA 加速与默认最强模型（2026-09-25）；可在设置中选择 OpenCL、原 b18 或 CPU b10。" :
                        "已升级为默认最强模型（2026-09-25）；原 b18 和 CPU b10 仍可在设置中选择。";
                }
                else if (settings.Backend == "cpu") settings = CreateCpu();
                // Keep an absent saved GPU profile explicit for repair.
                else
                {
                    settings = CreateGpu(ModelCatalog.PreviousGpuId);
                    settings.ModelId = ""; settings.ModelPath = Path.Combine("gpu", "model.bin.gz");
                }
            }
            else if (version == 1)
            {
                settings.ModelId = settings.Backend == "cpu" && String.IsNullOrWhiteSpace(settings.ModelPath) ? ModelCatalog.LegacyCpuId : "";
            }
            else
            {
                ModelCatalogEntry entry = ModelCatalog.Find(settings.ModelId);
                if (settings.Backend != "cpu" && entry != null && !entry.Embedded)
                {
                    settings.ModelPath = entry.RelativePath;
                    if (settings.IsGpu && IsPackagedEnginePath(settings.EnginePath, settings.Backend))
                        settings.EnginePath = settings.Backend == "cuda" ? PackagedCudaEnginePath : PackagedEnginePath;
                }
            }
            // Version 2 GPU profiles used the bundled OpenCL backend. Upgrade the
            // backend on NVIDIA while keeping the user's selected catalog model.
            // Version 3 explicitly selected OpenCL/custom profiles stay unchanged.
            if (version < 3 && cudaAvailable && nvidiaGpuAvailable && settings.Backend == "gpu" &&
                IsPackagedEnginePath(settings.EnginePath, "gpu") && IsCatalogGpuModel(settings.ModelId))
            {
                settings = CreateCuda(settings.ModelId);
                settings.MigrationNotice = "已启用 CUDA 加速，保留原来选择的模型；可在设置中切换回 OpenCL。";
            }
            // Parsing never writes preferences or loads a model. Missing-file
            // profiles stay repairable; Validate()/startup checks existence.
            return settings;
        }

        public void Save()
        {
            Validate();
            string directory = Path.GetDirectoryName(SettingsFile);
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "engine-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(SerializeSettings());
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

        internal string SerializeSettings()
        {
            string backend = NormalizedBackend;
            ModelCatalogEntry entry = ModelCatalog.Find(ModelId);
            bool packagedModel = backend != "cpu" && entry != null && !entry.Embedded;
            string engine = IsGpu && packagedModel && IsPackagedEnginePath(EnginePath, backend) ?
                (backend == "cuda" ? PackagedCudaEnginePath : PackagedEnginePath) : ResolvedEnginePath;
            string model = packagedModel ? entry.RelativePath : ResolvedModelPath;
            var content = new Dictionary<string, object> { { "Version", 3 }, { "Backend", backend },
                { "ModelId", ModelId ?? "" }, { "EnginePath", engine }, { "ModelPath", model } };
            return new JavaScriptSerializer().Serialize(content);
        }

        private static bool IsLegacyBundledProfile(EngineSettings settings)
        {
            if (settings.Backend == "cpu") return String.IsNullOrWhiteSpace(settings.EnginePath) && String.IsNullOrWhiteSpace(settings.ModelPath);
            if (settings.Backend != "gpu") return false;
            string model = NormalizeSeparators(settings.ModelPath);
            return IsPackagedEnginePath(settings.EnginePath, "gpu") && (model.Length == 0 ||
                String.Equals(model, "gpu/model.bin.gz", StringComparison.OrdinalIgnoreCase) ||
                model.EndsWith("/gpu/model.bin.gz", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsCatalogGpuModel(string id)
        {
            ModelCatalogEntry entry = ModelCatalog.Find(id);
            return entry != null && !entry.Embedded;
        }

        private static bool IsPackagedEnginePath(string path, string backend)
        {
            string normalized = NormalizeSeparators(path);
            string relative = backend == "cuda" ? "cuda/katago.exe" : "gpu/katago.exe";
            return normalized.Length == 0 || String.Equals(normalized, relative, StringComparison.OrdinalIgnoreCase) ||
                normalized.EndsWith("/" + relative, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSeparators(string value) { return (value ?? "").Trim().Replace('\\', '/'); }

        private static int ReadVersion(Dictionary<string, object> content)
        {
            object value;
            if (!content.TryGetValue("Version", out value)) return 1;
            if (!(value is int) || (int)value < 1 || (int)value > 3) throw new FormatException("不支持的引擎设置版本。");
            return (int)value;
        }

        private static string ReadBackendLabel(string directory)
        {
            try
            {
                string path = Path.Combine(directory, "runtime.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 65536) return "";
                var manifest = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                object value;
                if (manifest == null || !manifest.TryGetValue("backend", out value) || !(value is string)) return "";
                string label = ((string)value).Replace("\r", " ").Replace("\n", " ").Trim();
                return label.Length <= 80 ? label : "";
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
            catch (ArgumentException) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice information, uint flags);

        private static bool DetectNvidiaGpu()
        {
            try
            {
                // Hybrid notebooks may render the desktop on Intel/AMD while the
                // NVIDIA adapter is idle. Inspect every adapter, including those
                // without an attached display; never require the primary flag.
                for (uint index = 0; index < 256; index++)
                {
                    var information = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                    if (!EnumDisplayDevices(null, index, ref information, 0)) break;
                    if (IsNvidiaAdapter(information.DeviceString, information.DeviceId)) return true;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (System.Security.SecurityException) { }
            return false;
        }

        internal static bool IsNvidiaAdapter(string description, string deviceId)
        {
            return (description ?? "").IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (deviceId ?? "").IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0;
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
