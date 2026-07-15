using System.Collections.Generic;
using UnityEngine;

// Shows a marker on whichever selectable entity is currently under the cursor while a
// TargetEntityCard is selected/being dragged (see CardHandRenderer.ResolveActiveCard),
// previewing which entity the card would be played on. Hidden whenever no such card is
// active, or no matching entity is currently under the cursor. Reuses the same
// EntityTargeting/EntityTargetIndicator infrastructure AbilityIndicatorManager uses for an
// AbilityType.TargetEntity ability's own target preview — see those for why they're
// factored out as reusable.
//
// Separate from the ECS architecture, like CardPlacementIndicatorManager — purely reactive
// to CardHandRenderer's selection state and the live cursor position, no ECS event
// subscriptions needed.
public class CardTargetIndicatorManager : Singleton<CardTargetIndicatorManager>
{
    [SerializeField] private EntityTargetIndicatorPrefab _indicatorPrefab;

    private ECS _ecs;
    private EntityTargetIndicator _indicator;
    private readonly List<ulong> _queryBuffer = new List<ulong>();

    public void Initialize()
    {
        _ecs = TickManager.instance.ActiveECS;
        _indicator = new EntityTargetIndicator(_indicatorPrefab, transform);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) { _indicator?.Hide(); return; }

        TargetEntityCard activeCard = ResolveActiveTargetCard();
        if (activeCard == null || !TileSpaceMouse.TryGetPosition(out float x, out float y))
        {
            _indicator?.Hide();
            return;
        }

        ulong targetId = EntityTargeting.FindClosestSelectable(
            _ecs, x, y, activeCard.TargetSelectionRadius, LocalPlayerId(),
            activeCard.CanTargetFriendly, activeCard.CanTargetEnemyOrNeutral, _queryBuffer);
        if (targetId == 0)
        {
            _indicator?.Hide();
            return;
        }

        ComponentStore<PositionComponent> posStore = _ecs.GetComponentStore<PositionComponent>();
        if (posStore == null || !posStore.HasComponent(targetId))
        {
            _indicator?.Hide();
            return;
        }

        PositionComponent pos = posStore.GetComponent(targetId);
        _indicator.Show(WorldPositionForXY(pos.X, pos.Y));
    }

    private TargetEntityCard ResolveActiveTargetCard()
    {
        if (CardHandRenderer.instance == null) return null;
        return CardHandRenderer.instance.ResolveActiveCard() as TargetEntityCard;
    }

    private static ushort LocalPlayerId()
        => NetworkManager.instance != null ? NetworkManager.instance.LocalPlayerId : (ushort)0;

    private static Vector3 WorldPositionForXY(float x, float y)
    {
        float height = WorldManager.instance.Handler.GetHeight((ushort)x, (ushort)y);
        return new Vector3(x, height + 0.01f, y);
    }
}
