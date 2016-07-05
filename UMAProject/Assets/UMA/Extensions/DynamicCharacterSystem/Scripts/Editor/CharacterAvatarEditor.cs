using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UMA;
using UMACharacterSystem;

[CustomEditor(typeof(DynamicCharacterAvatar), true)]
public partial class DynamicCharacterAvatarEditor : Editor
{
    protected DynamicCharacterAvatar thisDCA;
    private RaceSetterPropertyDrawer _racePropDrawer = new RaceSetterPropertyDrawer();

    public void OnEnable()
    {
        thisDCA = target as DynamicCharacterAvatar;
        //Set this DynamicCharacterAvatar for RaceSetter so if the user chages the race dropdown the race changes
        if(_racePropDrawer.thisDCA == null)
        {
            _racePropDrawer.thisDCA = thisDCA;
            //Set the raceLibrary for the race setter
            var context = UMAContext.FindInstance();
            var dynamicRaceLibrary = (DynamicRaceLibrary)context.raceLibrary as DynamicRaceLibrary;
            _racePropDrawer.thisDynamicRaceLibrary = dynamicRaceLibrary;
        }
    }

    protected bool characterAvatarLoadSaveOpen;
    public override void OnInspectorGUI()
    {
        Editor.DrawPropertiesExcluding(serializedObject, new string[] { "activeRace","preloadWardrobeRecipes", "raceAnimationControllers", "characterColors", "loadFileOnStart", "loadPathType", "loadPath", "loadFilename","waitForBundles", "savePathType", "savePath", "saveFilename", "makeUnique", "BoundsOffset" });
        serializedObject.ApplyModifiedProperties();
        SerializedProperty thisRaceSetter = serializedObject.FindProperty("activeRace");
        Rect currentRect = EditorGUILayout.GetControlRect(false, _racePropDrawer.GetPropertyHeight(thisRaceSetter, GUIContent.none));
        EditorGUI.BeginChangeCheck();
        _racePropDrawer.OnGUI(currentRect, thisRaceSetter, new GUIContent(thisRaceSetter.displayName));
        //Other DCA propertyDrawers
        EditorGUILayout.PropertyField(serializedObject.FindProperty("preloadWardrobeRecipes"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("raceAnimationControllers"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("characterColors"),true);
        //Load save fields
        SerializedProperty loadPathType = serializedObject.FindProperty("loadPathType");
        loadPathType.isExpanded = EditorGUILayout.Foldout(loadPathType.isExpanded, "Load/Save Options");
        if (loadPathType.isExpanded)
        {
            SerializedProperty loadPath = serializedObject.FindProperty("loadPath");
            SerializedProperty loadFilename = serializedObject.FindProperty("loadFilename");
            SerializedProperty loadFileOnStart = serializedObject.FindProperty("loadFileOnStart");
            SerializedProperty waitForBundles = serializedObject.FindProperty("waitForBundles");
            SerializedProperty savePathType = serializedObject.FindProperty("savePathType");
            SerializedProperty savePath = serializedObject.FindProperty("savePath");
            SerializedProperty saveFilename = serializedObject.FindProperty("saveFilename");
            SerializedProperty makeUnique = serializedObject.FindProperty("makeUnique");
            EditorGUILayout.PropertyField(loadPathType);
            if (loadPathType.enumValueIndex <= 2)
            {
                EditorGUILayout.PropertyField(loadPath);
            }
            EditorGUILayout.PropertyField(loadFilename);
            if (loadFilename.stringValue != "")
            {
                EditorGUILayout.PropertyField(loadFileOnStart);
            }
            EditorGUILayout.PropertyField(waitForBundles);
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Perform Load"))
                {
                    thisDCA.DoLoad();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(savePathType);
            if (savePathType.enumValueIndex <= 2)
            {
                EditorGUILayout.PropertyField(savePath);
            }
            EditorGUILayout.PropertyField(saveFilename);
            EditorGUILayout.PropertyField(makeUnique);
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Perform Save"))
                {
                    thisDCA.DoSave();
                }
            }
            EditorGUILayout.Space();
        }
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("BoundsOffset"));
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
        }
        if (Application.isPlaying)
        {
            EditorGUILayout.LabelField("AssetBundles used by Avatar");
            SerializedProperty assetBundlesUsedbyCharacter = serializedObject.FindProperty("assetBundlesUsedbyCharacter");
            string assetBundlesUsed = "";
            if (assetBundlesUsedbyCharacter.arraySize == 0)
            {
                assetBundlesUsed = "None";
            }
            else
            {
                for (int i = 0; i < assetBundlesUsedbyCharacter.arraySize; i++)
                {
                    assetBundlesUsed = assetBundlesUsed + assetBundlesUsedbyCharacter.GetArrayElementAtIndex(i).stringValue;
                    if (i < (assetBundlesUsedbyCharacter.arraySize - 1))
                        assetBundlesUsed = assetBundlesUsed + "\n";
                }
            }
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(assetBundlesUsed);
            EditorGUI.EndDisabledGroup();
        }
    }
}

