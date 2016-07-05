using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMAAssetBundleManager;

public class DynamicRaceLibrary : RaceLibrary
{

    //extra fields for Dynamic Version
    public bool dynamicallyAddFromResources;
    public string resourcesFolderPath = "";
    public bool dynamicallyAddFromAssetBundles;
    public string assetBundleNamesToSearch = "";
    //This is a ditionary of asset bundles that were loaded into the library at runtime. 
    //CharacterAvatar can query this this to find out what asset bundles were required to create itself 
    //or other scripts can use it to find out which asset bundles are being used by the Libraries at any given point.
    public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
#if UNITY_EDITOR
    [HideInInspector]
    List<RaceData> editorAddedAssets = new List<RaceData>();
#endif

    [System.NonSerialized]
    [HideInInspector]
    public bool downloadAssetsEnabled = true;

    bool AllResourcesScanned = false;

    Coroutine WaitForAssetBundleManagerCO = null;

    public void Start()
    {
        if (Application.isPlaying)
        {
            AllResourcesScanned = false;
            assetBundlesUsedDict.Clear();
        }
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            ClearEditorAddedAssets();
        }
#endif
    }

    /// <summary>
    /// Clears any editor added assets when the Scene is closed
    /// </summary>
    void OnDestroy()
    {
#if UNITY_EDITOR
        ClearEditorAddedAssets();
#endif
    }

    public void ClearEditorAddedAssets()
    {
#if UNITY_EDITOR
        if(editorAddedAssets.Count > 0)
        {
            editorAddedAssets.Clear();
            AllResourcesScanned = false;
        }
#endif
    }

#if UNITY_EDITOR
    RaceData GetEditorAddedAsset(int? raceHash = null, string raceName = "")
    {
        RaceData foundRaceData = null;
        if(editorAddedAssets.Count > 0)
        {
            foreach(RaceData edRace in editorAddedAssets)
            {
                if (edRace != null)
                {
                    if (raceHash != null)
                    {
                        if (UMAUtils.StringToHash(edRace.raceName) == raceHash)
                            foundRaceData = edRace;
                    }
                    else if (raceName != null)
                    {
                        if(raceName != "")
                        if (edRace.raceName == raceName)
                            foundRaceData = edRace;
                    }
                }
            }
        }
        return foundRaceData;
    }
