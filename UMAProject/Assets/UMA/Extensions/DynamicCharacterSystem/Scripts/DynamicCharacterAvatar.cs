//#define IncludeDeprecated

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Generic;
using UMA;
using UMA.PoseTools;//so we can set the expression set based on the race
using System.IO;
using UMAAssetBundleManager;


namespace UMACharacterSystem
{
    public class DynamicCharacterAvatar : UMAAvatarBase
    {

        public DynamicCharacterSystem dynamicCharacterSystem;

        //This will generate itself from a list available Races and set itself to the current value of activeRace.name
        public RaceSetter activeRace;

        bool flagForRebuild = false;
        bool flagForReload = false;

        //TO DO can we get rid of WardrobeRecipes now and just use WardrobeRecipes2?
        public Dictionary<WardrobeSlot, UMATextRecipe> WardrobeRecipes = new Dictionary<WardrobeSlot, UMATextRecipe>();
        public Dictionary<string, UMATextRecipe> WardrobeRecipes2 = new Dictionary<string, UMATextRecipe>();

        [Tooltip("You can add wardrobe recipes for many races in here and only the ones that apply to the active race will be applied to the Avatar")]
        public WardrobeRecipeList preloadWardrobeRecipes;

        [Tooltip("Add animation controllers here for specific races. If no Controller is found for the active race, the Default Animation Controller is used")]
        public RaceAnimatorList raceAnimationControllers;

        [Tooltip("Any colors here are set when the Avatar is first generated and updated as the values are changed using the color sliders")]
        public ColorValueList characterColors = new ColorValueList();

        //in order to preserve the state of the Avatar when switching races (rather than having it loose its wardrobe when switching back and forth) we need to cache its current state before changing to the desired race
        Dictionary<string, CacheStateInfo> cacheStates = new Dictionary<string, CacheStateInfo>();
        class CacheStateInfo
        {
            public Dictionary<string, UMATextRecipe> wardrobeCache;
            public List<ColorValue> colorCache;
            public CacheStateInfo(Dictionary<string, UMATextRecipe> _wardrobeCache, List<ColorValue> _colorCache)
            {
                wardrobeCache = _wardrobeCache;
                colorCache = _colorCache;
            }
        }
        //load/save fields
        public enum loadPathTypes { persistentDataPath, streamingAssetsPath, Resources, FileSystem, CharacterSystem };
        public enum savePathTypes { persistentDataPath, streamingAssetsPath, Resources, FileSystem };
        public loadPathTypes loadPathType;
        public string loadPath;
        public string loadFilename;
        public bool loadFileOnStart;
        [Tooltip("If true if a loaded recipe requires assetBundles to download the Avatar will wait until they are downloaded before creating itself. Otherwise a temporary character will be shown.")]
        public bool waitForBundles;
        public savePathTypes savePathType;
        public string savePath;
        public string saveFilename;
        public bool makeUnique;

        public Vector3 BoundsOffset;

        private HashSet<string> HiddenSlots = new HashSet<string>();
        [HideInInspector]
        public List<string> assetBundlesUsedbyCharacter = new List<string>();
        [HideInInspector]
        public bool AssetBundlesUsedbyCharacterUpToDate = true;


        //this gets set by UMA when the chracater is loaded, but we need to know the race before hand to we can decide what wardrobe slots to use, so we have a work around above with RaceSetter
        public RaceData RaceData
        {
            get { return base.umaRace; }
            private set { base.umaRace = value; }
        }


        public override void Start()
        {
            StopAllCoroutines();
            base.Start();

            if (loadFilename != "" && loadFileOnStart)
            {
                DoLoad();
            }
            else
            {
                StartCoroutine(StartStartCoroutine());
            }
        }

        public void Update()
        {

        }

        //@ECURTZ right now each converters LateUpdateSkeleton method has to be called by something else since the converters prefab itself is in active and so cant do LateUpdate or do a StartCoRoutine for the end of frame reset (which we also hopefully wont need)
        void LateUpdate()
        {
            bool needsOnPostRender = false;
            if (activeRace.racedata != null && umaData != null && umaData.skeleton != null)
            {
                if (activeRace.racedata.dnaConverterList.Length > 0)
                {
                    foreach (DnaConverterBehaviour converter in activeRace.racedata.dnaConverterList)
                    {
                        if(converter.GetType() == typeof(DynamicDNAConverterBehaviour))
                        if (((DynamicDNAConverterBehaviour)converter).LateUpdateSkeleton(umaData))
                        {
                            needsOnPostRender = true;
                        }
                    }
                }
            }
            if (needsOnPostRender) {
                StopCoroutine(OnPostRender());
                StartCoroutine(OnPostRender());
            }
        }

        IEnumerator OnPostRender()
        {
            yield return new WaitForEndOfFrame();

            if (activeRace.racedata != null && umaData != null)
            {
                umaData.skeleton.RestoreAll();
            }
        }

        IEnumerator StartStartCoroutine()
        {
            //TODO Need this here?
            DynamicAssetLoader.Instance.requestingUMA = this;
            //
            bool mayRequireDownloads = false;
            if(activeRace.data == null)//This MUST validate otherwise the desired race will not download
            {
                mayRequireDownloads = true;
            }
            if (mayRequireDownloads)
            {
                while (AssetBundleManager.AssetBundleIndexObject == null && AssetBundleManager.SimulateOverride == false)
                {
                    yield return null;
                }
                //if the AssetBundleManifest download request fails in the editor AssetBundleManager.SimulateOverride becomes true and thus we are in simulation mode so we can just carry on.
                if (AssetBundleManager.SimulateOverride == true)
                {
                    mayRequireDownloads = false;
                }
                else
                {
                    //Geneate a new BatchID so that any items added by the folowing actions that trigger a download will all be part of the same batch and get processed in the same cycle once downloaded
                    DynamicAssetLoader.Instance.GenerateBatchID();
                    //make any items added by the folowing actions that trigger a download refrence this UMA so it can be updated when the batch of downloads completes
                    DynamicAssetLoader.Instance.requestingUMA = this;
                }
            }
            if(umaRecipe == null)
            {
                SetStartingRace();
            }
            else
            {
                //we have no choice but to wait for any downloads required by an umaRecipe that the avatar starts with...
                yield return StartCoroutine(SetStartingRaceFromUmaRecipe());
            }
            if (preloadWardrobeRecipes.loadDefaultRecipes && preloadWardrobeRecipes.recipes.Count > 0)
            {
                if (mayRequireDownloads)
                {
                    LoadDefaultWardrobe(true);
                    DynamicAssetLoader.Instance.requestingUMA = null;
                    dynamicCharacterSystem.Refresh();
                    if (waitForBundles)
                    {
                        while (DynamicAssetLoader.Instance.assetBundlesDownloading)
                        {
                            yield return null;
                        }
                    }
                }
                else
                {
                    LoadDefaultWardrobe();
                    BuildCharacter(false);
                }
                yield break;
            }
            else
            {
                if (mayRequireDownloads)//TODO this should be Definitive- is it?
                {
                    DynamicAssetLoader.Instance.requestingUMA = null;
                    dynamicCharacterSystem.Refresh();
                    if (waitForBundles)
                    {
                        while (DynamicAssetLoader.Instance.assetBundlesDownloading)
                        {
                            yield return null;
                        }
                    }
                }
                else
                {
                    BuildCharacter(false);
                }
                yield break;
            }
        }

