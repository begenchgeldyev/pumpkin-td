using UnityEngine;

// Handles a single tower slot on the map.
// Empty slot  → normal placement.
// Occupied slot + same-tier incoming tower → merge.
public class TowerPlacement : MonoBehaviour
{
    private Tower _occupiedTower;

    public bool  IsOccupied    => _occupiedTower != null;
    public Tower OccupiedTower => _occupiedTower;

    public void RegisterTower(Tower tower)
    {
        _occupiedTower      = tower;
        tower.OccupiedSlot  = this;
    }

    public void ClearSlot()
    {
        if (_occupiedTower != null)
            _occupiedTower.OccupiedSlot = null;
        _occupiedTower = null;
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        Tower incoming = collision.GetComponent<Tower>();
        if (incoming == null || incoming.IsPlaced) return; // ignore already-placed towers

        if (!IsOccupied)
        {
            // Empty slot — offer as placement target
            incoming.SetPlacePosition(transform.position);
        }
        else if (_occupiedTower != incoming && _occupiedTower.Tier == incoming.Tier)
        {
            // Occupied slot with same-tier tower — offer as merge target
            incoming.SetPlacePosition(transform.position);
            TowerMergeTarget mt = incoming.GetComponent<TowerMergeTarget>();
            if (mt == null) mt = incoming.gameObject.AddComponent<TowerMergeTarget>();
            mt.SetMergeSlot(this);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        Tower incoming = collision.GetComponent<Tower>();
        if (incoming == null || incoming.IsPlaced) return;

        incoming.SetPlacePosition(null);
        incoming.GetComponent<TowerMergeTarget>()?.ClearMergeSlot(this);
    }
}
