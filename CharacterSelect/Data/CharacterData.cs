using System.Collections.Generic;

namespace CharacterSelect.Data
{
    public static class CharacterData
    {
        public static readonly (string Name, int GameId, string IconName)[] Characters = {
            ("Frog", 1, "icon_character_frogdefault"),
            ("Penguin", 2, "icon_character_penguin"),
            ("Harbor Seal", 3, "icon_character_sealharbor"),
            ("Brown Bear", 5, "icon_character_bearbrown"),
            ("Polar Bear", 6, "icon_character_bearpolar"),
            ("Black Bear", 7, "icon_character_bearblack"),
            ("Ringed Seal", 8, "icon_character_sealringed"),
            ("Baikal Seal", 9, "icon_character_sealbaikal"),
            ("Strawberry Frog", 10, "icon_character_frogstrawberry"),
            ("Tree Frog", 11, "icon_character_frogtree"),
            ("Orange Toad", 12, "icon_character_toadorange"),
            ("Brown Toad", 13, "icon_character_toadbrown"),
            ("Orange Fox", 14, "icon_character_foxorange"),
            ("Arctic Fox", 15, "icon_character_foxarctic"),
            ("Panda", 16, "icon_character_panda"),
        };

        public static readonly (string GroupName, string ModelKey, int[] GameIds)[] ModelGroups = {
            ("Frog",    "frog",    new[] { 1, 10, 11 }),
            ("Penguin", "penguin", new[] { 2 }),
            ("Seal",    "seal",    new[] { 3, 8, 9 }),
            ("Bear",    "bear",    new[] { 5, 6, 7 }),
            ("Toad",    "toad",    new[] { 12, 13 }),
            ("Fox",     "fox",     new[] { 14, 15 }),
            ("Panda",   "panda",   new[] { 16 }),
        };

        public static readonly Dictionary<int, string> CustomIcons = new()
        {
            { 3, "harbor_seal.png" },
            { 6, "polar_bear.png" },
            { 7, "black_bear.png" },
            { 8, "ringed_seal.png" },
            { 10, "strawberry_frog.png" },
            { 11, "tree_frog.png" },
            { 13, "brown_toad.png" },
            { 15, "arctic_fox.png" },
            { 16, "panda.png" },
        };

        public static readonly Dictionary<int, string> CharacterSkinMaterials = new()
        {
            { 1, "Skin_Frog" },
            { 2, "Skin_Penguin" },
            { 3, "Skin_Seal" },
            { 5, "Skin_Bear_Brown" },
            { 6, "Skin_Bear_Polar" },
            { 7, "Skin_Bear_Black" },
            { 8, "Skin_Seal" },
            { 9, "Skin_Seal" },
            { 10, "Skin_Frog" },
            { 11, "Skin_Frog" },
            { 12, "Skin_Toad" },
            { 13, "Skin_Toad" },
            { 14, "Skin_Fox" },
            { 15, "Skin_Fox" },
            { 16, "Skin_Panda" },
        };

        public static readonly Dictionary<string, string> SkinPrefixToModelKey = new()
        {
            { "Skin_Frog", "frog" },
            { "Skin_Penguin", "penguin" },
            { "Skin_Seal", "seal" },
            { "Skin_Bear_Brown", "bear" },
            { "Skin_Bear_Polar", "bear" },
            { "Skin_Bear_Black", "bear" },
            { "Skin_Toad", "toad" },
            { "Skin_Fox", "fox" },
            { "Skin_Panda", "panda" },
        };

        public static string GetCharacterName(int gameId)
        {
            foreach (var character in Characters)
            {
                if (character.GameId == gameId)
                    return character.Name;
            }
            return "Unknown";
        }

        public static string GetModelKey(string skinPrefix)
        {
            return SkinPrefixToModelKey.TryGetValue(skinPrefix, out var key)
                ? key
                : skinPrefix.Replace("Skin_", "").ToLower();
        }
    }
}
