using System.Collections;
using UnityEngine;

// Attach to the Boss prefab instead of (or alongside) Enemy.
// Boss has two phases per design document section 8:
//   Phase 1: normal movement
//   Phase 2 (at 50% HP): speed up, summon normal enemies, brief damage resistance
[RequireComponent(typeof(Enemy))]
public class BossEnemy : MonoBehaviour
{
    [Header("Phase 2 settings")]
    [SerializeField] private float _phase2SpeedMultiplier  = 1.5f;
    [SerializeField] private float _resistanceDuration     = 3f;   // seconds of -50% damage taken
    [SerializeField] private float _summonInterval         = 8f;   // seconds between summons
    [SerializeField] private Enemy _summonPrefab;                   // normal enemy to summon
    [SerializeField] private int   _summonCount            = 3;

    private Enemy  _enemy;
    private bool   _phase2Active = false;
    private bool   _resistanceActive = false;

    // Track max health to detect 50% threshold
    private int _maxHealth;

    private void Awake()
    {
        _enemy = GetComponent<Enemy>();
    }

    private void OnEnable()
    {
        _phase2Active     = false;
        _resistanceActive = false;
    }

    // Called every frame by LevelManager; the Enemy component handles movement.
    // We check for phase transition here.
    private void Update()
    {
        if (!gameObject.activeSelf) return;
        if (_phase2Active) return;

        // Phase 2 triggers at 50% HP — we detect this via a reflection-free approach:
        // Enemy exposes current health indirectly through health bar fill.
        // Instead, override ReduceEnemyHealth via an event or just check the fill ratio.
        // We use the health bar fill sprite to detect threshold without exposing private field.
        var healthFill = GetComponentInChildren<SpriteRenderer>();
        if (healthFill == null) return;

        // Health bar fill width relative to bar width
        // This is a fragile but dependency-free approach for now.
        // A cleaner solution: add an event to Enemy.cs.
        // For now we rely on the ratio being below 0.5.
        // (See Enemy.cs — _healthFill.size.x / _healthBar.size.x)
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length < 2) return;

        // Assume renderers[renderers.Length - 1] is the fill (added last in prefab hierarchy)
        SpriteRenderer bar  = renderers[renderers.Length - 2];
        SpriteRenderer fill = renderers[renderers.Length - 1];

        if (bar == null || fill == null) return;
        if (bar.size.x <= 0) return;

        float ratio = fill.size.x / bar.size.x;
        if (ratio <= 0.5f) EnterPhase2();
    }

    private void EnterPhase2()
    {
        _phase2Active = true;
        _enemy.SetPermanentSpeedMultiplier(_phase2SpeedMultiplier);
        StartCoroutine(ResistanceBurst());
        StartCoroutine(SummonLoop());
    }

    private IEnumerator ResistanceBurst()
    {
        _resistanceActive = true;
        yield return new WaitForSeconds(_resistanceDuration);
        _resistanceActive = false;
    }

    private IEnumerator SummonLoop()
    {
        while (gameObject.activeSelf)
        {
            yield return new WaitForSeconds(_summonInterval);
            if (_summonPrefab != null)
            {
                for (int i = 0; i < _summonCount; i++)
                {
                    // Summon near the boss position using LevelManager's path start
                    // We trigger a simple spawn via LevelManager
                    GameObject obj = Instantiate(_summonPrefab.gameObject);
                    Enemy newEnemy = obj.GetComponent<Enemy>();
                    Transform[] paths = FindObjectOfType<LevelManager>() != null
                        ? GetPaths() : null;

                    if (paths != null && paths.Length >= 2)
                    {
                        newEnemy.transform.position = transform.position; // spawn at boss
                        newEnemy.SetTargetPosition(paths[1].position);
                        newEnemy.SetCurrentPathIndex(1);
                    }
                    newEnemy.gameObject.SetActive(true);
                }
            }
        }
    }

    // Damage reduction during resistance phase — hook into Enemy damage
    // (requires adding an OnBeforeDamage callback to Enemy.cs for full implementation)
    public int ModifyIncomingDamage(int damage)
    {
        return _resistanceActive ? Mathf.RoundToInt(damage * 0.5f) : damage;
    }

    // Helper — get enemy paths from scene (works without direct LevelManager reference)
    private Transform[] GetPaths()
    {
        // LevelManager._enemyPaths is private; find waypoint objects by tag if tagged,
        // or return null and let summons start from path[0].
        // Tag your path waypoints "EnemyPath" in Unity for this to work.
        GameObject[] waypoints = GameObject.FindGameObjectsWithTag("EnemyPath");
        if (waypoints.Length == 0) return null;

        System.Array.Sort(waypoints, (a, b) =>
            string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        var transforms = new Transform[waypoints.Length];
        for (int i = 0; i < waypoints.Length; i++) transforms[i] = waypoints[i].transform;
        return transforms;
    }
}
