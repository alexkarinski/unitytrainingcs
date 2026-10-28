using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CsAim.Core
{
    /// <summary>
    /// Tolerant KeyValues reader covering both shapes Valve ships weapon data in:
    ///
    ///   VDF / KV1 (csgo/scripts/items/items_game.txt):   "key" "value"   and   "key" { ... }
    ///   KV3       (CS2 *.vdata weapon files):            m_key = value   and   m_key = { ... }
    ///
    /// It is intentionally lenient: no schema, no type system, just a tree of strings. We only
    /// need to pull a few dozen numeric attributes out of a very large file, and being lenient
    /// means a future items_game.txt reshuffle does not break the importer.
    ///
    /// Keys are normalised (lowercase, spaces/dashes to underscores, leading m_ dropped) so that
    /// "inaccuracy stand", "inaccuracy_stand" and "m_flInaccuracyStand" all resolve to the same
    /// lookup name.
    /// </summary>
    public sealed class KeyValues
    {
        public string Key = string.Empty;
        public string Value = string.Empty;
        public readonly List<KeyValues> Children = new List<KeyValues>();

        public bool HasChildren => Children.Count > 0;

        public static string Normalise(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            var sb = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == ' ' || c == '-' || c == '\t') sb.Append('_');
                else if (char.IsUpper(c))
                {
                    // CamelCase -> snake_case, so m_flInaccuracyStand -> inaccuracy_stand.
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    sb.Append(char.ToLowerInvariant(c));
                }
                else sb.Append(c);
            }

            string s = sb.ToString();
            // Strip Valve's Hungarian prefixes: m_fl, m_n, m_b, m_v, m_, fl, n_.
            if (s.StartsWith("m_", StringComparison.Ordinal)) s = s.Substring(2);
            foreach (var prefix in HungarianPrefixes)
            {
                if (s.StartsWith(prefix, StringComparison.Ordinal))
                {
                    s = s.Substring(prefix.Length);
                    break;
                }
            }
            return s.Trim('_');
        }

        static readonly string[] HungarianPrefixes = { "fl_", "n_", "b_", "v_", "i_", "f_", "sz_", "str_" };

        public KeyValues FindChild(string normalisedKey)
        {
            for (int i = 0; i < Children.Count; i++)
                if (Children[i].Key == normalisedKey) return Children[i];
            return null;
        }

        public string GetString(string normalisedKey, string fallback = null)
        {
            var c = FindChild(normalisedKey);
            return c == null || c.HasChildren ? fallback : c.Value;
        }

        public bool TryGetFloat(string normalisedKey, out float result)
        {
            result = 0f;
            var raw = GetString(normalisedKey);
            return raw != null && TryParseFloat(raw, out result);
        }

        public static bool TryParseFloat(string raw, out float result)
        {
            // Valve writes plain decimals; a few attributes carry a trailing comment or units.
            return float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        // ------------------------------------------------------------------ parsing

        public static KeyValues Parse(string text)
        {
            int pos = 0;
            var root = new KeyValues { Key = "root" };
            ParseBody(text, ref pos, root, topLevel: true);
            return root;
        }

        static void ParseBody(string text, ref int pos, KeyValues parent, bool topLevel)
        {
            while (true)
            {
                SkipTrivia(text, ref pos);
                if (pos >= text.Length) return;

                char c = text[pos];
                if (c == '}')
                {
                    if (!topLevel) pos++;
                    return;
                }
                if (c == ',' || c == ';') { pos++; continue; }
                if (c == '{')
                {
                    // KV3 files open with an anonymous root object.
                    pos++;
                    var anon = new KeyValues();
                    parent.Children.Add(anon);
                    ParseBody(text, ref pos, anon, topLevel: false);
                    continue;
                }

                string key = ReadToken(text, ref pos);
                if (key == null) return;

                SkipTrivia(text, ref pos);
                if (pos < text.Length && (text[pos] == '=' || text[pos] == ':'))
                {
                    pos++;                       // KV3 "key = value"
                    SkipTrivia(text, ref pos);
                }

                var node = new KeyValues { Key = Normalise(key) };
                parent.Children.Add(node);

                if (pos >= text.Length) return;

                char v = text[pos];
                if (v == '{')
                {
                    pos++;
                    ParseBody(text, ref pos, node, topLevel: false);
                }
                else if (v == '[')
                {
                    node.Value = ReadBracketed(text, ref pos);
                }
                else
                {
                    node.Value = ReadToken(text, ref pos) ?? string.Empty;
                }
            }
        }

        static void SkipTrivia(string text, ref int pos)
        {
            while (pos < text.Length)
            {
                char c = text[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }

                if (c == '/' && pos + 1 < text.Length)
                {
                    if (text[pos + 1] == '/')
                    {
                        while (pos < text.Length && text[pos] != '\n') pos++;
                        continue;
                    }
                    if (text[pos + 1] == '*')
                    {
                        pos += 2;
                        while (pos + 1 < text.Length && !(text[pos] == '*' && text[pos + 1] == '/')) pos++;
                        pos = Math.Min(pos + 2, text.Length);
                        continue;
                    }
                }

                // KV3 header: <!-- kv3 ... -->
                if (c == '<' && text.IndexOf("-->", pos, StringComparison.Ordinal) > pos)
                {
                    pos = text.IndexOf("-->", pos, StringComparison.Ordinal) + 3;
                    continue;
                }

                return;
            }
        }

        static string ReadToken(string text, ref int pos)
        {
            if (pos >= text.Length) return null;

            if (text[pos] == '"')
            {
                pos++;
                var sb = new StringBuilder();
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\\' && pos + 1 < text.Length) pos++;
                    sb.Append(text[pos++]);
                }
                pos++;                            // closing quote
                return sb.ToString();
            }

            int start = pos;
            while (pos < text.Length)
            {
                char c = text[pos];
                if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '=' || c == ',' || c == '"') break;
                pos++;
            }
            return pos > start ? text.Substring(start, pos - start) : null;
        }

        static string ReadBracketed(string text, ref int pos)
        {
            int start = pos;
            int depth = 0;
            while (pos < text.Length)
            {
                if (text[pos] == '[') depth++;
                else if (text[pos] == ']') { depth--; pos++; if (depth == 0) break; continue; }
                pos++;
            }
            return text.Substring(start, Math.Min(pos, text.Length) - start);
        }
    }
}
