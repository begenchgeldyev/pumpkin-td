using UnityEngine;

public class Bullet : MonoBehaviour
{
    private int        _bulletPower;
    private float      _bulletSpeed;
    private float      _bulletSplashRadius;
    private AttackType _attackType;
    private float      _slowChance;

    private Enemy _targetEnemy;

    private void FixedUpdate()
    {
        if (LevelManager.Instance.IsOver) return;
        if (_targetEnemy == null) return;

        if (!_targetEnemy.gameObject.activeSelf)
        {
            _targetEnemy = null;
            gameObject.SetActive(false);
            return;
        }

        // Check distance BEFORE moving so bullet stops before overlapping enemy sprite
        float dist = Vector2.Distance(transform.position, _targetEnemy.transform.position);
        if (dist < 0.3f)
        {
            ApplyHit();
            return;
        }

        transform.position = Vector3.MoveTowards(
            transform.position, _targetEnemy.transform.position, _bulletSpeed * Time.fixedDeltaTime);

        Vector3 dir = _targetEnemy.transform.position - transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    // Trigger kept as backup for slow-moving bullets
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (_targetEnemy == null) return;
        if (collision.gameObject.Equals(_targetEnemy.gameObject))
            ApplyHit();
    }

    private void ApplyHit()
    {
        if (_targetEnemy == null) return;

        // Clear target first to prevent re-entry on same frame
        Enemy target = _targetEnemy;
        _targetEnemy = null;
        Vector2 hitPos = transform.position;
        gameObject.SetActive(false);

        if (_bulletSplashRadius > 0f)
        {
            LevelManager.Instance.ExplodeAt(hitPos, _bulletSplashRadius, _bulletPower, _attackType);
        }
        else
        {
            target.ReduceEnemyHealth(_bulletPower, _attackType);
            if (_slowChance > 0f && Random.value < _slowChance)
                target.ApplySlow(2f);
        }
    }

    public void SetProperties(int power, float speed, float splashRadius,
        AttackType attackType = AttackType.Magic, float slowChance = 0f)
    {
        _bulletPower        = power;
        _bulletSpeed        = speed;
        _bulletSplashRadius = splashRadius;
        _attackType         = attackType;
        _slowChance         = slowChance;
    }

    public void SetTargetEnemy(Enemy enemy) => _targetEnemy = enemy;
}
