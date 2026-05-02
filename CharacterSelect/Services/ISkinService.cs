using System.Collections.Generic;
using CharacterSelect.Data;
using UnityEngine;

namespace CharacterSelect.Services
{
    public interface ISkinService
    {
        Dictionary<string, List<SkinEntry>> AvailableSkins { get; }
        void DeployEmbeddedReskins();
        void ScanForReskins();
        void ScheduleReskin(int characterId, string skinPath);
        void ProcessPendingReskin();
        void ApplyReskin(int characterId, string skinPath);
        void DumpCurrentSkinTexture(int currentCharacterId);
        void ExportUVTemplate(GameObject playerObj, string skinPrefix, string charName, string dumpDir);
        Texture2D LoadReskinTexture(string filePath);
        SkinEntry? FindSkinEntry(string filePath);
        string GetCharacterReskinKey(int characterId);
    }
}
