using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;
using System.Collections.Generic;
using System;
/*using System.IO;*/
using UMA;
using UMAAssetBundleManager;

namespace UMACharacterSystem
{
    public class DynamicCharacterSystem : MonoBehaviour
    {
        public Dictionary<UMACharacterSystem.Sex, Dictionary<UMACharacterSystem.WardrobeSlot, List<UMATextRecipe>>> Recipes = new Dictionary<Sex, Dictionary<WardrobeSlot, List<UMATextRecipe>>>();
        public Dictionary<string, UMATextRecipe> RecipeIndex = new Dictionary<string, UMATextRecipe>();
        public Dictionary<string, string> XMLFiles = new Dictionary<string, string>();

        public bool makeSingleton;

        public bool initializeOnAwake = true;

        [HideInInspector]
        [System.NonSerialized]
        public bool initialized = false;

        //It could be a good idea to make this work like UMAContext- i.e. like a 'half' singleton- in that its not a 'dont destroyonload' singleton but you can accessit from anywhere using CharacterSystemm.Instance
        public static DynamicCharacterSystem Instance;

        //extra fields for Dynamic Version
        //TODO Definately make it possible to gather json recipes from PersistentAssets
        public bool dynamicallyAddFromResources;
        public string resourcesCharactersFolder = "CharacterRecipes";
        public string resourcesRecipesFolder = "Recipes";
        public bool dynamicallyAddFromAssetBundles;
        public string assetBundlesForCharactersToSearch;
        public string assetBundlesForRecipesToSearch;
        [NonSerialized]
        public Dictionary<string, Dictionary<string, List<UMATextRecipe>>> Recipes2 = new Dictionary<string, Dictionary<string, List<UMATextRecipe>>>();
        bool refresh = false;
        [HideInInspector]
        public UMAContext context;
        //This is a ditionary of asset bundles that were loaded into the library. 
        //CharacterAvatar can query this this to find out what asset bundles were required to create itself
        //these can be saved in the recipe so that when the character is recreated it makes sure these bundles
        //have been downloaded before it creates itself again.
        //TODO REPLACE THIS and its GetOriginatingAssetBundle function with something that uses AssetDatabase in the editor and AssetBundleManager.AssetIndexObject otherwise
        public Dictionary<string, List<string>> assetBundlesUsedDict = new Dictionary<string, List<string>>();
        [System.NonSerialized]
        [HideInInspector]
        public bool downloadAssetsEnabled = true;

        bool AllResourcesScannedXML = false;
        bool AllResourcesScannedRecipes = false;


        public virtual void Awake()
        {
            //TODO make this work and/or get CharacterSystem into UMAContext
            if (Instance == null && makeSingleton)
            {
                DontDestroyOnLoad(gameObject);
                Instance = this;
                if (!initialized)
                {
                    Init();
                }
            }
            else if (Instance != this && makeSingleton)
            {
                Destroy(gameObject);
            }
            else
            {
                if (initializeOnAwake)
                {
                    if (!initialized)
                    {
                        Init();
                    }
                }
            }
        }

        public virtual void OnEnable()
        {
            if (!initialized || refresh)
            {
                if (refresh)
                {
                    Debug.Log("CharacterSystem called Refresh onEnable");
                    Refresh();
                }
                else
                {
                    Init();
                }
            }
        }

        // Use this for initialization
        public virtual void Start()
        {
            AllResourcesScannedXML = false;
            AllResourcesScannedRecipes = false;
            if (!initialized)
            {
                Init();
            }

        }

        // Update is called once per frame
        public virtual void Update()
        {
            if (refresh)
            {
                Refresh();
            }
        }

        public virtual void Init()
        {
            if (context == null)
            {
                context = UMAContext.FindInstance();
            }

            if (initialized)
            {
                return;
            }
            // Create a dictionary for each sex.-not needed any more
            foreach (UMACharacterSystem.Sex sx in Enum.GetValues(typeof(UMACharacterSystem.Sex)))
            {
                Recipes.Add(sx, new Dictionary<WardrobeSlot, List<UMATextRecipe>>());
            }
            //UMA is planning to make GetAllRaces obsolite so this wont work in future...
            var possibleRaces = (context.raceLibrary as DynamicRaceLibrary).GetAllRaces();
            for (int i = 0; i < possibleRaces.Length; i++)
            {
                //we need to check that this is not null- the user may not have downloaded it yet
                if (possibleRaces[i] != null && possibleRaces[i].raceName != DynamicAssetLoader.Instance.placeholderRace.raceName)
                {
                    Recipes2.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
                }
            }

            GatherXMLFiles();
            GatherRecipeFiles();
            initialized = true;
        }

