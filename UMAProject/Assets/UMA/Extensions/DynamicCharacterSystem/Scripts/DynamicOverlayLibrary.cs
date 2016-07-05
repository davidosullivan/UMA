using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMAAssetBundleManager;

public class DynamicOverlayLibrary : OverlayLibrary
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
    public List<OverlayDataAsset> editorAddedAssets = new List<OverlayDataAsset>();
#endif
    [System.NonSerialized]
    [HideInInspector]
    public bool downloadAssetsEnabled = true;

    bool AllResourcesScanned = false;

    public void Start()
    {
        if (Application.isPlaying)
        {
            assetBundlesUsedDict.Clear();
            AllResourcesScanned = false;
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
        if (editorAddedAssets.Count > 0)
        {
            editorAddedAssets.Clear();
            AllResourcesScanned = false;
        }
#endif
    }

#if UNITY_EDITOR
    OverlayData GetEditorAddedAsset(int? nameHash = null, string overlayName = "")
    {
        OverlayData foundOverlay = null;
        if (editorAddedAssets.Count > 0)
        {
            foreach (OverlayDataAsset edOverlay in editorAddedAssets)
            {
                if(edOverlay != null)
                {
                    if(nameHash != null)
                    {
                        if(edOverlay.nameHash == nameHash)
                            foundOverlay = new OverlayData(edOverlay);
                    }
                    else if(overlayName != null)
                    {
                        if(overlayName != "")
                            if(edOverlay.overlayName == overlayName)
                                foundOverlay = new OverlayData(edOverlay);
                    }

                }
            }
        }
        return foundOverlay;
    }
#endif

    IEnumerator WaitForAssetBundleManager(int? nameHash = null)
    {
        while (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
        {
            yield return null;
        }
        UpdateDynamicOverlayLibrary(nameHash);
    }

    IEnumerator WaitForAssetBundleManager(string name)
    {
        while (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
        {
            yield return null;
        }
        UpdateDynamicOverlayLibrary(name);
    }

    public void UpdateDynamicOverlayLibrary(int? nameHash = null)
    {
        bool found = false;
        if (dynamicallyAddFromResources && AllResourcesScanned == false)
        {
            if (nameHash == null)
                AllResourcesScanned = true;
            found = DynamicAssetLoader.Instance.AddAssetsFromResources<OverlayDataAsset>(resourcesFolderPath, nameHash, "", AddOverlayAssets);
        }
        if (dynamicallyAddFromAssetBundles && (name == "" || found == false))
        {
            if (((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
            {
                StopCoroutine(WaitForAssetBundleManager(nameHash));
                StartCoroutine(WaitForAssetBundleManager(nameHash));
                return;
            }
            DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<OverlayDataAsset>(ref assetBundlesUsedDict, downloadAssetsEnabled, assetBundleNamesToSearch, nameHash, "", AddOverlayAssets);
        }
    }

    public void UpdateDynamicOverlayLibrary(string overlayName)
    {
        bool found = false;
        if (dynamicallyAddFromResources && AllResourcesScanned == false)
        {
            if (overlayName == "")
                AllResourcesScanned = true;
            found = DynamicAssetLoader.Instance.AddAssetsFromResources<OverlayDataAsset>(resourcesFolderPath, UMAUtils.StringToHash(overlayName), "", AddOverlayAssets);
        }
        if (dynamicallyAddFromAssetBundles && (overlayName == "" || found == false))
        {
            if (((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
            {
                StopCoroutine(WaitForAssetBundleManager(overlayName));
                StartCoroutine(WaitForAssetBundleManager(overlayName));
                return;
            }
            DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<OverlayDataAsset>(ref assetBundlesUsedDict, downloadAssetsEnabled, assetBundleNamesToSearch, null, overlayName, AddOverlayAssets);
        }
    }

    private void AddOverlayAssets(OverlayDataAsset[] overlays)
    {
        foreach (OverlayDataAsset overlay in overlays)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if(!editorAddedAssets.Contains(overlay))
                    editorAddedAssets.Add(overlay);
            }
            else
#endif
            AddOverlayAsset(overlay);
        }
        StartCoroutine(CleanOverlaysFromResourcesAndBundles());
    }

    IEnumerator CleanOverlaysFromResourcesAndBundles()
    {
        yield return null;
        Resources.UnloadUnusedAssets();
        yield break;
    }

    public override OverlayData InstantiateOverlay(string name)
    {
        OverlayData res;
        try
        {
            res = base.InstantiateOverlay(name);
        }
        catch
        {
            res = null;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(null, name);
        }
#endif
        if (res == null)
        {
            //we try to load the overlay dynamically
            UpdateDynamicOverlayLibrary(name);
            try {
                res = base.InstantiateOverlay(name);
            }
            catch
            {
                res = null;
            }
            if (res == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    res = GetEditorAddedAsset(null, name);
                    if (res != null)
                    {
                        return res;
                    }
                }
#endif
                throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + name);
            }
        }
        return res;
    }
    //we dont seem to be able to use nameHash for some reason so in this case we are screwed- DOES THIS EVER HAPPEN?
    public override OverlayData InstantiateOverlay(int nameHash)
    {
        Debug.Log("OverlayLibrary tried to InstantiateOverlay using Hash");
        OverlayData res;
        try
        {
            res = base.InstantiateOverlay(nameHash);
        }
        catch
        {
            res = null;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(nameHash);
        }
#endif
        if (res == null)
        {
            UpdateDynamicOverlayLibrary(nameHash);
            try {
                res = base.InstantiateOverlay(nameHash);
            }
            catch
            {
                res = null;
            }
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
                throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + nameHash);
            }
        }
        return res;
    }
    public override OverlayData InstantiateOverlay(string name, Color color)
    {
        OverlayData res;
        try
        {
            res = base.InstantiateOverlay(name);
        }
        catch
        {
            res = null;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(null, name);
        }
#endif
        if (res == null)
        {
            //we do something
            UpdateDynamicOverlayLibrary(name);
            try {
                res = base.InstantiateOverlay(name);
            }
            catch
            {
                res = null;
            }
            if (res == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    res = GetEditorAddedAsset(null, name);
                    if (res != null)
                    {
                        res.colorData.color = color;
                        return res;
                    }
                }
#endif
                throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + name);
            }
        }
        res.colorData.color = color;
        return res;
    }
    //we dont seem to be able to use nameHash for some reason so in this case we are screwed- DOES THIS EVER HAPPEN?
    public override OverlayData InstantiateOverlay(int nameHash, Color color) {
        Debug.Log("OverlayLibrary tried to InstantiateOverlay using Hash");
        OverlayData res;
        try
        {
            res = base.InstantiateOverlay(nameHash);
        }
        catch
        {
            res = null;
        }
#if UNITY_EDITOR
        if (!Application.isPlaying && res == null)
        {
            res = GetEditorAddedAsset(nameHash);
        }
#endif
        if (res == null)
        {
            UpdateDynamicOverlayLibrary(nameHash);
            try {
                res = base.InstantiateOverlay(nameHash);
            }
            catch
            {
                res = null;
            }
            if (res == null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    res = GetEditorAddedAsset(nameHash);
                    if (res != null)
                    {
                        res.colorData.color = color;
                        return res;
                    }
                }
#endif
                throw new UMAResourceNotFoundException("dOverlayLibrary: Unable to find: " + nameHash);
            }
        }
        res.colorData.color = color;
        return res;
    }
    /// <summary>
    /// Gets the originating asset bundle.
    /// </summary>
    /// <returns>The originating asset bundle.</returns>
    /// <param name="overlayName">Overlay name.</param>
    public string GetOriginatingAssetBundle(string overlayName)
    {
        string originatingAssetBundle = "";
        if (assetBundlesUsedDict.Count > 0)
        {
            foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
            {
                if (kp.Value.Contains(overlayName))
                {
                    originatingAssetBundle = kp.Key;
                    break;
                }
            }
        }
        if (originatingAssetBundle == "")
        {
            Debug.Log(overlayName + " has not been loaded from any AssetBundle");
        }
        else
        {
            Debug.Log("originatingAssetBundle for " + overlayName + " was " + originatingAssetBundle);
        }
        return originatingAssetBundle;
    }
}
