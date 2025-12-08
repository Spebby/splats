using UnityEditor;
using UnityEngine;


namespace Splats.Editor {
    [CustomEditor(typeof(SplatsConfig))]
    public class SplatConfigEditor : UnityEditor.Editor {
        UnityEditor.Editor _settingsEditor;
        bool _cmsFoldout;

        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            SplatsConfig config = (SplatsConfig)target;

            bool drawCM       = config.cm_Settings;
            bool drawnFoldout = drawCM;

            if (drawnFoldout) {
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // draw horizontal line
            }
            
            if (!config!.cm_Settings) return;
            DrawSettingsEditor(config.cm_Settings, ref _cmsFoldout, ref _settingsEditor);
            EditorPrefs.SetBool(nameof(_cmsFoldout), _cmsFoldout);
        }
        
        static void DrawSettingsEditor(Object settings, ref bool foldout, ref UnityEditor.Editor editor) {
            if (!settings) return;
            foldout = EditorGUILayout.InspectorTitlebar(foldout, settings);
            if (!foldout) return;
            CreateCachedEditor(settings, null, ref editor);
            editor.OnInspectorGUI();
        }

        void OnEnable () {
            _cmsFoldout = EditorPrefs.GetBool (nameof (_cmsFoldout), false);
        }
    }
}
