namespace DRPG.Client.Startup;

/// <summary>
/// The 39 table loaders of the original (✅ v322 <c>0x00719af0</c>: loop over 39 types, switch <c>0x007aa8d8</c>, factory
/// <c>0x007a9820</c>), in the order of the loop. Each group is read from <c>table\&lt;name&gt;_us.dat</c> in
/// <c>table.hf</c> and, on top of it, <c>Language\&lt;Name&gt;_US.bin</c>. Written out in full so the load does the same work.
/// </summary>
public static class TableList
{
    /// <summary>The id of the group that holds <c>TextAttach</c>, the interface texts.</summary>
    public const int TextAttachGroup = 26;

    public static readonly IReadOnlyList<string[]> Groups =
    [
        ["Dex"],
        ["Comment"],
        ["Tree"],
        ["BuffOriginalData", "R_Buff"],
        ["Riddle"],
        ["Item_Info", "Make_Item", "Digimon_Title", "Item_Equipment_Point", "MakeItem", "MakeItem_Material_Item"],
        ["Card_Info"],
        ["Digimon", "E_Attribute_Text"],
        ["Digimon_Skill"],
        ["Add_Attribute", "Attribute", "Ec_Attribute", "Ec_Battle_Attribute"],
        ["Resonance"],
        ["Map", "Map_PartySearch", "Window"],
        ["Keep_Connect", "P_Shop"],
        [],
        ["M_Comment", "M_Pattern_Message"],
        ["NPC_INFO", "NPC_Function", "Custom_AvatarNPC", "PersonalStore_NPC"],
        ["Avatar"],
        ["CapsuleMachine", "EventCapsuleMachine"],
        ["EventShop"],
        ["MonsterRegen"],
        ["Evo_Card_Info"],
        ["QuestDialog", "Pop-UpWindow"],
        ["ShopDialog", "ShopIconName"],
        ["NpcDialogWindow", "ExceptrNpcDialogWindow", "TutorialWindow"],
        ["Quiz_Event"],
        ["SystemMessage", "ProhibitedWords"],
        ["TextAttach"],
        ["Tamer_Title"],
        ["Item_Change", "Item_Composite"],
        // ❓ "InGameShop_* (several)": the names are the ones of the install. ✅ The DigiXros material tables are called *_Evol_Material_* there.
        ["InGameShop_Evolution", "InGameShop_CapsuleMachine", "InGameShop_ItemPage", "InGameShop_Item_Data", "InGameShop_Item_Explain", "InGameShop_MainPage_Item", "InGameShop_Notice", "InGameShop_RandomBox_In_Items", "InGameShop_RandomBox", "InGameShop_Text"],
        ["BattlePass_RewardFree", "BattlePass_RewardPay"],
        ["Collection"],
        ["Macro_Quiz_List"],
        ["DigiXros_Evol", "DigiXros_Evol_Material_Dm", "DigiXros_Evol_Material_Item"],
        ["Dungeon_Entrance_Data", "Dungeon_Guide_Data"],
        ["SpecialEvolReturn"],
        ["RandomDigitama"],
        ["Event_Schedule"],
        ["SealBuff", "SealList"],
    ];

    public static string TablePath(string name) => $"table\\{name.ToLowerInvariant()}_us.dat";

    public static string LanguageFile(string name) => $"{name}_US.bin";
}
