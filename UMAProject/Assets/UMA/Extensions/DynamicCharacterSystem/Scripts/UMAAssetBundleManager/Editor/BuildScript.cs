using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.Collections;
using System.Collections.Generic;
using System.IO;
//for AES128 encryption of assetBundles
using System.Text;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using UMA;

namespace UMAAssetBundleManager
{
    public class BuildScript
    {
        public static string overloadedDevelopmentServerURL = "";

        static public string CreateAssetBundleDirectory()
        {
            // Choose the output path according to the build target.
            string outputPath = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            return outputPath;
        }

        public static void BuildAssetBundles()
        {
            // Choose the output path according to the build target.
            string outputPath = CreateAssetBundleDirectory();

            var options = BuildAssetBundleOptions.None;

            bool shouldCheckODR = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS;
#if UNITY_TVOS
            shouldCheckODR |= EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS;
#endif
            if (shouldCheckODR)
            {
#if ENABLE_IOS_ON_DEMAND_RESOURCES
                if (PlayerSettings.iOS.useOnDemandResources)
                    options |= BuildAssetBundleOptions.UncompressedAssetBundle;
#endif
#if ENABLE_IOS_APP_SLICING
                options |= BuildAssetBundleOptions.UncompressedAssetBundle;
#endif
            }
            
            //here we build a PlatformName.index file that contains an index of all the assets in each bundle...
            //You can add to the AssetBundleIndex partial class to customize what data is saved for what assets.
            AssetBundleIndex thisIndex = ScriptableObject.CreateInstance<AssetBundleIndex>();
            string[] assetBundleNamesArray = AssetDatabase.GetAllAssetBundleNames();
            //DOS NOTES: To do encrypted asset bundles I think we will need to create our own buildmap so after the bundles have been 
            //created we can find them in the file system and encrypt them
            AssetBundleBuild[] buildMap = new AssetBundleBuild[assetBundleNamesArray.Length + 1];//+1 for the index bundle
            for (int i = 0; i < assetBundleNamesArray.Length; i++)
            {
                string bundleName = assetBundleNamesArray[i];
                thisIndex.bundlesIndex.Add(new AssetBundleIndex.AssetBundleIndexList(bundleName));

                if (bundleName.IndexOf('.') > -1)
                {
                    buildMap[i].assetBundleName = bundleName.Split('.')[0];
                    buildMap[i].assetBundleVariant = bundleName.Split('.')[1];
                }
                else
                {
                    buildMap[i].assetBundleName = bundleName;
                    //buildMap[i].assetBundleVariant = "";
                }
 
                string[] assetBundleAssetsArray = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                buildMap[i].assetNames = assetBundleAssetsArray;

                foreach(string path in assetBundleAssetsArray)
                {
                    var sysPath = Path.Combine(Application.dataPath, path);
                    var filename = Path.GetFileNameWithoutExtension(sysPath);
                    var tempObj = AssetDatabase.LoadMainAssetAtPath(path);
                    thisIndex.bundlesIndex[i].AddItem(filename, tempObj);
                }
            }
            var thisIndexAssetPath = "Assets/" + Utility.GetPlatformName() + "Index.asset";
            thisIndex.name = "AssetBundleIndex";
            AssetDatabase.CreateAsset(thisIndex, thisIndexAssetPath);
            AssetImporter thisIndexAsset = AssetImporter.GetAtPath(thisIndexAssetPath);
            thisIndexAsset.assetBundleName = Utility.GetPlatformName()+"Index";
            buildMap[assetBundleNamesArray.Length].assetBundleName = Utility.GetPlatformName() + "Index";
            buildMap[assetBundleNamesArray.Length].assetNames = new string[1] { "Assets/" + Utility.GetPlatformName() + "Index.asset" };

            //Save a json version of the data- just for reference, this does not need to be uploaded.
            var relativeAssetBundlesOutputPathForPlatform = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
            string thisIndexJson = JsonUtility.ToJson(thisIndex);
            var thisIndexJsonPath =  Path.Combine(relativeAssetBundlesOutputPathForPlatform, Utility.GetPlatformName().ToLower()) + "index.json";
            File.WriteAllText(thisIndexJsonPath, thisIndexJson);

            //@TODO: use append hash... (Make sure pipeline works correctly with it.)
            //BuildPipeline.BuildAssetBundles(outputPath, options, EditorUserBuildSettings.activeBuildTarget);
            //So now we have our buildMap we should get exactly the same results if we use it?
            BuildPipeline.BuildAssetBundles(outputPath, buildMap, options, EditorUserBuildSettings.activeBuildTarget);
            //And we should be able to encrypt them all now...
            for(int bmi = 0; bmi < buildMap.Length; bmi++)
            {
                //string thisABPath = Path.Combine(relativeAssetBundlesOutputPathForPlatform, buildMap[bmi].assetBundleName);
                //byte[] thisABBytes = File.ReadAllBytes(thisABPath);
                //So now I am not sure how to encrypt this, all the docs I can find seem to want a string rather than bytes to encrypt. 
                //Can we not just encrypt the bytes? or can I do ReadAllText and just encrypt the gobbledegook that is in the bundle already?
            }

            //Now we can remove the temp Index item from the assetDatabase
            AssetDatabase.DeleteAsset(thisIndexAssetPath);
        }


