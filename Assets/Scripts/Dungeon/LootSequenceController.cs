using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SeekerDungeon.Audio;
using SeekerDungeon.Solana;
using TMPro;
using UnityEngine;

namespace SeekerDungeon.Dungeon
{
    /// <summary>
    /// Plays an in-world loot reveal sequence: item pop, particle burst, then fly-to-inventory.
    /// Attach to the room or DungeonManager so it persists during gameplay.
    /// </summary>
    public sealed class LootSequenceController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ItemRegistry itemRegistry;
        [SerializeField] private Camera worldCamera;

        [Header("Particle")]
        [SerializeField] private ParticleSystem lootBurstPrefab;

        [Header("Item Display")]
        [SerializeField] private float itemPopDuration = 0.3f;
        [SerializeField] private float itemPopOvershoot = 1.3f;
        [SerializeField] private float itemHoldDuration = 0.6f;
        [SerializeField] private float itemFlyDuration = 0.4f;
        [SerializeField] private float itemWorldScale = 1.2f;

        [Header("Amount Label")]
        [SerializeField] private GameObject amountTextPrefab;

        [Header("Timing")]
        [SerializeField] private float delayBetweenItems = 0.15f;

        /// <summary>
        /// Fired for each item as it arrives at the inventory bar position.
        /// Listeners can use this to bump the corresponding slot.
        /// </summary>
        public event Action<ItemId> OnItemArrived;

        /// <summary>
        /// Fired when the full loot sequence finishes.
        /// </summary>
        public event Action OnSequenceComplete;

        private bool _isPlaying;

        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// Play the loot reveal sequence for the given loot result.
        /// </summary>
        /// <param name="lootResult">Items gained from loot.</param>
        /// <param name="chestWorldPos">World position of the chest center.</param>
        /// <param name="getInventorySlotScreenPos">
        /// Callback that returns the screen-space position for a given ItemId slot
        /// in the inventory bar. If null, items just fade out in place.
        /// </param>
        public void PlayLootSequence(
            LootResult lootResult,
            Vector3 chestWorldPos,
            Func<ItemId, Vector3?> getInventorySlotScreenPos = null)
        {
            if (lootResult?.Items == null || lootResult.Items.Count == 0)
            {
                return;
            }

            if (_isPlaying)
            {
                Debug.Log("[LootSequence] Already playing, skipping duplicate call");
                return;
            }

            PlayLootSequenceAsync(lootResult, chestWorldPos, getInventorySlotScreenPos).Forget();
        }

        private async UniTaskVoid PlayLootSequenceAsync(
            LootResult lootResult,
            Vector3 chestWorldPos,
            Func<ItemId, Vector3?> getInventorySlotScreenPos)
        {
            _isPlaying = true;

            try
            {
                var cam = worldCamera != null ? worldCamera : Camera.main;

                for (var i = 0; i < lootResult.Items.Count; i++)
                {
                    var item = lootResult.Items[i];
                    await ShowSingleItemAsync(item, chestWorldPos, cam, getInventorySlotScreenPos);

                    if (i < lootResult.Items.Count - 1)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(delayBetweenItems),
                            cancellationToken: this.GetCancellationTokenOnDestroy());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Object destroyed, sequence cancelled
            }
            finally
            {
                _isPlaying = false;
                OnSequenceComplete?.Invoke();
            }
        }

        private async UniTask ShowSingleItemAsync(
            InventoryItemView item,
            Vector3 chestWorldPos,
            Camera cam,
            Func<ItemId, Vector3?> getInventorySlotScreenPos)
        {
            var ct = this.GetCancellationTokenOnDestroy();
            var entry = itemRegistry != null ? itemRegistry.Get(item.ItemId) : null;
            GameAudioManager.Instance?.PlayLootRevealByRarity(
                entry?.rarity ?? ItemRarity.Common,
                chestWorldPos);

            // -- Spawn item sprite --
            var spawnPos = chestWorldPos + Vector3.up * 0.5f;
            var itemGo = new GameObject($"LootItem_{item.ItemId}");
            itemGo.transform.position = spawnPos;
            itemGo.transform.localScale = Vector3.zero;

            var sr = itemGo.AddComponent<SpriteRenderer>();
            sr.sprite = entry?.icon;
            sr.sortingOrder = 100;

            // -- Amount label (world-space TMP prefab) --
            if (item.Amount > 1 && amountTextPrefab != null)
            {
                var labelGo = Instantiate(amountTextPrefab, itemGo.transform, false);
                var tmp = labelGo.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = $"x{item.Amount}";
                }
            }

            // -- Pop animation (overshoot then settle) --
            var targetScale = Vector3.one * itemWorldScale;
            var popGrowDuration = itemPopDuration * 0.6f;
            var popSettleDuration = itemPopDuration * 0.4f;

            itemGo.transform
                .DOScale(targetScale * itemPopOvershoot, popGrowDuration)
                .SetEase(Ease.OutQuad);
            await UniTask.Delay(TimeSpan.FromSeconds(popGrowDuration), cancellationToken: ct);

            itemGo.transform
                .DOScale(targetScale, popSettleDuration)
                .SetEase(Ease.InOutQuad);
            await UniTask.Delay(TimeSpan.FromSeconds(popSettleDuration), cancellationToken: ct);

            // -- Particle burst --
            if (lootBurstPrefab != null)
            {
                var particle = Instantiate(lootBurstPrefab, spawnPos, Quaternion.identity);
                var mainModule = particle.main;
                var rarityColor = entry != null
                    ? ItemRegistry.RarityToColor(entry.rarity)
                    : Color.white;
                mainModule.startColor = new ParticleSystem.MinMaxGradient(rarityColor);
                particle.Emit(20);
                Destroy(particle.gameObject, mainModule.duration + mainModule.startLifetime.constantMax + 0.5f);
            }

            // -- Hold --
            await UniTask.Delay(TimeSpan.FromSeconds(itemHoldDuration), cancellationToken: ct);

            // -- Fly to inventory bar slot --
            Vector3? slotScreenPos = getInventorySlotScreenPos?.Invoke(item.ItemId);
            if (slotScreenPos.HasValue && cam != null)
            {
                // Convert screen position to world position for the sprite to fly toward.
                // Use a world Z that keeps the sprite visible.
                var worldTarget = cam.ScreenToWorldPoint(
                    new Vector3(slotScreenPos.Value.x, slotScreenPos.Value.y, cam.nearClipPlane + 1f));
                worldTarget.z = spawnPos.z;

                itemGo.transform
                    .DOMove(worldTarget, itemFlyDuration)
                    .SetEase(Ease.InBack);
                itemGo.transform
                    .DOScale(Vector3.one * 0.6f, itemFlyDuration)
                    .SetEase(Ease.InQuad);
                await UniTask.Delay(TimeSpan.FromSeconds(itemFlyDuration), cancellationToken: ct);

                itemGo.transform.DOScale(Vector3.zero, 0.1f);
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f), cancellationToken: ct);
            }
            else
            {
                // No target slot, just scale down in place
                itemGo.transform
                    .DOScale(Vector3.zero, itemFlyDuration)
                    .SetEase(Ease.InBack);
                await UniTask.Delay(TimeSpan.FromSeconds(itemFlyDuration), cancellationToken: ct);
            }

            OnItemArrived?.Invoke(item.ItemId);

            if (itemGo != null)
            {
                Destroy(itemGo);
            }
        }
    }
}
