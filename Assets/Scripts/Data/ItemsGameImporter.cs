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
    ///   game/csgo/scripts/items/items_game.txt   (VDF, prefab inheritance)
    ///   weapon_*.vdata extracted from pak01      (KV3, values often stored as [primary, alt])
    ///
    /// The file is read locally and never redistributed (it's Valve's content), which is why the
    /// repository .gitignore excludes it.
    /// </summary>
    public static class ItemsGameImporter
    {
        /// <summary>Default install path on Windows, used as a hint by the UI.</summary>
        public const string DefaultItemsGamePath =
            @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\scripts\items\items_game.txt";

        public static List<WeaponDefinition> ImportFile(string path)
        {
            string text = File.ReadAllText(path);
            var root = KeyValues.Parse(text);

            var result = path.EndsWith(".vdata", StringComparison.OrdinalIgnoreCase)
                ? ImportVData(root, Path.GetFileNameWithoutExtension(path))
                : ImportItemsGame(root);

            foreach (var w in result) w.source = $"{Path.GetFileName(path)} ({path})";
            return result;
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

        static List<WeaponDefinition> ImportVData(KeyValues root, string fileName)
        {
            var attrs = new Dictionary<string, string>();
            Flatten(root, attrs);

            var w = ScriptableObject.CreateInstance<WeaponDefinition>();
            w.weaponId = fileName.StartsWith("weapon_") ? fileName : "weapon_" + fileName;
            w.name = w.weaponId;
            w.displayName = Prettify(w.weaponId);
            Apply(w, new AttributeSource(attrs));
            return new List<WeaponDefinition> { w };
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