        /// <summary>
        /// Sets the starting race of the avatar based on the value of the 'activeRace' dropdown
        /// </summary>
        void SetStartingRace()
        {
            if (activeRace.data != null)//This MUST validate the race (i.e. try to download it if its missing)
            {
                activeRace.name = activeRace.racedata.raceName;
                umaRecipe = activeRace.racedata.baseRaceRecipe;
            }
            //otherwise...
            else if (activeRace.name != null)
            {
                //This only happens when the Avatar itself has an active race set to be one that is in an assetbundle
                activeRace.data = context.raceLibrary.GetRace(activeRace.name);// this will trigger a download if the race is in an asset bundle
                if (activeRace.racedata != null)
                {
                    umaRecipe = activeRace.racedata.baseRaceRecipe;
                }
            }
            //Failsafe: if everything else fails we try to do something based on the name of the race that was set
            if (umaRecipe == null)
            {//if its still null just load the first available race- try to match the gender at least
                var availableRaces = context.raceLibrary.GetAllRaces();
                if (availableRaces.Length > 0)
                {
                    bool raceFound = false;
                    if (activeRace.name.IndexOf("Female") > -1)
                    {
                        foreach (RaceData race in availableRaces)
                        {
                            if (race != null)
                            {
                                if (race.raceName.IndexOf("Female") > -1 || race.raceName.IndexOf("female") > -1)
                                {
                                    activeRace.name = race.raceName;
                                    activeRace.data = race;
                                    umaRecipe = activeRace.racedata.baseRaceRecipe;
                                    raceFound = true;
                                }
                            }
                        }
                    }
                    if (!raceFound)
                    {
                        activeRace.name = availableRaces[0].raceName;
                        activeRace.data = availableRaces[0];
                        umaRecipe = activeRace.racedata.baseRaceRecipe;
                    }
                }
            }
        }

        /// <summary>
        /// the umaRecipe that can be set in the umaRecipe field my also require assets to be downloaded so we need to deal with that
        /// </summary>
        /// <returns></returns>
        IEnumerator SetStartingRaceFromUmaRecipe()
        {
            var umaRecipeBU = umaRecipe;//needed?
            //we test load. if racedata is null then we know the recipe triggers downloads so we have to wait until they are done
            var umaDataRecipeTester = new UMAData.UMARecipe();
            try
            {
                umaRecipe.Load(umaDataRecipeTester, context);
            }
            catch { }
            if (umaDataRecipeTester.raceData == null)
            {
                //if AssetBundleManager has not initialized yet trying to get the racedata when unpacking the recipe will not have caused a download
                if ((AssetBundleManager.AssetBundleIndexObject == null && AssetBundleManager.SimulateOverride == false))
                {
                    while (AssetBundleManager.AssetBundleIndexObject == null && AssetBundleManager.SimulateOverride == false)
                    {
                        yield return null;
                    }
                    if (AssetBundleManager.SimulateOverride == true)
                    {
                        try
                        {
                            umaRecipe.Load(umaDataRecipeTester, context);
                        }
                        catch { }
                        if (umaDataRecipeTester.raceData != null)
                        {
                            activeRace.data = umaDataRecipeTester.raceData;
                            activeRace.name = umaDataRecipeTester.raceData.raceName;
                            umaRecipe = umaRecipeBU;
                        }
                    }
                    else
                    {
                        //kick off the download of the racedata by requesting it again
                        try
                        {
                            umaRecipe.Load(umaDataRecipeTester, context);
                        }
                        catch { }
                        while (DynamicAssetLoader.Instance.assetBundlesDownloading)
                        {
                            yield return null;
                        }
                        try
                        {
                            umaRecipe.Load(umaDataRecipeTester, context);
                        }
                        catch { }
                        activeRace.data = umaDataRecipeTester.raceData;
                        activeRace.name = umaDataRecipeTester.raceData.raceName;
                        umaRecipe = umaRecipeBU;
                    }
                }
                else
                {
                    //if we are in simulation mode then the recipe cannot have had any racedata
                    if (AssetBundleManager.SimulateOverride != false)
                    {
                        Debug.LogWarning("umaRecipe " + umaRecipe.name + " did not appear to have any RaceData set. Avatar could not load.");
                    }
                    else
                    {
                        //the asset should be downloading so check for the raceData asset to stop being null
                        while (DynamicAssetLoader.Instance.assetBundlesDownloading)//this will wait for all requested asset bundles to download which we dont want really
                        {
                            yield return null;
                        }
                        try
                        {
                            umaRecipe.Load(umaDataRecipeTester, context);
                        }
                        catch { }
                        activeRace.data = umaDataRecipeTester.raceData;
                        activeRace.name = umaDataRecipeTester.raceData.raceName;
                        umaRecipe = umaRecipeBU;
                    }
                }
            }
            else
            {
                activeRace.data = umaDataRecipeTester.raceData;
                activeRace.name = umaDataRecipeTester.raceData.raceName;
            }
            yield return true;
        }

