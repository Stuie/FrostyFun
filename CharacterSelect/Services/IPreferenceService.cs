namespace CharacterSelect.Services
{
    public interface IPreferenceService
    {
        int SavedCharacterId { get; }
        string ActiveSkinPath { get; }
        void Load();
        void Save(int characterId, string skinPath = null);
        void Clear();
    }
}
