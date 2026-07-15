using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the up-to-4 equipped abilities (Q/W/E/R — see AbilityComponent/AbilityInputManager)
// of the single currently-selected friendly troop. _root (the screen-space object with the
// 4 slot children) is disabled whenever zero or more than one entity is selected, or the
// selected entity has no AbilityComponent at all; otherwise each of the 4 slot GameObjects
// is independently enabled only for a slot that's actually equipped
// (AbilityComponent.GetAbilityId(slot) != 0), with its icon set from the equipped
// ability's ImageName (via ImageRegistry) and its cooldown text shown only while that slot
// is on cooldown.
//
// _root must be a different object than the one this script lives on — disabling the
// script's own GameObject would stop Update from running, and nothing would ever be able
// to turn it back on. Purely reactive: polls SelectionManager/the live ECS every frame,
// like AbilityInputManager, rather than subscribing to any ECS event (there's no "ability
// equipped" or "cooldown changed" flag event today).
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
    }

    [SerializeField] private GameObject _root;
    [SerializeField] private AbilitySlot[] _slots = new AbilitySlot[4];

    private ECS _ecs;
    private ComponentStore<AbilityComponent> _abilityStore;

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _abilityStore = _ecs.GetComponentStore<AbilityComponent>();

        SetRootActive(false);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        ulong casterId = GetOnlySelected();
        if (casterId == 0 || _abilityStore == null || !_abilityStore.HasComponent(casterId))
        {
            SetRootActive(false);
            return;
        }

        SetRootActive(true);
        AbilityComponent abilities = _abilityStore.GetComponent(casterId);

        for (int slot = 0; slot < _slots.Length; slot++)
            UpdateSlot(_slots[slot], abilities, slot);
    }

    private ulong GetOnlySelected()
    {
        if (SelectionManager.instance == null || SelectionManager.instance.SelectedEntityIds.Count != 1) return 0;
        foreach (ulong id in SelectionManager.instance.SelectedEntityIds)
            return id;
        return 0;
    }

    private void UpdateSlot(AbilitySlot uiSlot, AbilityComponent abilities, int slot)
    {
        if (uiSlot?.SlotObject == null) return;

        int abilityId = abilities.GetAbilityId(slot);
        if (abilityId == 0 || !AbilityManager.TryGet(abilityId, out Ability ability))
        {
            uiSlot.SlotObject.SetActive(false);
            return;
        }

        uiSlot.SlotObject.SetActive(true);

        if (uiSlot.Icon != null)
            uiSlot.Icon.sprite = ResolveIcon(ability.ImageName);

        int ticksRemaining = abilities.GetCooldownTicksRemaining(slot);
        bool onCooldown = ticksRemaining > 0;

        if (uiSlot.CooldownText != null)
        {
            uiSlot.CooldownText.gameObject.SetActive(onCooldown);
            if (onCooldown)
                uiSlot.CooldownText.text = Mathf.CeilToInt(TickManager.TicksToSeconds(ticksRemaining)).ToString();
        }

        if (uiSlot.CooldownOverlay != null)
        {
            int cooldownTicks = abilities.GetCooldownTicks(slot);
            uiSlot.CooldownOverlay.fillAmount = onCooldown && cooldownTicks > 0
                ? (float)ticksRemaining / cooldownTicks
                : 0f;
        }
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
}
