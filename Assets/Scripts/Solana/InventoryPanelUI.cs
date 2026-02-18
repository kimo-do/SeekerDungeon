using Chaindepth.Accounts;
using SeekerDungeon.Audio;
using SeekerDungeon.Dungeon;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;

namespace SeekerDungeon.Solana
{
    /// <summary>
    /// Full-screen inventory panel opened from the HUD bag button.
    /// Uses a separate UIDocument so it renders on top of the game HUD.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class InventoryPanelUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LGManager manager;
        [SerializeField] private ItemRegistry itemRegistry;
        [SerializeField] private LGGameHudUI gameHudUI;
        [SerializeField] private Sprite slotGlowSprite;

        private UIDocument _document;
        private VisualElement _overlay;
        private VisualElement _grid;
        private Button _closeButton;
        private VisualElement _selectedIcon;
        private VisualElement _selectedPanel;
        private VisualElement _selectedPanelGlow;
        private Label _selectedName;
        private Label _selectedMeta;
        private Label _selectedAmount;
        private Label _selectedDurability;

        private readonly List<InventoryDisplayItem> _displayItems = new();
        private int _selectedIndex = -1;

        private bool _isVisible;
        public bool IsVisible => _isVisible;

        private readonly struct InventoryDisplayItem
        {
            public InventoryDisplayItem(ItemId itemId, uint amount, ushort durability)
            {
                ItemId = itemId;
                Amount = amount;
                Durability = durability;
            }

            public ItemId ItemId { get; }
            public uint Amount { get; }
            public ushort Durability { get; }
        }

        private void Awake()
        {
            LGUiInputSystemGuard.EnsureEventSystemForRuntimeUi(createIfMissing: true);
            _document = GetComponent<UIDocument>();

            if (manager == null)
            {
                manager = LGManager.Instance;
            }

            if (manager == null)
            {
                manager = UnityEngine.Object.FindFirstObjectByType<LGManager>();
            }

            if (gameHudUI == null)
            {
                gameHudUI = UnityEngine.Object.FindFirstObjectByType<LGGameHudUI>();
            }

            ResolveSlotGlowSpriteIfMissing();
        }

        private void OnEnable()
        {
            var root = _document?.rootVisualElement;
            if (root == null)
            {
                return;
            }

            _overlay = root.Q<VisualElement>("inventory-overlay");
            _grid = root.Q<VisualElement>("inventory-grid");
            _closeButton = root.Q<Button>("inventory-btn-close");
            _selectedIcon = root.Q<VisualElement>("inventory-selected-icon");
            _selectedPanel = root.Q<VisualElement>("inventory-selected-panel");
            _selectedPanelGlow = root.Q<VisualElement>("inventory-selected-panel-glow");
            _selectedName = root.Q<Label>("inventory-selected-name");
            _selectedMeta = root.Q<Label>("inventory-selected-meta");
            _selectedAmount = root.Q<Label>("inventory-selected-amount");
            _selectedDurability = root.Q<Label>("inventory-selected-durability");

            if (_closeButton != null)
            {
                _closeButton.clicked += HandleCloseClicked;
            }

            // Click overlay backdrop to close
            if (_overlay != null)
            {
                _overlay.RegisterCallback<PointerDownEvent>(OnOverlayPointerDown);
            }

            // Subscribe to HUD bag button
            if (gameHudUI != null)
            {
                gameHudUI.OnBagClicked += Toggle;
            }

            // Subscribe to inventory updates so the panel refreshes while open
            if (manager != null)
            {
                manager.OnInventoryUpdated += OnInventoryUpdated;
            }

            // Start hidden
            SetVisible(false);
            ClearSelectedDetails();
        }

        private void OnDisable()
        {
            if (_closeButton != null)
            {
                _closeButton.clicked -= HandleCloseClicked;
            }

            if (_overlay != null)
            {
                _overlay.UnregisterCallback<PointerDownEvent>(OnOverlayPointerDown);
            }

            if (gameHudUI != null)
            {
                gameHudUI.OnBagClicked -= Toggle;
            }

            if (manager != null)
            {
                manager.OnInventoryUpdated -= OnInventoryUpdated;
            }
        }

