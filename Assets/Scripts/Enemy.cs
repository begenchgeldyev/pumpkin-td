using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("Base Stats")]
    [SerializeField] private int   _maxHealth = 1;
    [SerializeField] private float _moveSpeed = 1f;
    [SerializeField] private SpriteRenderer _healthBar;
    [SerializeField] private SpriteRenderer _healthFill;

    [Header("Enemy Identity")]
    [SerializeField] public EnemyType EnemyType   = EnemyType.Normal;
    [SerializeField] public ArmorType ArmorType    = ArmorType.None;
    [SerializeField] public int       GoldReward   = 10;
    // Lives deducted from player's base when this enemy reaches the end
    [SerializeField] public int       LivesDamage  = 1;

    private int   _currentHealth;
    private float _baseSpeed;

    // Slow state
    private float _slowTimer = 0f;
    private bool  _isSlowed  = false;

    public Vector3 TargetPosition   { get; private set; }
    public int     CurrentPathIndex { get; private set; }

    // -------------------------------------------------------------------------
    // Unity callbacks
    // -------------------------------------------------------------------------

    private void OnEnable()
    {
        _currentHealth = _maxHealth;
        _healthFill.size = _healthBar.size;
        _baseSpeed = _moveSpeed;
        _slowTimer = 0f;
        _isSlowed  = false;
    }

    private void Update()
    {
        if (_isSlowed)
        {
            _slowTimer -= Time.deltaTime;
            if (_slowTimer <= 0f)
            {
                _moveSpeed = _baseSpeed;
                _isSlowed  = false;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Difficulty scaling — called by LevelManager before the wave starts
    // -------------------------------------------------------------------------

    public void ApplyDifficultyScale(float healthScale, float speedScale)
    {
        _maxHealth  = Mathf.Max(1, Mathf.RoundToInt(_maxHealth * healthScale));
        _moveSpeed *= speedScale;
        _baseSpeed  = _moveSpeed;
    }

    // -------------------------------------------------------------------------
    // Effects
    // -------------------------------------------------------------------------

    // slowFactor = fraction of base speed (0.5 = half speed)
    public void ApplySlow(float duration, float slowFactor = 0.5f)
    {
        _moveSpeed = _baseSpeed * slowFactor;
        _slowTimer = duration;
        _isSlowed  = true;
    }

    // Permanent speed multiplier (for boss phase 2 speed boost, factor > 1)
    public void SetPermanentSpeedMultiplier(float mult)
    {
        _baseSpeed = _baseSpeed * mult;
        _moveSpeed = _baseSpeed;
    }

    // Support enemies buff nearby enemies — implemented in LevelManager update loop
    public bool IsSupport => EnemyType == EnemyType.Support;

    // -------------------------------------------------------------------------
    // Movement
    // -------------------------------------------------------------------------

    public void MoveToTarget()
    {
        transform.position = Vector3.MoveTowards(
            transform.position, TargetPosition, _moveSpeed * Time.deltaTime);
    }

    public void SetTargetPosition(Vector3 targetPosition)
    {
        TargetPosition = targetPosition;
        _healthBar.transform.parent = null;

        Vector3 delta = TargetPosition - transform.position;
        /*if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
            transform.rotation = Quaternion.Euler(0f, 0f, delta.y > 0f ? 90f : -90f);
        else
            transform.rotation = Quaternion.Euler(0f, 0f, delta.x > 0f ? 0f : 180f);
*/
        _healthBar.transform.parent = transform;
    }

    public void SetCurrentPathIndex(int index) => CurrentPathIndex = index;

    // -------------------------------------------------------------------------
    // Damage
    // -------------------------------------------------------------------------

    public void ReduceEnemyHealth(int damage, AttackType attackType = AttackType.Magic)
    {
        float mult = GetArmorMultiplier(attackType);
        int finalDmg = Mathf.Max(1, Mathf.RoundToInt(damage * mult));

        _currentHealth -= finalDmg;
        AudioPlayer.Instance.PlaySFX("hit-enemy");

        LevelManager.Instance.RegisterMagicKill(attackType == AttackType.Magic && _currentHealth <= 0);

        if (_currentHealth <= 0)
        {
            _currentHealth = 0;
            gameObject.SetActive(false);
            AudioPlayer.Instance.PlaySFX("enemy-die");
            LevelManager.Instance.AddGold(GoldReward);
            LevelManager.Instance.OnEnemyKilled(this);
        }

        float pct = (float)_currentHealth / _maxHealth;
        _healthFill.size = new Vector2(pct * _healthBar.size.x, _healthBar.size.y);
    }

    // -------------------------------------------------------------------------
    // Armor vs attack type table (design doc section 5.3)
    //   Magic:   +25% vs Medium, -25% vs Heavy
    //   Siege:   +25% vs Heavy,  -25% vs Light
    //   Piercing:+25% vs Light,  -25% vs Medium
    // -------------------------------------------------------------------------
    private float GetArmorMultiplier(AttackType at)
    {
        switch (at)
        {
            case AttackType.Magic:
                if (ArmorType == ArmorType.Medium) return 1.25f;
                if (ArmorType == ArmorType.Heavy)  return 0.75f;
                return 1f;
            case AttackType.Siege:
                if (ArmorType == ArmorType.Heavy)  return 1.25f;
                if (ArmorType == ArmorType.Light)  return 0.75f;
                return 1f;
            case AttackType.Piercing:
                if (ArmorType == ArmorType.Light)  return 1.25f;
                if (ArmorType == ArmorType.Medium) return 0.75f;
                return 1f;
            default:
                return 1f;
        }
    }
}