        /// <summary>
        /// Loads the default wardobe items set in 'defaultWardrobeRecipes' in the CharacterAvatar itself onto the Avatar's base race recipe. Use this to make a naked avatar always have underwear or a set of clothes for example
        /// </summary>
        /// <param name="allowDownloadables">Optionally allow this function to trigger downloads of wardrobe recipes in an asset bundle</param>
        public void LoadDefaultWardrobe(bool allowDownloadables = false)
        {
            int validRecipes = preloadWardrobeRecipes.Validate(dynamicCharacterSystem, allowDownloadables, activeRace.name);
            if (validRecipes != 0)
            {
                foreach (WardrobeRecipeListItem recipe in preloadWardrobeRecipes.recipes)
                {
                    if (recipe._recipe != null)
                    {
                        if (activeRace.name == "")//should never happen TODO: Check if it does
                        {
                            SetSlot(recipe._recipe);
                        }
                        //this does not need to validate
                        else if (((recipe._recipe.compatibleRaces.Count == 0 || recipe._recipe.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))))
                        {
                            //the check activeRace.data.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot) makes sure races that are backwards compatible 
                            //with another race but which dont have all of that races wardrobeslots, dont try to load things they dont have wardrobeslots for
                            //However we need to make sure that if a slot has already been assigned that is DIRECTLY compatible with the race it is not overridden
                            //by one that is backwards compatible
                            //this does not need to validate
                            if(activeRace.racedata.findBackwardsCompatibleWith(recipe._recipe.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(recipe._recipe.wardrobeSlot))
                            {
                                if (!WardrobeRecipes2.ContainsKey(recipe._recipe.wardrobeSlot)){
                                    SetSlot(recipe._recipe);
                                }
                            }
                            else {
                                SetSlot(recipe._recipe);
                            }
                        }
                    }
                    else
                    {
                        if (allowDownloadables)
                        {
                            //this means a temporary recipe was not returned for some reason
                            Debug.LogWarning("[CharacterAvatar:LoadDefaultWardrobe] recipe._recipe was null for " + recipe._recipeName);
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Sets the Expression set for the Avatar based on the Avatars set race.
        /// </summary>
        public void SetExpressionSet()
        {
            if (this.gameObject.GetComponent<UMAExpressionPlayer>() == null)
            {
                return;
            }
            UMAExpressionSet expressionSetToUse = null;
            if (activeRace.racedata != null)
            {
                expressionSetToUse = activeRace.racedata.expressionSet;
            }
            if (expressionSetToUse != null)
            {
                //set the expression set and reset all the values
                var thisExpressionsPlayer = this.gameObject.GetComponent<UMAExpressionPlayer>();
                thisExpressionsPlayer.expressionSet = expressionSetToUse;
                thisExpressionsPlayer.Values = new float[thisExpressionsPlayer.Values.Length];
            }
        }

        /// <summary>
        /// Sets the Animator Controller for the Avatar based on the best match found for the Avatars race. If no animator for the active race has explicitly been set, the default animator is used
        /// </summary>
        public void SetAnimatorController()
        {
            int validControllers = raceAnimationControllers.Validate();

            RuntimeAnimatorController controllerToUse = raceAnimationControllers.defaultAnimationController;
            if (validControllers > 0)
            {
                foreach (RaceAnimator raceAnimator in raceAnimationControllers.animators)
                {
                    if (raceAnimator.raceName == activeRace.name && raceAnimator.animatorController != null)
                    {
                        controllerToUse = raceAnimator.animatorController;
                        break;
                    }
                }
            }
            animationController = controllerToUse;
            if (this.gameObject.GetComponent<Animator>())
            {
                this.gameObject.GetComponent<Animator>().runtimeAnimatorController = controllerToUse;
            }
        }
        /// <summary>
        /// Sets the given color name to the given OverlayColorData optionally updating the texture (default:true)
        /// </summary>
        /// <param name="Name"></param>
        /// <param name="colorData"></param>
        /// <param name="UpdateTexture"></param>
        public void SetColor(string Name, OverlayColorData colorData, bool UpdateTexture = true)
        {
            characterColors.SetColor(Name, colorData);
            if (UpdateTexture)
            {
                UpdateColors();
                ForceUpdate(false, UpdateTexture, false);
            }
        }
        /// <summary>
        /// Builds the character by combining the Avatar's raceData.baseRecipe with the any wardrobe recipes that have been applied to the avatar.
        /// </summary>
        /// <returns>Can also be used to return an array of additional slots if this avatars flagForReload field is set to true before calling</returns>
        /// <param name="RestoreDNA">If updating the same race set this to true to restore the current DNA.</param>
        /// <param name="prioritySlot">Priority slot- use this to make slots lower down the wardrobe slot list suppress ones that are higher.</param>
        /// <param name="prioritySlotOver">a list of slots the priority slot overrides</param>
        public UMARecipeBase[] BuildCharacter(bool RestoreDNA = true, string prioritySlot = "", List<string> prioritySlotOver = default(List<string>))
        {
            if (prioritySlotOver == default(List<string>))
                prioritySlotOver = new List<string>();

            HiddenSlots.Clear();

            UMADnaBase[] CurrentDNA = null;
            if (umaData != null)
            {
                if (umaData.umaRecipe != null)
                {
                    CurrentDNA = umaData.umaRecipe.GetAllDna();
                }
            }
            if (CurrentDNA == null)
                RestoreDNA = false;

            List<UMARecipeBase> Recipes = new List<UMARecipeBase>();
            List<string> SuppressSlotsStrings = new List<string>();

            if ((preloadWardrobeRecipes.loadDefaultRecipes || WardrobeRecipes2.Count > 0) && activeRace.racedata != null)
            {
                foreach (UMATextRecipe utr in WardrobeRecipes2.Values)
                {
                    if (utr.suppressWardrobeSlots != null)
                    {
                        if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
                        {
                            if (prioritySlotOver.Count > 0)
                            {
                                foreach (string suppressedSlot in prioritySlotOver)
                                {
                                    if (suppressedSlot == utr.wardrobeSlot)
                                    {
                                        SuppressSlotsStrings.Add(suppressedSlot);
                                    }
                                }
                            }
                            if (!SuppressSlotsStrings.Contains(utr.wardrobeSlot))
                            {
                                foreach (string suppressedSlot in utr.suppressWardrobeSlots)
                                {
                                    if (prioritySlot == "" || prioritySlot != suppressedSlot)
                                        SuppressSlotsStrings.Add(suppressedSlot);
                                }
                            }
                        }
                    }
                }

                foreach (string ws in activeRace.racedata.wardrobeSlots)//this doesn't need to validate racedata- we wouldn't be here if it was null
                {
                    if (SuppressSlotsStrings.Contains(ws))
                    {
                        continue;
                    }
                    if (WardrobeRecipes2.ContainsKey(ws))
                    {
                        UMATextRecipe utr = WardrobeRecipes2[ws];
                        //we can use the race data here to filter wardrobe slots
                        //if checking a backwards compatible race we also need to check the race has a compatible wardrobe slot, 
                        //since while a race can be backwards compatible it does not *have* to have all the same wardrobeslots as the race it is compatible with
                        if (activeRace.name == "" || ((utr.compatibleRaces.Count == 0 || utr.compatibleRaces.Contains(activeRace.name)) || (activeRace.racedata.findBackwardsCompatibleWith(utr.compatibleRaces) && activeRace.racedata.wardrobeSlots.Contains(utr.wardrobeSlot))))
                        {
                            Recipes.Add(utr);
                            if(utr.Hides.Count > 0)
                            {
                                foreach (string s in utr.Hides)
                                {
                                    HiddenSlots.Add(s);
                                }
                            }
                        }
                    }
                }
            }

            foreach (UMATextRecipe utr in umaAdditionalRecipes)
            {
                if (utr.Hides.Count > 0)
                {
                    foreach (string s in utr.Hides)
                    {
                        HiddenSlots.Add(s);
                    }
                }
            }

            // Load all the recipes
            if (!flagForReload)
            {
                if (flagForRebuild)
                {
                    flagForReload = true;
                    flagForRebuild = false;
                }
                //set the expression set to match the new character- needs to happen before load...
                if (activeRace.racedata != null && !RestoreDNA)//This does NOT need to validate the race
                {
                    SetAnimatorController();
                    SetExpressionSet();
                }
                Load(umaRecipe, Recipes.ToArray());

                // Add saved DNA
                if (RestoreDNA)
                {
                    umaData.umaRecipe.ClearDna();
                    foreach (UMADnaBase ud in CurrentDNA)
                    {
                        umaData.umaRecipe.AddDna(ud);
                    }
                }
                return null;
            }
            else
            {
                //CONFIRM THIS IS NOT NEEDED ANY MORE
                flagForReload = false;
                //this is used by the load function in the case where an umaRecipe is directly defined since in this case we dont know what the race of that recipe is until its loaded
                return Recipes.ToArray();
            }
        }

        void RemoveHiddenSlots()
        {
            List<SlotData> NewSlots = new List<SlotData>();
            foreach (SlotData sd in umaData.umaRecipe.slotDataList)
            {
                if (sd == null)
                    continue;
                if (!HiddenSlots.Contains(sd.asset.slotName))
                {
                    NewSlots.Add(sd);
                }
            }
            umaData.umaRecipe.slotDataList = NewSlots.ToArray();
        }

        /// <summary>
        /// Applies these colors to the loaded Avatar and adds any colors the loaded Avatar has which are missing from this list, to this list
        /// </summary>
        void UpdateColors()
        {
            foreach (UMA.OverlayColorData ucd in umaData.umaRecipe.sharedColors)
            {
                if (ucd.HasName())
                {
                    OverlayColorData c;
                    if (characterColors.GetColor(ucd.name, out c))
                    {
                        ucd.color = c.color;
                        if (ucd.channelAdditiveMask.Length == 3)
                            ucd.channelAdditiveMask[2] = c.channelAdditiveMask[2];
                    }
                    else
                    {
                        characterColors.SetColor(ucd.name, ucd);
                    }
                }
            }
        }

        protected void UpdateUMA()
        {
            if (umaRace != umaData.umaRecipe.raceData)
            {
                UpdateNewRace();
            }
            else
            {
                UpdateSameRace();
            }
        }

        public void ForceUpdate(bool DnaDirty, bool TextureDirty = false, bool MeshDirty = false)
        {
            umaData.Dirty(DnaDirty, TextureDirty, MeshDirty);
        }

        /// <summary>
        /// Clears all the wardrobe slots of any wardrobeRecipes that have been set on the avatar
        /// </summary>
        public void ClearSlots()
        {
            WardrobeRecipes.Clear();
            WardrobeRecipes2.Clear();
        }
        /// <summary>
        /// Clears the given wardrobe slot of any recipes that have been set on the Avatar
        /// </summary>
        /// <param name="ws"></param>
        public void ClearSlot(string ws)
        {
            if (WardrobeRecipes2.ContainsKey(ws))
            {
                WardrobeRecipes2.Remove(ws);
            }
        }
        /// <summary>
        /// Clears the given wardrobe slot of any recipes that have been set on the Avatar
        /// </summary>
        /// <param name="ws"></param>
        public void ClearSlot(WardrobeSlot ws)
        {
            if (WardrobeRecipes.ContainsKey(ws))
            {
                WardrobeRecipes.Remove(ws);
            }
        }
        /// <summary>
        /// Use when temporary wardrobe recipes have been used while the real ones have been downloading. Will replace the temp textrecipes with the downloaded ones.
        /// </summary>
        public void UpdateSetSlots(string recipeToUpdate = "")
        {
            string slotsInWardrobe = "";
            foreach (KeyValuePair<string, UMATextRecipe> kp in WardrobeRecipes2)
            {
                slotsInWardrobe = slotsInWardrobe + " , " + kp.Key;
            }
            Dictionary<string, UMATextRecipe> newWardrobeRecipes2 = new Dictionary<string, UMATextRecipe>();
            foreach (KeyValuePair<string,UMATextRecipe> kp in WardrobeRecipes2)
            {
                if(dynamicCharacterSystem.GetRecipe(kp.Value.name,false) != null)
                {
                    newWardrobeRecipes2.Add(kp.Key, dynamicCharacterSystem.GetRecipe(kp.Value.name, false));
                }
                else
                {
                    newWardrobeRecipes2.Add(kp.Key, kp.Value);
                }
            }
            WardrobeRecipes2 = newWardrobeRecipes2;
        }
        /// <summary>
        /// Sets the avatars wardrobe slot to use the given wardrobe recipe (not to be mistaken with an UMA SlotDataAsset)
        /// </summary>
        /// <param name="utr"></param>
        public void SetSlot(UMATextRecipe utr)
        {
            if(utr.wardrobeSlot != "" && utr.wardrobeSlot != "None")
            {
                if (WardrobeRecipes2.ContainsKey(utr.wardrobeSlot))
                {
                    WardrobeRecipes2[utr.wardrobeSlot] = utr;
                }
                else
                {
                    WardrobeRecipes2.Add(utr.wardrobeSlot, utr);
                }
            }
        }

        public void ChangeRace(string racename)
        {
            ChangeRace((context.raceLibrary as DynamicRaceLibrary).GetRace(racename));
        }
        public void ChangeRace(RaceData race)
        {
            StartCoroutine(ChangeRaceCoroutine(race));
        }
        public IEnumerator ChangeRaceCoroutine(RaceData race)
        {
            yield return null;
            if (Application.isPlaying)
            {
                //TODO should this incude DNA- probably... Though it would be nice if DNA could persist across race changes
                if (!cacheStates.ContainsKey(activeRace.name))
                {
                    var tempCols = new List<ColorValue>();
                    foreach (ColorValue col in characterColors.Colors)
                    {
                        var tempCol = new ColorValue();
                        tempCol.Name = col.Name;
                        tempCol.Color = col.Color;
                        tempCol.MetallicGloss = col.MetallicGloss;
                        tempCols.Add(tempCol);
                    }
                    var thisCacheStateInfo = new CacheStateInfo(new Dictionary<string, UMATextRecipe>(WardrobeRecipes2), tempCols);
                    cacheStates.Add(activeRace.name, thisCacheStateInfo);
                }
                else
                {
                    cacheStates[activeRace.name].wardrobeCache = new Dictionary<string, UMATextRecipe>(WardrobeRecipes2);
                    var tempCols = new List<ColorValue>();
                    foreach (ColorValue col in characterColors.Colors)
                    {
                        var tempCol = new ColorValue();
                        tempCol.Name = col.Name;
                        tempCol.Color = col.Color;
                        tempCol.MetallicGloss = col.MetallicGloss;
                        tempCols.Add(tempCol);
                    }
                    cacheStates[activeRace.name].colorCache = tempCols;
                }
                if (cacheStates.ContainsKey(race.raceName))
                {
                    activeRace.name = race.raceName;
                    activeRace.data = race;
                    umaRecipe = race.baseRaceRecipe;
                    WardrobeRecipes2 = cacheStates[race.raceName].wardrobeCache;
                    characterColors.Colors.Clear(); characterColors.Colors = cacheStates[race.raceName].colorCache;
                    BuildCharacter(false);
                    yield break;
                }
            }
            if (Application.isPlaying)
            {
                DynamicAssetLoader.Instance.GenerateBatchID();
                DynamicAssetLoader.Instance.requestingUMA = this;
            }
            activeRace.name = race.raceName;
            activeRace.data = race;
            if (Application.isPlaying)
            {
                umaRecipe = activeRace.racedata.baseRaceRecipe;
                ClearSlots();
                //The following may cause more bundles to download- and if the race/raceBaseRecipe WAS there 
                //(i.e. was not a PlaceholderRace) then the slots and overlays in the temp items that get sent back
                //from DynamicAssetLoader may not match the slots in the base Recipe. 
                //I'm not sure there is anything that can be done in this scenario other than wait?
                //or maybe there is some way of setting the placeholderslots/overlays to have the same material?
                LoadDefaultWardrobe(true);
                DynamicAssetLoader.Instance.requestingUMA = null;
                dynamicCharacterSystem.Refresh();
                //So right now there is not any way of making this work for sure without waiting...
                if (/*waitForBundles &&*/DynamicAssetLoader.Instance.assetBundlesDownloading)
                {
                    while (DynamicAssetLoader.Instance.assetBundlesDownloading)
                    {
                        yield return null;
                    }
                    yield break;
                }
                else
                {
                    BuildCharacter(false);
                    yield break;
                }
            }
            yield break;
        }

        #region LoadSaveFunctions

        /// <summary>
        /// Returns a Standard UMATextRecipe string that can be used with NON-CharacterAvatar UMAs. For saving a recipe that will also save the Avatars current wardrobe slots (i.e. for use with another CharacterAvatar) use DoSave instead.
        /// </summary>
        /// <returns></returns>
        public string GetCurrentRecipe()
        {
            // save 
            UMATextRecipe u = new UMATextRecipe();
            u.Save(umaData.umaRecipe, context);
            return u.recipeString;
        }

        /// <summary>
        /// Loads the Avatar from the given recipe and additional recipe. 
        /// Has additional functions for removing any slots that should be hidden by any 'wardrobe Recipes' that are in the additional recipes array.
        /// </summary>
        /// <param name="umaRecipe"></param>
        /// <param name="umaAdditionalRecipes"></param>
        public override void Load(UMARecipeBase umaRecipe, params UMARecipeBase[] umaAdditionalSerializedRecipes)
        {
            if (umaRecipe == null)
            {
                return;
            }
            if (umaData == null)
            {
                Initialize();
            }
            Profiler.BeginSample("Load");

            this.umaRecipe = umaRecipe;

            umaRecipe.Load(umaData.umaRecipe, context);

            if (flagForReload)
            {
                activeRace.data = umaData.umaRecipe.raceData;
                activeRace.name = activeRace.racedata.raceName;
                SetAnimatorController();
                umaAdditionalSerializedRecipes = BuildCharacter();
            }

            umaData.AddAdditionalRecipes(umaAdditionalRecipes, context);
            AddAdditionalSerializedRecipes(umaAdditionalSerializedRecipes);

            RemoveHiddenSlots();
            UpdateColors();

            if (umaRace != umaData.umaRecipe.raceData)
            {
                UpdateNewRace();
            }
            else
            {
                UpdateSameRace();
            }
            Profiler.EndSample();

            StartCoroutine(UpdateAssetBundlesUsedbyCharacter());
        }

        public void AddAdditionalSerializedRecipes(UMARecipeBase[] umaAdditionalSerializedRecipes)
        {
            if (umaAdditionalSerializedRecipes != null)
            {
                foreach (var umaAdditionalRecipe in umaAdditionalSerializedRecipes)
                {
                    UMAData.UMARecipe cachedRecipe = umaAdditionalRecipe.GetCachedRecipe(context);
                    umaData.umaRecipe.Merge(cachedRecipe,false);
                }
            }
        }
        /// <summary>
        /// Loads an avatar from a recipe string, optionally waiting for any assets that will need to be downloaded (according to the CharacterAvatar 'waitForBundles' setting
        /// </summary>
        /// <param name="recipeString"></param>
        /// <returns></returns>
        public IEnumerator LoadFromRecipeString(string recipeString)
        {
            //TODO For some reason, sometimes we get an error saying 'UMA data missing required generator!' It seems intermittent and random- Work out what is causing it
            //For now specify the generator...
            umaGenerator = UMAGenerator.FindInstance();
            var umaTextRecipe = ScriptableObject.CreateInstance<UMATextRecipe>();
            umaTextRecipe.name = loadFilename;
            umaTextRecipe.recipeString = recipeString;
            UMADataCharacterSystem.UMACharacterSystemRecipe tempRecipe = new UMADataCharacterSystem.UMACharacterSystemRecipe();
            //Before we actually call Load we need to know the race so that any wardrobe recipes can be assigned to it in CharacterSystem.
            Regex raceRegex = new Regex("\"([^\"]*)\"", RegexOptions.Multiline);
            var raceMatches = raceRegex.Match(recipeString.Replace(Environment.NewLine, ""), (recipeString.IndexOf("\"race\":") + 6));
            string raceString = raceMatches.Groups[1].ToString().Replace("\"", "");
            //Geneate a new BatchID so that any items added by the folowing actions that trigger a download will all be part of the same batch and get processed in the same cycle once downloaded
            DynamicAssetLoader.Instance.GenerateBatchID();
            //make any items added by the folowing actions that trigger a download refrence this UMA so it can be updated when the batch of downloads completes
            DynamicAssetLoader.Instance.requestingUMA = this;
            if (raceString != "")
            {
                context.GetRace(raceString);//update the race library with this race- triggers the race to download and sets it to placeholder race if its in an assetbundle
                dynamicCharacterSystem.Refresh();//Refresh Character System so it has a key in the dictionary for this race
            }
            //If the user doesn't have the content and it cannot be downloaded then we get an error. So try-catch...
            try
            {
                //Unpacking the recipe will kick off the downloading of any assets we dont already have if they are available in any assetbundles, 
                umaTextRecipe.LoadCharacterSystem((UMA.UMADataCharacterSystem.UMACharacterSystemRecipe)tempRecipe, context);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CharacterAvatar.LoadFromRecipeString] Exception: "+e);
                yield break;
            }
            DynamicAssetLoader.Instance.requestingUMA = null;
            activeRace.name = tempRecipe.raceData.raceName;
            activeRace.data = tempRecipe.raceData;
            if (waitForBundles)
            {
                while (DynamicAssetLoader.Instance.assetBundlesDownloading)
                {
                    yield return null;
                }
            }
            SetStartingRace();
            ClearSlots();
            if (tempRecipe.wardrobeRecipes != null)
            {//means we have a characterSystemTextRecipe
                if (tempRecipe.wardrobeRecipes.Count > 0)
                {
                    DynamicAssetLoader.Instance.requestingUMA = this;
                    foreach (KeyValuePair<string, string> kp in tempRecipe.wardrobeRecipes)
                    {
                        //by using GetRecipe CharacterSystem will retrieve it from an asset bundle if it needs too, 
                        //this shouldn't trigger any downloading because that will have already happened when we unpacked above.
                        if (dynamicCharacterSystem.GetRecipe(kp.Value)!= null)
                        {
                            UMATextRecipe utr = dynamicCharacterSystem.GetRecipe(kp.Value);
                            SetSlot(utr);
                        }
                    }
                    DynamicAssetLoader.Instance.requestingUMA = null;
                }
                umaData.umaRecipe.sharedColors = tempRecipe.sharedColors;
                characterColors.Colors = new List<ColorValue>();
                foreach (OverlayColorData col in umaData.umaRecipe.sharedColors)
                {
                    characterColors.Colors.Add(new ColorValue(col.name, col));
                }
                BuildCharacter(false);
                umaData.umaRecipe.ClearDna();
                foreach (UMADnaBase dna in tempRecipe.GetAllDna())
                {
                    umaData.umaRecipe.AddDna(dna);
                }
            }
            else
            {
                //if its a standard UmaTextRecipe load it directly into UMAData since there wont be any wardrobe slots...
                umaData.umaRecipe.sharedColors = tempRecipe.sharedColors;
                umaTextRecipe.Load(umaData.umaRecipe, context);
                umaData.umaRecipe.sharedColors = tempRecipe.sharedColors;
                characterColors.Colors = new List<ColorValue>();
                foreach (OverlayColorData col in umaData.umaRecipe.sharedColors)
                {
                    characterColors.Colors.Add(new ColorValue(col.name, col.color));
                }
                SetAnimatorController();
                SetExpressionSet();
                UpdateColors();
                if (umaRace != umaData.umaRecipe.raceData)
                {
                    UpdateNewRace();
                }
                else
                {
                    UpdateSameRace();
                }
                StartCoroutine(UpdateAssetBundlesUsedbyCharacter());
            }
            tempRecipe = null;
            Destroy(umaTextRecipe);
            yield break;//never sure if we need to do this?
        }
        /// <summary>
        /// Checks what assetBundles (if any) were used in the creation of this Avatar. NOTE: Query this UMA's AssetBundlesUsedbyCharacterUpToDate field before calling this function
        /// </summary>
        /// <param name="verbose">set this to true to get more information to track down when asset bundles are getting dependencies when they shouldn't because they are refrencing things from asset bundles you did not intend them to</param>
        /// <returns></returns>
        //DOS NOTES: The fact this is an Enumerator may cause issues for any script wanting to get upToDate data- on the otherhand we dont want to slow down rendering by making it wait for this list
        IEnumerator UpdateAssetBundlesUsedbyCharacter(bool verbose = false)
        {
            AssetBundlesUsedbyCharacterUpToDate = false;
            yield return null;
            assetBundlesUsedbyCharacter.Clear();
            if (umaData != null)
            {
                var raceLibraryDict = ((DynamicRaceLibrary)context.raceLibrary as DynamicRaceLibrary).assetBundlesUsedDict;
                var slotLibraryDict = ((DynamicSlotLibrary)context.slotLibrary as DynamicSlotLibrary).assetBundlesUsedDict;
                var overlayLibraryDict = ((DynamicOverlayLibrary)context.overlayLibrary as DynamicOverlayLibrary).assetBundlesUsedDict;
                var characterSystemDict = dynamicCharacterSystem.assetBundlesUsedDict;
                var raceAnimatorsDict = raceAnimationControllers.assetBundlesUsedDict;
                if (raceLibraryDict.Count > 0)
                {
                    foreach (KeyValuePair<string, List<string>> kp in raceLibraryDict)
                    {
                        if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
                            if (kp.Value.Contains(activeRace.name))
                            {
                                assetBundlesUsedbyCharacter.Add(kp.Key);
                            }
                    }
                }
                var activeSlots = umaData.umaRecipe.GetAllSlots();
                if (slotLibraryDict.Count > 0)
                {
                    foreach (SlotData slot in activeSlots)
                    {
                        if (slot != null)
                        {
                            foreach (KeyValuePair<string, List<string>> kp in slotLibraryDict)
                            {
                                if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
                                    if (kp.Value.Contains(slot.asset.name))
                                    {
                                        if(verbose)
                                            assetBundlesUsedbyCharacter.Add(kp.Key +" (Slot:"+ slot.asset.name+")");
                                        else
                                            assetBundlesUsedbyCharacter.Add(kp.Key);
                                    }
                            }
                        }
                    }
                }
                if (overlayLibraryDict.Count > 0)
                {
                    foreach (SlotData slot in activeSlots)
                    {
                        if (slot != null)
                        {
                            var overLaysinSlot = slot.GetOverlayList();
                            foreach (OverlayData overlay in overLaysinSlot)
                            {
                                foreach (KeyValuePair<string, List<string>> kp in overlayLibraryDict)
                                {
                                    if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
                                        if (kp.Value.Contains(overlay.asset.name))
                                        {
                                            if (verbose)
                                                assetBundlesUsedbyCharacter.Add(kp.Key + " (Overlay:" + overlay.asset.name + ")");
                                            else
                                                assetBundlesUsedbyCharacter.Add(kp.Key);
                                        }
                                }
                            }
                        }
                    }
                }
                if (characterSystemDict.Count > 0)
                {
                    foreach (KeyValuePair<string, UMATextRecipe> recipe in WardrobeRecipes2)
                    {
                        foreach (KeyValuePair<string, List<string>> kp in characterSystemDict)
                        {
                            if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
                                if (kp.Value.Contains(recipe.Key))
                                {
                                    assetBundlesUsedbyCharacter.Add(kp.Key);
                                }
                        }
                    }
                }
                string specificRaceAnimator = "";
                foreach (RaceAnimator raceAnimator in raceAnimationControllers.animators)
                {
                    if (raceAnimator.raceName == activeRace.name && raceAnimator.animatorController != null)
                    {
                        specificRaceAnimator = raceAnimator.animatorControllerName;
                        break;
                    }
                }
                if (raceAnimatorsDict.Count > 0 && specificRaceAnimator != "")
                {
                    foreach (KeyValuePair<string, List<string>> kp in raceAnimatorsDict)
                    {
                        if (!assetBundlesUsedbyCharacter.Contains(kp.Key))
                            if (kp.Value.Contains(specificRaceAnimator))
                            {
                                assetBundlesUsedbyCharacter.Add(kp.Key);
                            }
                    }
                }
            }
            AssetBundlesUsedbyCharacterUpToDate = true;
            yield break;
        }
        /// <summary>
        /// Returns the list of AssetBundles used by the avatar. IMPORTANT you must wait for AssetBundlesUsedbyCharacterUpToDate to be true before calling this method.
        /// </summary>
        /// <returns></returns>
        public List<string> GetAssetBundlesUsedByCharacter()
        {
            return assetBundlesUsedbyCharacter;
        }

        /// <summary>
        /// Loads the text file in the loadFilename field to get its recipe string, and then calls LoadFromRecipeString to to process the recipe and load the Avatar.
        /// </summary>
        public void DoLoad()
        {
            StartCoroutine(DoLoadCoroutine());
        }
        IEnumerator DoLoadCoroutine()
        {
            yield return null;
            string path = "";
            string recipeString = "";
#if UNITY_EDITOR
            if (loadFilename == "" && Application.isEditor)
            {
                loadPathType = loadPathTypes.FileSystem;
            }
#endif
            if (loadPathType == loadPathTypes.CharacterSystem)
            {
                if (dynamicCharacterSystem.XMLFiles.ContainsKey(loadFilename.Trim()))
                {
                    dynamicCharacterSystem.XMLFiles.TryGetValue(loadFilename.Trim(), out recipeString);
                }
            }
            if (loadPathType == loadPathTypes.FileSystem)
            {
#if UNITY_EDITOR
                if (Application.isEditor)
                {
                    path = EditorUtility.OpenFilePanel("Load saved Avatar", Application.dataPath, "txt");
                    if (string.IsNullOrEmpty(path)) yield break;
                    recipeString = FileUtils.ReadAllText(path);
                    path = "";
                }
                else
#endif
                    loadPathType = loadPathTypes.persistentDataPath;
            }
            if (loadPathType == loadPathTypes.persistentDataPath)
            {
                path = Application.persistentDataPath;
            }
            else if (loadPathType == loadPathTypes.streamingAssetsPath)
            {
                path = Application.streamingAssetsPath;
            }
            if (path != "" || loadPathType == loadPathTypes.Resources)
            {
                if (loadPathType == loadPathTypes.Resources)
                {
                    TextAsset[] textFiles = Resources.LoadAll<TextAsset>(loadPath);
                    for (int i = 0; i < textFiles.Length; i++)
                    {
                        if (textFiles[i].name == loadFilename.Trim() || textFiles[i].name.ToLower() == loadFilename.Trim())
                        {
                            recipeString = textFiles[i].text;
                        }
                    }
                }
                else
                {
                    path = (loadPath != "") ? System.IO.Path.Combine(path, loadPath.TrimStart('\\', '/').TrimEnd('\\', '/').Trim()) : path;
                    if (loadFilename == "")
                    {
                        Debug.LogWarning("[CharacterAvatar.DoLoad] No filename specified to load!");
                        yield break;
                    }
                    else
                    {
                        if (path.Contains("://"))
                        {
                            WWW www = new WWW(path + loadFilename);
                            yield return www;
                            recipeString = www.text;
                        }
                        else
                        {
                            recipeString = FileUtils.ReadAllText(System.IO.Path.Combine(path, loadFilename));
                        }
                    }
                }
            }
            if (recipeString != "")
            {
                StartCoroutine(LoadFromRecipeString(recipeString));
                yield break;
            }
            else
            {
                Debug.LogWarning("[CharacterAvatar.DoLoad] No TextRecipe found with filename " + loadFilename);
            }
            yield break;
        }

        /// <summary>
        /// Saves the current avatar and its wardrobe slots and colors to a UMATextRecipe (text) compatible recipe. Use this instead of GetCurrentRecipe if you want to create a file that includes wardrobe recipes for use with another CharacterAvatar
        /// </summary>
        public void DoSave()
        {
            StartCoroutine(DoSaveCoroutine());
        }
        IEnumerator DoSaveCoroutine()
        {
            yield return null;
            string path = "";
            string filePath = "";
#if UNITY_EDITOR
            if (saveFilename == "" && Application.isEditor)
            {
                savePathType = savePathTypes.FileSystem;
            }
#endif
            if (savePathType == savePathTypes.FileSystem)
            {
#if UNITY_EDITOR
                if (Application.isEditor)
                {
                    path = EditorUtility.SaveFilePanel("Save Avatar", Application.dataPath, (saveFilename != "" ? saveFilename : ""), "txt");

                }
                else
#endif
               savePathType = savePathTypes.persistentDataPath;

            }
            //I dont think we can save anywhere but persistentDataPath on most platforms
            if ((savePathType == savePathTypes.Resources || savePathType == savePathTypes.streamingAssetsPath))
            {
#if UNITY_EDITOR
                if(!Application.isEditor)
#endif
                    savePathType = savePathTypes.persistentDataPath;
            }
            if (savePathType == savePathTypes.Resources)
            {
                path = System.IO.Path.Combine(Application.dataPath, "Resources");//This needs to be exactly the right folder to work in the editor
            }
            else if (savePathType == savePathTypes.streamingAssetsPath)
            {
                path = Application.streamingAssetsPath;
            }
            else if (savePathType == savePathTypes.persistentDataPath)
            {
                path = Application.persistentDataPath;
            }
            if (path != "")
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                if (makeUnique || saveFilename == "")
                {
                    saveFilename = saveFilename + Guid.NewGuid().ToString();
                }
                if (savePathType != savePathTypes.FileSystem)
                {
                    path = (savePath != "") ? System.IO.Path.Combine(path, savePath.TrimStart('\\', '/').TrimEnd('\\', '/').Trim()) : path;
                    filePath = System.IO.Path.Combine(path, saveFilename + ".txt");
                    FileUtils.EnsurePath(path);
                }
                else
                {
                    filePath = path;
                }
                var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
                asset.SaveCharacterSystem(umaData.umaRecipe, context, WardrobeRecipes2);
                FileUtils.WriteAllText(filePath, asset.recipeString);
                if (savePathType == savePathTypes.Resources || savePathType == savePathTypes.streamingAssetsPath)
                {
#if UNITY_EDITOR
					AssetDatabase.Refresh();
#endif
                }
                ScriptableObject.Destroy(asset);
            }
            else
            {
                Debug.LogError("CharacterSystem Save Error! Could not save file, check you have set the filename and path correctly...");
                yield break;
            }
            yield break;
        }

        #endregion
        public void AvatarCreated()
        {
            SkinnedMeshRenderer smr = this.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.localBounds = new Bounds(smr.localBounds.center + BoundsOffset, smr.localBounds.size);
        }

        #region Possibly Obsolete functions

        public UMADnaBase[] GetAllDNA()
        {
            return umaData.GetAllDna();
        }

        public UMADnaHumanoid GetDNAValues()
        {
            UMADnaHumanoid humanDNA = umaData.GetDna<UMADnaHumanoid>();
            return humanDNA;
        }

        public string GetDNA()
        {
            UMADnaHumanoid humanDNA = umaData.GetDna<UMADnaHumanoid>();
            string[] array2 = Array.ConvertAll(humanDNA.Values, element => element.ToString());
            return string.Join(",", array2);
        }

        public void SetDNA(string DNA)
        {
            string[] strvals = DNA.Split(',');
            float[] values = new float[strvals.Length];
            for (int i = 0; i < strvals.Length; i++)
            {
                if (String.IsNullOrEmpty(strvals[i]))
                    values[i] = 0.0f;
                else
                    values[i] = Convert.ToSingle(strvals[i]);
            }
            UMADnaHumanoid humanDNA = umaData.GetDna<UMADnaHumanoid>();
            humanDNA.Values = values;
            umaData.ApplyDNA();
        }

        #endregion

        #region special classes

        [Serializable]
        public class RaceSetter
        {
            public string name;
            [SerializeField]
            RaceData _data;//This was not properly reporting itself as 'missing' when it is set to an asset that is in an asset bundle, so now data is a property that validates itself
            [SerializeField]//Needs to be serialized for the inspector but otherwise no- TODO what happens in a build? will this get saved across sessions- because we dont want that
            RaceData[] _cachedRaceDatas;

            /// <summary>
            /// Will return the current active racedata- if it is in an asset bundle or in resources it will find/download it. If you ony need to know if the data is there use the racedata field instead.
            /// </summary>
            //DOS NOTES this is because we have decided to make the libraries get stuff they dont have and return a temporary asset- but there are still occasions where we dont want this to happen
            // or at least there are occasions where we JUST WANT THE LIST and dont want it do do anything else...
            public RaceData data
            {
                get {
                    if (Application.isPlaying)
                        return Validate();
                      else
                        return _data; }
                set { _data = value;
                    if (Application.isPlaying)
                        Validate();
                }
            }
            /// <summary>
            /// returns the active raceData (quick)
            /// </summary>
            public RaceData racedata
            {
                get { return _data; }
            }

            RaceData Validate()
            {
                RaceData racedata = null;
                var thisContext = UMAContext.FindInstance();
                var thisDynamicRaceLibrary = (DynamicRaceLibrary)thisContext.raceLibrary as DynamicRaceLibrary;
                _cachedRaceDatas = thisDynamicRaceLibrary.GetAllRaces();
                foreach (RaceData race in _cachedRaceDatas)
                {
                    if (race.raceName == this.name)
                        racedata = race;
                }
                return racedata;
            }
        }

        [Serializable]
        public class WardrobeRecipeListItem
        {
            public string _recipeName;
            public UMATextRecipe _recipe;
            //store compatible races here because when a recipe is not downloaded we dont have access to this info...
            public List<string> _compatibleRaces;

            public WardrobeRecipeListItem()
            {

            }
            public WardrobeRecipeListItem(string recipeName)//TODO: Does this constructor ever get used? We dont want it to...
            {
                _recipeName = recipeName;
            }
            public WardrobeRecipeListItem(UMATextRecipe recipe)
            {
                _recipeName = recipe.name;
                _recipe = recipe;
                _compatibleRaces = recipe.compatibleRaces;
            }
        }

        [Serializable]
        public class WardrobeRecipeList
        {
            [Tooltip("If this is checked and the Avatar is NOT creating itself from a previously saved recipe, recipes in here will be added to the Avatar when it loads")]
            public bool loadDefaultRecipes = true;
            public List<WardrobeRecipeListItem> recipes = new List<WardrobeRecipeListItem>();
            public int Validate(DynamicCharacterSystem characterSystem, bool allowDownloadables = false, string raceName = "")
            {
                int validRecipes = 0;
                foreach (WardrobeRecipeListItem recipe in recipes)
                {
                    if (allowDownloadables && (raceName == "" || recipe._compatibleRaces.Contains(raceName)))
                    {
                    if (characterSystem.GetRecipe(recipe._recipeName, true) != null)
                        {
                            recipe._recipe = characterSystem.GetRecipe(recipe._recipeName);
                            validRecipes++;
                        }

                    }
                    else
                    {
                        if (characterSystem.RecipeIndex.ContainsKey(recipe._recipeName))
                        {
                            bool recipeFound = false;
                            recipeFound = characterSystem.RecipeIndex.TryGetValue(recipe._recipeName, out recipe._recipe);
                            if (recipeFound)
                            {
                                recipe._compatibleRaces = recipe._recipe.compatibleRaces;
                                validRecipes++;
                            }
                                    
                        }
                    }
                }
                return validRecipes;
            }
        }
        [Serializable]
        public class RaceAnimator
        {
            public string raceName;
            public string animatorControllerName;
            public RuntimeAnimatorController animatorController;
        }

        [Serializable]
        public class RaceAnimatorList
        {
            public RuntimeAnimatorController defaultAnimationController;
            public List<RaceAnimator> animators = new List<RaceAnimator>();
            public bool dynamicallyAddFromResources;
            public string resourcesFolderPath;
            public bool dynamicallyAddFromAssetBundles;
            public string assetBundleNames;
            public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
            public int Validate()
            {
                UpdateAnimators();
                int validAnimators = 0;
                foreach (RaceAnimator animator in animators)
                {
                    if (animator.animatorController != null)
                    {
                        validAnimators++;
                    }
                }
                return validAnimators;
            }
            public void UpdateAnimators()
            {
                foreach (RaceAnimator animator in animators)
                {
                    if (animator.animatorController == null)
                    {
                        if (animator.animatorControllerName != "")
                        {
                            FindAnimatorByName(animator.animatorControllerName);
                        }
                    }
                }
            }
            public void FindAnimatorByName(string animatorName)
            {
                bool found = false;
                if (dynamicallyAddFromResources)
                {
                    found = DynamicAssetLoader.Instance.AddAssetsFromResources<RuntimeAnimatorController>("", null, animatorName, SetFoundAnimators);
                }
                if(found == false && dynamicallyAddFromAssetBundles)
                {
                    DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<RuntimeAnimatorController>(ref assetBundlesUsedDict, false, "", null, animatorName, SetFoundAnimators);
                }
            }
            //This function is probablt redundant since animators from this class should never cause assetbundles to download
            //and therefore there should never be any 'temp' assets that need to be replaced
            public void SetAnimator(RuntimeAnimatorController controller)
            {
                foreach (RaceAnimator animator in animators)
                {
                    if (animator.animatorControllerName != "")
                    {
                        if (animator.animatorControllerName == controller.name)
                        {
                            animator.animatorController = controller;
                        }
                    }
                }
            }
            private void SetFoundAnimators(RuntimeAnimatorController[] foundControllers)
            {
                foreach (RuntimeAnimatorController foundController in foundControllers)
                {
                    foreach (RaceAnimator animator in animators)
                    {
                        if (animator.animatorController == null)
                        {
                            if (animator.animatorControllerName != "")
                            {
                                if (animator.animatorControllerName == foundController.name)
                                {
                                    animator.animatorController = foundController;
                                }
                            }
                        }
                    }
                }
                CleanAnimatorsFromResourcesAndBundles();
            }
            public void CleanAnimatorsFromResourcesAndBundles()
            {
                Resources.UnloadUnusedAssets();
            }
        }

        [Serializable]
        public class ColorValue
        {
            public string Name;
            public Color Color = Color.white;
            public Color MetallicGloss = new Color(0, 0, 0, 0);
            public ColorValue() { }

            public ColorValue(string name, Color color)
            {
                Name = name;
                Color = color;
            }
            public ColorValue(string name, OverlayColorData color)
            {
                Name = name;
                Color = color.color;
                if (color.channelAdditiveMask.Length == 3)
                {
                    MetallicGloss = color.channelAdditiveMask[2];
                }
            }
        }

        //I need colors to be able to have the channelAdditiveMask[2] aswell because if they do you can set Metallic/Glossy colors
        [Serializable]
        public class ColorValueList
        {

            public List<ColorValue> Colors = new List<ColorValue>();

            public ColorValueList()
            {
                //
            }
            public ColorValueList(List<ColorValue> colorValueList)
            {
                Colors = colorValueList;
            }

            private ColorValue GetColorValue(string name)
            {
                foreach (ColorValue cv in Colors)
                {
                    if (cv.Name == name)
                        return cv;
                }
                return null;
            }

            public bool GetColor(string Name, out Color c)
            {
                ColorValue cv = GetColorValue(Name);
                if (cv != null)
                {
                    c = cv.Color;
                    return true;
                }
                c = Color.white;
                return false;
            }

            public bool GetColor(string Name, out OverlayColorData c)
            {
                ColorValue cv = GetColorValue(Name);
                if (cv != null)
                {
                    c = new OverlayColorData(3);
                    c.name = cv.Name;
                    c.color = cv.Color;
                    c.channelAdditiveMask[2] = cv.MetallicGloss;
                    return true;
                }
                c = new OverlayColorData(3);
                return false;
            }

            public void SetColor(string name, Color c)
            {
                ColorValue cv = GetColorValue(name);
                if (cv != null)
                {
                    cv.Color = c;
                }
                else
                {
                    Colors.Add(new ColorValue(name, c));
                }
            }

            public void SetColor(string name, OverlayColorData c)
            {
                ColorValue cv = GetColorValue(name);
                if (cv != null)
                {
                    cv.Color = c.color;
                    if (c.channelAdditiveMask.Length == 3)
                    {
                        cv.MetallicGloss = c.channelAdditiveMask[2];
                    }
                }
                else
                {
                    Colors.Add(new ColorValue(name, c));
                }
            }

            public void RemoveColor(string name)
            {
                foreach (ColorValue cv in Colors)
                {
                    if (cv.Name == name)
                        Colors.Remove(cv);
                }
            }
        }

        #endregion
    }
}
