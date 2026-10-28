using System;
using System.Collections.Generic;
using System.IO;
using CsAim.Core;
using UnityEngine;

namespace CsAim.Data
{
    /// <summary>
    /// Pulls weapon stats out of the player's own CS2 install, so the trainer uses the exact
    /// numbers the game uses instead of anything hard-coded here.
    ///
    /// Supported inputs:
    ///   game/csgo/pak01_dir.vpk (or the game/csgo folder) - CS2 keeps its scripts inside the VPK,
    ///                            items_game.txt and any text .vdata are read straight from it
    ///   items_game.txt           (VDF, prefab inheritance; CS:GO had it loose on disk)
    ///   *.vdata or a folder of them, e.g. decompiled with Source 2 Viewer (KV3, [primary, alt])
    ///
    /// The file is read locally and never redistributed (it's Valve's content), which is why the
    /// repository .gitignore excludes it.
    /// </summary>
    public static class ItemsGameImporter
    {
        /// <summary>Default CS2 install location on Windows, used as a hint by the UI.</summary>
        public const string DefaultItemsGamePath =
            @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk";

        const string ItemsGameInVpk = "scripts/items/items_game.txt";

        public static List<WeaponDefinition> ImportFile(string path) => ImportFile(path, out _);

        /// <param name="report">Human-readable summary of what was found, for the UI.</param>
        public static List<WeaponDefinition> ImportFile(string path, out string report)
        {
            path = path.Trim().Trim('"');

            if (Directory.Exists(path))
            {
                // game/csgo, the game root, or a folder of extracted .vdata files.
                foreach (var candidate in new[] { "pak01_dir.vpk", Path.Combine("game", "csgo", "pak01_dir.vpk"), Path.Combine("csgo", "pak01_dir.vpk") })
                {
                    string vpk = Path.Combine(path, candidate);
                    if (File.Exists(vpk)) return ImportFile(vpk, out report);
                }

                var fromFolder = new List<WeaponDefinition>();
                foreach (var f in Directory.GetFiles(path, "*.vdata", SearchOption.AllDirectories))
                    fromFolder.AddRange(Tag(ImportVDataText(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f)), f));
                string items = Path.Combine(path, "items_game.txt");
                if (fromFolder.Count == 0 && File.Exists(items)) return ImportFile(items, out report);

                report = fromFolder.Count > 0
                    ? $"{fromFolder.Count} weapons from .vdata files in the folder"
                    : "No pak01_dir.vpk, items_game.txt or .vdata files in that folder.";
                return fromFolder;
            }

            if (path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) return ImportVpk(path, out report);

            string text = File.ReadAllText(path);
            var result = path.EndsWith(".vdata", StringComparison.OrdinalIgnoreCase)
                ? ImportVDataText(text, Path.GetFileNameWithoutExtension(path))
                : ImportItemsGame(KeyValues.Parse(text));
            report = $"{result.Count} weapons from {Path.GetFileName(path)}";
            return Tag(result, path);
        }

