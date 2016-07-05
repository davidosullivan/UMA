using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UMAAssetBundleManager;

namespace UMA
{
    public class DynamicAssetLoader : MonoBehaviour
    {
        static DynamicAssetLoader _instance;

        [Tooltip("Set the server URL that assetbundles can be loaded from. Used in a live build and when the LocalAssetServer is turned off.")]
        public string remoteServerURL = "";
        [Tooltip("A list of assetbundles to preload when the game starts. After these have completed loading any GameObject in the gameObjectsToActivate field will be activated.")]
        public List<string> assetBundlesToPreLoad = new List<string>();
        [Tooltip("GameObjects that will be activated after the list of assetBundlesToPreLoad has finished downloading.")]
        public List<GameObject> gameObjectsToActivate = new List<GameObject>();
        [Space]
        public GameObject loadingMessageObject;
        public Text loadingMessageText;
        public string loadingMessage = "";
        [HideInInspector]
        [System.NonSerialized]
        public float percentDone = 0f;
        [HideInInspector]
        [System.NonSerialized]
        public bool assetBundlesDownloading;
        bool canCheckDownloadingBundles;
        bool isInitializing = false;
        bool isInitialized = false;
        bool gameObjectsActivated;
        [Space]
        //Default assets fields
        public RaceData placeholderRace;//temp race based on UMAMale with a baseRecipe to generate a temp umaMale TODO: Could have a female too and search the required racename to see if it contains female...
        public UMATextRecipe placeholderWardrobeRecipe;//empty temp wardrobe recipe
        public SlotDataAsset placeholderSlot;//empty temp slot
        public OverlayDataAsset placeholderOverlay;//empty temp overlay. Would be nice if there was some way we could have a shader on this that would 'fill up' as assets loaded maybe?
        [HideInInspector]
        [System.NonSerialized]
        public UMAAvatarBase requestingUMA;
        //TODO: Just visible for dev
        //[HideInInspector]
        //[System.NonSerialized]
        public DownloadingAssetsList downloadingAssets = new DownloadingAssetsList();

        //Because searching Resources for UMA Assets is so slow we will cache the results as we get them
        [System.NonSerialized]
        public Dictionary<Type, Dictionary<int, string>> UMAResourcesIndex = new Dictionary<Type, Dictionary<int, string>>();

        int? _currentBatchID = null;

        /// <summary>
        /// Gets the currentBatchID or generates a new one if it is null. 
        /// Tip: Rather than setting this explicily, consider calling GenerateBatchID which will provide a unique random id number and set this property at the same time.
        /// </summary>
        public int CurrentBatchID
        {
            get
            {
                if (_currentBatchID == null)
                    _currentBatchID = GenerateBatchID();
                return (int)_currentBatchID;
            }
            set
            {
                _currentBatchID = value;
            }
        }

