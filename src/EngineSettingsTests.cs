using System;
using System.IO;

namespace GoDesk
{
    public static class EngineSettingsTests
    {
        public static string Run()
        {
            int assertions = 0;
            Action<bool, string> check = (condition, label) =>
            {
                assertions++;
                if (!condition) throw new Exception("Engine settings: " + label);
            };
            EngineSettings cpu = EngineSettings.CreateCpu();
            cpu.Validate();
            check(cpu.Backend == "cpu" && cpu.EnginePath == "" && cpu.ModelPath == "", "embedded CPU defaults");
            check(cpu.Description.Contains("CPU") && cpu.Description.Contains("b10"), "CPU label");
            var clone = cpu.Clone();
            clone.Backend = "gpu";
            check(cpu.Backend == "cpu", "clone independent");
            var gpu = EngineSettings.CreateGpu();
            check(gpu.Backend == "gpu", "GPU selection");
            check(gpu.EnginePath == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu", "katago.exe"), "bundled GPU executable");
            check(gpu.ModelPath == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gpu", "model.bin.gz"), "bundled GPU model");
            check(gpu.Description.Contains("GPU"), "GPU label");
            string absent = Path.Combine(Path.GetTempPath(), "GoDesk-missing-" + Guid.NewGuid().ToString("N"), "model.bin.gz");
            var saved = EngineSettings.ParseSettings("{\"Backend\":\"GPU\",\"EnginePath\":\"missing.exe\",\"ModelPath\":\"missing.bin.gz\"}");
            check(saved.Backend == "gpu" && saved.ModelPath == "missing.bin.gz", "missing saved GPU profile preserved");
            bool rejected = false;
            try { saved.Validate(); } catch (FileNotFoundException) { rejected = true; }
            check(rejected, "missing GPU files do not fall back");
            cpu.ModelPath = absent;
            rejected = false;
            try { cpu.Validate(); } catch (FileNotFoundException) { rejected = true; }
            check(rejected, "CPU model override validated");
            var malformed = EngineSettings.ParseSettings("{\"Backend\":\"gpu\",\"ModelPath\":\"bad\\\"path\"}");
            check(malformed.Description.Contains("待检查"), "malformed saved path has repairable label");
            rejected = false;
            try { malformed.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "malformed saved path rejected at validation");
            rejected = false;
            try { EngineSettings.ParseSettings("{\"Backend\":\"unknown\"}"); } catch (ArgumentException) { rejected = true; }
            check(rejected, "unknown backend rejected");
            rejected = false;
            try { EngineSettings.ParseSettings("{\"ModelPath\":123}"); } catch (FormatException) { rejected = true; }
            check(rejected, "wrong settings field type rejected");
            var custom = new EngineSettings { Backend = "custom" };
            rejected = false;
            try { custom.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "custom profile needs concrete paths");
            string cpuConfig = KataGoEngine.BuildAnalysisConfiguration("cpu");
            string gpuConfig = KataGoEngine.BuildAnalysisConfiguration("gpu");
            check(cpuConfig.Contains("numEigenThreadsPerModel = 4"), "CPU Eigen setting");
            check(!gpuConfig.Contains("Eigen"), "GPU config excludes Eigen-only settings");
            check(gpuConfig.Contains("numSearchThreadsPerAnalysisThread = 16"), "GPU search workers");
            check(gpuConfig.Contains("nnMaxBatchSize = 32"), "GPU batch size");
            check(gpuConfig.Contains("reportAnalysisWinratesAs = BLACK") && gpuConfig.Contains("wideRootNoise = 0.0"), "playing and score semantics preserved");
            check(!gpuConfig.Contains("maxTime") && !cpuConfig.Contains("maxTime"), "no per-analysis time cutoff");
            using (var engine = new KataGoEngine()) check(engine.Status.Contains("CPU"), "parameterless checks use CPU");
            using (var engine = new KataGoEngine(malformed)) check(engine.Status.Contains("GPU"), "constructor preserves repairable GPU profile");
            return "PASS " + assertions + " engine settings assertions (no preference writes).";
        }
    }
}