        public void Show()
        {
            SetVisible(true);
            RebuildGrid(manager?.CurrentInventoryState);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        public void Toggle()
        {
            if (_isVisible)
            {
                Hide();
                return;
            }

            Show();
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_overlay != null)
            {
                _overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void OnOverlayPointerDown(PointerDownEvent evt)
        {
            // Only close if the click was on the backdrop, not on the panel itself
            if (evt.target == _overlay)
            {
                GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
                Hide();
            }
        }

        private void HandleCloseClicked()
        {
            GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Secondary);
            Hide();
        }

        private void OnInventoryUpdated(InventoryAccount inventory)
        {
            if (_isVisible)
            {
                RebuildGrid(inventory);
            }
        }

        private void RebuildGrid(InventoryAccount inventory)
        {
            if (_grid == null)
            {
                return;
            }

            _displayItems.Clear();
            _grid.Clear();

            if (inventory?.Items == null || inventory.Items.Length == 0)
            {
                _selectedIndex = -1;
                ClearSelectedDetails();
                var emptyLabel = new Label("Your inventory is empty.");
                emptyLabel.AddToClassList("inventory-empty-label");
                _grid.Add(emptyLabel);
                return;
            }

            foreach (var item in inventory.Items)
            {
                if (item == null || item.Amount == 0)
                {
                    continue;
                }

                var itemId = LGDomainMapper.ToItemId(item.ItemId);
                _displayItems.Add(new InventoryDisplayItem(itemId, item.Amount, item.Durability));
                _grid.Add(CreateSlot(_displayItems.Count - 1, itemId, item.Amount));
            }

            if (_displayItems.Count == 0)
            {
                _selectedIndex = -1;
                ClearSelectedDetails();
                var emptyLabel = new Label("Your inventory is empty.");
                emptyLabel.AddToClassList("inventory-empty-label");
                _grid.Add(emptyLabel);
                return;
            }

            if (_selectedIndex < 0 || _selectedIndex >= _displayItems.Count)
            {
                _selectedIndex = 0;
            }

            SelectIndex(_selectedIndex);
        }

        private VisualElement CreateSlot(int index, ItemId itemId, uint amount)
        {
            var slot = new VisualElement();
            slot.AddToClassList("inventory-panel-slot");
            var slotIndex = index;
            slot.RegisterCallback<ClickEvent>(_ =>
            {
                GameAudioManager.Instance?.PlayButton(ButtonSfxCategory.Nav);
                SelectIndex(slotIndex);
            });

            var glow = new VisualElement();
            glow.AddToClassList("inventory-panel-slot-glow");
            var rarityColor = ResolveRarityColor(itemId);
            glow.style.unityBackgroundImageTintColor = new StyleColor(new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.38f));
            if (slotGlowSprite != null)
            {
                glow.style.backgroundImage = new StyleBackground(slotGlowSprite);
            }
            slot.Add(glow);

            // Icon
            var icon = new VisualElement();
            icon.AddToClassList("inventory-panel-slot-icon");
            if (itemRegistry != null)
            {
                var sprite = itemRegistry.GetIcon(itemId);
                if (sprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(sprite);
                }
            }

            slot.Add(icon);

            var amountLabel = new Label($"x{amount}");
            amountLabel.AddToClassList("inventory-panel-slot-count");
            slot.Add(amountLabel);

            var displayName = itemRegistry != null
                ? itemRegistry.GetDisplayName(itemId)
                : itemId.ToString();
            slot.tooltip = displayName;

            return slot;
        }

        private void SelectIndex(int index)
        {
            if (index < 0 || index >= _displayItems.Count)
            {
                return;
            }

            _selectedIndex = index;
            UpdateSelectedDetails(_displayItems[index]);
            RefreshSelectedSlotStyles();
        }

        private void RefreshSelectedSlotStyles()
        {
            if (_grid == null)
            {
                return;
            }

            for (var i = 0; i < _grid.childCount; i++)
            {
                var child = _grid[i];
                if (!child.ClassListContains("inventory-panel-slot"))
                {
                    continue;
                }

                if (i == _selectedIndex)
                {
                    child.AddToClassList("inventory-panel-slot-selected");
                }
                else
                {
                    child.RemoveFromClassList("inventory-panel-slot-selected");
                }
            }
        }