        //Refresh just adds to what is there rather than clearing it all
        //used after asset bundles have been loaded to add any new recipes to the dictionaries
        public virtual void Refresh()
        {
            refresh = false;
            var possibleRaces = context.raceLibrary.GetAllRaces();//UMA is planning to make GetAllRaces obsolite so this wont work in future...
            for (int i = 0; i < possibleRaces.Length; i++)
            {
                //we need to check that this is not null- the user may not have downloaded it yet
                if (possibleRaces[i] != null)
                {
                    if (!Recipes2.ContainsKey(possibleRaces[i].raceName) &&  possibleRaces[i].raceName != DynamicAssetLoader.Instance.placeholderRace.raceName)
                    {
                        Recipes2.Add(possibleRaces[i].raceName, new Dictionary<string, List<UMATextRecipe>>());
                    }
                }
            }
            GatherXMLFiles();
            GatherRecipeFiles();
        }


        IEnumerator XMLWaitForAssetBundleManager(string filename)
        {
            while (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
            {
                yield return null;
            }
            GatherXMLFiles(filename);
        }

        private void GatherXMLFiles(string filename = "")
        {
            bool found = false;
            if (dynamicallyAddFromResources && AllResourcesScannedXML == false)
            {
                if (filename == "")
                    AllResourcesScannedXML = true;
                found = DynamicAssetLoader.Instance.AddAssetsFromResources<TextAsset>(resourcesCharactersFolder, null, filename, AddXMLFiles);
            }
            if (dynamicallyAddFromAssetBundles && (filename == "" || found == false))
            {
                if (((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
                {
                    StopCoroutine(XMLWaitForAssetBundleManager(filename));
                    StartCoroutine(XMLWaitForAssetBundleManager(filename));
                    return;
                }
                DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<TextAsset>(ref assetBundlesUsedDict, downloadAssetsEnabled, assetBundlesForCharactersToSearch, null, filename, AddXMLFiles);
            }
        }

        private void AddXMLFiles(TextAsset[] xmlFiles)
        {
            foreach(TextAsset xmlFile in xmlFiles)
            {
                if (!XMLFiles.ContainsKey(xmlFile.name))
                    XMLFiles.Add(xmlFile.name, xmlFile.text);
            }
            StartCoroutine(CleanFilesFromResourcesAndBundles());
        }

        IEnumerator RecipesWaitForAssetBundleManager(string filename)
        {
            while (AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null)
            {
                yield return null;
            }
            GatherRecipeFiles(filename);
        }

        private void GatherRecipeFiles(string filename = "")
        {
            bool found = false;
            if (dynamicallyAddFromResources && AllResourcesScannedRecipes == false)
            {
                if (filename == "")
                    AllResourcesScannedRecipes = true;
                found = DynamicAssetLoader.Instance.AddAssetsFromResources<UMATextRecipe>(resourcesRecipesFolder, null, filename, AddRecipesFromAB);
            }
            if (dynamicallyAddFromAssetBundles && (filename == "" || found == false))
            {
                if (((AssetBundleManager.AssetBundleManifestObject == null || AssetBundleManager.AssetBundleIndexObject == null) && AssetBundleManager.SimulateAssetBundleInEditor == false) && Application.isPlaying == true)
                {
                    StopCoroutine(RecipesWaitForAssetBundleManager(filename));
                    StartCoroutine(RecipesWaitForAssetBundleManager(filename));
                    return;
                }
                DynamicAssetLoader.Instance.AddAssetsFromAssetBundles<UMATextRecipe>(ref assetBundlesUsedDict, downloadAssetsEnabled, resourcesRecipesFolder, null, filename, AddRecipesFromAB);
            }
        }

        IEnumerator CleanFilesFromResourcesAndBundles()
        {
            yield return null;
            Resources.UnloadUnusedAssets();
            yield break;
        }

        public void AddRecipesFromAB(UMATextRecipe[] uparts)
        {
            AddRecipes(uparts, "");
        }

        public void AddRecipe(UMATextRecipe upart)
        {
            if(upart != null)
            AddRecipes(new UMATextRecipe[] { upart });
        }

        public void AddRecipes(UMATextRecipe[] uparts, string filename = "")
        {
            foreach (UMATextRecipe u in uparts)
            {
                if (filename == "" || (filename != "" && filename.Trim() == u.name))
                {
                    //we might be refreshing so check its not already there
                    if (!RecipeIndex.ContainsKey(u.name))
                        RecipeIndex.Add(u.name, u);
                    else
                    {
                        RecipeIndex[u.name] = u;
                    }
                    for (int i = 0; i < u.compatibleRaces.Count; i++)
                    {
                        if (Recipes2.ContainsKey(u.compatibleRaces[i]))
                        {
                            Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes2[u.compatibleRaces[i]];

                            if (!RaceRecipes.ContainsKey(u.wardrobeSlot))
                            {
                                RaceRecipes.Add(u.wardrobeSlot, new List<UMATextRecipe>());
                            }
                            //we might be refreshing so check its not already there
                            if (!RaceRecipes[u.wardrobeSlot].Contains(u))
                            {
                                RaceRecipes[u.wardrobeSlot].Add(u);
                            }
                            else
                            {
                                //now we are trying to replace temprecipes with actual ones we need to replace this refrence
                                int? removeIndex = null;
                                for(int ir = 0; ir < RaceRecipes[u.wardrobeSlot].Count; ir++)
                                {
                                    if(RaceRecipes[u.wardrobeSlot][ir].name == u.name)
                                    {
                                        removeIndex = ir;
                                    }
                                }
                                if(removeIndex != null)
                                {
                                    RaceRecipes[u.wardrobeSlot].RemoveAt((int)removeIndex);
                                    RaceRecipes[u.wardrobeSlot].Add(u);
                                }
                            }
                        }
                        //backwards compatible race slots
                        foreach (string racekey in Recipes2.Keys)
                        {
                            //here we also need to check that the race itself has a wardrobe slot that matches the one i the compatible race
                            if (context.raceLibrary.GetRace(racekey).backwardsCompatibleWith.Contains(u.compatibleRaces[i]) && context.raceLibrary.GetRace(racekey).wardrobeSlots.Contains(u.wardrobeSlot))
                            {
                                Dictionary<string, List<UMATextRecipe>> RaceRecipes = Recipes2[racekey];
                                if (!RaceRecipes.ContainsKey(u.wardrobeSlot))
                                {
                                    RaceRecipes.Add(u.wardrobeSlot, new List<UMATextRecipe>());
                                }
                                //we might be refreshing so check its not already there
                                if (!RaceRecipes[u.wardrobeSlot].Contains(u))
                                    RaceRecipes[u.wardrobeSlot].Add(u);
                                else
                                {
                                    //now we are trying to replace temprecipes with actual ones we need to replace this refrence
                                    int? removeIndex = null;
                                    for (int ir = 0; ir < RaceRecipes[u.wardrobeSlot].Count; ir++)
                                    {
                                        if (RaceRecipes[u.wardrobeSlot][ir].name == u.name)
                                        {
                                            removeIndex = ir;
                                        }
                                    }
                                    if (removeIndex != null)
                                    {
                                        RaceRecipes[u.wardrobeSlot].RemoveAt((int)removeIndex);
                                        RaceRecipes[u.wardrobeSlot].Add(u);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            StartCoroutine(CleanFilesFromResourcesAndBundles());
        }
        public virtual UMATextRecipe GetRecipe(string filename, bool dynamicallyAdd = true)
        {
            UMATextRecipe foundRecipe = null;
            if (RecipeIndex.ContainsKey(filename))
            {
                foundRecipe = RecipeIndex[filename];
            }
            else
            {
                if (dynamicallyAdd)
                {
                    GatherRecipeFiles(filename);
                    if (RecipeIndex.ContainsKey(filename))
                    {
                        foundRecipe = RecipeIndex[filename];
                    }
                }
            }
            return foundRecipe;
        }
        /// <summary>
        /// Gets the originating asset bundle.
        /// </summary>
        /// <returns>The originating asset bundle.</returns>
        /// <param name="recipeName">Recipe name.</param>
        public string GetOriginatingAssetBundle(string recipeName)
        {
            string originatingAssetBundle = "";
            if (assetBundlesUsedDict.Count == 0)
                return originatingAssetBundle;
            else
            {
                foreach (KeyValuePair<string, List<string>> kp in assetBundlesUsedDict)
                {
                    if (kp.Value.Contains(recipeName))
                    {
                        originatingAssetBundle = kp.Key;
                        break;
                    }
                }
            }
            if (originatingAssetBundle == "")
            {
                Debug.Log(recipeName + " was not found in any loaded AssetBundle");
            }
            else
            {
                Debug.Log("originatingAssetBundle for " + recipeName + " was " + originatingAssetBundle);
            }
            return originatingAssetBundle;
        }
    }
}
