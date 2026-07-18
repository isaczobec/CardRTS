using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows/hides _panel and colors _passiveImage/_guardImage/_aggressiveImage based on the
/// AIModeComponent.Mode of the currently selected entities (SelectionManager.SelectedEntityIds)
/// that actually have one — buildings don't, so a selection of only buildings hides the
/// panel entirely. If every selected troop with an AIModeComponent shares the same mode,
/// that mode's image turns _singleModeColor and the other two _inactiveColor; if the
/// selection mixes modes, every mode actually present turns _mixedModeColor instead (still
/// _inactiveColor for any mode not present at all).
///
/// Pressing S/D/F sends a SetAIModeInput (Passive/Guard/Aggressive respectively) for the
/// whole current selection — SetAIModeSystem silently skips any selected entity without an
/// AIModeComponent (buildings) or not owned by the local player, so this never needs to
/// pre-filter the selection itself, same as SelectionManager's own SendSetTargets/
/// SendMoveCommand.
/// </summary>
public class AIModeUI : Singleton<AIModeUI>
{
    [SerializeField] private GameObject _panel;
    [SerializeField] private Image _passiveImage;
    [SerializeField] private Image _guardImage;
    [SerializeField] private Image _aggressiveImage;

    [Header("Colors")]
    [SerializeField] private Color _singleModeColor = Color.green;
    [SerializeField] private Color _mixedModeColor = Color.blue;
    [SerializeField] private Color _inactiveColor = Color.gray;

    private ComponentStore<AIModeComponent> _aiModeStore;

    // Scratch set reused every frame — which modes are currently present among selected
    // entities that have an AIModeComponent.
    private readonly HashSet<AIMode> _presentModes = new HashSet<AIMode>();

    public void Initialize()
    {
        _aiModeStore = TickManager.instance.ActiveECS.GetComponentStore<AIModeComponent>();

        if (_panel != null)
            _panel.SetActive(false);
    }

    void Update()
    {
        if (TickManager.instance == null || !TickManager.instance.IsGameStarted) return;

        RefreshPanel();
        HandleModeHotkeys();
    }

    private void RefreshPanel()
    {
        _presentModes.Clear();

        if (SelectionManager.instance != null && _aiModeStore != null)
        {
            foreach (ulong entityId in SelectionManager.instance.SelectedEntityIds)
            {
                if (!_aiModeStore.HasComponent(entityId)) continue;
                _presentModes.Add(_aiModeStore.GetComponent(entityId).Mode);
            }
        }

        if (_panel != null)
            _panel.SetActive(_presentModes.Count > 0);

        if (_presentModes.Count == 0) return;

        Color activeColor = _presentModes.Count == 1 ? _singleModeColor : _mixedModeColor;

        SetImageColor(_passiveImage, _presentModes.Contains(AIMode.Passive) ? activeColor : _inactiveColor);
        SetImageColor(_guardImage, _presentModes.Contains(AIMode.Guard) ? activeColor : _inactiveColor);
        SetImageColor(_aggressiveImage, _presentModes.Contains(AIMode.Aggressive) ? activeColor : _inactiveColor);
    }

    private static void SetImageColor(Image image, Color color)
    {
        if (image != null) image.color = color;
    }

    private void HandleModeHotkeys()
    {
        if (DevConsole.IsOpen) return;
        if (SelectionManager.instance == null || SelectionManager.instance.SelectedEntityIds.Count == 0) return;

        if (Input.GetKeyDown(KeyCode.S)) SendSetMode(AIMode.Passive);
        else if (Input.GetKeyDown(KeyCode.D)) SendSetMode(AIMode.Guard);
        else if (Input.GetKeyDown(KeyCode.F)) SendSetMode(AIMode.Aggressive);
    }

    private void SendSetMode(AIMode mode)
    {
        InputBuffer.EnqueueInput(new SetAIModeInput
        {
            Mode = mode,
            EntityIds = new List<ulong>(SelectionManager.instance.SelectedEntityIds),
        });
    }
}