        private void UpdateSelectedDetails(InventoryDisplayItem item)
        {
            if (_selectedName != null)
            {
                _selectedName.text = itemRegistry != null
                    ? itemRegistry.GetDisplayName(item.ItemId)
                    : item.ItemId.ToString();
            }

            if (_selectedMeta != null)
            {
                var category = itemRegistry != null ? itemRegistry.GetCategory(item.ItemId).ToString() : "Unknown";
                var rarity = itemRegistry != null ? itemRegistry.GetRarity(item.ItemId).ToString() : "Common";
                _selectedMeta.text = $"{category}  {rarity}";
            }

            var rarityColor = ResolveRarityColor(item.ItemId);

            if (_selectedAmount != null)
            {
                _selectedAmount.text = $"Quantity: x{item.Amount}";
            }

            if (_selectedDurability != null)
            {
                if (ItemRegistry.IsWearable(item.ItemId))
                {
                    _selectedDurability.style.display = DisplayStyle.Flex;
                    _selectedDurability.text = $"{item.Durability}/{item.Durability}";
                }
                else
                {
                    _selectedDurability.style.display = DisplayStyle.None;
                    _selectedDurability.text = string.Empty;
                }
            }

            if (_selectedIcon != null)
            {
                var sprite = itemRegistry != null ? itemRegistry.GetIcon(item.ItemId) : null;
                _selectedIcon.style.backgroundImage = sprite != null
                    ? new StyleBackground(sprite)
                    : new StyleBackground((Sprite)null);

                var iconBorder = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.85f);
                _selectedIcon.style.borderTopColor = new StyleColor(iconBorder);
                _selectedIcon.style.borderRightColor = new StyleColor(iconBorder);
                _selectedIcon.style.borderBottomColor = new StyleColor(new Color(iconBorder.r * 0.55f, iconBorder.g * 0.55f, iconBorder.b * 0.55f, 0.95f));
                _selectedIcon.style.borderLeftColor = new StyleColor(iconBorder);
            }

            if (_selectedPanel != null)
            {
                var panelBg = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.16f);
                var borderStrong = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.75f);
                var borderDark = new Color(rarityColor.r * 0.42f, rarityColor.g * 0.42f, rarityColor.b * 0.42f, 0.92f);
                _selectedPanel.style.backgroundColor = new StyleColor(panelBg);
                _selectedPanel.style.borderTopColor = new StyleColor(borderStrong);
                _selectedPanel.style.borderLeftColor = new StyleColor(borderStrong);
                _selectedPanel.style.borderRightColor = new StyleColor(new Color(borderStrong.r * 0.72f, borderStrong.g * 0.72f, borderStrong.b * 0.72f, borderStrong.a));
                _selectedPanel.style.borderBottomColor = new StyleColor(borderDark);
            }

            if (_selectedPanelGlow != null)
            {
                _selectedPanelGlow.style.unityBackgroundImageTintColor = new StyleColor(new Color(rarityColor.r, rarityColor.g, rarityColor.b, 0.45f));
                if (slotGlowSprite != null)
                {
                    _selectedPanelGlow.style.backgroundImage = new StyleBackground(slotGlowSprite);
                }
            }
        }

        private void ClearSelectedDetails()
        {
            if (_selectedName != null)
            {
                _selectedName.text = "Select an item";
            }

            if (_selectedMeta != null)
            {
                _selectedMeta.text = string.Empty;
            }

            if (_selectedAmount != null)
            {
                _selectedAmount.text = string.Empty;
            }

            if (_selectedDurability != null)
            {
                _selectedDurability.style.display = DisplayStyle.None;
                _selectedDurability.text = string.Empty;
            }

            if (_selectedIcon != null)
            {
                _selectedIcon.style.backgroundImage = new StyleBackground((Sprite)null);
                _selectedIcon.style.borderTopColor = StyleKeyword.Null;
                _selectedIcon.style.borderRightColor = StyleKeyword.Null;
                _selectedIcon.style.borderBottomColor = StyleKeyword.Null;
                _selectedIcon.style.borderLeftColor = StyleKeyword.Null;
            }

            if (_selectedPanel != null)
            {
                _selectedPanel.style.backgroundColor = StyleKeyword.Null;
                _selectedPanel.style.borderTopColor = StyleKeyword.Null;
                _selectedPanel.style.borderRightColor = StyleKeyword.Null;
                _selectedPanel.style.borderBottomColor = StyleKeyword.Null;
                _selectedPanel.style.borderLeftColor = StyleKeyword.Null;
            }

            if (_selectedPanelGlow != null)
            {
                _selectedPanelGlow.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                _selectedPanelGlow.style.backgroundImage = new StyleBackground((Sprite)null);
            }
        }

        private Color ResolveRarityColor(ItemId itemId)
        {
            if (itemRegistry == null)
            {
                return Color.white;
            }

            return ItemRegistry.RarityToColor(itemRegistry.GetRarity(itemId));
        }

        private void ResolveSlotGlowSpriteIfMissing()
        {
            if (slotGlowSprite != null)
            {
                return;
            }

            var candidate = Resources
                .FindObjectsOfTypeAll<Sprite>()
                .FirstOrDefault(sprite => string.Equals(sprite.name, "Glow", StringComparison.OrdinalIgnoreCase));

            if (candidate != null)
            {
                slotGlowSprite = candidate;
            }
        }
    }
}
