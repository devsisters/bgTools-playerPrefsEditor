﻿using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BgTools.Utils;
using BgTools.Dialogs;

namespace BgTools.PlayerPreferencesEditor
{
    public class PreferencesEditorWindow : EditorWindow
    {
        #region ErrorValues
        private readonly int ERROR_VALUE_INT = int.MinValue;
        private readonly string ERROR_VALUE_STR = "<bgTool_error_24072017>";
        #endregion //ErrorValues

        //private TabState tabState = TabState.PlayerPrefs;
        //private enum TabState
        //{
        //    PlayerPrefs,
        //    EditorPrefs
        //}

        private static string pathToPrefs = String.Empty;
        private static string platformPathPrefix = @"~";

        private string[] userDef;
        private string[] unityDef;
        private bool showSystemGroup = false;

        private SerializedObject serializedObject;
        private ReorderableList userDefList;
        private ReorderableList unityDefList;

        private PreferenceEntryHolder prefEntryHolder;

        private Vector2 scrollPos;

        private PreferanceStorageAccessor entryAccessor;

        private SearchField searchfield;
        private string searchTxt;

        private bool updateView = false;
        private bool monitoring = false;

        private readonly List<TextValidator> prefKeyValidatorList = new List<TextValidator>()
        {
            new TextValidator(TextValidator.ErrorType.Error, @"Invalid character detected. Only letters, numbers, space and _!§$%&/()=?*+~#-]+$ are allowed", @"(^$)|(^[a-zA-Z0-9 _!§$%&/()=?*+~#-]+$)"),
            new TextValidator(TextValidator.ErrorType.Warning, @"The given key already exist. The existing entry would be overridden!", (key) => { return !PlayerPrefs.HasKey(key); })
        };

        [MenuItem("Tools/BG Tools/Player Preferences Editor", false, 1)]
        static void ShowWindow()
        {
            PreferencesEditorWindow window = EditorWindow.GetWindow<PreferencesEditorWindow>(false, "Prefs Editor");
            window.minSize = new Vector2(400.0f, 300.0f);
            window.name = "Prefs Editor";

            //window.titleContent = EditorGUIUtility.IconContent("SettingsIcon"); // Icon

            window.Show();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR_WIN
            pathToPrefs = @"SOFTWARE\Unity\UnityEditor\" + PlayerSettings.companyName + @"\" + PlayerSettings.productName;
            platformPathPrefix = @"<CurrendUser>";
            entryAccessor = new WindowsPrefStorage(pathToPrefs);
#elif UNITY_EDITOR_OSX
            pathToPrefs = @"Library/Preferences/unity." + PlayerSettings.companyName + "." + PlayerSettings.productName + ".plist";
            entryAccessor = new MacEntryIndexer(pathToPrefs);
#elif UNITY_EDITOR_LINUX
            pathToPrefs = @".config/unity3d/" + PlayerSettings.companyName + "/" + PlayerSettings.productName + "/prefs";
            entryAccessor = new LinuxPrefStorage(pathToPrefs);
#endif
            entryAccessor.PrefEntryChangedDelegate = () => { updateView = true; };

            monitoring = EditorPrefs.GetBool("DevTools.PlayerPreferencesEditor.WatchingForChanges", false);
            if(monitoring)
                entryAccessor.StartMonitoring();

            searchfield = new SearchField();

            // Fix for serialisation issue of static fields
            if (userDefList == null)
            {
                InitReorderedList();
                PrepareData();
            }
        }

        // Handel view updates for monitored changes
        // Necessary to avoid main thread access issue
        private void Update()
        {
            bool currValue = EditorPrefs.GetBool("DevTools.PlayerPreferencesEditor.WatchingForChanges", false);

            if (monitoring != currValue)
            {
                monitoring = currValue;

                if (monitoring)
                    entryAccessor.StartMonitoring();
                else
                    entryAccessor.StopMonitoring();

                Repaint();
            }

            if (updateView)
            {
                updateView = false;
                PrepareData();
                Repaint();
            }
        }

        private void OnDisable()
        {
            entryAccessor.StopMonitoring();
        }

