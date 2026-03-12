using UnityEngine;

// Attached to every Tower (or added at runtime).
// Tracks whether the tower is hovering over a merge-eligible slot while being dragged.
public class TowerMergeTarget : MonoBehaviour
{
    private TowerPlacement _pendingMergeSlot;

    public bool HasMergeTarget => _pendingMergeSlot != null;
    public TowerPlacement MergeSlot => _pendingMergeSlot;

    public void SetMergeSlot(TowerPlacement slot) => _pendingMergeSlot = slot;

    public void ClearMergeSlot(TowerPlacement slot)
    {
        if (_pendingMergeSlot == slot)
            _pendingMergeSlot = null;
    }

    public void Reset() => _pendingMergeSlot = null;
}