        static List<WeaponDefinition> ImportVpk(string vpkPath, out string report)
        {
            if (!vpkPath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
            {
                report = "Pick the *_dir.vpk file (e.g. pak01_dir.vpk), not a numbered pak01_NNN.vpk.";
                return new List<WeaponDefinition>();
            }

            var vpk = VpkArchive.Open(vpkPath);
            var byId = new Dictionary<string, WeaponDefinition>();
            string where = Path.GetFileName(vpkPath);

            // 1. items_game.txt prefabs (still the source of truth wherever it carries stats).
            if (vpk.Contains(ItemsGameInVpk))
                foreach (var w in ImportItemsGame(KeyValues.Parse(vpk.ReadText(ItemsGameInVpk))))
                    byId[w.weaponId] = Tag(w, $"{where}/{ItemsGameInVpk}");

            // 2. Text weapon .vdata, if this build ships any, override per weapon.
            int compiled = 0;
            foreach (var file in vpk.Files)
            {
                if (!file.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.EndsWith(".vdata_c", StringComparison.OrdinalIgnoreCase)) { if (file.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0) compiled++; continue; }
                if (!file.EndsWith(".vdata", StringComparison.OrdinalIgnoreCase) || file.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) < 0) continue;
                foreach (var w in ImportVDataText(vpk.ReadText(file), Path.GetFileNameWithoutExtension(file)))
                    byId[w.weaponId] = Tag(w, $"{where}/{file}");
            }

            var list = new List<WeaponDefinition>(byId.Values);
            report = list.Count > 0
                ? $"{list.Count} weapons read from {where} ({vpk.Count} files in archive)"
                : $"{where}: no weapon stats in items_game.txt and no text .vdata. " +
                  (compiled > 0
                      ? $"This CS2 build keeps them in {compiled} compiled .vdata_c file(s): decompile scripts/weapons*.vdata_c with Source 2 Viewer and import the resulting .vdata (or its folder)."
                      : "Weapon data was not found in this archive.");
            return list;
        }

        static List<WeaponDefinition> Tag(List<WeaponDefinition> list, string source)
        {
            foreach (var w in list) Tag(w, source);
            return list;
        }

        static WeaponDefinition Tag(WeaponDefinition w, string source)
        {
            w.source = source;
            return w;
        }

        // ------------------------------------------------------------------ items_game.txt

        static List<WeaponDefinition> ImportItemsGame(KeyValues root)
        {
            var itemsGame = root.FindChild("items_game") ?? root;
            var prefabs = itemsGame.FindChild("prefabs");
            var items = itemsGame.FindChild("items");
            var output = new List<WeaponDefinition>();
            if (prefabs == null || items == null)
            {
                Debug.LogWarning("[CS Aim] items_game.txt: 'prefabs' or 'items' block not found.");
                return output;
            }

            var prefabByName = new Dictionary<string, KeyValues>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in prefabs.Children) prefabByName[p.Key] = p;

            foreach (var item in items.Children)
            {
                string name = item.GetString("name");
                if (name == null || !name.StartsWith("weapon_", StringComparison.Ordinal)) continue;
                if (name.Contains("knife") || name.Contains("bayonet") || name == "weapon_c4" || name == "weapon_taser") continue;

                // Flatten the prefab chain: parents first, then the item itself overrides.
                var attrs = new Dictionary<string, string>();
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectPrefab(item.GetString("prefab"), prefabByName, attrs, visited);
                MergeAttributes(item, attrs);

                if (!attrs.ContainsKey("cycletime") && !attrs.ContainsKey("damage")) continue; // grenades, gear

                var w = ScriptableObject.CreateInstance<WeaponDefinition>();
                w.weaponId = name;
                w.name = name;
                w.displayName = Prettify(item.GetString("item_name") ?? name);
                Apply(w, new AttributeSource(attrs));
                output.Add(w);
            }