        private void InitReorderedList()
        {
            if (prefEntryHolder == null)
            {
                var tmp = Resources.FindObjectsOfTypeAll<PreferenceEntryHolder>();
                if (tmp.Length > 0)
                {
                    prefEntryHolder = tmp[0];
                }
                else
                {
                    prefEntryHolder = ScriptableObject.CreateInstance<PreferenceEntryHolder>();
                }
            }

            if (serializedObject == null)
            {
                serializedObject = new SerializedObject(prefEntryHolder);
            }

            userDefList = new ReorderableList(serializedObject, serializedObject.FindProperty("userDefList"), false, true, true, true);
            unityDefList = new ReorderableList(serializedObject, serializedObject.FindProperty("unityDefList"), false, true, false, false);

            userDefList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "User defined");
            };
            userDefList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = userDefList.serializedProperty.GetArrayElementAtIndex(index);
                SerializedProperty key = element.FindPropertyRelative("m_key");
                SerializedProperty type = element.FindPropertyRelative("m_typeSelection");
                SerializedProperty strValue = element.FindPropertyRelative("m_strValue");
                SerializedProperty intValue = element.FindPropertyRelative("m_intValue");
                SerializedProperty floatValue = element.FindPropertyRelative("m_floatValue");
                rect.y += 2;

