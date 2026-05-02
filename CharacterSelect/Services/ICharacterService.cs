namespace CharacterSelect.Services
{
    public interface ICharacterService
    {
        void SwitchCharacter(int characterId);
        int GetCurrentCharacterId();
    }
}
