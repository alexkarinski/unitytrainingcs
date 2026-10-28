using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CsAim.Core
{
    /// <summary>
    /// Reads CS2 config files and keeps what an aim trainer needs: convars (sensitivity,
    /// zoom_sensitivity_ratio, cl_crosshair*) and key binds. Both formats are supported:
    ///
    ///   *.cfg   (autoexec.cfg, config.cfg):   sensitivity "1.25"   /   bind "MOUSE5" "+jump"
    ///   *.vcfg  (cs2_user_convars_0_slot0.vcfg, cs2_user_keys_0_slot0.vcfg, user_keys_default.vcfg):
    ///           "convars" { "sensitivity" "1.25" }   /   "bindings" { "MOUSE1" "+attack" }
    ///
    /// Later files override earlier ones, like the game executing them in order.
    /// </summary>
    public sealed class Cs2Config
    {
        public readonly Dictionary<string, string> Convars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Key name -> command, in the order they were applied. Empty command = unbind.</summary>
        public readonly List<KeyValuePair<string, string>> Binds = new List<KeyValuePair<string, string>>();
        public readonly List<string> SourceNames = new List<string>();

        public bool IsEmpty => Convars.Count == 0 && Binds.Count == 0;

        /// <summary>Order in which a cfg folder is applied: stock defaults first, then the user's own files.</summary>
        public static int LoadPriority(string fileName)
        {
            string f = fileName.ToLowerInvariant();
            if (f.Contains("default")) return 0;
            if (f.StartsWith("cs2_machine_convars")) return 1;
            if (f.StartsWith("cs2_user_keys") || f.StartsWith("cs2_user_convars")) return 2;
            if (f == "config.cfg") return 3;
            if (f == "autoexec.cfg") return 5;
            return 4;
        }

        /// <summary>Loads a file or every .cfg/.vcfg in a folder (gamemode_*.cfg server configs are skipped).</summary>
        public static Cs2Config Load(string path)
        {
            var cfg = new Cs2Config();
            var files = new List<string>();
            if (Directory.Exists(path))
            {
                foreach (var f in Directory.GetFiles(path))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    if ((ext == ".cfg" || ext == ".vcfg") && !name.StartsWith("gamemode_") && !name.StartsWith("server"))
                        files.Add(f);
                }
                files.Sort((a, b) =>
                {
                    int pa = LoadPriority(Path.GetFileName(a)), pb = LoadPriority(Path.GetFileName(b));
                    return pa != pb ? pa.CompareTo(pb) : string.CompareOrdinal(a, b);
                });
            }
            else if (File.Exists(path))
            {
                files.Add(path);
            }

            foreach (var f in files) cfg.AddText(File.ReadAllText(f), Path.GetFileName(f));
            return cfg;
        }

        public void AddText(string text, string sourceName)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            SourceNames.Add(sourceName);

            // A .vcfg is a KeyValues file with a "bindings" or "convars" block; anything else is console syntax.
            if (Regex.IsMatch(text, "\"(bindings|convars)\"\\s*\\{", RegexOptions.IgnoreCase)) AddVcfg(text);
            else AddCfg(text);
        }

        void AddVcfg(string text)
        {
            // KeyValues normalises key names, which would mangle "MOUSE1"/"cl_crosshairsize"; read
            // the two blocks we need with a tiny raw tokenizer instead.
            var tokens = Tokenize(text);
            string block = null;
            int depth = 0, blockDepth = -1;
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i];
                if (t == "{") { depth++; continue; }
                if (t == "}") { if (depth == blockDepth) block = null; depth--; continue; }

                if (block == null)
                {
                    if (i + 1 < tokens.Count && tokens[i + 1] == "{" &&
                        (t.Equals("bindings", StringComparison.OrdinalIgnoreCase) || t.Equals("convars", StringComparison.OrdinalIgnoreCase)))
                    {
                        block = t.ToLowerInvariant();
                        blockDepth = depth + 1;
                    }
                    continue;
                }

                if (depth != blockDepth || i + 1 >= tokens.Count) continue;
                string v = tokens[i + 1];
                if (v == "{" || v == "}") continue;
                if (block == "bindings") Binds.Add(new KeyValuePair<string, string>(t, v));
                else Convars[t] = v;
                i++;
            }
        }

        void AddCfg(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                // A line may hold several commands separated by ';' outside quotes.
                foreach (var command in SplitCommands(StripComment(raw)))
                {
                    var args = Tokenize(command);
                    if (args.Count == 0) continue;
                    string head = args[0].ToLowerInvariant();

                    if (head == "bind" && args.Count >= 3) Binds.Add(new KeyValuePair<string, string>(args[1], args[2]));
                    else if (head == "unbind" && args.Count >= 2) Binds.Add(new KeyValuePair<string, string>(args[1], string.Empty));
                    else if (head == "unbindall") Binds.Add(new KeyValuePair<string, string>("*", string.Empty));
                    else if (args.Count >= 2 && head != "echo" && head != "exec" && head != "alias") Convars[args[0]] = args[1];
                }
            }
        }

        public bool TryGetFloat(string name, out float value)
        {
            value = 0f;
            return Convars.TryGetValue(name, out var raw) &&
                   float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public float GetFloat(string name, float fallback) => TryGetFloat(name, out var v) ? v : fallback;

        public bool GetBool(string name, bool fallback)
        {
            if (!Convars.TryGetValue(name, out var raw)) return fallback;
            raw = raw.Trim().ToLowerInvariant();
            if (raw == "true") return true;
            if (raw == "false") return false;
            return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f != 0f : fallback;
        }

        /// <summary>Serialises back to .cfg syntax so it can be stored and re-applied on next launch.</summary>
        public string ToCfgText()
        {
            var sb = new StringBuilder();
            foreach (var kv in Convars) sb.Append(kv.Key).Append(" \"").Append(kv.Value).Append("\"\n");
            foreach (var kv in Binds)
            {
                if (kv.Key == "*") sb.Append("unbindall\n");
                else if (kv.Value.Length == 0) sb.Append("unbind \"").Append(kv.Key).Append("\"\n");
                else sb.Append("bind \"").Append(kv.Key).Append("\" \"").Append(kv.Value).Append("\"\n");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ tokenizing

        static string StripComment(string line)
        {
            bool quoted = false;
            for (int i = 0; i < line.Length - 1; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (!quoted && line[i] == '/' && line[i + 1] == '/') return line.Substring(0, i);
            }
            return line;
        }

        static IEnumerable<string> SplitCommands(string line)
        {
            bool quoted = false;
            int start = 0;
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"') quoted = !quoted;
                else if (!quoted && line[i] == ';')
                {
                    yield return line.Substring(start, i - start);
                    start = i + 1;
                }
            }
            if (start < line.Length) yield return line.Substring(start);
        }

        static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c == '{' || c == '}') { result.Add(c.ToString()); i++; continue; }
                if (c == '"')
                {
                    int end = text.IndexOf('"', i + 1);
                    if (end < 0) end = text.Length;
                    result.Add(text.Substring(i + 1, end - i - 1));
                    i = end + 1;
                    continue;
                }
                int s = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '"' && text[i] != '{' && text[i] != '}') i++;
                result.Add(text.Substring(s, i - s));
            }
            return result;
        }
    }
}
