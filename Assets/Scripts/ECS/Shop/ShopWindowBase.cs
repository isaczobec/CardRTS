using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Search")]
    // Optional — shops without a search bar wired just never call OnSearchTextChanged.
    // While this is focused, _toggleKey is suppressed (see Update) so typing e.g. "p" into
    // the search bar can't also close the window out from under the player.
    [SerializeField] private TMP_InputField _searchInputField;

    [Header("Audio")]
    [SerializeField] private string _openSoundName = "ShopOpen";
    [SerializeField] private string _closeSoundName = "ShopClose";

    protected ECS Ecs { get; private set; }
    protected CardRTSAudioSource AudioSource { get; private set; }

    // Current contents of _searchInputField, kept in sync via onValueChanged — "" (never
    // null) when no search field is wired or nothing's been typed. Subclasses read this from
    // their own OnSearchTextChanged override to re-filter their grid.
    protected string SearchText { get; private set; } = "";

    private ulong _localPlayerResourceEntityId;

    // Set once ResolveLocalPlayerResourceEntity first succeeds — see Update's own retry
    // comment for why this can legitimately fail the first few frames.
    private bool _resourceEntityResolved;

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

        if (_searchInputField != null)
            _searchInputField.onValueChanged.AddListener(OnSearchTextChangedInternal);

        TickManager.instance.ServerFlagEvents.Subscribe<ResourcesChangedEvent>(OnResourcesChangedInternal);
    }

    protected virtual void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;
        if (Input.GetKeyDown(_toggleKey) && !IsSearchFieldFocused) ToggleShop();

        // The local player's PlayerResourcesComponent entity may not have synced into Ecs yet
        // the exact frame InitializeBase/the subclass's own Initialize ran (e.g. a host/
        // client's ClientLocalECS hasn't reconciled the just-spawned player entity from the
        // authoritative ECS within that same frame) — the very first RefreshAffordability
        // call then sees GetAvailableGold() return 0 (resource entity unresolved, not
        // genuinely 0 gold) and shows every card/upgrade as unaffordable. Nothing else was
        // re-triggering that check: ResolveLocalPlayerResourceEntity caches its failure
        // (_localPlayerResourceEntityId stays 0) until the next ResourcesChangedEvent, and
        // passive Gold generation is 0 by default (see NetworkManager.SpawnPlayerEntity), so
        // without a purchase (the first thing that actually fires that event) it could sit
        // wrong indefinitely. Retried here every frame — cheap, just a dictionary lookup —
        // until it succeeds once, then this stops; ResourcesChangedEvent keeps affordability
        // current from then on exactly as before.
        if (!_resourceEntityResolved && ResolveLocalPlayerResourceEntity() != 0)
        {
            _resourceEntityResolved = true;
            RefreshAffordability();
        }
    }

    // Checks both isFocused (the normal case) and whether the EventSystem currently has this
    // field selected, as a fallback — belt-and-suspenders against isFocused not being update
    // yet on the very frame a click hands it keyboard focus.
    private bool IsSearchFieldFocused
    {
        get
        {
            if (_searchInputField == null) return false;
            if (_searchInputField.isFocused) return true;
            return EventSystem.current != null
                && EventSystem.current.currentSelectedGameObject == _searchInputField.gameObject;
        }
    }

    private void OnSearchTextChangedInternal(string text)
    {
        SearchText = text ?? "";
        OnSearchTextChanged();
    }

    /// <summary>
    /// Called whenever the search bar's text changes (never, if no search field is wired).
    /// Override to re-apply filtering — read the new value via SearchText.
    /// </summary>
    protected virtual void OnSearchTextChanged() { }

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

    /// <summary>
    /// True while this shop's window is open — used by CameraController to suppress
    /// scroll-wheel zoom so scrolling a shop list (see ScrollPanel) doesn't also zoom the
    /// camera underneath it.
    /// </summary>
    public bool IsOpen => _shopWindow != null && _shopWindow.activeInHierarchy;

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
