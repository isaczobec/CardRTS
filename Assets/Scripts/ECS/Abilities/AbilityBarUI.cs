using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the LOCAL PLAYER's player-wide ability bar (see AbilityBarComponent) — up to 8
// distinct ability types the player has ever purchased a card for, in the order first
// purchased — always visible while the match is running (no longer tied to troop selection;
// see AbilityInputManager for the matching change on the input side). Each slot i shows
// AbilityBarComponent.GetAbilityId(i)'s icon; since a bar slot can be backed by SEVERAL live
// troops at once (e.g. several Ice Men), each with its own independent cooldown/charge state
// — or by ZERO live troops right now (purchased but not deployed, or deployed and since died
// — the bar entry itself never disappears, since the purchased card is still in the player's
// deck/hand) — the cooldown/charge readout shown is whichever owned troop with that ability
// equipped is soonest-ready (see FindBestTroop); with no such troop at all, the icon is just
// dimmed instead.
//
// _root must be a different object than the one this script lives on — disabling the
// script's own GameObject would stop Update from running, and nothing would ever be able
// to turn it back on. Purely reactive: polls the live ECS every frame, like
// AbilityInputManager, rather than subscribing to any ECS event (there's no "ability bar
// changed" flag event today).
public class AbilityBarUI : Singleton<AbilityBarUI>
{
    [Serializable]
    private class AbilitySlot
    {
        public GameObject SlotObject;
        public Image Icon;
        public TMP_Text CooldownText;
        // Filled-type Image (radial/wipe) drawn over the icon while on cooldown — fillAmount
        // goes from 1 (just used) down to 0 (ready) over the course of the cooldown.
        public Image CooldownOverlay;
        // Shows the soonest-ready troop's charges remaining (see FindBestTroop) — disabled
        // outright for a MaxCharges <= 1 ability (nothing meaningful to show here).
        public TMP_Text ChargesText;
        // Optional — attach a HoverEventForwarder to the icon to show a DescriptionTooltip
        // (name + description) while this slot's ability is hovered. Left unwired, the slot
        // just has no hover tooltip.
        public HoverEventForwarder HoverForwarder;
        // Optional — "Q".."I" hotkey caption for this slot. Left unassigned, no label shown.
        public TMP_Text KeyLabel;
    }

    // Visual left-to-right order == key order — see AbilityInputManager.SlotKeys.
    private static readonly string[] SlotKeyLabels = { "Q", "W", "E", "R", "T", "Y", "U", "I" };

    // Icon alpha when a bar slot's ability is currently backed by zero owned troops — dimmed
    // instead of showing a cooldown readout, since there's nothing to count down.
    private const float NoTroopIconAlpha = 0.4f;

    [SerializeField] private GameObject _root;
    [SerializeField] private AbilitySlot[] _slots = new AbilitySlot[AbilityBarComponent.SlotCount];

    private ECS _ecs;
    private ComponentStore<AbilityBarComponent> _barStore;
    private ComponentStore<AbilityComponent> _abilityStore;
    private ComponentStore<TroopComponent> _troopStore;

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _barStore = _ecs.GetComponentStore<AbilityBarComponent>();
        _abilityStore = _ecs.GetComponentStore<AbilityComponent>();
        _troopStore = _ecs.GetComponentStore<TroopComponent>();

        SetRootActive(false);

