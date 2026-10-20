using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

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
            check(cpu.ModelId == ModelCatalog.LegacyCpuId && cpu.Description.Contains("CPU") && cpu.Description.Contains("2020"), "CPU accurately identified and dated");
            var clone = cpu.Clone();
            clone.Backend = "gpu"; clone.ModelId = ModelCatalog.StrongestId;
            check(cpu.Backend == "cpu" && cpu.ModelId == ModelCatalog.LegacyCpuId, "clone independent");
            var gpu = EngineSettings.CreateGpu();
            check(gpu.Backend == "gpu" && gpu.ModelId == ModelCatalog.StrongestId, "new GPU defaults to strongest");
            check(gpu.EnginePath == Path.Combine("gpu", "katago.exe"), "portable packaged executable");
            check(gpu.ModelPath == ModelCatalog.Find(ModelCatalog.StrongestId).RelativePath, "strongest packaged model");
            check(gpu.ResolvedModelPath == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, gpu.ModelPath), "model resolved beside current application");
            check(gpu.Description.Contains("GPU") && gpu.Description.Contains("2026-09-25"), "GPU model and date visible");
            var previous = EngineSettings.CreateGpu(ModelCatalog.PreviousGpuId);
            check(previous.ModelId == ModelCatalog.PreviousGpuId && previous.Description.Contains("2024-05-26"), "old GPU still selectable and dated");
            bool rejected = false;
            try { EngineSettings.CreateGpu(ModelCatalog.LegacyCpuId); } catch (ArgumentException) { rejected = true; }
            check(rejected, "embedded CPU not mislabelled as a GPU network");
            rejected = false;
            try { EngineSettings.CreateGpu("unknown"); } catch (ArgumentException) { rejected = true; }
            check(rejected, "unknown GPU network rejected");

            string absent = Path.Combine(Path.GetTempPath(), "GoDesk-missing-" + Guid.NewGuid().ToString("N"), "model.bin.gz");
            var saved = EngineSettings.ParseSettings("{\"Backend\":\"GPU\",\"EnginePath\":\"missing.exe\",\"ModelPath\":\"missing.bin.gz\"}", true);
            check(saved.Backend == "gpu" && saved.ModelPath == "missing.bin.gz" && saved.ModelId == "", "external saved GPU profile preserved");
            rejected = false;
            try { saved.Validate(); } catch (FileNotFoundException) { rejected = true; }
            check(rejected, "missing GPU files do not fall back");
            cpu.ModelPath = absent;
            rejected = false;
            try { cpu.Validate(); } catch (FileNotFoundException) { rejected = true; }
            check(rejected, "CPU model override validated");
            var malformed = EngineSettings.ParseSettings("{\"Backend\":\"gpu\",\"ModelPath\":\"bad\\\"path\"}", false);
            check(malformed.Description.Contains("待检查"), "malformed saved path has repairable label");
            rejected = false;
            try { malformed.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "malformed saved path rejected at validation");
            rejected = false;
            try { EngineSettings.ParseSettings("{\"Backend\":\"unknown\"}", true); } catch (ArgumentException) { rejected = true; }
            check(rejected, "unknown backend rejected");
            rejected = false;
            try { EngineSettings.ParseSettings("{\"ModelPath\":123}", true); } catch (FormatException) { rejected = true; }
            check(rejected, "wrong settings field type rejected");
            rejected = false;
            try { EngineSettings.ParseSettings("{\"Version\":3}", true); } catch (FormatException) { rejected = true; }
            check(rejected, "unknown future schema does not silently overwrite preferences");
            var custom = new EngineSettings { Backend = "custom", ModelId = "" };
            rejected = false;
            try { custom.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "custom profile needs concrete paths");

            var upgradedCpu = EngineSettings.ParseSettings("{\"Version\":1,\"Backend\":\"cpu\",\"EnginePath\":\"\",\"ModelPath\":\"\"}", true);
            check(upgradedCpu.Backend == "gpu" && upgradedCpu.ModelId == ModelCatalog.StrongestId && upgradedCpu.MigrationNotice.Contains("最强"), "old embedded default migrates once GPU package is installed");
            check(upgradedCpu.Clone().MigrationNotice == upgradedCpu.MigrationNotice, "migration notice preserved for startup UI");
            var upgradedGpu = EngineSettings.ParseSettings("{\"Version\":1,\"Backend\":\"gpu\",\"EnginePath\":\"C:/old-package/gpu/katago.exe\",\"ModelPath\":\"C:/old-package/gpu/model.bin.gz\"}", true);
            check(upgradedGpu.ModelId == ModelCatalog.StrongestId && !Path.IsPathRooted(upgradedGpu.EnginePath), "old bundled GPU migrates from stale absolute location");
            var bareCpu = EngineSettings.ParseSettings("{\"Version\":1,\"Backend\":\"cpu\"}", false);
            check(bareCpu.Backend == "cpu" && bareCpu.ModelId == ModelCatalog.LegacyCpuId && !bareCpu.Description.Contains("最强"), "single CPU package accurately keeps embedded model");
            var absentGpu = EngineSettings.ParseSettings("{\"Version\":1,\"Backend\":\"gpu\",\"EnginePath\":\"C:/old/gpu/katago.exe\",\"ModelPath\":\"C:/old/gpu/model.bin.gz\"}", false);
            check(absentGpu.Backend == "gpu" && absentGpu.ModelId == "" && absentGpu.ModelPath == Path.Combine("gpu", "model.bin.gz"), "missing old GPU stays an explicit repairable profile");
            string customJson = "{\"Version\":1,\"Backend\":\"custom\",\"EnginePath\":\"C:/user/katago.exe\",\"ModelPath\":\"C:/user/network.bin.gz\"}";
            var oldCustom = EngineSettings.ParseSettings(customJson, true);
            check(oldCustom.Backend == "custom" && oldCustom.ModelId == "" && oldCustom.ModelPath == "C:/user/network.bin.gz", "user-selected custom engine/model preserved");
            var oldCpuOverride = EngineSettings.ParseSettings("{\"Version\":1,\"Backend\":\"cpu\",\"ModelPath\":\"C:/user/network.bin.gz\"}", true);
            check(oldCpuOverride.Backend == "cpu" && oldCpuOverride.ModelId == "" && oldCpuOverride.ModelPath == "C:/user/network.bin.gz", "explicit CPU model override preserved");

            string serialized = previous.SerializeSettings();
            var content = new JavaScriptSerializer().DeserializeObject(serialized) as Dictionary<string, object>;
            check((int)content["Version"] == 2 && (string)content["ModelId"] == ModelCatalog.PreviousGpuId, "new save schema keeps explicit network ID");
            check(!Path.IsPathRooted((string)content["EnginePath"]) && !Path.IsPathRooted((string)content["ModelPath"]), "packaged settings portable across folder moves");
            var roundTrip = EngineSettings.ParseSettings(serialized, true);
            check(roundTrip.ModelId == ModelCatalog.PreviousGpuId && roundTrip.MigrationNotice == "", "new explicit previous model is not remigrated");
            var cpuRoundTrip = EngineSettings.ParseSettings(EngineSettings.CreateCpu().SerializeSettings(), true);
            check(cpuRoundTrip.Backend == "cpu" && cpuRoundTrip.ModelId == ModelCatalog.LegacyCpuId, "new explicit CPU choice is not overridden");
            string staleJson = "{\"Version\":2,\"Backend\":\"gpu\",\"ModelId\":\"" + ModelCatalog.PreviousGpuId + "\",\"EnginePath\":\"C:/moved/gpu/katago.exe\",\"ModelPath\":\"C:/moved/gpu/models/unused.bin.gz\"}";
            var moved = EngineSettings.ParseSettings(staleJson, true);
            check(moved.EnginePath == Path.Combine("gpu", "katago.exe") && moved.ModelPath == previous.ModelPath, "catalog paths resolve from current package after a move");
            check(moved.ResolvedModelPath == previous.ResolvedModelPath, "saved path cannot swap catalog model identity");
            var customCatalog = EngineSettings.CreateGpu(ModelCatalog.PreviousGpuId);
            customCatalog.Backend = "custom"; customCatalog.EnginePath = "C:/user-engine/katago.exe";
            var customCatalogRoundTrip = EngineSettings.ParseSettings(customCatalog.SerializeSettings(), true);
            check(customCatalogRoundTrip.Backend == "custom" && customCatalogRoundTrip.EnginePath == Path.GetFullPath(customCatalog.EnginePath), "custom executable remains user-selected");
            check(customCatalogRoundTrip.ModelPath == previous.ModelPath && customCatalogRoundTrip.ResolvedModelPath == previous.ResolvedModelPath,
                "custom engine can use a portable catalog network");
            check(customCatalogRoundTrip.ModelDescription.Contains("2024-05-26"), "catalog date retained for a custom executable");
            var unknown = EngineSettings.ParseSettings("{\"Version\":2,\"Backend\":\"gpu\",\"ModelId\":\"unknown\"}", true);
            check(unknown.ModelId == "unknown" && unknown.ModelDescription.Contains("待检查"), "unknown saved model remains repairable");
            rejected = false;
            try { unknown.Validate(); } catch (ArgumentException) { rejected = true; }
            check(rejected, "unknown saved model never falls back to another network");

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
            return "PASS " + assertions + " engine settings assertions (model migration, path portability, no preference writes).";
        }
    }
}
