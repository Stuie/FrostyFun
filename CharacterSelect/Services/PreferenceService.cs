using System.IO;
using UnityEngine;
using CharacterSelect.Data;
using CharacterSelect.Infrastructure;

namespace CharacterSelect.Services
{
    public class PreferenceService : IPreferenceService
    {
        private const string PREF_KEY = "CharacterSelect_SavedCharacterId";
        private const string PREF_SKIN_KEY = "CharacterSelect_SavedSkinName";

        private readonly IModLogger _logger;

        public int SavedCharacterId { get; private set; }
        public string ActiveSkinPath { get; private set; }

        public PreferenceService(IModLogger logger)
        {
            _logger = logger;
        }

        public void Load()
        {
            SavedCharacterId = PlayerPrefs.GetInt(PREF_KEY, 0);
            ActiveSkinPath = PlayerPrefs.GetString(PREF_SKIN_KEY, "");
            if (string.IsNullOrEmpty(ActiveSkinPath)) ActiveSkinPath = null;
            if (SavedCharacterId > 0)
                _logger.Info($"Saved character: {CharacterData.GetCharacterName(SavedCharacterId)}");
        }

        public void Save(int characterId, string skinPath = null)
        {
            SavedCharacterId = characterId;
            ActiveSkinPath = skinPath;
            PlayerPrefs.SetInt(PREF_KEY, characterId);
            PlayerPrefs.SetString(PREF_SKIN_KEY, skinPath ?? "");
            PlayerPrefs.Save();
            _logger.Info($"Saved preference: {CharacterData.GetCharacterName(characterId)}" +
                (skinPath != null ? $" (skin: {Path.GetFileNameWithoutExtension(skinPath)})" : ""));
        }

        public void Clear()
        {
            if (SavedCharacterId != 0)
            {
                SavedCharacterId = 0;
                ActiveSkinPath = null;
                PlayerPrefs.SetInt(PREF_KEY, 0);
                PlayerPrefs.SetString(PREF_SKIN_KEY, "");
                PlayerPrefs.Save();
            }
        }
    }
}