#endif

    IEnumerator WaitForAssetBundleManager(bool downloadAssets, int? raceHash = null)
    {
        while(AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
        {
            yield return null;
        }
        UpdateDynamicRaceLibrary(downloadAssets, raceHash);
    }

    IEnumerator WaitForAssetBundleManager(string raceName)
    {
        while (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
        {
            yield return null;
        }
        UpdateDynamicRaceLibrary(raceName);
    }

    public void UpdateDynamicRaceLibrary(bool downloadAssets, int? raceHash = null)
    {
        bool found = false;
        if (dynamicallyAddFromResources && AllResourcesScanned == false)
        {
            if (raceHash == null)
                AllResourcesScanned = true;
            found = DynamicAssetLoader.Instance.AddAssetsFromResources<RaceData>(resourcesFolderPath, raceHash, "", AddRaces);
        }
        if (dynamicallyAddFromAssetBundles && (raceHash == null || found == false))
        {
            if(((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
            {
                if (WaitForAssetBundleManagerCO != null)
                    StopCoroutine(WaitForAssetBundleManagerCO);
                WaitForAssetBundleManagerCO = StartCoroutine(WaitForAssetBundleManager(downloadAssets, raceHash));
                return;
            }
            DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<RaceData>(ref assetBundlesUsedDict, downloadAssets, assetBundleNamesToSearch, raceHash, "", AddRaces);
        }
    }

    public void UpdateDynamicRaceLibrary(string raceName)
    {
        bool found = false;
        if (dynamicallyAddFromResources && AllResourcesScanned == false)
        {
            if (raceName == "")
                AllResourcesScanned = true;
            found = DynamicAssetLoader.Instance.AddAssetsFromResources<RaceData>(resourcesFolderPath, null, raceName, AddRaces);
        }
        if (dynamicallyAddFromAssetBundles && (raceName == "" || found == false))
        {
            if (((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
            {
                if(WaitForAssetBundleManagerCO != null)
                StopCoroutine(WaitForAssetBundleManagerCO);
                WaitForAssetBundleManagerCO = StartCoroutine(WaitForAssetBundleManager(raceName));
                return;
            }
            DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<RaceData>(ref assetBundlesUsedDict, downloadAssetsEnabled, assetBundleNamesToSearch, null, raceName, AddRaces);
        }
    }

    private void AddRaces(RaceData[] races)
    {
        foreach(RaceData race in races)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if(!editorAddedAssets.Contains(race))
                    editorAddedAssets.Add(race);
            }
            else
#endif
                AddRace(race);
        }
        StartCoroutine(CleanRacesFromResourcesAndBundles());
    }

    IEnumerator CleanRacesFromResourcesAndBundles()
    {
        yield return null;
        Resources.UnloadUnusedAssets();
        yield break;
    }

#pragma warning disable 618
    //We need to override AddRace Too because if the element is not in the list anymore it causes an error...
    //BUT THIS WILL BE OBSOLETE raceElementList is going to be marked PRIVATE so this wont work soon...
    override public void AddRace(RaceData race)
    {
        if (race == null) return;
        race.UpdateDictionary();
        try
        {
            base.AddRace(race);
        }
        catch
        {
            //if there is an error it will be because RaceElementList contained an empty refrence
            List<RaceData> newRaceElementList = new List<RaceData>();
            for (int i = 0; i < raceElementList.Length; i++)
            {
                if (raceElementList[i] != null)
                {
                    raceElementList[i].UpdateDictionary();
                    newRaceElementList.Add(raceElementList[i]);
                }
            }
            raceElementList = newRaceElementList.ToArray();
            base.AddRace(race);
        }
    }
#pragma warning restore 618

    public override RaceData GetRace(string raceName)
    {
        if ((raceName == null) || (raceName.Length == 0))
            return null;

        RaceData res;
        res = base.GetRace(raceName);
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(null, raceName);
        }
#endif
        if (res == null)
        {
            //we try to load the race dynamically
            UpdateDynamicRaceLibrary(raceName);
            res = base.GetRace(raceName);
            if (res == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    res = GetEditorAddedAsset(null, raceName);
                    if (res != null)
                    {
                        return res;
                    }
                }
#endif
                return null;
            }
        }
        return res;
    }
    public override RaceData GetRace(int nameHash)
    {
        if (nameHash == 0)
            return null;

        RaceData res;
        res = base.GetRace(nameHash);
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(nameHash);
        }
#endif
        if (res == null)
        {
            UpdateDynamicRaceLibrary(true, nameHash);
            res = base.GetRace(nameHash);
            if (res == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    res = GetEditorAddedAsset(nameHash);
                    if (res != null)
                    {
                        return res;
                    }
                }
#endif
                return null;
            }
        }
        return res;
    }
    /// <summary>
    /// Returns the current list of races without adding from assetBundles or Resources
    /// </summary>
    /// <returns></returns>
    public RaceData[] GetAllRacesBase()
    {
        return base.GetAllRaces();
    }
    /// <summary>
    /// Gets all the races that are available including in Resources (but does not cause downloads for races that are in assetbundles)
    /// </summary>
    /// <returns></returns>
    public override RaceData[] GetAllRaces()
    {
        UpdateDynamicRaceLibrary(false);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            //we need a combined array of the editor added assets and the baseGetAllRaces Array
            List<RaceData> combinedRaceDatas = new List<RaceData>(base.GetAllRaces());
            if(editorAddedAssets.Count > 0)
            {
                combinedRaceDatas.AddRange(editorAddedAssets);
            }
            return combinedRaceDatas.ToArray();
        }
        else
#endif
        return base.GetAllRaces();
    }

    /// <summary>
    /// Gets the originating asset bundle.
    /// </summary>
    /// <returns>The originating asset bundle.</returns>
    /// <param name="raceName">Race name.</param>
    public string GetOriginatingAssetBundle(string raceName)
    {
        string originatingAssetBundle = "";
        if (assetBundlesUsedDict.Count > 0)
        {
            foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
            {
                if (kp.Value.Contains(raceName))
                {
                    originatingAssetBundle = kp.Key;
                    break;
                }
            }
        }
        if (originatingAssetBundle == "")
        {
            Debug.Log(raceName + " was not found in any loaded AssetBundle");
        }
        else
        {
            Debug.Log("originatingAssetBundle for " + raceName + " was " + originatingAssetBundle);
        }
        return originatingAssetBundle;
    }
}
