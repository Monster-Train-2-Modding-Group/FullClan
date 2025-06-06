using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;

namespace FullClan.Plugin
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger = new(MyPluginInfo.PLUGIN_GUID);
        public void Awake()
        {
            // Plugin startup logic
            Logger = base.Logger;

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
        }
    }

    // Patch allows you to roll Clan/Clan
    [HarmonyPatch(typeof(SaveManager), "GetRandomUnlockedClass")]
    public class SaveManager_GetRandomUnlockedClass_AvoidSameClassPatch
    {
        public static void Prefix(ref ClassData? avoidClass)
        {
            avoidClass = null;
        }
    }


    [HarmonyPatch(typeof(RunSetupScreen), "HandleClanOptionsSelected")]
    public class RunSetupScreen_GetRandomUnlockedClass_DisableSwapPatch
    {
        // Gathering private methods to invoke them later
        // C# specific ? makes the type nullable, means that null could be a value instead of the type present.
        static readonly MethodInfo? RefreshCharacters = typeof(RunSetupScreen).GetMethod("RefreshCharacters", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo? RefreshClanCovenantUI = typeof(RunSetupScreen).GetMethod("RefreshClanCovenantUI", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly MethodInfo? RefreshWinStreak = typeof(RunSetupScreen).GetMethod("RefreshWinStreak", BindingFlags.NonPublic | BindingFlags.Instance);

        // Full rewrite patch, these should be avoided in general. Simple enough method that it shouldn't cause too much of an issue.
        // Access fields in the object you are patching with "___"
        // Access the object itself with "__instance"
        public static void Prefix(bool isMainClass, RunSetupClanSelectionLayoutUI.ClanOptionData? newClanOptionData, RunSetupClassLevelInfoUI ___mainClassInfo, RunSetupClassLevelInfoUI ___subClassInfo, SaveManager ___saveManager, RunSetupScreen __instance)
        {
            RunSetupClassLevelInfoUI runSetupClassLevelInfoUI = (isMainClass ? ___mainClassInfo : ___subClassInfo);
            if (newClanOptionData != null && newClanOptionData.IsRandom)
            {
                runSetupClassLevelInfoUI.SetClassRandom(newClanOptionData.randomId);
            }
            else if (___saveManager != null)
            {
                ClassData? classData = newClanOptionData?.clanData;
                int setLevel = 0;
                if (classData != null)
                {
                    setLevel = ___saveManager.GetClassLevelInMetagame(classData.GetID());
                    runSetupClassLevelInfoUI.SetClass(classData, setLevel, runSetupClassLevelInfoUI.ChampionIndex);
                }
            }

            // C# specific this is guaranteed to not be null so use null forgiveness operator
            // RefreshCharacters(delaySfx: false);
            RefreshCharacters!.Invoke(__instance, [false]);

            // RefreshClanCovenantUI();
            RefreshClanCovenantUI!.Invoke(__instance, []);

            // RefreshWinStreak();
            RefreshWinStreak!.Invoke(__instance, []);

            ___mainClassInfo.ShowCardPreview();
            ___subClassInfo.ShowCardPreview();
        }
    }

    // Patch ensures you can access the Banner on Ring 2.
    // Apparently it moves it to the center which was unexpected (not complaining).
    [HarmonyPatch(typeof(RandomMapDataContainer), nameof(RandomMapDataContainer.GetMapNodeData))]
    public class RandomMapDataContainer_GetMapNodeData_ForceDoubleBannersOnFullClanPatch
    {
        public static void Prefix(RandomMapDataContainer __instance, ref bool ___disallowDuplicatesOnSameSection, SaveManager saveManager)
        {
            // Lazy override field if the classes match. You you play with a normal clan combination it should reset back to the default value.
            if (__instance.name == "RewardsSimpleRing2Banners")
            {
                ___disallowDuplicatesOnSameSection = saveManager.GetMainClass() != saveManager.GetSubClass();
            }
        }
    }

    // Patch that prevents a bug with the showcase cards.
    // If the same card is pulled the UI is thrown off only showing 2 cards.
    [HarmonyPatch(typeof(CardSetBuilder), nameof(CardSetBuilder.CollectCards))]
    public class CardSetBuilder_CollectCards_PreventSameCommonStartOfCardsPatch
    {
        public static List<CardData> extraPull = [];

        public static void Postfix(CardSetBuilder __instance, ref List<CardData> collectedCards, ref List<CardData> showcaseCards, List<CardPull> ___paramCardPulls, SaveManager saveManager)
        {
            if (__instance.name != "StarterDeck_C01" || saveManager.GetMainClass() != saveManager.GetSubClass())
                return;

            // Reroll and modify main common card if MainClassCommon2X and SubClassCommon2X pulled the same card.
            if (showcaseCards[0] == showcaseCards[1] && showcaseCards[1] == showcaseCards[2] && showcaseCards[2] == showcaseCards[3])
            {
                Plugin.Logger.LogInfo("Rerolling due to same cards.");
                if (___paramCardPulls[0]?.name != "MainClassCommon2X")
                {
                    Plugin.Logger.LogWarning("Possible mod incompatibility, ParamCardPulls has been tampered with! Bail!");
                    return;
                }

                int i = 0;
                do
                {
                    extraPull.Clear();
                    ___paramCardPulls[0].CollectCards(extraPull, saveManager);
                    i++;
                    Plugin.Logger.LogInfo("Rerolling again due to same cards.");
                } while (extraPull[0] == showcaseCards[0] && i < 200);

                // Should be rerolled (1/8)**200 chance of failure.
                for (i = 0; i < extraPull.Count; i++)
                {
                    showcaseCards[i] = extraPull[i];
                    collectedCards[i] = extraPull[i];
                }
            }
        }
    }

}