        public static void BuildAndRunPlayer(bool developmentBuild)
        {
            string outputPath = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL ? Utility.AssetBundlesOutputPath : (EditorUserBuildSettings.activeBuildTarget.ToString().IndexOf("Standalone") > -1 ? "Builds-Standalone" : "Builds-Devices");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);
            //IMPORTANT Standalone Builds DELETE everything in the folder they are saved in- so building into the AssetBundles Folder DELETES ALL ASSETBUNDLES
            string[] levels = GetLevelsFromBuildSettings();
            if (levels.Length == 0)
            {
                Debug.LogWarning("There were no Scenes in you Build Settings. Adding the current active Scene.");
#if UNITY_5_3_OR_NEWER
                levels = new string[1] { UnityEngine.SceneManagement.SceneManager.GetActiveScene().path };
#else
                levels = new string[1] { EditorApplication.currentScene };
#endif
                //return;
            }
            string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
            if (targetName == null)
                return;
            //For Standalone or WebGL that can run locally make the server write a file with its current setting that it can get when the game runs if the localserver is enabled
            if (SimpleWebServer.serverStarted && CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
                SimpleWebServer.WriteServerURL();
            else if(SimpleWebServer.serverStarted && !CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
            {
                Debug.LogWarning("Builds for " + EditorUserBuildSettings.activeBuildTarget.ToString() + " cannot access the LocalServer. AssetBundles will be downloaded from the remoteServerUrl's");
            }
            //BuildOptions
            BuildOptions option = BuildOptions.None;
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
            {
                option = developmentBuild ? BuildOptions.Development : BuildOptions.None;
            }
            else
            {
                option = developmentBuild ? BuildOptions.Development | BuildOptions.AutoRunPlayer : BuildOptions.AutoRunPlayer;
            }
            string buildError = "";
#if UNITY_5_4 || UNITY_5_3 || UNITY_5_2 || UNITY_5_1 || UNITY_5_0
            buildError = BuildPipeline.BuildPlayer(levels, outputPath + targetName, EditorUserBuildSettings.activeBuildTarget, option);
#else
            Debug.Log("I build using old method");
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = levels;
            buildPlayerOptions.locationPathName = outputPath + targetName;
            buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
            buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
            buildPlayerOptions.options = option;
            buildError = BuildPipeline.BuildPlayer(buildPlayerOptions);
#endif
            //after the build completes destroy the serverURL file
            if (SimpleWebServer.serverStarted && CanRunLocally(EditorUserBuildSettings.activeBuildTarget))
                SimpleWebServer.DestroyServerURLFile();

            if (buildError == "" || buildError == null)
            {
                string fullPathToBuild = Path.Combine(Directory.GetParent(Application.dataPath).FullName,outputPath);
                Debug.Log("Built Successful! Build Location: "+ fullPathToBuild);
                if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL)
                {
                    Application.OpenURL(SimpleWebServer.ServerURL + "index.html");
                }
            }
        }

