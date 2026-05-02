using System;

namespace CharacterSelect.UI
{
    public interface ICharacterSelectUI
    {
        bool IsVisible { get; }
        void Open();
        void Close();
        void Draw(int currentCharacterId, string activeSkinPath, Action<int, string> onSelect);
    }
}
