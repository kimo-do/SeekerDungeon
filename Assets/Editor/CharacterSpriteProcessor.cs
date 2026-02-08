using System;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace SeekerDungeon.Editor
{
    public static class CharacterSpriteProcessor
    {
        private const string CharacterFolderPath = "Assets/Sprites/Characters";
        private static readonly Vector2 TargetPivot = new(0.5f, 0.0f);
        private static readonly SpriteDataProviderFactories SpriteDataProviderFactories = new();

        [MenuItem("Tools/Process Characters")]
        public static void ProcessCharacters()
        {
            if (!AssetDatabase.IsValidFolder(CharacterFolderPath))
            {
                Debug.LogError($"[Process Characters] Folder not found: {CharacterFolderPath}");
                return;
            }

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CharacterFolderPath });
            var updatedCount = 0;
            var skippedCount = 0;
            SpriteDataProviderFactories.Init();

            for (var index = 0; index < guids.Length; index += 1)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[index]);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;

                if (importer == null)
                {
                    skippedCount += 1;
                    continue;
                }

                var changed = false;

                if (importer.filterMode != FilterMode.Point)
                {
                    importer.filterMode = FilterMode.Point;
                    changed = true;
                }

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }

                var textureSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(textureSettings);

                if (textureSettings.spriteAlignment != (int)SpriteAlignment.Custom)
                {
                    textureSettings.spriteAlignment = (int)SpriteAlignment.Custom;
                    changed = true;
                }

                if (!Approximately(textureSettings.spritePivot, TargetPivot))
                {
                    textureSettings.spritePivot = TargetPivot;
                    changed = true;
                }

                if (ApplySpriteRectPivotWithDataProvider(importer))
                {
                    changed = true;
                }

                if (!changed)
                {
                    continue;
                }

                importer.SetTextureSettings(textureSettings);
                importer.SaveAndReimport();
                updatedCount += 1;
            }

            Debug.Log($"[Process Characters] Done. Updated: {updatedCount}, Skipped: {skippedCount}, Total: {guids.Length}");
        }

        [MenuItem("Tools/Process Characters", true)]
        private static bool ValidateProcessCharacters()
        {
            return AssetDatabase.IsValidFolder(CharacterFolderPath);
        }

        private static bool Approximately(Vector2 a, Vector2 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.0001f && Mathf.Abs(a.y - b.y) < 0.0001f;
        }

        private static bool ApplySpriteRectPivotWithDataProvider(TextureImporter importer)
        {
            var dataProvider = SpriteDataProviderFactories.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                return false;
            }

            dataProvider.InitSpriteEditorDataProvider();
            var spriteRects = dataProvider.GetSpriteRects();
            if (spriteRects == null || spriteRects.Length == 0)
            {
                return false;
            }

            var changed = false;
            for (var index = 0; index < spriteRects.Length; index += 1)
            {
                var spriteRect = spriteRects[index];
                var spriteChanged = false;

                if (spriteRect.alignment != SpriteAlignment.Custom)
                {
                    spriteRect.alignment = SpriteAlignment.Custom;
                    spriteChanged = true;
                }

                if (!Approximately(spriteRect.pivot, TargetPivot))
                {
                    spriteRect.pivot = TargetPivot;
                    spriteChanged = true;
                }

                if (!spriteChanged)
                {
                    continue;
                }

                spriteRects[index] = spriteRect;
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            dataProvider.SetSpriteRects(spriteRects);
            dataProvider.Apply();
            return true;
        }
    }
}