        //DOS NOTES This is called by the Menu Item Assets/AssetBundles/Build Player (for use with engine code stripping)
        //Not sure we need it.
        public static void BuildPlayer()
        {
            var outputPath = EditorUtility.SaveFolderPanel("Choose Location of the Built Game", "", "");
            if (outputPath.Length == 0)
                return;

            string[] levels = GetLevelsFromBuildSettings();
            if (levels.Length == 0)
            {
                Debug.Log("Nothing to build.");
                return;
            }

            string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
            if (targetName == null)
                return;

            // Build and copy AssetBundles.
            BuildScript.BuildAssetBundles();
            //DOS NOTES this was added in the latest pull requests for the original AssetBundleManager not sure why?
#if UNITY_5_4 || UNITY_5_3 || UNITY_5_2 || UNITY_5_1 || UNITY_5_0
            BuildOptions option = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
            BuildPipeline.BuildPlayer(levels, outputPath + targetName, EditorUserBuildSettings.activeBuildTarget, option);
#else
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = levels;
            buildPlayerOptions.locationPathName = outputPath + targetName;
            buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
            buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
            buildPlayerOptions.options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
            BuildPipeline.BuildPlayer(buildPlayerOptions);
#endif
        }
        //DOS NOTES WHAT CALL THIS
        public static void BuildStandalonePlayer()
        {
            var outputPath = EditorUtility.SaveFolderPanel("Choose Location of the Built Game BORIS2", "", "");
            if (outputPath.Length == 0)
                return;

            string[] levels = GetLevelsFromBuildSettings();
            if (levels.Length == 0)
            {
                Debug.Log("Nothing to build.");
                return;
            }

            string targetName = GetBuildTargetName(EditorUserBuildSettings.activeBuildTarget);
            if (targetName == null)
                return;

            // Build and copy AssetBundles.
            BuildScript.BuildAssetBundles();
            BuildScript.CopyAssetBundlesTo(Path.Combine(Application.streamingAssetsPath, Utility.AssetBundlesOutputPath));
            AssetDatabase.Refresh();
            //DOS NOTES this was added in the latest pull requests but I dont understand why?
#if UNITY_5_4 || UNITY_5_3 || UNITY_5_2 || UNITY_5_1 || UNITY_5_0
            BuildOptions option = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
            BuildPipeline.BuildPlayer(levels, outputPath + targetName, EditorUserBuildSettings.activeBuildTarget, option);
#else
            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = levels;
            buildPlayerOptions.locationPathName = outputPath + targetName;
            buildPlayerOptions.assetBundleManifestPath = GetAssetBundleManifestFilePath();
            buildPlayerOptions.target = EditorUserBuildSettings.activeBuildTarget;
            buildPlayerOptions.options = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
            BuildPipeline.BuildPlayer(buildPlayerOptions);
#endif
        }
        /// <summary>
        /// Returns true if the build can potentially run on the current machine (a local build)
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static bool CanRunLocally(BuildTarget target)
        {
            var currentEnvironment = Application.platform.ToString();
            switch (target)
            {
                case BuildTarget.Android:
                case BuildTarget.iOS:
                case BuildTarget.Nintendo3DS:
                case BuildTarget.PS3:
                case BuildTarget.PS4:
                case BuildTarget.PSM:
                case BuildTarget.PSP2:
                case BuildTarget.SamsungTV:
                case BuildTarget.Tizen:
                case BuildTarget.tvOS:
                case BuildTarget.WiiU:
                case BuildTarget.XBOX360:
                case BuildTarget.XboxOne:
                    return false;
                case BuildTarget.StandaloneLinux:
                case BuildTarget.StandaloneLinux64:
                case BuildTarget.StandaloneLinuxUniversal:
                    if (currentEnvironment.IndexOf("Linux") > -1)
                        return true;
                    else
                        return false;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                case BuildTarget.WSAPlayer:
                    if (currentEnvironment.IndexOf("Windows") > -1 || currentEnvironment.IndexOf("WSA") > -1)
                        return true;
                    else
                        return false;
                case BuildTarget.StandaloneOSXIntel:
                case BuildTarget.StandaloneOSXIntel64:
                case BuildTarget.StandaloneOSXUniversal:
                    if (currentEnvironment.IndexOf("OSX") > -1)
                        return true;
                    else
                        return false;
                case BuildTarget.WebGL:
#if !UNITY_5_4_OR_NEWER
                case BuildTarget.WebPlayer:
                case BuildTarget.WebPlayerStreamed:
#endif
                    return true;
                default:
                    Debug.Log("Target not implemented.");
                    return false;
            }

          }

        public static string GetBuildTargetName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return "/test.apk";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return "/test.exe";
                case BuildTarget.StandaloneOSXIntel:
                case BuildTarget.StandaloneOSXIntel64:
                case BuildTarget.StandaloneOSXUniversal:
                    return "/test.app";
#if !UNITY_5_4_OR_NEWER
                case BuildTarget.WebPlayer:
                case BuildTarget.WebPlayerStreamed:
#endif
                case BuildTarget.WebGL:
                case BuildTarget.iOS:
                    return "";
                // Add more build targets for your own.
                default:
                    Debug.Log("Target not implemented.");
                    return null;
            }
        }

        static void CopyAssetBundlesTo(string outputPath)
        {
            // Clear streaming assets folder.
            FileUtil.DeleteFileOrDirectory(Application.streamingAssetsPath);
            Directory.CreateDirectory(outputPath);

            string outputFolder = Utility.GetPlatformName();

            // Setup the source folder for assetbundles.
            var source = Path.Combine(Path.Combine(System.Environment.CurrentDirectory, Utility.AssetBundlesOutputPath), outputFolder);
            if (!System.IO.Directory.Exists(source))
                Debug.Log("No assetBundle output folder, try to build the assetBundles first.");

            // Setup the destination folder for assetbundles.
            var destination = System.IO.Path.Combine(outputPath, outputFolder);
            if (System.IO.Directory.Exists(destination))
                FileUtil.DeleteFileOrDirectory(destination);

            FileUtil.CopyFileOrDirectory(source, destination);
        }

        static string[] GetLevelsFromBuildSettings()
        {
            List<string> levels = new List<string>();
            for (int i = 0; i < EditorBuildSettings.scenes.Length; ++i)
            {
                if (EditorBuildSettings.scenes[i].enabled)
                    levels.Add(EditorBuildSettings.scenes[i].path);
            }

            return levels.ToArray();
        }


        static string GetAssetBundleManifestFilePath()
        {
            var relativeAssetBundlesOutputPathForPlatform = Path.Combine(Utility.AssetBundlesOutputPath, Utility.GetPlatformName());
            return Path.Combine(relativeAssetBundlesOutputPathForPlatform, Utility.GetPlatformName()) + ".manifest";
        }
    }
}