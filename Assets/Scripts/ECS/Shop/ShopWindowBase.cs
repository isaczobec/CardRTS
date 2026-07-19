using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared "toggle a shop window open/closed with a key or a button, and keep it gated by the
/// local player's live Gold balance" behavior — everything about a shop that doesn't depend
/// on what's actually being sold. ShopUIManager (cards, P key) and UpgradeShopUIManager
/// (upgrades, O key) both derive from this instead of duplicating it.
///
/// CRTP (ShopWindowBase&lt;TSelf&gt; : Singleton&lt;TSelf&gt;) rather than composition, so each
/// concrete shop still gets its own Singleton&lt;T&gt;.instance the same way every other manager
/// in this codebase does.
/// </summary>
public abstract class ShopWindowBase<TSelf> : Singleton<TSelf> where TSelf : ShopWindowBase<TSelf>
{
    [Header("Window")]
    [SerializeField] private GameObject _shopWindow;
    [SerializeField] private Button _closeButton;
    [SerializeField] private KeyCode _toggleKey = KeyCode.P;

    [Header("Audio")]
    [SerializeField] private string _openSoundName = "ShopOpen";
    [SerializeField] private string _closeSoundName = "ShopClose";

    protected ECS Ecs { get; private set; }
    protected CardRTSAudioSource AudioSource { get; private set; }

    private ulong _localPlayerResourceEntityId;

    /// <summary>
    /// Call from the subclass's own Initialize() before doing anything else that needs Ecs/
    /// AudioSource/the close button/resource-change notifications.
    /// </summary>
    protected void InitializeBase()
    {
        Ecs = TickManager.instance.ActiveECS;

        if (AudioManager.instance != null)
            AudioSource = AudioManager.instance.CreateAudioSource(Vector3.zero, spatialBlend: 0f);

        if (_closeButton != null)
            _closeButton.onClick.AddListener(CloseShop);

        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChangedInternal);
    }

    protected virtual void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (Input.GetKeyDown(_toggleKey)) ToggleShop();
    }

    protected void ToggleShop()
    {
        if (_shopWindow == null) return;

        bool willBeOpen = !_shopWindow.activeSelf;
        _shopWindow.SetActive(willBeOpen);
        PlayShopSound(willBeOpen ? _openSoundName : _closeSoundName);
    }

    protected void CloseShop()
    {
        if (_shopWindow == null || !_shopWindow.activeSelf) return;

        _shopWindow.SetActive(false);
        PlayShopSound(_closeSoundName);
    }

    protected void PlayShopSound(string soundName) => AudioSource?.PlaySound(soundName);

    protected ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    // Re-resolved lazily rather than cached forever, in case the local player's entity
    // doesn't exist yet the first time this is queried.
    protected ulong ResolveLocalPlayerResourceEntity()
    {
        if (Ecs == null) return 0;

        ComponentStore<PlayerResourcesComponent> resourceStore = Ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore != null && _localPlayerResourceEntityId != 0 && resourceStore.HasComponent(_localPlayerResourceEntityId))
            return _localPlayerResourceEntityId;

        _localPlayerResourceEntityId = ResourceHelper.FindPlayerResourcesEntity(Ecs, LocalPlayerId());
        return _localPlayerResourceEntityId;
    }

    protected int GetAvailableGold()
    {
        ulong resourceEntityId = ResolveLocalPlayerResourceEntity();
        if (resourceEntityId == 0) return 0;

        ComponentStore<PlayerResourcesComponent> resourceStore = Ecs.GetComponentStore<PlayerResourcesComponent>();
        if (resourceStore == null || !resourceStore.HasComponent(resourceEntityId)) return 0;

        return resourceStore.GetComponent(resourceEntityId).GoldFloor;
    }

    private void OnResourcesChangedInternal(ResourcesChangedEvent e)
    {
        if (e.EntityId != ResolveLocalPlayerResourceEntity()) return;
        RefreshAffordability();
    }

    /// <summary>Called on Initialize and whenever the local player's Gold changes.</summary>
    protected abstract void RefreshAffordability();
}
