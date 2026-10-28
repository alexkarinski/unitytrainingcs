using System.IO;
using CsAim.Data;
using UnityEditor;
using UnityEngine;

namespace CsAim.EditorTools
{
    /// <summary>
    /// Tools > CS Aim > Import items_game.txt: parses the file from your CS2 install and saves
    /// one WeaponDefinition asset per weapon into Assets/Resources/Weapons, where WeaponLibrary
    /// picks them up first. Existing assets are updated in place so a measured pattern you
    /// pasted into one survives a re-import.
    /// </summary>
    public static class ItemsGameImportMenu
    {
        const string OutputFolder = "Assets/Resources/Weapons";

        [MenuItem("Tools/CS Aim/Import items_game.txt or .vdata...")]
        static void Import()
        {
            string start = File.Exists(ItemsGameImporter.DefaultItemsGamePath)
                ? Path.GetDirectoryName(ItemsGameImporter.DefaultItemsGamePath)
                : string.Empty;
            string path = EditorUtility.OpenFilePanel("CS2 items_game.txt or weapon .vdata", start, "txt,vdata");
            if (string.IsNullOrEmpty(path)) return;

            var weapons = ItemsGameImporter.ImportFile(path);
            if (weapons.Count == 0)
            {
                EditorUtility.DisplayDialog("CS Aim", "No weapons found in that file.", "OK");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            int created = 0, updated = 0;
            foreach (var w in weapons)
            {
                string assetPath = $"{OutputFolder}/{w.weaponId}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(assetPath);
                if (existing != null)
                {
                    string keepPattern = existing.measuredPatternCsv;
                    EditorUtility.CopySerialized(w, existing);
                    existing.measuredPatternCsv = keepPattern;
                    existing.name = w.weaponId;
                    EditorUtility.SetDirty(existing);
                    updated++;
                }
                else
                {
                    AssetDatabase.CreateAsset(w, assetPath);
                    created++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("CS Aim", $"Imported {weapons.Count} weapons ({created} new, {updated} updated) into {OutputFolder}.", "OK");
        }

        [MenuItem("Tools/CS Aim/Create fallback weapon assets")]
        static void CreateFallbacks()
        {
            Directory.CreateDirectory(OutputFolder);
            foreach (var w in WeaponLibrary.Fallbacks())
            {
                string assetPath = $"{OutputFolder}/{w.weaponId}.asset";
                if (AssetDatabase.LoadAssetAtPath<WeaponDefinition>(assetPath) == null)
                    AssetDatabase.CreateAsset(w, assetPath);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
