using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UMAAssetBundleManager;

namespace UMA
{

    //TODO when finished developing make this non serialized
    [System.Serializable]
    public class DownloadingAssetsList
    {
        public List<DownloadingAssetItem> downloadingItems = new List<DownloadingAssetItem>();
        Dictionary<int, List<DownloadingAssetItem>> downloadingItemsDict = new Dictionary<int, List<DownloadingAssetItem>>();
        public bool areDownloadedItemsReady = true;

        /// <summary>
        /// Generates a temporary item of type T. It then adds a new DownloadingAssetItem to downloadingItems that contains a refrence to this created temp asset and the name of the asset that it should be replaced by once the given assetbundle has completed downloading.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="requiredAssetName"></param>
        /// <param name="containingBundle"></param>
        /// <returns></returns>
        public T AddDownloadItem<T>(int batchID, string requiredAssetName, int? requiredAssetNameHash, string containingBundle, UMAAvatarBase requestingUma = null) where T : UnityEngine.Object
        {
            T thisTempAsset = null;
            if (downloadingItems.Find(item => item.requiredAssetName == requiredAssetName) == null)
            {
                if (requiredAssetNameHash == null)
                {
                    requiredAssetNameHash = UMAUtils.StringToHash(requiredAssetName);
                }
                if (typeof(T) == typeof(RaceData))
                {
                    RaceData tempAsset = ScriptableObject.Instantiate(DynamicAssetLoader.Instance.placeholderRace);
                    tempAsset.raceName = requiredAssetName;
                    tempAsset.name = requiredAssetName;
                    thisTempAsset = tempAsset as T;
                }
                else if (typeof(T) == typeof(SlotDataAsset))
                {
                    SlotDataAsset tempAsset = ScriptableObject.Instantiate(DynamicAssetLoader.Instance.placeholderSlot);
                    tempAsset.name = requiredAssetName;
                    tempAsset.slotName = requiredAssetName;
                    //also needs the name hash
                    tempAsset.nameHash = (int)requiredAssetNameHash;//we can safely force because we just set this above
                    thisTempAsset = tempAsset as T;
                }
                else if (typeof(T) == typeof(OverlayDataAsset))
                {
                    OverlayDataAsset tempAsset = ScriptableObject.Instantiate(DynamicAssetLoader.Instance.placeholderOverlay);
                    tempAsset.name = requiredAssetName;
                    tempAsset.overlayName = requiredAssetName;
                    tempAsset.nameHash = (int)requiredAssetNameHash;
                    thisTempAsset = tempAsset as T;
                }
                else if (typeof(T) == typeof(UMATextRecipe))
                {
                    UMATextRecipe tempAsset = null;
                    if (AssetBundleManager.AssetBundleIndexObject.IsAssetWardrobeRecipe(containingBundle, requiredAssetName))
                    {
                        tempAsset = (UMATextRecipe)ScriptableObject.Instantiate(DynamicAssetLoader.Instance.placeholderWardrobeRecipe);
                        tempAsset.recipeType = "Wardrobe";
                        tempAsset.wardrobeSlot = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeSlot(containingBundle, requiredAssetName);
                        tempAsset.Hides = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeHides(containingBundle, requiredAssetName);
                        tempAsset.compatibleRaces = AssetBundleManager.AssetBundleIndexObject.AssetWardrobeCompatibleWith(containingBundle, requiredAssetName);
                    }
                    else
                    {
                        tempAsset = (UMATextRecipe)ScriptableObject.Instantiate(DynamicAssetLoader.Instance.placeholderRace.baseRaceRecipe);
                    }
                    tempAsset.name = requiredAssetName;
                    thisTempAsset = tempAsset as T;
                }
                else if (typeof(T) == typeof(RuntimeAnimatorController))
                {
                    T tempAsset = (T)Activator.CreateInstance(typeof(T));
                    (tempAsset as RuntimeAnimatorController).name = requiredAssetName;
                    thisTempAsset = tempAsset as T;
                }
                else
                {
                    T tempAsset = (T)Activator.CreateInstance(typeof(T));
                    tempAsset.name = requiredAssetName;
                    thisTempAsset = tempAsset as T;
                }
                var thisDlItem = new DownloadingAssetItem(batchID, requiredAssetName, thisTempAsset, containingBundle, requestingUma);
                downloadingItems.Add(thisDlItem);
                if (!downloadingItemsDict.ContainsKey(batchID))
                {
                    downloadingItemsDict[batchID] = new List<DownloadingAssetItem>();
                }
                if (!downloadingItemsDict[batchID].Contains(thisDlItem))
                {
                    downloadingItemsDict[batchID].Add(thisDlItem);
                }
            }
            else
            {
                DownloadingAssetItem dlItem = null;
                if (downloadingItems.Find(item => item.requiredAssetName == requiredAssetName) != null)
                    dlItem = downloadingItems.Find(item => item.requiredAssetName == requiredAssetName);
                if (dlItem != null)
                    thisTempAsset = dlItem.tempAsset as T;
            }
            return thisTempAsset;
        }
        /// <summary>
        /// Removes a list of downloadingAssetItems from the downloadingItems List
        /// </summary>
        /// <param name="assetName"></param>
        public IEnumerator RemoveDownload(List<DownloadingAssetItem> itemsToRemove, string onlyUpdateType = "")
        {
            List<UMAAvatarBase> updatedUMAs = new List<UMAAvatarBase>();
            Dictionary<UMAAvatarBase, List<string>> updatedUMAs2 = new Dictionary<UMAAvatarBase, List<string>>();
            foreach (DownloadingAssetItem item in itemsToRemove)
            {
                item.isBeingRemoved = true;
            }

            foreach (DownloadingAssetItem item in itemsToRemove)
            {
                string error = "";
                //we need to check everyitem in this batch belongs to an asset bundle that has actually been loaded
                LoadedAssetBundle loadedBundleTest = AssetBundleManager.GetLoadedAssetBundle(item.containingBundle, out error);
                AssetBundle loadedBundleABTest = loadedBundleTest.m_AssetBundle;
                if (loadedBundleABTest == null && (error == null || error == ""))
                {
                    while (loadedBundleTest.m_AssetBundle == null)
                    {
                        //could say we are unpacking here
                        yield return null;
                    }
                }
                if ((error != null && error != ""))
                {
                    Debug.LogError(error);
                    yield break;
                }
            }
            //Now every item in the batch should be in a loaded bundle that is ready to use.
            foreach (DownloadingAssetItem item in itemsToRemove)
            {
                if (item != null)
                {
                    string error = "";
                    var loadedBundle = AssetBundleManager.GetLoadedAssetBundle(item.containingBundle, out error);
                    var loadedBundleAB = loadedBundle.m_AssetBundle;
                    if ((error != null && error != ""))
                    {
                        Debug.LogError(error);
                        yield break;
                    }
                    if (item.tempAsset.GetType() == typeof(RaceData))
                    {
                        RaceData actualRace = loadedBundleAB.LoadAsset<RaceData>(item.requiredAssetName);
                        UMAContext.Instance.raceLibrary.AddRace(actualRace);
                        UMAContext.Instance.raceLibrary.UpdateDictionary();
                        if (item.requestingUma != null)
                        {
                            if (!updatedUMAs.Contains(item.requestingUma))
                            {
                                updatedUMAs.Add(item.requestingUma);
                            }
                            if (!updatedUMAs2.ContainsKey(item.requestingUma))
                            {
                                updatedUMAs2.Add(item.requestingUma, new List<string>());
                            }
                            if (!updatedUMAs2[item.requestingUma].Contains("race"))
                            {
                                updatedUMAs2[item.requestingUma].Add("race");
                            }
                            //We have to do this explicitly or downloaded slots just get added into the placeholderBaseRecipe
                            item.requestingUma.umaRecipe = actualRace.baseRaceRecipe;
                            (item.requestingUma as UMACharacterSystem.DynamicCharacterAvatar).activeRace.data = actualRace;
                            (item.requestingUma as UMACharacterSystem.DynamicCharacterAvatar).activeRace.name = actualRace.raceName;
                        }
                    }
                    else if (item.tempAsset.GetType() == typeof(SlotDataAsset))
                    {
                        SlotDataAsset thisSlot = null;
                        thisSlot = loadedBundleAB.LoadAsset<SlotDataAsset>(item.requiredAssetName);
                        if (thisSlot == null)
                        {
                            //check for item.requiredAssetName + "_Slot" here since we cant get SlotDataAsset.slotName 
                            //unless the asset is actually loaded and we can only load from an asset bundle by file name
                            thisSlot = loadedBundleAB.LoadAsset<SlotDataAsset>(item.requiredAssetName + "_Slot");
                        }
                        if (thisSlot != null)
                        {
                            UMAContext.Instance.slotLibrary.AddSlotAsset(thisSlot);
                            if (item.requestingUma != null)
                            {
                                if (!updatedUMAs.Contains(item.requestingUma))
                                {
                                    updatedUMAs.Add(item.requestingUma);
                                }
                                if (!updatedUMAs2.ContainsKey(item.requestingUma))
                                {
                                    updatedUMAs2.Add(item.requestingUma, new List<string>());
                                }
                                if (!updatedUMAs2[item.requestingUma].Contains("slots"))
                                {
                                    updatedUMAs2[item.requestingUma].Add("slots");
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[DynamicAssetLoader] could not add downloaded slot" + item.requiredAssetName);
                        }
                    }
                    else if (item.tempAsset.GetType() == typeof(OverlayDataAsset))
                    {
                        OverlayDataAsset thisOverlay = null;
                        thisOverlay = loadedBundleAB.LoadAsset<OverlayDataAsset>(item.requiredAssetName);
                        if (thisOverlay != null)
                        {
                            UMAContext.Instance.overlayLibrary.AddOverlayAsset(thisOverlay);
                            if (item.requestingUma != null)
                            {
                                if (!updatedUMAs.Contains(item.requestingUma))
                                {
                                    updatedUMAs.Add(item.requestingUma);
                                }
                                if (!updatedUMAs2.ContainsKey(item.requestingUma))
                                {
                                    updatedUMAs2.Add(item.requestingUma, new List<string>());
                                }
                                if (!updatedUMAs2[item.requestingUma].Contains("overlays"))
                                {
                                    updatedUMAs2[item.requestingUma].Add("overlays");
                                }
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[DynamicAssetLoader] could not add downloaded overlay" + item.requiredAssetName + " from assetbundle " + item.containingBundle);
                        }
                    }
                    else if (item.tempAsset.GetType() == typeof(UMATextRecipe))
                    {
                        //TODO we need DynamicCharacterSystem to be a semi-singleton like UMAContext so that we dont have to rely on the UMAAvatarBase to have a refrence to it
                        if (item.requestingUma != null)
                        {
                            UMATextRecipe downloadedRecipe = loadedBundleAB.LoadAsset<UMATextRecipe>(item.requiredAssetName);
                            (item.requestingUma as UMACharacterSystem.DynamicCharacterAvatar).dynamicCharacterSystem.AddRecipe(downloadedRecipe);
                            if (!updatedUMAs.Contains(item.requestingUma))
                            {
                                updatedUMAs.Add(item.requestingUma);
                            }
                            if (!updatedUMAs2.ContainsKey(item.requestingUma))
                            {
                                updatedUMAs2.Add(item.requestingUma, new List<string>());
                            }
                            if (!updatedUMAs2[item.requestingUma].Contains("recipes"))
                            {
                                updatedUMAs2[item.requestingUma].Add("recipes");
                            }
                        }
                    }
                    else if (item.tempAsset.GetType() == typeof(RuntimeAnimatorController) && item.requestingUma != null)
                    {
                        var downloadedController = loadedBundleAB.LoadAsset<RuntimeAnimatorController>(item.requiredAssetName);
                        (item.requestingUma as UMACharacterSystem.DynamicCharacterAvatar).raceAnimationControllers.SetAnimator(downloadedController);
                    }
                    if (error != "" && error != null)
                    {
                        Debug.LogError(error);
                    }
                }
                var batchId = item.batchID;
                downloadingItems.Remove(item);
                downloadingItemsDict[batchId].Remove(item);
                if (downloadingItemsDict[batchId].Count == 0)
                {
                    downloadingItemsDict.Remove(batchId);
                }
            }
            if (updatedUMAs.Count > 0 && downloadingItems.Count == 0)//TODO this is not right. It should really be whether the batch that this UMA is in is finished because itdoesn't need to wait for any other batches...
            {
                foreach (KeyValuePair<UMAAvatarBase, List<string>> kp in updatedUMAs2)
                {
                    if (kp.Key as UMACharacterSystem.DynamicCharacterAvatar)// TODO check how this works with derived classes
                    {
                        //Refresh CharacterSystems libraries of the available items...
                        (kp.Key as UMACharacterSystem.DynamicCharacterAvatar).dynamicCharacterSystem.Refresh();
                        //the download of a wardroberecipe on its own could occur when a character is loaded from an UMAText string that uses a wardrobe item that has not been downloaded yet
                        if (kp.Value.Contains("recipes"))
                        {
                            //Force the WardrobeSlots used by a DynamicCharacterAvatar to update the recipes they refer to to the actual downloaded ones
                            (kp.Key as UMACharacterSystem.DynamicCharacterAvatar).UpdateSetSlots();
                        }
                        //Finally rebuild the character!
                        (kp.Key as UMACharacterSystem.DynamicCharacterAvatar).BuildCharacter(false);
                    }
                    else
                    {
                        kp.Key.umaData.Dirty(true, true, true);
                    }
                }
                areDownloadedItemsReady = true;
            }
            yield break;
        }

        /// <summary>
        /// Updates the list of downloadingItems, checks if any have finished downloading and if they have triggers the RemoveDownload method on them
        /// </summary>
        public void Update()
        {
            List<DownloadingAssetItem> finishedItems = new List<DownloadingAssetItem>();
            if (downloadingItems.Count > 0)
            {
                areDownloadedItemsReady = false;
                List<string> finishedBundles = new List<string>();
                foreach (KeyValuePair<int, List<DownloadingAssetItem>> kp in downloadingItemsDict)
                {
                    bool canProcessBatch = true;
                    foreach (DownloadingAssetItem dl in kp.Value)
                    {
                        string error = "";
                        if (finishedBundles.Contains(dl.containingBundle))
                        {
                            if (dl.flagForRemoval == false)
                            {
                                dl.flagForRemoval = true;
                            }
                            else
                            {
                                if (dl.isBeingRemoved)
                                    canProcessBatch = false;
                            }
                        }
                        else if (AssetBundleManager.GetLoadedAssetBundle(dl.containingBundle, out error) != null)
                        {
                            finishedBundles.Add(dl.containingBundle);
                            if (dl.flagForRemoval == false)
                            {
                                dl.flagForRemoval = true;
                            }
                            else
                            {
                                if (dl.isBeingRemoved)
                                    canProcessBatch = false;
                            }
                        }
                        else
                        {
                            canProcessBatch = false;
                        }
                        if (error != "")
                        {
                            //AssetBundleManager already logs the error
                        }
                    }
                    if (canProcessBatch)
                    {
                        finishedItems.AddRange(kp.Value);
                    }
                }
            }
            //send the finished downloads to be processed
            if (finishedItems.Count > 0)
            {
                DynamicAssetLoader.Instance.StartCoroutine(RemoveDownload(finishedItems));
            }
        }
        /// <summary>
        /// Returns the temporary asset that was generated when the DownloadingAssetItem for the given assetName was created
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="assetName"></param>
        /// <returns></returns>
        public T Get<T>(string assetName) where T : UnityEngine.Object
        {
            T tempAsset = null;
            if (downloadingItems.Find(item => item.requiredAssetName == assetName) != null)
            {
                if (downloadingItems.Find(item => item.requiredAssetName == assetName).tempAsset.GetType() == typeof(T))
                    tempAsset = downloadingItems.Find(item => item.requiredAssetName == assetName) as T;
            }
            return tempAsset;
        }
        /// <summary>
        /// Returns the download progress of the asset bundle(s) required for the given asset to become available
        /// </summary>
        /// <param name="assetName"></param>
        /// <returns></returns>
        public float GetDownloadProgressOf(string assetName)
        {
            float progress = 0;
            DownloadingAssetItem item = null;
            item = downloadingItems.Find(aitem => aitem.requiredAssetName == assetName);
            if (item != null)
            {
                progress = item.Progress;
            }
            else
            {
                Debug.Log(assetName + " was not downloading");
            }
            return progress;
        }
    }
}