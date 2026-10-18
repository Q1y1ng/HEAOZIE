// 极简 JSON：只做本应用真正需要的事（配置读写 + 诊断导出）。
//
// 为什么自己写而不是引第三方：目标 .NET Framework 4.8 + 手写 csc 编译，
// 加依赖就要带 DLL、破坏「单文件零依赖」。Newtonsoft 之类对本项目是杀鸡用牛刀。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Hz
{
    internal static class Json
    {
        // ── 解析 ────────────────────────────────────────────────

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("JSON 末尾有多余内容（位置 " + i + "）");
            return v;
        }

        /// 宽容解析：坏文件不该让应用起不来。
        public static bool TryParse(string text, out object value, out string error)
        {
            value = null; error = null;
            try { value = Parse(text); return true; }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 提前结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("对象键必须是字符串（位置 " + i + "）");
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("对象缺少 ':'（位置 " + i + "）");
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("对象中出现意外字符 '" + s[i] + "'（位置 " + i + "）");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("数组中出现意外字符 '" + s[i] + "'（位置 " + i + "）");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("转义序列不完整");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("未知转义 '\\" + e + "'");
                }
            }
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            if (i == start) throw new FormatException("不是合法 JSON 值（位置 " + start + "）");
            double d;
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("数字格式错误：" + s.Substring(start, i - start));
            return d;
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("期望 '" + word + "'（位置 " + i + "）");
            i += word.Length;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        // ── 序列化 ──────────────────────────────────────────────

        public static string Write(object value, bool pretty)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }
            if (v is int || v is long) { sb.Append(Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is float || v is double || v is decimal)
            {
                double d = Convert.ToDouble(v);
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var map = v as IDictionary<string, object>;
            if (map != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, pretty, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    if (pretty) sb.Append(' ');
                    WriteValue(sb, kv.Value, pretty, depth + 1);
                }
                if (!first) NewLine(sb, pretty, depth);
                sb.Append('}');
                return;
            }
            var list = v as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    NewLine(sb, pretty, depth + 1);
                    WriteValue(sb, item, pretty, depth + 1);
                }
                if (!first) NewLine(sb, pretty, depth);
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        private static void NewLine(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ── 取值助手（读配置时最常用）────────────────────────────

        public static Dictionary<string, object> AsObj(object v)
        {
            return v as Dictionary<string, object> ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public static List<object> AsList(object v)
        {
            return v as List<object> ?? new List<object>();
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> map, string key)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v)) return AsObj(v);
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public static List<object> List(Dictionary<string, object> map, string key)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v)) return AsList(v);
            return new List<object>();
        }

        public static string Str(Dictionary<string, object> map, string key, string fallback)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v is string) return (string)v;
            return fallback;
        }

        public static double Num(Dictionary<string, object> map, string key, double fallback)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && (v is double || v is int || v is long))
                return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return fallback;
        }

        public static int Int(Dictionary<string, object> map, string key, int fallback)
        {
            return (int)Math.Round(Num(map, key, fallback));
        }

        public static bool Bool(Dictionary<string, object> map, string key, bool fallback)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v is bool) return (bool)v;
            return fallback;
        }

        public static List<string> StrList(Dictionary<string, object> map, string key)
        {
            var result = new List<string>();
            foreach (object item in List(map, key))
            {
                if (item is string) result.Add((string)item);
            }
            return result;
        }
    }
}
