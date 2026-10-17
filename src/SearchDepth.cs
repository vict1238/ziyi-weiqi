using System;
using System.Globalization;
using System.Numerics;

namespace GoDesk
{
    /// <summary>
    /// Parses an exact positive search target independently of KataGo's protocol.
    /// Native requests have their own explicit validation and are never clamped.
    /// </summary>
    public static class SearchDepth
    {
        // KataGo 1.14.1 analysis protocol: maxVisits is an integer in [1, 2^50].
        public const long NativeMaximumVisits = 1125899906842624L;

        public static bool TryParse(string text, out BigInteger visits, out string error)
        {
            visits = BigInteger.Zero;
            error = null;
            string value = text == null ? "" : text.Trim();
            if (value.Length == 0)
            {
                error = "请输入搜索次数（大于 0 的整数）。";
                return false;
            }
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    error = "搜索次数必须是大于 0 的整数，例如 64、256、10000。";
                    return false;
                }
            }
            BigInteger parsed;
            if (!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
            {
                error = "无法读取搜索次数，请输入大于 0 的整数。";
                return false;
            }
            if (parsed <= BigInteger.Zero)
            {
                error = "搜索次数必须大于 0。";
                return false;
            }
            visits = parsed;
            return true;
        }

        public static bool TryGetNativeValue(BigInteger visits, out long value, out string error)
        {
            value = 0;
            error = null;
            if (visits < BigInteger.One || visits > NativeMaximumVisits)
            {
                error = "KataGo 单次支持范围为 1 至 " + NativeMaximumVisits.ToString(CultureInfo.InvariantCulture) +
                    " 次搜索。请输入该范围内的正整数，或选择“持续思考”模式。";
                return false;
            }
            value = (long)visits;
            return true;
        }
    }
}