        public static DynamicAssetLoader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindInstance();
                }
                return _instance;
            }
            set { _instance = value; }
        }

        #region BASE METHODS
        void OnEnable()
        {
            if (_instance == null) _instance = this;//TODO check whether we can use multiple DALs by making this always set on start.
            if (!isInitialized)
            {
                StartCoroutine(Initialize());
            }
        }

        IEnumerator Start()
        {
            if (_instance == null) _instance = this;//TODO check whether we can use multiple DALs by making this always set on start.
            if (!isInitialized)
            {
                yield return StartCoroutine(Initialize());
            }

            //Load any preload asset bundles if there are any
            if (assetBundlesToPreLoad.Count > 0)
            {
                yield return StartCoroutine(LoadAssetBundlesAsync(assetBundlesToPreLoad));
                //we may need to update the libraries after this...
            }
        }

        void Update()
        {
#if UNITY_EDITOR
            if (AssetBundleManager.SimulateAssetBundleInEditor)
            {
                if (!gameObjectsActivated)
                {
                    if (gameObjectsToActivate.Count > 0)
                    {
                        foreach (GameObject go in gameObjectsToActivate)
                        {
                            if (!go.activeSelf)
                            {
                                go.SetActive(true);
                            }
                        }
                    }
                    gameObjectsActivated = true;
                }
            }
#endif
            if (downloadingAssets.downloadingItems.Count > 0)
                downloadingAssets.Update();
            if (downloadingAssets.areDownloadedItemsReady == false)
                assetBundlesDownloading = true;
            if ((assetBundlesDownloading || downloadingAssets.areDownloadedItemsReady == false) && canCheckDownloadingBundles == true)
            {
                if (!AssetBundleManager.AreBundlesDownloading() && downloadingAssets.areDownloadedItemsReady == true)
                {
                    assetBundlesDownloading = false;
                    if (!gameObjectsActivated)
                    {
                        if (gameObjectsToActivate.Count > 0)
                        {
                            foreach (GameObject go in gameObjectsToActivate)
                            {
                                if (!go.activeSelf)
                                {
                                    go.SetActive(true);
                                }
                            }
                        }
                        gameObjectsActivated = true;
                    }
                }
            }
        }
        /// <summary>
        /// Finds the DynamicAssetLoader in the scene and treats it like a singleton.
        /// </summary>
        /// <returns>The DynamicAssetLoader.</returns>
        public static DynamicAssetLoader FindInstance()
        {
            if (_instance == null)
            {
                DynamicAssetLoader[] dynamicAssetLoaders = FindObjectsOfType(typeof(DynamicAssetLoader)) as DynamicAssetLoader[];
                if (dynamicAssetLoaders[0] != null)
                {
                    _instance = dynamicAssetLoaders[0];
                }
            }
            return _instance;
        }
        #endregion


        #region DOWNLOAD METHODS

        /// <summary>
        /// Initialize the downloading URL. eg. local server / iOS ODR / or the download URL as defined in the component settings if Simulation Mode and Local Asset Server is off
        /// </summary>
        void InitializeSourceURL()
        {
            string URLToUse = "";
            if (SimpleWebServer.ServerURL != "")
            {
#if UNITY_EDITOR
                if(SimpleWebServer.serverStarted)//this is not true in builds no matter what- but we in the editor we need to know
#endif
                URLToUse = SimpleWebServer.ServerURL;
                Debug.Log("[DynamicAssetLoader] SimpleWebServer.ServerURL = " + URLToUse);
            }
            else
            {
                URLToUse = remoteServerURL;
            }
//#endif
            if (URLToUse != "")
                AssetBundleManager.SetSourceAssetBundleURL(URLToUse);
            else
            {
                string errorString = "LocalAssetBundleServer was off and no remoteServerURL was specified. One of these must be set in order to use any AssetBundles!";
#if UNITY_EDITOR
                errorString = "Switched to Simulation Mode because LocalAssetBundleServer was off and no remoteServerURL was specified in the Scenes' DynamicAssetLoader. One of these must be set in order to actually use your AssetBundles.";
                AssetBundleManager.SimulateOverride = true;
#endif
                Debug.LogWarning(errorString);
            }
            return;

        }
        /// <summary>
        /// Initializes AssetBundleManager which loads the AssetBundleManifest object and the AssetBundleIndex object.
        /// </summary>
        /// <returns></returns>
        protected IEnumerator Initialize()
        {
#if UNITY_EDITOR
            if (AssetBundleManager.SimulateAssetBundleInEditor)
            {
                isInitialized = true;
                yield break;
            }
#endif
            if (isInitializing == false)
            {
                isInitializing = true;
                InitializeSourceURL();//in the editor this might set AssetBundleManager.SimulateAssetBundleInEditor to be true aswell so check that
#if UNITY_EDITOR
                if (AssetBundleManager.SimulateAssetBundleInEditor)
                {
                    isInitialized = true;
                    yield break;
                }
#endif
                var request = AssetBundleManager.Initialize();
                if (request != null)
                {
                    while (AssetBundleManager.IsOperationInProgress(request))
                    {
                        yield return null;
                    }
                    isInitializing = false;
                    if (AssetBundleManager.AssetBundleManifestObject != null && AssetBundleManager.AssetBundleIndexObject != null)
                    {
                        isInitialized = true;
                    }
                    else
                    {
                        //if we are in the editor this can only have happenned because the asset bundles were not built and by this point
                        //an error will have already been shown about that and AssetBundleManager.SimulationOverride will be true so we can just continue.
#if UNITY_EDITOR
                        if (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
                        {
                            isInitialized = true;
                            yield break;
                        }
#endif
                    }
                }
                else
                {
                    Debug.LogWarning("AssetBundleManager failed to initialize correctly");
                }
            }
        }
        /// <summary>
        /// Generates a batch ID for use when grouping assetbundle asset requests together so they can be processed in the same cycle (to avoid UMA Generation errors).
        /// </summary>
        /// <returns></returns>
        public int GenerateBatchID()
        {
            CurrentBatchID = UnityEngine.Random.Range(1000000, 2000000);
            return CurrentBatchID;
        }
        /// <summary>
        /// Load a single assetbundle (and its dependencies) asynchroniously and sets the Loading Messages.
        /// </summary>
        /// <param name="assetBundleToLoad"></param>
        /// <param name="loadingMsg"></param>
        /// <param name="loadedMsg"></param>
        public void LoadAssetBundle(string assetBundleToLoad, string loadingMsg = "", string loadedMsg = "")
        {
            var assetBundlesToLoadList = new List<string>();
            assetBundlesToLoadList.Add(assetBundleToLoad);
            LoadAssetBundles(assetBundlesToLoadList, loadingMsg, loadedMsg);
        }
        /// <summary>
        /// Load multiple assetbundles (and their dependencies) asynchroniously and sets the Loading Messages.
        /// </summary>
        /// <param name="assetBundlesToLoad"></param>
        /// <param name="loadingMsg"></param>
        /// <param name="loadedMsg"></param>
        public void LoadAssetBundles(string[] assetBundlesToLoad, string loadingMsg = "", string loadedMsg = "")
        {
            var assetBundlesToLoadList = new List<string>(assetBundlesToLoad);
            LoadAssetBundles(assetBundlesToLoadList, loadingMsg, loadedMsg);
        }
        /// <summary>
        /// Load multiple assetbundles (and their dependencies) asynchroniously and sets the Loading Messages.
        /// </summary>
        /// <param name="assetBundlesToLoad"></param>
        /// <param name="loadingMsg"></param>
        /// <param name="loadedMsg"></param>
        public void LoadAssetBundles(List<string> assetBundlesToLoad, string loadingMsg = "", string loadedMsg = "")
        {
#if UNITY_EDITOR
            if (AssetBundleManager.SimulateAssetBundleInEditor)
            {
                //Actually we DO still need to do something here
                foreach (string requiredBundle in assetBundlesToLoad)
                {
                    SimulateLoadAssetBundle(requiredBundle);
                }
                return;
            }
#endif
            List<string> assetBundlesToReallyLoad = new List<string>();
            foreach (string requiredBundle in assetBundlesToLoad)
            {
                if (!AssetBundleManager.IsAssetBundleDownloaded(requiredBundle))
                {
                    assetBundlesToReallyLoad.Add(requiredBundle);
                }
            }
            if (assetBundlesToReallyLoad.Count > 0)
            {
                AssetBundleLoadingIndicator.Instance.Show(assetBundlesToReallyLoad, loadingMsg, "", loadedMsg);
                assetBundlesDownloading = true;
                canCheckDownloadingBundles = false;
                StartCoroutine(LoadAssetBundlesAsync(assetBundlesToReallyLoad));
            }
        }
        /// <summary>
        /// Loads a list of asset bundles and their dependencies asynchroniously
        /// </summary>
        /// <param name="assetBundlesToLoad"></param>
        /// <returns></returns>
        protected IEnumerator LoadAssetBundlesAsync(List<string> assetBundlesToLoad)
        {
#if UNITY_EDITOR
            if (AssetBundleManager.SimulateAssetBundleInEditor)
                yield break;
#endif
            if (!isInitialized)
            {
                if (!isInitializing)
                {
                    Debug.LogWarning("[DynamicAssetLoader] isInitialized was false");
                    yield return StartCoroutine(Initialize());
                }
                else
                {
                    Debug.Log("Waiting for Initializing to complete...");
                    while (isInitialized == false)
                    {
                        yield return null;
                    }
                }
            }
            string[] bundlesInManifest = AssetBundleManager.AssetBundleManifestObject.GetAllAssetBundles();
            foreach (string assetBundleName in assetBundlesToLoad)
            {
                foreach (string bundle in bundlesInManifest)
                {
                    if ((bundle == assetBundleName || bundle.IndexOf(assetBundleName + "/") > -1))
                    {
                        StartCoroutine(LoadAssetBundleAsync(bundle));
                    }
                }
            }
            canCheckDownloadingBundles = true;
            assetBundlesDownloading = true;
            yield return null;
        }
        /// <summary>
        /// Loads an asset bundle and its dependencies asynchroniously
        /// </summary>
        /// <param name="bundle"></param>
        /// <returns></returns>
        //DOS NOTES: if the local server is turned off after it was on when AssetBundleManager was initialized 
        //(like could happen in the editoror if you run a build that uses the local server but you have not started Unity and turned local server on)
        //then this wrongly says that the bundle has downloaded
#pragma warning disable 0219 //remove the warning that we are not using loadedBundle- since we want the error
        protected IEnumerator LoadAssetBundleAsync(string bundle)
        {
            float startTime = Time.realtimeSinceStartup;
            AssetBundleManager.LoadAssetBundle(bundle, false);
            while (AssetBundleManager.IsAssetBundleDownloaded(bundle) == false)
            {
                yield return null;
            }
            string error = null;
            LoadedAssetBundle loadedBundle = AssetBundleManager.GetLoadedAssetBundle(bundle, out error);
            float elapsedTime = Time.realtimeSinceStartup - startTime;
            Debug.Log(bundle + (error != null ? " was not" : " was") + " loaded successfully in " + elapsedTime + " seconds");
            if (error != null)
            {
                Debug.LogError("[DynamicAssetLoader] Bundle Load Error: " +error);
            }
            yield return true;
        }
#pragma warning restore 0219

#endregion

#region LOAD ASSETS METHODS

        /// <summary>
        /// Generic Library function to search Resources for a type of asset, optionally filtered by folderpath and asset assetNameHash or assetName. 
        /// Optionally sends the found assets to the supplied callback for processing.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="resourcesFolderPath"></param>
        /// <param name="assetNameHash"></param>
        /// <param name="assetName"></param>
        /// <param name="callback"></param>
        /// <returns>Returns true if a assetNameHash or assetName were specified and an asset with that assetNameHash or assetName is found. Else returns false.</returns>
        /// TODO: Loading from resources like this is REALLY slow, but because UMA races/slots/overalays can have names that differ from the asset name there is no quick way to find them without loading them
        /// What we could do is create an UMAResourcesIndex that indexed assets in a way that you could get the asset name by asking for the slot/overlay/race name
        public bool AddAssetsFromResources<T>(string resourcesFolderPath = "", int? assetNameHash = null, string assetName = "", Action<T[]> callback = null) where T : UnityEngine.Object
        {
            bool found = false;
            List<T> assetsToReturn = new List<T>();
            if (!UMAResourcesIndex.ContainsKey(typeof(T)) && ((typeof(T) == typeof(SlotDataAsset)) || (typeof(T) == typeof(OverlayDataAsset)) || (typeof(T) == typeof(RaceData))))
            {
                UMAResourcesIndex[typeof(T)] = new Dictionary<int, string>();
            }
            if (UMAResourcesIndex.ContainsKey(typeof(T))){
                if (assetNameHash != null || assetName != "")
                {
                    string foundAssetName = "";
                    if (assetNameHash != null)
                    {
                        UMAResourcesIndex[typeof(T)].TryGetValue((int)assetNameHash, out foundAssetName);
                    }
                    else if (assetName != "")
                    {
                        UMAResourcesIndex[typeof(T)].TryGetValue(UMAUtils.StringToHash(assetName), out foundAssetName);
                    }
                    if(foundAssetName != "")
                    {
                       T foundAsset =  Resources.Load<T>(foundAssetName);//Wont be correct if there was a path Also bloody Resources wont find anything in subfolders so this is pretty useless
                        if(foundAsset != null)
                        {
                            assetsToReturn.Add(foundAsset);
                            found = true;
                        }
                    }
                }
            }
            if (found == false)
            {
                string[] resourcesFolderPathArray = SearchStringToArray(resourcesFolderPath);
                foreach (string path in resourcesFolderPathArray)
                {
                    T[] foundAssets = new T[0];
                    var pathPrefix = path == "" ? "" : path + "/";
                    if ((typeof(T) == typeof(SlotDataAsset)) || (typeof(T) == typeof(OverlayDataAsset)) || (typeof(T) == typeof(RaceData)))
                    {
                        //This is hugely expensive but we have to do this as we dont know the asset name, only the race/slot/overlayName which may not be the same. 
                        //This will only happen once now that I added the UMAResourcesDictionary
                        foundAssets = Resources.LoadAll<T>(path);
                    }
                    else
                    {
                        if (assetName == "")
                            foundAssets = Resources.LoadAll<T>(path);
                        else
                        {
                            if(pathPrefix != "")
                            {
                                T foundAsset = Resources.Load<T>(pathPrefix + assetName);
                                if (foundAsset != null)
                                {
                                    assetsToReturn.Add(foundAsset);
                                    found = true;
                                }
                                else
                                {
                                    foundAssets = Resources.LoadAll<T>(path);
                                }
                            }
                            else
                            {
                                foundAssets = Resources.LoadAll<T>(path);
                            }
                        }
                    }
                    if (found == false)
                    {
                        for (int i = 0; i < foundAssets.Length; i++)
                        {
                            if (assetNameHash != null)
                            {
                                int foundHash = UMAUtils.StringToHash(foundAssets[i].name);
                                if (typeof(T) == typeof(SlotDataAsset))
                                {
                                    foundHash = (foundAssets[i] as SlotDataAsset).nameHash;
                                    UMAResourcesIndex[typeof(T)][(foundAssets[i] as SlotDataAsset).nameHash] = pathPrefix + (foundAssets[i] as SlotDataAsset).name;
                                }
                                if (typeof(T) == typeof(OverlayDataAsset))
                                {
                                    foundHash = UMAUtils.StringToHash((foundAssets[i] as OverlayDataAsset).overlayName);
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as OverlayDataAsset).overlayName)] = pathPrefix + (foundAssets[i] as OverlayDataAsset).name;
                                }
                                if (typeof(T) == typeof(RaceData))
                                {
                                    foundHash = UMAUtils.StringToHash((foundAssets[i] as RaceData).raceName);
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as RaceData).raceName)] = pathPrefix + (foundAssets[i] as RaceData).name;
                                }
                                if (foundHash == assetNameHash)
                                {
                                    assetsToReturn.Add(foundAssets[i]);
                                    found = true;
                                }
                            }
                            else if (assetName != "")
                            {
                                string foundName = foundAssets[i].name;
                                if (typeof(T) == typeof(OverlayDataAsset))
                                {
                                    foundName = (foundAssets[i] as OverlayDataAsset).overlayName;
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as OverlayDataAsset).overlayName)] = pathPrefix + (foundAssets[i] as OverlayDataAsset).name;
                                }
                                if (typeof(T) == typeof(SlotDataAsset))
                                {
                                    foundName = (foundAssets[i] as SlotDataAsset).slotName;
                                    UMAResourcesIndex[typeof(T)][(foundAssets[i] as SlotDataAsset).nameHash] = pathPrefix + (foundAssets[i] as SlotDataAsset).name;
                                }
                                if (typeof(T) == typeof(RaceData))
                                {
                                    foundName = (foundAssets[i] as RaceData).raceName;
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as RaceData).raceName)] = pathPrefix + (foundAssets[i] as RaceData).name;
                                }
                                if (foundName == assetName)
                                {
                                    assetsToReturn.Add(foundAssets[i]);
                                    found = true;
                                }

                            }
                            else
                            {
                                if (typeof(T) == typeof(RaceData))
                                {
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as RaceData).raceName)] = pathPrefix + (foundAssets[i] as RaceData).name;
                                }
                                if (typeof(T) == typeof(SlotDataAsset))
                                {
                                    UMAResourcesIndex[typeof(T)][(foundAssets[i] as SlotDataAsset).nameHash] = pathPrefix + (foundAssets[i] as SlotDataAsset).name;
                                }
                                if (typeof(T) == typeof(OverlayDataAsset))
                                {
                                    UMAResourcesIndex[typeof(T)][UMAUtils.StringToHash((foundAssets[i] as OverlayDataAsset).overlayName)] = pathPrefix + (foundAssets[i] as OverlayDataAsset).name;
                                }
                                assetsToReturn.Add(foundAssets[i]);
                            }
                        }
                    }
                }
            }
            if (callback != null)
            {
                callback(assetsToReturn.ToArray());
            }
            return found;
        }
        /// <summary>
        /// Override for AddAssetsFromAssetBundles that does not require a dictionary
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="downloadAssetsEnabled"></param>
        /// <param name="bundlesToSearch"></param>
        /// <param name="assetNameHash"></param>
        /// <param name="assetName"></param>
        /// <param name="callback"></param>
        public bool AddAssetsFromAssetBundles<T>(bool downloadAssetsEnabled, string bundlesToSearch = "", int? assetNameHash = null, string assetName = "", Action<T[]> callback = null) where T : UnityEngine.Object
        {
            var dummyAssetBundlesUsedDict = new Dictionary<string, List<string>>();
            return AddAssetsFromAssetBundles<T>(ref dummyAssetBundlesUsedDict, downloadAssetsEnabled, bundlesToSearch, assetNameHash, assetName, callback);
        }
        /// <summary>
        /// Generic Library function to search AssetBundles for a type of asset, optionally filtered by bundle name, and asset assetNameHash or assetName. 
        /// Optionally sends the found assets to the supplied callback for processing.
        /// Automatically performs the operation in SimulationMode if AssetBundleManager.SimulationMode is enabled or if the Application is not playing.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bundlesToSearch"></param>
        /// <param name="assetNameHash"></param>
        /// <param name="assetName"></param>
        /// <param name="callback"></param>
        public bool AddAssetsFromAssetBundles<T>(ref Dictionary<string, List<string>> assetBundlesUsedDict, bool downloadAssetsEnabled, string bundlesToSearch = "", int? assetNameHash = null, string assetName = "", Action<T[]> callback = null) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            if (AssetBundleManager.SimulateAssetBundleInEditor)
            {
                return SimulateAddAssetsFromAssetBundles<T>(ref assetBundlesUsedDict, bundlesToSearch, assetNameHash, assetName, callback);
            }
            else
            {
#endif
                if (AssetBundleManager.AssetBundleManifestObject == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning("[DynamicAssetLoader] No AssetBundleManager.AssetBundleManifestObject found. Do you need to rebuild your AssetBundles and/or upload the platform manifest bundle?");
                    AssetBundleManager.SimulateOverride = true;
                    return SimulateAddAssetsFromAssetBundles<T>(ref assetBundlesUsedDict, bundlesToSearch, assetNameHash, assetName, callback);
#else
					Debug.LogError("[DynamicAssetLoader] No AssetBundleManager.AssetBundleManifestObject found. Do you need to rebuild your AssetBundles and/or upload the platform manifest bundle?");
                    return false;
#endif
                }
                if (AssetBundleManager.AssetBundleIndexObject == null)
                {
#if UNITY_EDITOR
                    Debug.LogWarning("[DynamicAssetLoader] No AssetBundleManager.AssetBundleIndexObject found. Do you need to rebuild your AssetBundles and/or upload the platform index bundle?");
                    AssetBundleManager.SimulateOverride = true;
                    return SimulateAddAssetsFromAssetBundles<T>(ref assetBundlesUsedDict, bundlesToSearch, assetNameHash, assetName, callback);
#else
					Debug.LogError("[DynamicAssetLoader] No AssetBundleManager.AssetBundleIndexObject found. Do you need to rebuild your AssetBundles and/or upload the platform index bundle?");
                    return false;
#endif
                }
                string[] allAssetBundleNames = AssetBundleManager.AssetBundleIndexObject.GetAllAssetBundleNames();
                string[] assetBundleNamesArray = allAssetBundleNames;
                List<T> assetsToReturn = new List<T>();
                Type typeParameterType = typeof(T);
                var typeString = typeParameterType.FullName;
                if (bundlesToSearch != "")
                {
                    List<string> processedBundleNamesArray = new List<string>();
                    var bundlesToSearchArray = SearchStringToArray(bundlesToSearch);
                    foreach (string bundleToSearch in bundlesToSearchArray)
                    {
                        foreach (string bundleName in allAssetBundleNames)
                        {
                            if (bundleName.IndexOf(bundleToSearch) > -1 && !processedBundleNamesArray.Contains(bundleName))
                            {
                                processedBundleNamesArray.Add(bundleName);
                            }
                        }
                    }
                    assetBundleNamesArray = processedBundleNamesArray.ToArray();
                }
                bool assetFound = false;
                foreach (string assetBundleName in assetBundleNamesArray)
                {
                    string error = "";
                    if (assetNameHash != null && assetName == "")
                    {
                        assetName = AssetBundleManager.AssetBundleIndexObject.GetAssetNameFromHash(assetBundleName, assetNameHash, typeString);
                    }
                    if (assetName != "" || assetNameHash != null)
                    {
                        if (assetName == "" && assetNameHash != null)
                        {
                            continue;
                        }
                        bool assetBundleContains = AssetBundleManager.AssetBundleIndexObject.AssetBundleContains(assetBundleName, assetName, typeString);
                        if (!assetBundleContains && typeof(T) == typeof(SlotDataAsset))
                        {
                            //try the '_Slot' version
                            assetBundleContains = AssetBundleManager.AssetBundleIndexObject.AssetBundleContains(assetBundleName, assetName + "_Slot", typeString);
                        }
                        if (assetBundleContains)
                        {
                            if (AssetBundleManager.IsAssetBundleDownloaded(assetBundleName))
                            {
                                T target = (T)AssetBundleManager.GetLoadedAssetBundle(assetBundleName, out error).m_AssetBundle.LoadAsset<T>(assetName);
                                if (target == null && typeof(T) == typeof(SlotDataAsset))
                                {
                                    target = (T)AssetBundleManager.GetLoadedAssetBundle(assetBundleName, out error).m_AssetBundle.LoadAsset<T>(assetName + "_Slot");
                                }
                                if (target != null)
                                {
                                    assetFound = true;
                                    if (!assetBundlesUsedDict.ContainsKey(assetBundleName))
                                    {
                                        assetBundlesUsedDict[assetBundleName] = new List<string>();
                                    }
                                    if (!assetBundlesUsedDict[assetBundleName].Contains(assetName))
                                    {
                                        assetBundlesUsedDict[assetBundleName].Add(assetName);
                                    }
                                    assetsToReturn.Add(target);
                                    if (assetName != "")
                                        break;
                                }
                                else
                                {
                                    if (error != "")
                                    {
                                        Debug.LogWarning(error);
                                    }
                                }
                            }
                            else if (downloadAssetsEnabled)
                            {
                                //Here we return a temp asset and wait for the bundle to download
                                //We dont want to create multiple downloads of the same bundle so check its not already downloading
                                if (AssetBundleManager.AreBundlesDownloading(assetBundleName) == false)
                                {
                                    LoadAssetBundle(assetBundleName);
                                }
                                else
                                {
                                    //do nothing its already downloading
                                }
                                if (assetNameHash == null)
                                {
                                    assetNameHash = AssetBundleManager.AssetBundleIndexObject.GetAssetHashFromName(assetBundleName, assetName, typeString);
                                }
                                T target = downloadingAssets.AddDownloadItem<T>(CurrentBatchID, assetName, assetNameHash, assetBundleName, requestingUMA);
                                if (target != null)
                                {
                                    assetFound = true;
                                    if (!assetBundlesUsedDict.ContainsKey(assetBundleName))
                                    {
                                        assetBundlesUsedDict[assetBundleName] = new List<string>();
                                    }
                                    if (!assetBundlesUsedDict[assetBundleName].Contains(assetName))
                                    {
                                        assetBundlesUsedDict[assetBundleName].Add(assetName);
                                    }
                                    assetsToReturn.Add(target);
                                    if (assetName != "")
                                        break;
                                }
                            }
                        }
                    }
                    else //we are just loading in all assets of type from the downloaded bundles- only realistically possible when the bundles have been downloaded already because otherwise this would trigger the download of all possible assetbundles that contain anything of type T...
                    {
                        if (AssetBundleManager.IsAssetBundleDownloaded(assetBundleName))
                        {
                            string[] assetsInBundle = AssetBundleManager.AssetBundleIndexObject.GetAllAssetsOfTypeInBundle(assetBundleName, typeString);
                            if (assetsInBundle.Length > 0)
                            {
                                foreach (string asset in assetsInBundle)
                                {
                                    T target = (T)AssetBundleManager.GetLoadedAssetBundle(assetBundleName, out error).m_AssetBundle.LoadAsset<T>(asset);
                                    if (target == null && typeof(T) == typeof(SlotDataAsset))
                                    {
                                        target = (T)AssetBundleManager.GetLoadedAssetBundle(assetBundleName, out error).m_AssetBundle.LoadAsset<T>(asset + "_Slot");
                                    }
                                    if (target != null)
                                    {
                                        assetFound = true;
                                        if (!assetBundlesUsedDict.ContainsKey(assetBundleName))
                                        {
                                            assetBundlesUsedDict[assetBundleName] = new List<string>();
                                        }
                                        if (!assetBundlesUsedDict[assetBundleName].Contains(asset))
                                        {
                                            assetBundlesUsedDict[assetBundleName].Add(asset);
                                        }
                                        assetsToReturn.Add(target);
                                    }
                                    else
                                    {
                                        if (error != "")
                                        {
                                            Debug.LogWarning(error);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                if (!assetFound && assetName != "")
                {
                    string[] assetIsInArray = AssetBundleManager.AssetBundleIndexObject.FindContainingAssetBundle(assetName, typeString);
                    string assetIsIn = assetIsInArray.Length > 0 ? " but it was in "+assetIsInArray[0] : ". Do you need to reupload you platform manifest and index?";
                    Debug.LogWarning("Dynamic" + typeof(T).Name + "Library (" + typeString + ") could not load " + assetName + " from any of the AssetBundles searched" + assetIsIn);
                }
                if (assetsToReturn.Count > 0 && callback != null)
                {
                    callback(assetsToReturn.ToArray());
                }

                return assetFound;
#if UNITY_EDITOR
            }
#endif
        }
#if UNITY_EDITOR
        /// <summary>
        /// Simulates the loading of assets when AssetBundleManager is set to 'SimulationMode'
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bundlesToSearch"></param>
        /// <param name="assetNameHash"></param>
        /// <param name="assetName"></param>
        /// <param name="callback"></param>
        //TODO: This really slows things down in the inspector when the game is playing, maybe it could be a coroutine?
        //TODO2: If it really is going to be this slow consider showing the LoadingIndicator
        bool SimulateAddAssetsFromAssetBundles<T>(ref Dictionary<string, List<string>> assetBundlesUsedDict, string bundlesToSearch = "", int? assetNameHash = null, string assetName = "", Action<T[]> callback = null) where T : UnityEngine.Object
        {
            Type typeParameterType = typeof(T);
            var typeString = typeParameterType.FullName;
            if (assetNameHash != null)
            {
                //actually this is not true. We could load all assets of type, iterate over them and get the hash and see if it matches...
                Debug.Log("It is not currently possible to search for assets in assetbundles using the assetNameHash. " + typeString + " is trying to do this with assetNameHash " + assetNameHash);
            }
            string[] allAssetBundleNames = AssetDatabase.GetAllAssetBundleNames();
            string[] assetBundleNamesArray;
            List<T> assetsToReturn = new List<T>();
            if (bundlesToSearch != "")
            {
                List<string> processedBundleNamesArray = new List<string>();
                var bundlesToSearchArray = SearchStringToArray(bundlesToSearch);
                foreach (string bundleToSearch in bundlesToSearchArray)
                {
                    foreach (string bundleName in allAssetBundleNames)
                    {
                        if (bundleName.IndexOf(bundleToSearch) > -1 && !processedBundleNamesArray.Contains(bundleName))
                        {
                            processedBundleNamesArray.Add(bundleName);
                        }
                    }
                }
                assetBundleNamesArray = processedBundleNamesArray.ToArray();
            }
            else
            {
                assetBundleNamesArray = allAssetBundleNames;
            }
            bool assetFound = false;
            List<string> dependencies = new List<string>();
            foreach (string assetBundleName in assetBundleNamesArray)
            {
                if (assetFound && assetName != "")//Do we want to break actually? What if the user has named two overlays the same? Or would this not work anyway?
                    break;
                string[] possiblePaths = new string[0];
                if (assetName != "")
                {
                    //if this is looking for SlotsDataAssets then the asset name has _Slot after it usually even if the slot name doesn't have that-but the user might have renamed it so cover both cases
                    if (typeof(T) == typeof(SlotDataAsset))
                    {
                        string[] possiblePathsTemp = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, assetName);
                        string[] possiblePaths_SlotTemp = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, assetName + "_Slot");
                        List<string> possiblePathsList = new List<string>(possiblePathsTemp);
                        foreach (string path in possiblePaths_SlotTemp)
                        {
                            if (!possiblePathsList.Contains(path))
                            {
                                possiblePathsList.Add(path);
                            }
                        }
                        possiblePaths = possiblePathsList.ToArray();
                    }
                    else
                    {
                        possiblePaths = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(assetBundleName, assetName);
                    }
                }
                else
                {
                    //Ideally we should load all the dependent assets too but when we are simulating we dont have access 
                    //to this data because its in the manifest, which is not there, otherwise we would not be simulating in the first place
                    if (!Application.isPlaying)
                    {
                        possiblePaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                    }
                }
                foreach (string path in possiblePaths)
                {
                    T target = (T)AssetDatabase.LoadAssetAtPath(path, typeof(T));
                    if (target != null)
                    {
                        assetFound = true;
                        if (!assetBundlesUsedDict.ContainsKey(assetBundleName))
                        {
                            assetBundlesUsedDict[assetBundleName] = new List<string>();
                        }
                        if (!assetBundlesUsedDict[assetBundleName].Contains(assetName))
                        {
                            assetBundlesUsedDict[assetBundleName].Add(assetName);
                        }
                        assetsToReturn.Add(target);
                        //if the application is not playing we want to load ALL the assets from the bundle this asset will be in
                        if (Application.isPlaying)
                        {
                            var thisAssetBundlesAssets = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                            for (int i = 0; i < thisAssetBundlesAssets.Length; i++)
                            {
                                if (!dependencies.Contains(thisAssetBundlesAssets[i]) && thisAssetBundlesAssets[i] != path)
                                {
                                    dependencies.Add(thisAssetBundlesAssets[i]);
                                }
                            }
                        }
                        if (assetName != "")
                            break;
                    }
                }
            }
            if (!assetFound && assetName != "")
            {
                Debug.LogWarning("Dynamic" + typeString + "Library could not simulate the loading of " + assetName + " from any AssetBundles");
            }
            if (assetsToReturn.Count > 0 && callback != null)
            {
                callback(assetsToReturn.ToArray());
            }
            if (dependencies.Count > 0)
            {
                //we need to load ALL the assets from every Assetbundle that has a dependency in it.
                List<string> AssetBundlesToFullyLoad = new List<string>();
                foreach (string assetBundleName in assetBundleNamesArray)
                {
                    var allAssetBundlePaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                    bool processed = false;
                    for(int i = 0; i < allAssetBundlePaths.Length; i++)
                    {
                        for (int di = 0; di < dependencies.Count; di++)
                        {
                            if(allAssetBundlePaths[i] == dependencies[di])
                            {
                                if (!AssetBundlesToFullyLoad.Contains(assetBundleName))
                                {
                                    AssetBundlesToFullyLoad.Add(assetBundleName);
                                }
                                processed = true;
                                break;
                            }
                        }
                        if (processed) break;
                    }              
                }
                foreach (string assetBundleName in AssetBundlesToFullyLoad)
                {
                    var allAssetBundlePaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName);
                    for (int ai = 0; ai < allAssetBundlePaths.Length; ai++)
                    {
                        UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(allAssetBundlePaths[ai]);
                        //we actually only seem to need to do DCS stuff...
                        if (obj.GetType() == typeof(UMATextRecipe))
                        {
                            if (requestingUMA as UMACharacterSystem.DynamicCharacterAvatar)
                            {
                                (requestingUMA as UMACharacterSystem.DynamicCharacterAvatar).dynamicCharacterSystem.AddRecipe(obj as UMATextRecipe);
                            }
                        }
                    }
                }
                if (requestingUMA as UMACharacterSystem.DynamicCharacterAvatar)
                {
                    (requestingUMA as UMACharacterSystem.DynamicCharacterAvatar).dynamicCharacterSystem.Refresh();
                }
            }
            return assetFound;
        }

        public void SimulateLoadAssetBundle(string assetBundleToLoad)
        {
            var allAssetBundlePaths = AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleToLoad);
            UMACharacterSystem.DynamicCharacterSystem thisDCS = null;
            for (int i = 0; i < allAssetBundlePaths.Length; i++)
            {
                UnityEngine.Object obj = AssetDatabase.LoadMainAssetAtPath(allAssetBundlePaths[i]);
                //we could really do with UMAContext having a ref for DynamicCharacterSystem
                thisDCS = GameObject.Find("DynamicCharacterSystem").GetComponent<UMACharacterSystem.DynamicCharacterSystem>();
                if (obj.GetType() == typeof(UMATextRecipe))
                {
                    if (thisDCS)
                    {
                        thisDCS.AddRecipe(obj as UMATextRecipe);
                    }
                }
            }
            if (thisDCS)
            {
                thisDCS.Refresh();
            }
        }
#endif
        /// <summary>
        /// Splits the 'ResourcesFolderPath(s)' and 'AssetBundleNamesToSearch' fields up by comma if the field is using that functionality...
        /// </summary>
        /// <param name="searchString"></param>
        /// <returns></returns>
        string[] SearchStringToArray(string searchString = "")
        {
            string[] searchArray;
            if (searchString == "")
            {
                searchArray = new string[] { "" };
            }
            else
            {
                searchString.Replace(" ,", ",").Replace(", ", ",");
                if (searchString.IndexOf(",") == -1)
                {
                    searchArray = new string[1] { searchString };
                }
                else
                {
                    searchArray = searchString.Split(new string[1] { "," }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            return searchArray;
        }

#endregion

#region SPECIAL TYPES
       //DownloadingAssetsList and DownloadingAssetItem moved into their own scripts to make this one a bit more manageable!        
#endregion
    }
}