                EditorGUI.BeginChangeCheck();
                EditorGUI.LabelField(new Rect(rect.x, rect.y, 100, EditorGUIUtility.singleLineHeight), new GUIContent(key.stringValue, key.stringValue));
                GUI.enabled = false;
                EditorGUI.PropertyField(new Rect(rect.x + 100, rect.y, 60, EditorGUIUtility.singleLineHeight), type, GUIContent.none);
                GUI.enabled = true;
                switch ((PreferenceEntry.PrefTypes)type.enumValueIndex)
                {
                    case PreferenceEntry.PrefTypes.Float:
                        EditorGUI.DelayedFloatField(new Rect(rect.x + 161, rect.y, rect.width - 160, EditorGUIUtility.singleLineHeight), floatValue, GUIContent.none);
                        break;
                    case PreferenceEntry.PrefTypes.Int:
                        EditorGUI.DelayedIntField(new Rect(rect.x + 161, rect.y, rect.width - 160, EditorGUIUtility.singleLineHeight), intValue, GUIContent.none);
                        break;
                    case PreferenceEntry.PrefTypes.String:
                        EditorGUI.DelayedTextField(new Rect(rect.x + 161, rect.y, rect.width - 160, EditorGUIUtility.singleLineHeight), strValue, GUIContent.none);
                        break;
                }
                if (EditorGUI.EndChangeCheck())
                {
                    entryAccessor.IgnoreNextChange();

                    switch ((PreferenceEntry.PrefTypes)type.enumValueIndex)
                    {
                        case PreferenceEntry.PrefTypes.Float:
                            PlayerPrefs.SetFloat(key.stringValue, floatValue.floatValue);
                            break;
                        case PreferenceEntry.PrefTypes.Int:
                            PlayerPrefs.SetInt(key.stringValue, intValue.intValue);
                            break;
                        case PreferenceEntry.PrefTypes.String:
                            PlayerPrefs.SetString(key.stringValue, strValue.stringValue);
                            break;
                    }

                    PlayerPrefs.Save();
                }
            };
            userDefList.onRemoveCallback = (ReorderableList l) =>
            {
                // ToDo: remove tabstate if clear that editorprefs not supported
                var tabState = "PlayerPrefs";

                userDefList.ReleaseKeyboardFocus();
                unityDefList.ReleaseKeyboardFocus();

                if (EditorUtility.DisplayDialog("Warning!", "Are you sure you want to delete this entry from " + tabState + "?", "Yes", "No"))
                {
                    entryAccessor.IgnoreNextChange();

                    PlayerPrefs.DeleteKey(l.serializedProperty.GetArrayElementAtIndex(l.index).FindPropertyRelative("m_key").stringValue);
                    PlayerPrefs.Save();

                    ReorderableList.defaultBehaviours.DoRemoveButton(l);
                    //PrepareData();
                }
            };

            userDefList.onAddDropdownCallback = (Rect buttonRect, ReorderableList l) =>
            {
                var menu = new GenericMenu();
                foreach (PreferenceEntry.PrefTypes type in Enum.GetValues(typeof(PreferenceEntry.PrefTypes)))
                {
                    menu.AddItem(new GUIContent(type.ToString()), false, () =>
                    {
                        TextFieldDialog.OpenDialog("Create new property", "Key for the new property:", prefKeyValidatorList, (key) => {

                            entryAccessor.IgnoreNextChange();

                            switch (type)
                            {
                                case PreferenceEntry.PrefTypes.Float:
                                    PlayerPrefs.SetFloat(key, 0.0f);

                                    break;
                                case PreferenceEntry.PrefTypes.Int:
                                    PlayerPrefs.SetInt(key, 0);

                                    break;
                                case PreferenceEntry.PrefTypes.String:
                                    PlayerPrefs.SetString(key, string.Empty);

                                    break;
                            }
                            PlayerPrefs.Save();

                            PrepareData();

                            Focus();
                        }, this);

                    });
                }
                menu.ShowAsContext();
            };

            unityDefList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = unityDefList.serializedProperty.GetArrayElementAtIndex(index);
                SerializedProperty key = element.FindPropertyRelative("m_key");
                SerializedProperty type = element.FindPropertyRelative("m_typeSelection");
                SerializedProperty strValue = element.FindPropertyRelative("m_strValue");
                SerializedProperty intValue = element.FindPropertyRelative("m_intValue");
                SerializedProperty floatValue = element.FindPropertyRelative("m_floatValue");
                rect.y += 2;

                GUI.enabled = false;
                EditorGUI.LabelField(new Rect(rect.x, rect.y, 100, EditorGUIUtility.singleLineHeight), new GUIContent(key.stringValue, key.stringValue));
                EditorGUI.PropertyField(new Rect(rect.x + 100, rect.y, rect.width - 100 - 231, EditorGUIUtility.singleLineHeight), type, GUIContent.none);

                switch ((PreferenceEntry.PrefTypes)type.enumValueIndex)
                {
                    case PreferenceEntry.PrefTypes.Float:
                        EditorGUI.DelayedFloatField(new Rect(rect.x + rect.width - 229, rect.y, 229, EditorGUIUtility.singleLineHeight), floatValue, GUIContent.none);
                        break;
                    case PreferenceEntry.PrefTypes.Int:
                        EditorGUI.DelayedIntField(new Rect(rect.x + rect.width - 229, rect.y, 229, EditorGUIUtility.singleLineHeight), intValue, GUIContent.none);
                        break;
                    case PreferenceEntry.PrefTypes.String:
                        EditorGUI.DelayedTextField(new Rect(rect.x + rect.width - 229, rect.y, 229, EditorGUIUtility.singleLineHeight), strValue, GUIContent.none);
                        break;
                }
                GUI.enabled = true;
            };
            unityDefList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Unity defined");
            };
        }

        void OnGUI()
        {
            // Need to catch 'Stack empty' error on linux
            try
            {
                Color defaultColor = GUI.contentColor;

                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();

                GUI.contentColor = (EditorGUIUtility.isProSkin) ? Styles.Colors.LightGray : Styles.Colors.DarkGray;
                GUILayout.Box(ImageManager.GetOsIcon(), Styles.icon);
                GUI.contentColor = defaultColor;

                GUILayout.TextField(platformPathPrefix + Path.DirectorySeparatorChar + pathToPrefs, GUILayout.MinWidth(200));

                GUI.contentColor = (EditorGUIUtility.isProSkin) ? Styles.Colors.LightGray : Styles.Colors.DarkGray;
                if (GUILayout.Button(new GUIContent(ImageManager.Refresh, "Refresh"), Styles.miniButton))
                {
                    PlayerPrefs.Save();
                    PrepareData();
                }
                if (GUILayout.Button(new GUIContent(ImageManager.Trash, "Delete all"), Styles.miniButton))
                {
                    // ToDo: remove tabstate if clear that editorprefs not supported
                    var tabState = "PlayerPrefs";
                    if (EditorUtility.DisplayDialog("Warning!", "Are you sure you want to delete ALL entries from " + tabState + "?\n\nUse with caution! Unity defined keys are affected too.", "Yes", "No"))
                    {
                        PlayerPrefs.DeleteAll();
                        PrepareData();
                    }
                }
                GUI.contentColor = defaultColor;

                GUILayout.EndHorizontal();

                EditorGUI.BeginChangeCheck();
                searchTxt = searchfield.OnGUI(searchTxt);
                if (EditorGUI.EndChangeCheck())
                {
                    PrepareData(false);
                }

                GUILayout.Space(3);

                //GUILayout.BeginHorizontal();

                //if (GUILayout.Toggle(tabState == TabState.PlayerPrefs, "PlayerPrefs", EditorStyles.toolbarButton))
                //    tabState = TabState.PlayerPrefs;

                //GUI.enabled = false;
                //if (GUILayout.Toggle(tabState == TabState.EditorPrefs, "EditorPrefs", EditorStyles.toolbarButton))
                //    tabState = TabState.EditorPrefs;
                //GUI.enabled = true;

                //GUILayout.EndHorizontal();

                scrollPos = GUILayout.BeginScrollView(scrollPos);
                serializedObject.Update();
                userDefList.DoLayoutList();
                serializedObject.ApplyModifiedProperties();

                GUILayout.FlexibleSpace();

                showSystemGroup = EditorGUILayout.Foldout(showSystemGroup, new GUIContent("Show System"));
                if (showSystemGroup)
                {
                    unityDefList.DoLayoutList();
                }
                GUILayout.EndScrollView();

                GUI.contentColor = (EditorGUIUtility.isProSkin) ? Styles.Colors.LightGray : Styles.Colors.DarkGray;

                GUIContent watcherContent = (entryAccessor.IsMonitoring()) ? new GUIContent(ImageManager.Watching, "Watch changes") : new GUIContent(ImageManager.NotWatching, "Not watching changes");
                GUILayout.Box(watcherContent, Styles.icon);

                GUI.contentColor = defaultColor;

                GUILayout.EndVertical();
            }
            catch (InvalidOperationException)
            { }
        }

        private void PrepareData(bool reloadKeys = true)
        {
            prefEntryHolder.ClearLists();

            LoadKeys(out userDef, out unityDef, reloadKeys);

            CreatePrefEntries(userDef, prefEntryHolder.userDefList);
            CreatePrefEntries(unityDef, prefEntryHolder.unityDefList);
        }

        private void CreatePrefEntries(string[] keySource, List<PreferenceEntry> listDest)
        {
            if (!string.IsNullOrEmpty(searchTxt))
            {
                keySource = keySource.Where((a) => a.ToLower().Contains(searchTxt.ToLower())).ToArray();
            }

            foreach (string key in keySource)
            {
                var entry = new PreferenceEntry();
                entry.m_key = key;

                string s = PlayerPrefs.GetString(key, ERROR_VALUE_STR);

                if (s != ERROR_VALUE_STR)
                {
                    entry.m_strValue = s;
                    entry.m_typeSelection = PreferenceEntry.PrefTypes.String;
                    listDest.Add(entry);
                    continue;
                }

                float f = PlayerPrefs.GetFloat(key, float.NaN);
                if (!float.IsNaN(f))
                {
                    entry.m_floatValue = f;
                    entry.m_typeSelection = PreferenceEntry.PrefTypes.Float;
                    listDest.Add(entry);
                    continue;
                }

                int i = PlayerPrefs.GetInt(key, ERROR_VALUE_INT);
                if (i != ERROR_VALUE_INT)
                {
                    entry.m_intValue = i;
                    entry.m_typeSelection = PreferenceEntry.PrefTypes.Int;
                    listDest.Add(entry);
                    continue;
                }
            }
        }

        private void LoadKeys(out string[] userDef, out string[] unityDef, bool reloadKeys)
        {
            string[] keys = entryAccessor.GetKeys(reloadKeys);

            // keys.ToList().ForEach( e => { Debug.Log(e); } );

            // Seperate keys int unity defined and user defined
            Dictionary<bool, List<string>> groups = keys
                .GroupBy( (key) => key.StartsWith("unity.") || key.StartsWith("UnityGraphicsQuality") )
                .ToDictionary( (g) => g.Key, (g) => g.ToList() );

            unityDef = (groups.ContainsKey(true)) ? groups[true].ToArray() : new string[0];
            userDef = (groups.ContainsKey(false)) ? groups[false].ToArray() : new string[0];
        }
    }
}