            return output;
        }

        static void CollectPrefab(string prefabList, Dictionary<string, KeyValues> prefabs,
                                  Dictionary<string, string> attrs, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(prefabList)) return;

            // An item may name several prefabs separated by spaces; later ones win.
            foreach (var prefabName in prefabList.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = KeyValues.Normalise(prefabName);
                if (!visited.Add(key) || !prefabs.TryGetValue(key, out var prefab)) continue;

                CollectPrefab(prefab.GetString("prefab"), prefabs, attrs, visited);
                MergeAttributes(prefab, attrs);
            }
        }

        static void MergeAttributes(KeyValues node, Dictionary<string, string> attrs)
        {
            var block = node.FindChild("attributes");
            if (block == null) return;
            foreach (var a in block.Children)
            {
                // Some attributes are { "attribute_class" ... "value" "x" } blocks.
                string v = a.HasChildren ? a.GetString("value") : a.Value;
                if (v != null) attrs[a.Key] = v;
            }
        }

        // ------------------------------------------------------------------ CS2 .vdata

        /// <summary>
        /// A .vdata holds either one weapon (weapon_ak47.vdata) or many keyed by class name
        /// (weapons.vdata: weapon_ak47 = { ... }, weapon_m4a1 = { ... }).
        /// </summary>
        public static List<WeaponDefinition> ImportVDataText(string text, string fileName)
        {
            var result = new List<WeaponDefinition>();
            if (string.IsNullOrEmpty(text) || text.IndexOf('\0') >= 0) return result;   // compiled/binary resource

            var root = KeyValues.Parse(text);
            var body = root.Children.Count == 1 && root.Children[0].Key.Length == 0 ? root.Children[0] : root;

            foreach (var child in body.Children)
            {
                if (!child.HasChildren || !child.Key.StartsWith("weapon_", StringComparison.Ordinal)) continue;
                var w = FromVData(child, child.Key);
                if (w != null) result.Add(w);
            }

            if (result.Count == 0)
            {
                var single = FromVData(root, fileName.StartsWith("weapon_") ? fileName : "weapon_" + fileName);
                if (single != null) result.Add(single);
            }
            return result;
        }

        static WeaponDefinition FromVData(KeyValues node, string id)
        {
            var attrs = new Dictionary<string, string>();
            Flatten(node, attrs);
            if (!attrs.ContainsKey("cycle_time") && !attrs.ContainsKey("cycletime") && !attrs.ContainsKey("damage")) return null;

            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            w.weaponId = id;
            w.name = id;
            w.displayName = Prettify(id);
            Apply(w, new AttributeSource(attrs));
            return w;
        }

        static void Flatten(KeyValues node, Dictionary<string, string> attrs)
        {
            foreach (var c in node.Children)
            {
                if (c.HasChildren) Flatten(c, attrs);
                else if (!attrs.ContainsKey(c.Key)) attrs[c.Key] = c.Value;
            }
        }

        // ------------------------------------------------------------------ mapping

        /// <summary>Resolves an attribute under any of its known spellings, in primary or alt mode.</summary>
        sealed class AttributeSource
        {
            readonly Dictionary<string, string> _a;
            public AttributeSource(Dictionary<string, string> a) { _a = a; }

            public bool TryGet(out float primary, out float alt, params string[] names)
            {
                primary = alt = 0f;
                foreach (var n in names)
                {
                    if (!_a.TryGetValue(n, out var raw)) continue;

                    if (raw.StartsWith("[", StringComparison.Ordinal))
                    {
                        // KV3 array: [ primary, alt ]
                        var parts = raw.Trim('[', ']', ' ').Split(new[] { ',', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 0 || !KeyValues.TryParseFloat(parts[0], out primary)) continue;
                        alt = parts.Length > 1 && KeyValues.TryParseFloat(parts[1], out var a2) ? a2 : primary;
                        return true;
                    }

                    if (!KeyValues.TryParseFloat(raw, out primary)) continue;
                    alt = _a.TryGetValue(n + "_alt", out var rawAlt) && KeyValues.TryParseFloat(rawAlt, out var a) ? a : primary;
                    return true;
                }
                return false;
            }

            public void Read(ref float primaryField, ref float altField, params string[] names)
            {
                if (TryGet(out var p, out var a, names)) { primaryField = p; altField = a; }
            }

            public void Read(ref float field, params string[] names)
            {
                if (TryGet(out var p, out _, names)) field = p;
            }

            public bool Has(string name) => _a.ContainsKey(name);
        }

        static void Apply(WeaponDefinition w, AttributeSource s)
        {
            ref var p = ref w.primary;
            ref var a = ref w.secondary;

            s.Read(ref w.damage, "damage");
            s.Read(ref w.armorRatio, "armor_ratio");
            s.Read(ref w.penetration, "penetration");
            s.Read(ref w.range, "range");
            s.Read(ref w.rangeModifier, "range_modifier");
            s.Read(ref w.headshotMultiplier, "headshot_multiplier");
            s.Read(ref w.reloadTime, "reload_time");

            float tmp = 0f;
            if (s.TryGet(out tmp, out _, "bullets", "num_bullets")) w.bullets = Mathf.Max(1, Mathf.RoundToInt(tmp));
            if (s.TryGet(out tmp, out _, "primary_clip_size", "max_clip1")) w.clipSize = Mathf.RoundToInt(tmp);
            if (s.TryGet(out tmp, out _, "is_full_auto")) w.isFullAuto = tmp > 0.5f;
            if (s.TryGet(out tmp, out _, "recoil_seed")) w.recoilSeed = Mathf.RoundToInt(tmp);
            if (s.TryGet(out tmp, out _, "recovery_transition_start_bullet")) w.recoveryTransitionStartBullet = Mathf.RoundToInt(tmp);
            if (s.TryGet(out tmp, out _, "recovery_transition_end_bullet")) w.recoveryTransitionEndBullet = Mathf.RoundToInt(tmp);

            s.Read(ref p.cycleTime, ref a.cycleTime, "cycletime", "cycle_time");
            s.Read(ref p.maxPlayerSpeed, ref a.maxPlayerSpeed, "max_player_speed", "max_speed");
            s.Read(ref p.spread, ref a.spread, "spread");
            s.Read(ref p.inaccuracyCrouch, ref a.inaccuracyCrouch, "inaccuracy_crouch");
            s.Read(ref p.inaccuracyStand, ref a.inaccuracyStand, "inaccuracy_stand");
            s.Read(ref p.inaccuracyJump, ref a.inaccuracyJump, "inaccuracy_jump", "inaccuracy_jump_initial");
            s.Read(ref p.inaccuracyLand, ref a.inaccuracyLand, "inaccuracy_land");
            s.Read(ref p.inaccuracyLadder, ref a.inaccuracyLadder, "inaccuracy_ladder");
            s.Read(ref p.inaccuracyFire, ref a.inaccuracyFire, "inaccuracy_fire");
            s.Read(ref p.inaccuracyMove, ref a.inaccuracyMove, "inaccuracy_move");
            s.Read(ref p.recoilAngle, ref a.recoilAngle, "recoil_angle");
            s.Read(ref p.recoilAngleVariance, ref a.recoilAngleVariance, "recoil_angle_variance");
            s.Read(ref p.recoilMagnitude, ref a.recoilMagnitude, "recoil_magnitude");
            s.Read(ref p.recoilMagnitudeVariance, ref a.recoilMagnitudeVariance, "recoil_magnitude_variance");

            s.Read(ref w.recoveryTimeCrouch, "recovery_time_crouch");
            s.Read(ref w.recoveryTimeStand, "recovery_time_stand");
            w.recoveryTimeCrouchFinal = w.recoveryTimeCrouch;
            w.recoveryTimeStandFinal = w.recoveryTimeStand;
            s.Read(ref w.recoveryTimeCrouchFinal, "recovery_time_crouch_final");
            s.Read(ref w.recoveryTimeStandFinal, "recovery_time_stand_final");

            float z1 = w.zoomFov.x, z2 = w.zoomFov.y;
            s.Read(ref z1, "zoom_fov_1");
            s.Read(ref z2, "zoom_fov_2");
            w.zoomFov = new Vector2(z1, z2);

            w.altIsScope = s.TryGet(out tmp, out _, "zoom_levels") && tmp > 0.5f;
            w.hasAltMode = w.altIsScope || (s.TryGet(out tmp, out _, "has_silencer") && tmp > 0.5f);
        }

        static string Prettify(string raw)
        {
            if (raw.StartsWith("#")) raw = raw.Substring(1);
            raw = raw.Replace("SFUI_WPNHUD_", "").Replace("weapon_", "");
            return raw.Length == 0 ? raw : char.ToUpperInvariant(raw[0]) + raw.Substring(1);
        }
    }
}
