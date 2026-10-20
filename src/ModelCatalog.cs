using System;
using System.IO;

namespace GoDesk
{
    public sealed class ModelCatalogEntry
    {
        public string Id { get; private set; }
        public string Label { get; private set; }
        public string Date { get; private set; }
        public string ModelName { get; private set; }
        public string RelativePath { get; private set; }
        public bool Embedded { get; private set; }

        internal ModelCatalogEntry(string id, string label, string date, bool embedded)
        {
            Id = id; ModelName = id; Label = label; Date = date; Embedded = embedded;
            RelativePath = embedded ? "" : Path.Combine("gpu", "models", id + ".bin.gz");
        }

        public override string ToString() { return Label; }
    }

    public static class ModelCatalog
    {
        // IDs are immutable upstream names, independent of labels and locations.
        public const string StrongestId = "kata1-tf3-b11c768-s11003M-d5973M-7gres";
        public const string PreviousGpuId = "kata1-b18c384nbt-s9996604416-d4316597426";
        public const string LegacyCpuId = "g170e-b10c128-s1141046784-d204142634";

        private static readonly ModelCatalogEntry strongest = new ModelCatalogEntry(StrongestId,
            "tf3-b11c768 最强模型 · 2026-09-25（官方上传）", "2026-09-25", false);
        private static readonly ModelCatalogEntry previousGpu = new ModelCatalogEntry(PreviousGpuId,
            "b18c384nbt 原 GPU 模型 · 2024-05-26（官方上传）", "2024-05-26", false);
        private static readonly ModelCatalogEntry legacyCpu = new ModelCatalogEntry(LegacyCpuId,
            "b10 轻量模型 · 2020年旧版（2020-11-28 官方收录，内置 CPU）", "2020-11-28", true);

        public static ModelCatalogEntry[] GpuModels { get { return new ModelCatalogEntry[] { strongest, previousGpu }; } }
        public static ModelCatalogEntry[] All { get { return new ModelCatalogEntry[] { strongest, previousGpu, legacyCpu }; } }

        public static ModelCatalogEntry Find(string id)
        {
            foreach (ModelCatalogEntry entry in All)
                if (String.Equals(entry.Id, id, StringComparison.Ordinal)) return entry;
            return null;
        }
    }
}
