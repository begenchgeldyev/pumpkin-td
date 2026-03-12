using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Represents the "Build Tower" button in the UI.
// On drag: spends gold and instantiates a random tier-1 tower from LevelManager's pool.
// Supports Merge: when the dragged tower lands on a same-tier occupied slot, a merge fires.
public class TowerUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Text _costLabel;   // Optional — shows build cost
    [SerializeField] private Image _icon;        // Optional — generic build icon

    private Tower _currentSpawnedTower;

    // Called once by LevelManager.InstantiateAllTowerUI — sets up cost display
    public void Initialise(int cost, Sprite icon = null)
    {
        if (_costLabel != null) _costLabel.text = $"{cost}g";
        if (_icon != null && icon != null) _icon.sprite = icon;
    }

    // -------------------------------------------------------------------------
    // Drag handlers
    // -------------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        Tower prefab = LevelManager.Instance.GetRandomTier1Prefab();
        if (prefab == null) return;

        // Check gold before spawning
        if (!LevelManager.Instance.HasFreeBuild && !LevelManager.Instance.CanAfford(prefab.GoldCost))
        {
            Debug.Log("Not enough gold to build.");
            return;
        }

        GameObject obj = Instantiate(prefab.gameObject);
        // Ensure TowerMergeTarget component exists on dragged tower
        if (obj.GetComponent<TowerMergeTarget>() == null)
            obj.AddComponent<TowerMergeTarget>();

        _currentSpawnedTower = obj.GetComponent<Tower>();
        _currentSpawnedTower.ToggleOrderInLayer(true);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_currentSpawnedTower == null) return;

        Camera cam = Camera.main;
        Vector3 mouse = Input.mousePosition;
        mouse.z = -cam.transform.position.z;
        _currentSpawnedTower.transform.position = cam.ScreenToWorldPoint(mouse);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_currentSpawnedTower == null) return;

        TowerMergeTarget mergeTarget = _currentSpawnedTower.GetComponent<TowerMergeTarget>();
        bool wantsMerge = mergeTarget != null && mergeTarget.HasMergeTarget;

        if (wantsMerge)
        {
            // Merge: combine two same-tier towers into a random next-tier tower
            LevelManager.Instance.MergeTowers(
                _currentSpawnedTower,
                mergeTarget.MergeSlot);
            Destroy(_currentSpawnedTower.gameObject);
        }
        else if (_currentSpawnedTower.PlacePosition != null)
        {
            // Normal placement
            bool free = LevelManager.Instance.HasFreeBuild;
            if (free || LevelManager.Instance.SpendGold(_currentSpawnedTower.GoldCost))
            {
                if (free) LevelManager.Instance.UseFreeBuild();

                _currentSpawnedTower.LockPlacement();
                _currentSpawnedTower.ToggleOrderInLayer(false);
                _currentSpawnedTower.ApplyRandomModifier();
                LevelManager.Instance.RegisterSpawnedTower(_currentSpawnedTower);
            }
            else
            {
                // Can't afford — cancel
                Destroy(_currentSpawnedTower.gameObject);
            }
        }
        else
        {
            // Invalid placement — cancel
            Destroy(_currentSpawnedTower.gameObject);
        }

        _currentSpawnedTower = null;
    }
}
