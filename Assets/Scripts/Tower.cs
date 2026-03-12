using System.Collections.Generic;
using UnityEngine;

public class Tower : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private SpriteRenderer _towerPlace;
    [SerializeField] private SpriteRenderer _towerHead;

    [Header("Base Stats")]
    [SerializeField] private int _shootPower = 1;
    [SerializeField] private float _shootDistance = 5f;
    [SerializeField] private float _shootDelay = 1f;
    [SerializeField] private float _bulletSpeed = 4f;
    [SerializeField] private float _bulletSplashRadius = 0f;
    [SerializeField] private Bullet _bulletPrefab;

    [Header("Tower Identity")]
    [SerializeField] public TowerTier Tier = TowerTier.Tier1;
    [SerializeField] public TowerRole Role = TowerRole.Assault;
    [SerializeField] public AttackType AttackType = AttackType.Magic;
    // Cost used when purchased; also base for sell calculation
    [SerializeField] public int GoldCost = 50;

    // Random modifier applied after placement
    public TowerModifierType Modifier { get; private set; } = TowerModifierType.None;

    // Modifier-derived bonuses
    private float _modifierDmgMult   = 1f;
    private float _modifierSpeedMult = 1f;
    private float _modifierRangeBonus = 0f;
    private float _critChance        = 0f;
    private float _slowChance        = 0f;
    private float _vsHeavyMult       = 1f;

    // Session (in-run) bonuses applied via wave buffs
    private float _sessionDmgMult   = 1f;
    private float _sessionSpeedMult = 1f;

    private float _runningShootDelay;
    private Enemy _targetEnemy;
    private Quaternion _targetRotation;

    public Vector2? PlacePosition { get; private set; }
    public bool     IsPlaced      { get; private set; }
    // The slot this tower occupies (set by TowerPlacement)
    public TowerPlacement OccupiedSlot { get; set; }

    // -------------------------------------------------------------------------
    // Modifier
    // -------------------------------------------------------------------------

    public void ApplyRandomModifier()
    {
        // 50% chance to receive a modifier
        if (Random.value < 0.5f) return;

        var values = System.Enum.GetValues(typeof(TowerModifierType));
        Modifier = (TowerModifierType)values.GetValue(Random.Range(1, values.Length));

        switch (Modifier)
        {
            case TowerModifierType.BonusDamage:      _modifierDmgMult   = 1.15f; break;
            case TowerModifierType.BonusAttackSpeed: _modifierSpeedMult = 1.20f; break;
            case TowerModifierType.BonusRange:       _modifierRangeBonus = 1f;   break;
            case TowerModifierType.CritChance:       _critChance        = 0.10f; break;
            case TowerModifierType.SlowChance:       _slowChance        = 0.15f; break;
            case TowerModifierType.BonusVsHeavy:     _vsHeavyMult       = 1.25f; break;
        }
    }

    // -------------------------------------------------------------------------
    // Session buffs (wave rewards)
    // -------------------------------------------------------------------------

    public void ApplySessionBuff(WaveBuffType buff, TowerRole? roleFilter = null)
    {
        switch (buff)
        {
            case WaveBuffType.DamageBonus:
                if (roleFilter == null || roleFilter == Role)
                    _sessionDmgMult += 0.10f;
                break;
            case WaveBuffType.AttackSpeedBonus:
                if ((int)Tier <= 2)
                    _sessionSpeedMult += 0.15f;
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Economy
    // -------------------------------------------------------------------------

    // Returns 60% of total invested gold
    public int GetSellValue() => Mathf.RoundToInt(GoldCost * 0.6f);

    // -------------------------------------------------------------------------
    // Placement
    // -------------------------------------------------------------------------

    public Sprite GetTowerHeadIcon() => _towerHead.sprite;

    public void SetPlacePosition(Vector2? pos) => PlacePosition = pos;

    public void LockPlacement()
    {
        transform.position = (Vector2)PlacePosition;
        IsPlaced = true;
    }

    public void ToggleOrderInLayer(bool toFront)
    {
        int order = toFront ? 2 : 0;
        _towerPlace.sortingOrder = order;
        _towerHead.sortingOrder  = order;
    }

    // -------------------------------------------------------------------------
    // Targeting
    // -------------------------------------------------------------------------

    public void CheckNearestEnemy(List<Enemy> enemies)
    {
        float range = _shootDistance + _modifierRangeBonus;

        if (_targetEnemy != null)
        {
            bool stillValid = _targetEnemy.gameObject.activeSelf
                && Vector3.Distance(transform.position, _targetEnemy.transform.position) <= range;
            if (stillValid) return;
            _targetEnemy = null;
        }

        float nearestDist = Mathf.Infinity;
        Enemy nearest = null;

        foreach (Enemy e in enemies)
        {
            if (!e.gameObject.activeSelf) continue;
            float d = Vector3.Distance(transform.position, e.transform.position);
            if (d <= range && d < nearestDist) { nearestDist = d; nearest = e; }
        }

        _targetEnemy = nearest;
    }

    public void SeekTarget()
    {
        if (_targetEnemy == null) return;
        Vector3 dir = _targetEnemy.transform.position - transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        _targetRotation = Quaternion.Euler(0f, 0f, angle - 90f);
        _towerHead.transform.rotation = Quaternion.RotateTowards(
            _towerHead.transform.rotation, _targetRotation, Time.deltaTime * 180f);
    }

    public void ShootTarget()
    {
        if (_targetEnemy == null) return;

        float effectiveDelay = _shootDelay / (_modifierSpeedMult * _sessionSpeedMult);
        _runningShootDelay -= Time.unscaledDeltaTime;
        if (_runningShootDelay > 0f) return;

        bool aimed = Mathf.Abs(
            _towerHead.transform.rotation.eulerAngles.z - _targetRotation.eulerAngles.z) < 10f;
        if (!aimed) return;

        float dmgMult = _modifierDmgMult * _sessionDmgMult;
        if (_targetEnemy.ArmorType == ArmorType.Heavy) dmgMult *= _vsHeavyMult;

        int dmg = Mathf.RoundToInt(_shootPower * dmgMult);
        if (_critChance > 0 && Random.value < _critChance) dmg *= 2;

        Bullet bullet = LevelManager.Instance.GetBulletFromPool(_bulletPrefab);
        bullet.transform.position = transform.position;
        bullet.SetProperties(dmg, _bulletSpeed, _bulletSplashRadius, AttackType, _slowChance);
        bullet.SetTargetEnemy(_targetEnemy);
        bullet.gameObject.SetActive(true);

        _runningShootDelay = effectiveDelay;
    }

    // -------------------------------------------------------------------------
    // Right-click to sell
    // -------------------------------------------------------------------------

    private void Update()
    {
        // Right-click sell only works on fully placed towers
        if (!IsPlaced) return;
        if (!Input.GetMouseButtonDown(1)) return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        RaycastHit2D hit = Physics2D.Raycast(ray.origin, ray.direction);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
        {
            LevelManager.Instance.SellTower(this);
        }
    }
}
