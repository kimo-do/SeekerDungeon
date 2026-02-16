using Chaindepth.Accounts;
using SeekerDungeon.Dungeon;
using UnityEngine;
using UnityEngine.UIElements;

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

        private UIDocument _document;
        private VisualElement _overlay;
        private VisualElement _grid;
        private Button _closeButton;

        private bool _isVisible;
        public bool IsVisible => _isVisible;

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
                manager = Object.FindFirstObjectByType<LGManager>();
            }

            if (gameHudUI == null)
            {
                gameHudUI = Object.FindFirstObjectByType<LGGameHudUI>();
            }
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

            if (_closeButton != null)
            {
                _closeButton.clicked += Hide;
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
        }

        private void OnDisable()
        {
            if (_closeButton != null)
            {
                _closeButton.clicked -= Hide;
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
                Hide();
            }
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

            _grid.Clear();

            if (inventory?.Items == null || inventory.Items.Length == 0)
            {
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
                _grid.Add(CreateSlot(itemId, item.Amount, item.Durability));
            }
        }

        private VisualElement CreateSlot(ItemId itemId, uint amount, ushort durability)
        {
            var slot = new VisualElement();
            slot.AddToClassList("inventory-panel-slot");

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

            // Name
            var displayName = itemRegistry != null
                ? itemRegistry.GetDisplayName(itemId)
                : itemId.ToString();
            var nameLabel = new Label(displayName);
            nameLabel.AddToClassList("inventory-panel-slot-name");
            slot.Add(nameLabel);

            // Amount
            var amountLabel = new Label($"x{amount}");
            amountLabel.AddToClassList("inventory-panel-slot-amount");
            slot.Add(amountLabel);

            // Durability (only show for wearable weapons)
            if (ItemRegistry.IsWearable(itemId) && durability > 0)
            {
                var durabilityLabel = new Label($"Dur: {durability}");
                durabilityLabel.AddToClassList("inventory-panel-slot-durability");
                slot.Add(durabilityLabel);
            }

            return slot;
        }
    }
}
