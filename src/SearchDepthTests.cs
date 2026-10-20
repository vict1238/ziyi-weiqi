using System;
using System.Globalization;
using System.Numerics;

namespace GoDesk
{
    public static class SearchDepthTests
    {
        private static int checks;

        public static string Run()
        {
            checks = 0;
            long[] expected = new long[] { 256, 1024, 2048, 4096, 8192, 16384 };
            string[] choices = SearchDepth.Choices();
            Check(SearchDepth.PresetCount == expected.Length && SearchDepth.CustomIndex == expected.Length && choices.Length == expected.Length + 1,
                "主界面与设置共用全部预设及自定义索引");
            for (int i = 0; i < expected.Length; i++)
                Check(SearchDepth.PresetVisits(i) == expected[i] && choices[i].Contains(expected[i].ToString("N0", CultureInfo.InvariantCulture)), "预设标签与实际搜索预算一致：" + expected[i]);
            Check(SearchDepth.PresetVisits(SearchDepth.DefaultIndex(true)) == 2048 && SearchDepth.PresetVisits(SearchDepth.DefaultIndex(false)) == 256,
                "GPU 默认 2048，CPU 便携默认 256");
            choices[0] = "changed";
            Check(SearchDepth.Choices()[0] != "changed", "调用方不能修改共享预设标签");
            BigInteger visits;
            string error;
            foreach (string input in new string[] { null, "", " ", "\t\r\n" })
            {
                Check(!SearchDepth.TryParse(input, out visits, out error) && visits == BigInteger.Zero &&
                    !String.IsNullOrWhiteSpace(error), "拒绝空输入并返回可读错误");
            }
            foreach (string input in new string[] { "0", "0000", "-1", "−1", "+1", "1.0", "6.25", "1e9", "1,000", "1 2", "１２", "١٢", "NaN", "Infinity" })
            {
                Check(!SearchDepth.TryParse(input, out visits, out error) && visits == BigInteger.Zero &&
                    !String.IsNullOrWhiteSpace(error), "拒绝非正整数输入：" + input);
            }
            Check(SearchDepth.TryParse("1", out visits, out error) && visits == BigInteger.One && error == null,
                "接受最小正整数");
            Check(SearchDepth.TryParse(" \t0004096\r\n", out visits, out error) && visits == 4096 && error == null,
                "允许首尾空白与前导零");
            Check(SearchDepth.TryParse("2147483648", out visits, out error) && visits == new BigInteger(2147483648L),
                "接受超过 Int32 的搜索次数");
            string enormous = new String('9', 256);
            Check(SearchDepth.TryParse(enormous, out visits, out error) &&
                visits == BigInteger.Pow(10, 256) - BigInteger.One && error == null,
                "解析 256 位正整数，不设应用数字上限");
            Check(SearchDepth.TryParse("9223372036854775808", out visits, out error) &&
                visits == new BigInteger(Int64.MaxValue) + BigInteger.One,
                "解析超过 Int64 的正整数且保留精度");

            foreach (long input in new long[] { 1, 16, 256, Int32.MaxValue, 2147483648L, SearchDepth.NativeMaximumVisits })
            {
                long native;
                Check(SearchDepth.TryGetNativeValue(new BigInteger(input), out native, out error) &&
                    native == input && error == null, "引擎范围内精确转换：" + input);
            }
            foreach (BigInteger input in new BigInteger[] { BigInteger.Zero, -BigInteger.One,
                new BigInteger(SearchDepth.NativeMaximumVisits) + BigInteger.One, BigInteger.Pow(10, 256) })
            {
                long native;
                Check(!SearchDepth.TryGetNativeValue(input, out native, out error) && native == 0,
                    "引擎范围外明确拒绝且不截断");
                Check(error != null && error.Contains("KataGo 单次支持范围") &&
                    error.Contains(SearchDepth.NativeMaximumVisits.ToString(CultureInfo.InvariantCulture)) &&
                    error.Contains("持续思考"), "范围错误解释原生限制并提供持续思考模式");
            }
            Check(SearchDepth.TryParse((new BigInteger(SearchDepth.NativeMaximumVisits) + BigInteger.One)
                .ToString(CultureInfo.InvariantCulture), out visits, out error), "语法解析与引擎范围检查相互独立");
            return "自定义搜索次数测试通过：" + checks + " 项断言。";
        }

        private static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception("搜索次数测试失败：" + message);
        }
    }
}
