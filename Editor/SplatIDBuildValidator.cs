#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Splats.Editor {
    public class SplatIDBuildValidator : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            ValidateDefIds();
        }

        static void ValidateDefIds() {
            // Find all Def assets in the project
            string[] guids = AssetDatabase.FindAssets("t:Splat");

            Dictionary<uint, string> idToPath = new();
            List<string>             errors   = new();

            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ISplatSettings def = AssetDatabase.LoadAssetAtPath<SplatSettings>(path);

                if (def == null) continue;

                uint id = def.ID;
                if (id == 0) {
                    errors.Add($"ID of 0 is reserved:\n-{path}");
                } else if (idToPath.TryGetValue(id, out string existingPath)) {
                    errors.Add($"Duplicate Splat ID {id}:\n- {existingPath}\n- {path}");
                } else {
                    idToPath[id] = path;
                }
            }

            if (errors.Count > 0) {
                string msg = "\n❌ Build aborted! Duplicate Def IDs detected:\n\n" + string.Join("\n\n", errors);
                Debug.LogError(msg);
                throw new BuildFailedException("Duplicate Def IDs found. See console for details.");
            }

            Debug.Log("✔ All Def IDs validated. No duplicates found.");
        }
    }
}
#endif
