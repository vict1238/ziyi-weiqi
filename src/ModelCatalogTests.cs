using System;
using System.Collections.Generic;
using System.IO;

namespace GoDesk
{
    public static class ModelCatalogTests
    {
        public static string Run()
        {
            int assertions = 0;
            Action<bool, string> check = (condition, label) =>
            {
                assertions++;
                if (!condition) throw new Exception("Model catalog: " + label);
            };
            check(ModelCatalog.GpuModels.Length == 2 && ModelCatalog.All.Length == 3, "both GPU networks and embedded CPU remain available");
            check(ModelCatalog.GpuModels[0].Id == ModelCatalog.StrongestId, "strongest appears first");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModelCatalogEntry entry in ModelCatalog.All)
            {
                check(ids.Add(entry.Id), "unique immutable network ID");
                check(entry.Id == entry.ModelName && entry.ToString() == entry.Label, "identity and display contract");
                check(entry.Label.Contains(entry.Date), "date visible in selection label");
                check(Object.ReferenceEquals(ModelCatalog.Find(entry.Id), entry), "network lookup exact");
                if (!entry.Embedded)
                {
                    check(!Path.IsPathRooted(entry.RelativePath), "portable model location");
                    check(entry.RelativePath == Path.Combine("gpu", "models", entry.Id + ".bin.gz"), "pinned filename and directory");
                }
                else check(entry.RelativePath == "", "CPU model remains an embedded resource");
            }
            check(ModelCatalog.Find(ModelCatalog.StrongestId).Date == "2026-09-25", "strongest official upload date");
            check(ModelCatalog.Find(ModelCatalog.PreviousGpuId).Date == "2024-05-26", "previous GPU official upload date");
            check(ModelCatalog.Find(ModelCatalog.LegacyCpuId).Label.Contains("2020年旧版") &&
                ModelCatalog.Find(ModelCatalog.LegacyCpuId).Label.Contains("官方收录"), "CPU archival date distinguished from training date");
            check(ModelCatalog.Find(null) == null && ModelCatalog.Find("unregistered-model") == null, "unknown ID not guessed");
            ModelCatalogEntry[] copy = ModelCatalog.GpuModels;
            copy[0] = null;
            check(ModelCatalog.GpuModels[0] != null, "callers cannot alter global model order");
            return "PASS " + assertions + " model catalog assertions (immutable IDs, dates, portable paths).";
        }
    }
}
