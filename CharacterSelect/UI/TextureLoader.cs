using System;
using System.Reflection;
using UnityEngine;
using CharacterSelect.Data;
using CharacterSelect.Infrastructure;

namespace CharacterSelect.UI
{
    public class TextureLoader
    {
        private readonly IModLogger _logger;

        private Texture2D[] _characterTextures;
        private Texture2D _placeholderTexture;
        private Texture2D _wrenchTexture;
        private bool _texturesLoaded;

        public TextureLoader(IModLogger logger)
        {
            _logger = logger;
        }

        public bool IsLoaded => _texturesLoaded;
        public Texture2D WrenchTexture => _wrenchTexture;

        public void LoadCharacterTextures()
        {
            if (_texturesLoaded) return;

            _placeholderTexture = LoadEmbeddedTexture("character_placeholder.png");

            _characterTextures = new Texture2D[CharacterData.Characters.Length];
            var allTextures = Resources.FindObjectsOfTypeAll<Texture2D>();

            for (int i = 0; i < CharacterData.Characters.Length; i++)
            {
                var character = CharacterData.Characters[i];
                string iconName = character.IconName;

                if (iconName != null)
                {
                    foreach (var tex in allTextures)
                    {
                        if (tex != null && tex.name == iconName)
                        {
                            _characterTextures[i] = tex;
                            break;
                        }
                    }
                }

                if (_characterTextures[i] == null)
                {
                    _logger.Warning($"Icon not found for {character.Name} (tried: {iconName ?? "null"})");
                    if (_placeholderTexture != null)
                        _characterTextures[i] = _placeholderTexture;
                }
            }

            // Log all icon_character textures in the game for discovery
            _logger.Info("=== Available icon_character textures ===");
            foreach (var tex in allTextures)
            {
                if (tex != null && tex.name != null && tex.name.StartsWith("icon_character"))
                    _logger.Info($"  {tex.name}");
            }
            _logger.Info("=== End icon_character textures ===");

            // Find wrench/settings icon from game textures
            foreach (var tex in allTextures)
            {
                if (tex == null) continue;
                var name = tex.name?.ToLower() ?? "";
                if (name.Contains("wrench") || name.Contains("spanner"))
                {
                    _wrenchTexture = tex;
                    break;
                }
            }
            if (_wrenchTexture == null)
                _wrenchTexture = TextureFactory.MakeWrenchTexture();

            _texturesLoaded = true;
        }

        public Texture2D GetCharacterIcon(int gameId)
        {
            if (_characterTextures == null) return null;
            for (int i = 0; i < CharacterData.Characters.Length; i++)
            {
                if (CharacterData.Characters[i].GameId == gameId && i < _characterTextures.Length)
                    return _characterTextures[i];
            }
            return null;
        }

        public Texture2D LoadEmbeddedTexture(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"CharacterSelect.Assets.{fileName}";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, data))
                        return texture;
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading embedded texture {fileName}: {ex.Message}");
                return null;
            }
        }
    }
}