        for (int slot = 0; slot < _slots.Length; slot++)
        {
            if (slot < SlotKeyLabels.Length && _slots[slot]?.KeyLabel != null)
                _slots[slot].KeyLabel.text = SlotKeyLabels[slot];

            HoverEventForwarder forwarder = _slots[slot]?.HoverForwarder;
            if (forwarder == null) continue;

            int capturedSlot = slot;
            forwarder.HoverEntered += () => OnSlotHovered(capturedSlot);
            forwarder.HoverExited += () => DescriptionTooltip.instance?.Hide();
        }
    }

    private void OnSlotHovered(int slot)
    {
        int abilityId = GetBarAbilityId(slot);
        if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability)) return;

        DescriptionTooltip.instance?.Show(ability.Name, ability.Description);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted)
        {
            SetRootActive(false);
            return;
        }

        SetRootActive(true);

        ulong barEntityId = AbilityBarHelper.FindPlayerAbilityBarEntity(_ecs, LocalPlayerId());
        bool hasBar = barEntityId != 0 && _barStore != null && _barStore.HasComponent(barEntityId);
        AbilityBarComponent bar = hasBar ? _barStore.GetComponent(barEntityId) : default;

        for (int slot = 0; slot < _slots.Length; slot++)
            UpdateSlot(_slots[slot], hasBar ? bar.GetAbilityId(slot) : 0);
    }

    private int GetBarAbilityId(int slot)
    {
        ulong barEntityId = AbilityBarHelper.FindPlayerAbilityBarEntity(_ecs, LocalPlayerId());
        if (barEntityId == 0 || _barStore == null || !_barStore.HasComponent(barEntityId)) return 0;
        return _barStore.GetComponent(barEntityId).GetAbilityId(slot);
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private void UpdateSlot(AbilitySlot uiSlot, int abilityId)
    {
        if (uiSlot?.SlotObject == null) return;

        if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability))
        {
            uiSlot.SlotObject.SetActive(false);
            return;
        }

        uiSlot.SlotObject.SetActive(true);

        if (uiSlot.Icon != null)
            uiSlot.Icon.sprite = ResolveIcon(ability.ImageName);

        bool found = FindBestTroop(abilityId, out ulong troopId, out int troopSlot);

        if (uiSlot.Icon != null)
        {
            Color color = uiSlot.Icon.color;
            color.a = found ? 1f : NoTroopIconAlpha;
            uiSlot.Icon.color = color;
        }

        if (!found)
        {
            SetActiveSafe(uiSlot.CooldownText, false);
            SetOverlay(uiSlot.CooldownOverlay, 0f);
            SetActiveSafe(uiSlot.ChargesText, false);
            return;
        }

        AbilityComponent abilities = _abilityStore.GetComponent(troopId);

        // A charge-based slot (MaxCharges > 1) that's fully depleted needs to show the
        // (much longer) time until its NEXT charge, not the ordinary per-cast cooldown —
        // that one may well have already expired while still stuck at 0 charges, which
        // would otherwise make the overlay/text disappear even though the ability still
        // can't be cast — mirrors the old per-selected-troop AbilityBarUI logic exactly.
        int maxCharges = abilities.GetMaxCharges(troopSlot);
        int ticksRemaining;
        int cooldownTicks;
        if (maxCharges > 1 && abilities.GetChargesRemaining(troopSlot) <= 0)
        {
            ticksRemaining = abilities.GetChargeCooldownTicksRemaining(troopSlot);
            cooldownTicks = abilities.GetChargeCooldownTicks(troopSlot);
        }
        else
        {
            ticksRemaining = abilities.GetCooldownTicksRemaining(troopSlot);
            cooldownTicks = abilities.GetCooldownTicks(troopSlot);
        }

        bool onCooldown = ticksRemaining > 0;

        if (uiSlot.CooldownText != null)
        {
            uiSlot.CooldownText.gameObject.SetActive(onCooldown);
            if (onCooldown)
                uiSlot.CooldownText.text = Mathf.CeilToInt(TickManager.TicksToSeconds(ticksRemaining)).ToString();
        }

        if (uiSlot.CooldownOverlay != null)
        {
            uiSlot.CooldownOverlay.fillAmount = onCooldown && cooldownTicks > 0
                ? (float)ticksRemaining / cooldownTicks
                : 0f;
        }

        if (uiSlot.ChargesText != null)
        {
            bool hasCharges = maxCharges > 1;
            uiSlot.ChargesText.gameObject.SetActive(hasCharges);
            if (hasCharges)
                uiSlot.ChargesText.text = abilities.GetChargesRemaining(troopSlot).ToString();
        }
    }

    // Among every troop the local player owns with abilityId equipped, finds the one that'll
    // be usable soonest (min effective ticks-remaining — same MaxCharges-aware "which timer
    // actually gates the next cast" logic UpdateSlot itself applies once a candidate is
    // found). Returns false (out params left at 0/-1) if the player owns no troop with this
    // ability equipped at all right now.
    private bool FindBestTroop(int abilityId, out ulong bestTroopId, out int bestSlot)
    {
        ulong foundTroopId = 0;
        int foundSlot = -1;
        int bestTicksRemaining = int.MaxValue;

        if (_abilityStore != null && _troopStore != null)
        {
            ushort localPlayerId = LocalPlayerId();

            _abilityStore.ForEach((ulong id) =>
            {
                if (!_troopStore.HasComponent(id) || _troopStore.GetComponent(id).OwnerPlayerId != localPlayerId) return;

                AbilityComponent abilities = _abilityStore.GetComponent(id);
                int slot = abilities.FindSlot(abilityId);
                if (slot < 0) return;

                int ticksRemaining = EffectiveTicksRemaining(abilities, slot);
                if (foundTroopId != 0 && ticksRemaining >= bestTicksRemaining) return;

                foundTroopId = id;
                foundSlot = slot;
                bestTicksRemaining = ticksRemaining;
            });
        }

        bestTroopId = foundTroopId;
        bestSlot = foundSlot;
        return foundTroopId != 0;
    }

    private static int EffectiveTicksRemaining(AbilityComponent abilities, int slot)
    {
        return abilities.GetMaxCharges(slot) > 1 && abilities.GetChargesRemaining(slot) <= 0
            ? abilities.GetChargeCooldownTicksRemaining(slot)
            : abilities.GetCooldownTicksRemaining(slot);
    }

    private static Sprite ResolveIcon(string imageName)
    {
        if (ImageRegistry.instance == null) return null;
        return ImageRegistry.instance.TryGet(imageName, out Sprite sprite) ? sprite : null;
    }

    private void SetRootActive(bool active)
    {
        if (_root != null)
            _root.SetActive(active);
    }

    private static void SetActiveSafe(TMP_Text text, bool active)
    {
        if (text != null) text.gameObject.SetActive(active);
    }

    private static void SetOverlay(Image overlay, float fill)
    {
        if (overlay != null) overlay.fillAmount = fill;
    }
}
