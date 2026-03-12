using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// Wave data classes (serializable for Inspector configuration)
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class WaveEnemyEntry
{
    public Enemy  EnemyPrefab;
    public int    Count         = 5;
    public float  SpawnInterval = 1f;
}

[System.Serializable]
public class WaveQuest
{
    public QuestType Type;
    public int       Target;      // e.g. 10 magic kills
    public int       GoldReward  = 30;
    public bool      HasQuest    = false;
}

[System.Serializable]
public class WaveDefinition
{
    public WaveEnemyEntry[] Enemies;
    public bool             ShowBuffAfter  = false;  // open buff selection after this wave
    public WaveQuest        Quest;                   // optional side quest
}

// ─────────────────────────────────────────────────────────────────────────────
// LevelManager
// ─────────────────────────────────────────────────────────────────────────────

public class LevelManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    private static LevelManager _instance;
    public  static LevelManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindObjectOfType<LevelManager>();
            return _instance;
        }
    }

    // ── Tower UI ───────────────────────────────────────────────────────────────
    [Header("Tower UI")]
    [SerializeField] private Transform  _towerUIParent;
    [SerializeField] private GameObject _towerUIPrefab;

    // Pool of tier-1 tower prefabs; higher tiers are only obtained via merging.
    // Named _towerPrefabs for backward-compat with existing scenes.
    [SerializeField] private Tower[] _towerPrefabs;
    private Tower[] _tier1TowerPrefabs => _towerPrefabs;
    // Per-tier pools for merge results
    [SerializeField] private Tower[] _tier2TowerPrefabs;
    [SerializeField] private Tower[] _tier3TowerPrefabs;

    private List<Tower>         _spawnedTowers  = new List<Tower>();
    private List<TowerPlacement> _allSlots       = new List<TowerPlacement>();

    // ── Enemies ────────────────────────────────────────────────────────────────
    [Header("Enemy Settings")]
    // Fallback enemy pool used when _waves is not configured (backward-compatible)
    [SerializeField] private Enemy[]     _enemyPrefabs;
    [SerializeField] private int         _totalEnemy  = 15;
    [SerializeField] private float       _spawnDelay  = 2f;
    [SerializeField] private Transform[] _enemyPaths;

    private List<Enemy>  _spawnedEnemies  = new List<Enemy>();
    private List<Bullet> _spawnedBullets  = new List<Bullet>();

    // ── Waves ──────────────────────────────────────────────────────────────────
    [Header("Waves")]
    [SerializeField] private WaveDefinition[] _waves;
    [SerializeField] private float            _timeBetweenWaves = 5f;

    private int  _currentWaveIndex      = -1;
    private bool _waveInProgress        = false;
    private int  _enemiesAliveInWave    = 0;
    private int  _enemiesSpawnedInWave  = 0;

    // Fallback (old-style) spawning state
    private float _runningSpawnDelay;
    private int   _enemyCounter;
    private bool  _useFallbackSpawning;

    // ── Buff selection UI ──────────────────────────────────────────────────────
    [Header("Buff UI (optional)")]
    [SerializeField] private GameObject _buffPanel;
    [SerializeField] private Button[]   _buffButtons;  // expects 3 buttons
    [SerializeField] private Text[]     _buffLabels;   // expects 3 text labels

    private WaveBuffType[] _currentBuffOptions;

    // ── Economy ────────────────────────────────────────────────────────────────
    [Header("Economy")]
    [SerializeField] private int  _startGold = 100;
    [SerializeField] private Text _goldInfo;

    private int _gold;
    private bool _freeBuild  = false;
    private bool _freeMerge  = false;
    private float _goldBonusMultiplier = 1f;

    public bool HasFreeBuild => _freeBuild;
    public bool HasFreeMerge => _freeMerge;

    // ── Lives ──────────────────────────────────────────────────────────────────
    [Header("Lives")]
    [SerializeField] private int  _maxLives = 10;
    [SerializeField] private Text _livesInfo;

    private int _currentLives;

    // ── Difficulty ─────────────────────────────────────────────────────────────
    [Header("Difficulty")]
    [SerializeField] private Difficulty _difficulty = Difficulty.Normal;

    private float _enemyHealthScale;
    private float _enemySpeedScale;

    // ── UI ─────────────────────────────────────────────────────────────────────
    [Header("UI")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Text       _statusInfo;
    [SerializeField] private Text       _waveInfo;

    public bool IsOver { get; private set; }

    // ── Quest tracking ─────────────────────────────────────────────────────────
    private int  _magicKillsThisWave  = 0;
    private bool _livesLostThisWave   = false;
    private int  _mergesThisWave      = 0;
    private bool _eliteKilledThisWave = false;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        ApplyDifficultySettings();
        SetCurrentLives(_maxLives);
        SetGold(_startGold);
        InstantiateAllTowerUI();
        CacheAllSlots();

        // If no waves are configured in Inspector, fall back to the old
        // continuous enemy spawning so the scene works out of the box.
        bool hasWaves = _waves != null && _waves.Length > 0;
        _useFallbackSpawning = !hasWaves;

        if (_useFallbackSpawning)
        {
            _enemyCounter      = _totalEnemy;
            _runningSpawnDelay = _spawnDelay;
            UpdateWaveUI();   // shows "Волна: —"
        }
        else
        {
            StartCoroutine(RunWaves());
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);

        if (IsOver) return;

        // ── Fallback continuous spawning (when _waves not configured) ──────────
        if (_useFallbackSpawning)
        {
            _runningSpawnDelay -= Time.unscaledDeltaTime;
            if (_runningSpawnDelay <= 0f)
            {
                SpawnFallbackEnemy();
                _runningSpawnDelay = _spawnDelay;
            }
        }

        // Tower logic
        foreach (Tower tower in _spawnedTowers)
        {
            tower.CheckNearestEnemy(_spawnedEnemies);
            tower.SeekTarget();
            tower.ShootTarget();
        }

        // Enemy movement + path logic
        foreach (Enemy enemy in _spawnedEnemies)
        {
            if (!enemy.gameObject.activeSelf) continue;

            if (Vector2.Distance(enemy.transform.position, enemy.TargetPosition) < 0.1f)
            {
                int next = enemy.CurrentPathIndex + 1;
                enemy.SetCurrentPathIndex(next);

                if (next < _enemyPaths.Length)
                    enemy.SetTargetPosition(_enemyPaths[next].position);
                else
                    ReachBase(enemy);
            }
            else
            {
                enemy.MoveToTarget();
            }
        }
    }

    private void OnDrawGizmos()
    {
        for (int i = 0; i < _enemyPaths.Length - 1; i++)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(_enemyPaths[i].position, _enemyPaths[i + 1].position);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Difficulty
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyDifficultySettings()
    {
        switch (_difficulty)
        {
            case Difficulty.Easy:
                _startGold        = 150;
                _enemyHealthScale = 0.8f;
                _enemySpeedScale  = 0.9f;
                break;
            case Difficulty.Normal:
                _startGold        = 100;
                _enemyHealthScale = 1f;
                _enemySpeedScale  = 1f;
                break;
            case Difficulty.Hard:
                _startGold        = 75;
                _enemyHealthScale = 1.3f;
                _enemySpeedScale  = 1.2f;
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Wave system
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator RunWaves()
    {
        yield return new WaitForSeconds(2f); // initial delay

        for (int i = 0; i < _waves.Length; i++)
        {
            _currentWaveIndex = i;
            UpdateWaveUI();
            ResetQuestTracking();
            yield return StartCoroutine(SpawnWave(_waves[i]));

            // Wait for all enemies in wave to die
            yield return new WaitUntil(() => _enemiesAliveInWave <= 0);

            CheckQuestCompletion(_waves[i].Quest);

            if (_waves[i].ShowBuffAfter)
            {
                yield return StartCoroutine(ShowBuffSelection());
            }

            if (i < _waves.Length - 1)
                yield return new WaitForSeconds(_timeBetweenWaves);
        }

        // All waves done
        if (!IsOver)
            SetGameOver(true);
    }

    private IEnumerator SpawnWave(WaveDefinition wave)
    {
        _waveInProgress = true;
        _enemiesAliveInWave = 0;
        _enemiesSpawnedInWave = 0;

        // Count total enemies in this wave
        foreach (var entry in wave.Enemies)
            _enemiesAliveInWave += entry.Count;

        foreach (var entry in wave.Enemies)
        {
            for (int i = 0; i < entry.Count; i++)
            {
                SpawnSpecificEnemy(entry.EnemyPrefab);
                yield return new WaitForSeconds(entry.SpawnInterval);
            }
        }

        _waveInProgress = false;
    }

    private void SpawnSpecificEnemy(Enemy prefab)
    {
        string pName = prefab.name;
        GameObject obj = _spawnedEnemies.Find(e => !e.gameObject.activeSelf && e.name.Contains(pName))?.gameObject;
        if (obj == null) obj = Instantiate(prefab.gameObject);

        Enemy enemy = obj.GetComponent<Enemy>();
        enemy.ApplyDifficultyScale(_enemyHealthScale, _enemySpeedScale);

        if (!_spawnedEnemies.Contains(enemy)) _spawnedEnemies.Add(enemy);

        enemy.transform.position = _enemyPaths[0].position;
        enemy.SetTargetPosition(_enemyPaths[1].position);
        enemy.SetCurrentPathIndex(1);
        enemy.gameObject.SetActive(true);
    }

    // Old-style continuous spawning (fallback when waves not configured)
    private void SpawnFallbackEnemy()
    {
        if (_enemyPrefabs == null || _enemyPrefabs.Length == 0) return;

        _enemyCounter--;
        if (_enemyCounter < 0)
        {
            // All enemies spawned — wait for remaining to die
            bool allDead = _spawnedEnemies.Find(e => e.gameObject.activeSelf) == null;
            if (allDead) SetGameOver(true);
            return;
        }

        int idx = Random.Range(0, _enemyPrefabs.Length);
        string name = (idx + 1).ToString();
        GameObject obj = _spawnedEnemies.Find(
            e => !e.gameObject.activeSelf && e.name.Contains(name))?.gameObject;
        if (obj == null) obj = Instantiate(_enemyPrefabs[idx].gameObject);

        Enemy enemy = obj.GetComponent<Enemy>();
        if (!_spawnedEnemies.Contains(enemy)) _spawnedEnemies.Add(enemy);

        enemy.transform.position = _enemyPaths[0].position;
        enemy.SetTargetPosition(_enemyPaths[1].position);
        enemy.SetCurrentPathIndex(1);
        enemy.gameObject.SetActive(true);

        if (_waveInfo != null) _waveInfo.text = $"Враги: {Mathf.Max(_enemyCounter, 0)}";
    }

    private void ReachBase(Enemy enemy)
    {
        int liveDmg = enemy.LivesDamage;
        ReduceLives(liveDmg);
        _livesLostThisWave = true;
        _enemiesAliveInWave = Mathf.Max(0, _enemiesAliveInWave - 1);
        enemy.gameObject.SetActive(false);
    }

    // Called by Enemy.ReduceEnemyHealth when enemy dies
    public void OnEnemyKilled(Enemy enemy)
    {
        _enemiesAliveInWave = Mathf.Max(0, _enemiesAliveInWave - 1);
        if (enemy.EnemyType == EnemyType.Elite) _eliteKilledThisWave = true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Between-wave buff selection
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator ShowBuffSelection()
    {
        if (_buffPanel == null) yield break;

        _currentBuffOptions = GetRandomBuffOptions(3);

        for (int i = 0; i < _buffButtons.Length && i < _currentBuffOptions.Length; i++)
        {
            int idx = i;
            WaveBuffType buff = _currentBuffOptions[i];
            if (_buffLabels != null && i < _buffLabels.Length)
                _buffLabels[i].text = GetBuffDescription(buff);
            _buffButtons[i].onClick.RemoveAllListeners();
            _buffButtons[i].onClick.AddListener(() => OnBuffSelected(buff));
        }

        _buffPanel.SetActive(true);
        yield return new WaitUntil(() => !_buffPanel.activeSelf);
    }

    private WaveBuffType[] GetRandomBuffOptions(int count)
    {
        var all = (WaveBuffType[])System.Enum.GetValues(typeof(WaveBuffType));
        var result = new WaveBuffType[count];
        var used   = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int r;
            do { r = Random.Range(0, all.Length); } while (used.Contains(r));
            used.Add(r);
            result[i] = all[r];
        }
        return result;
    }

    private void OnBuffSelected(WaveBuffType buff)
    {
        ApplyBuff(buff);
        if (_buffPanel != null) _buffPanel.SetActive(false);
    }

    private void ApplyBuff(WaveBuffType buff)
    {
        switch (buff)
        {
            case WaveBuffType.ExtraLife:
                SetCurrentLives(_currentLives + 1);
                break;
            case WaveBuffType.FreeBuild:
                _freeBuild = true;
                break;
            case WaveBuffType.FreeMerge:
                _freeMerge = true;
                break;
            case WaveBuffType.GoldBonus:
                _goldBonusMultiplier += 0.15f;
                break;
            case WaveBuffType.DamageBonus:
            case WaveBuffType.AttackSpeedBonus:
                foreach (Tower t in _spawnedTowers) t.ApplySessionBuff(buff);
                break;
        }
    }

    private string GetBuffDescription(WaveBuffType buff)
    {
        switch (buff)
        {
            case WaveBuffType.DamageBonus:      return "+10% урон для всех башен";
            case WaveBuffType.AttackSpeedBonus: return "+15% скорость атаки 1–2 тира";
            case WaveBuffType.ExtraLife:        return "+1 жизнь";
            case WaveBuffType.FreeBuild:        return "Бесплатное строительство";
            case WaveBuffType.FreeMerge:        return "Бесплатное слияние";
            case WaveBuffType.GoldBonus:        return "+15% золото за убийства";
            default:                            return buff.ToString();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Quest system
    // ─────────────────────────────────────────────────────────────────────────

    private void ResetQuestTracking()
    {
        _magicKillsThisWave  = 0;
        _livesLostThisWave   = false;
        _mergesThisWave      = 0;
        _eliteKilledThisWave = false;
    }

    // Called by Enemy.cs
    public void RegisterMagicKill(bool isMagic)
    {
        if (isMagic) _magicKillsThisWave++;
    }

    private void CheckQuestCompletion(WaveQuest quest)
    {
        if (quest == null || !quest.HasQuest) return;

        bool completed = false;
        switch (quest.Type)
        {
            case QuestType.KillWithMagic:   completed = _magicKillsThisWave  >= quest.Target; break;
            case QuestType.SurviveWave:     completed = !_livesLostThisWave;                  break;
            case QuestType.MergeTowers:     completed = _mergesThisWave      >= quest.Target;  break;
            case QuestType.KillEliteEnemy:  completed = _eliteKilledThisWave;                  break;
        }

        if (completed) AddGold(quest.GoldReward);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tower management
    // ─────────────────────────────────────────────────────────────────────────

    private void InstantiateAllTowerUI()
    {
        if (_towerUIPrefab == null || _towerUIParent == null) return;

        // Create a single "Build Tower" button — player gets a random tower
        int cost = _tier1TowerPrefabs.Length > 0 ? _tier1TowerPrefabs[0].GoldCost : 50;
        GameObject uiObj  = Instantiate(_towerUIPrefab, _towerUIParent);
        TowerUI    towerUI = uiObj.GetComponent<TowerUI>();
        Sprite     icon   = _tier1TowerPrefabs.Length > 0
            ? _tier1TowerPrefabs[0].GetTowerHeadIcon() : null;
        towerUI.Initialise(cost, icon);
    }

    private void CacheAllSlots()
    {
        var slots = FindObjectsOfType<TowerPlacement>();
        _allSlots.AddRange(slots);
    }

    public void RegisterSpawnedTower(Tower tower)
    {
        _spawnedTowers.Add(tower);

        // Register tower in the nearest empty slot
        TowerPlacement slot = GetSlotAt(tower.PlacePosition ?? (Vector2)tower.transform.position);
        if (slot != null && !slot.IsOccupied) slot.RegisterTower(tower);
    }

    private TowerPlacement GetSlotAt(Vector2 pos)
    {
        foreach (var s in _allSlots)
            if (Vector2.Distance(s.transform.position, pos) < 0.2f) return s;
        return null;
    }

    // Returns a random tier-1 prefab to be built
    public Tower GetRandomTier1Prefab()
    {
        if (_tier1TowerPrefabs == null || _tier1TowerPrefabs.Length == 0) return null;
        return _tier1TowerPrefabs[Random.Range(0, _tier1TowerPrefabs.Length)];
    }

    // Returns a random tower of the next tier (for merging)
    private Tower GetRandomPrefabForTier(TowerTier tier)
    {
        Tower[] pool = tier == TowerTier.Tier2 ? _tier2TowerPrefabs : _tier3TowerPrefabs;
        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    // Sell a placed tower — refund gold, free its slot
    public void SellTower(Tower tower)
    {
        int refund = tower.GetSellValue();
        AddGold(refund);

        if (tower.OccupiedSlot != null) tower.OccupiedSlot.ClearSlot();
        _spawnedTowers.Remove(tower);
        Destroy(tower.gameObject);
    }

    // Merge two same-tier towers into a random next-tier tower
    // incomingTower: the tower being dragged (not yet placed)
    // targetSlot:    the slot containing the tower to merge with
    public void MergeTowers(Tower incomingTower, TowerPlacement targetSlot)
    {
        if (targetSlot == null || !targetSlot.IsOccupied) return;
        Tower existingTower = targetSlot.OccupiedTower;
        if (existingTower.Tier != incomingTower.Tier) return;

        TowerTier nextTier = (TowerTier)((int)existingTower.Tier + 1);
        Tower nextPrefab   = GetRandomPrefabForTier(nextTier);
        if (nextPrefab == null)
        {
            Debug.LogWarning($"No prefab pool for tier {nextTier}");
            return;
        }

        if (!_freeMerge && !CanAfford(0)) { /* future: merge cost */ }
        if (_freeMerge) _freeMerge = false;

        // Compute total invested gold for sell-value inheritance
        int combinedCost = existingTower.GoldCost + incomingTower.GoldCost;

        // Remove existing tower from slot and list
        _spawnedTowers.Remove(existingTower);
        Vector2 slotPos = (Vector2)targetSlot.transform.position;
        targetSlot.ClearSlot();
        Destroy(existingTower.gameObject);

        // Spawn merged tower
        GameObject newObj  = Instantiate(nextPrefab.gameObject);
        Tower newTower     = newObj.GetComponent<Tower>();
        newTower.GoldCost  = combinedCost;       // inherit cost for sell calculation
        newTower.SetPlacePosition(slotPos);
        newTower.LockPlacement();
        newTower.ApplyRandomModifier();
        targetSlot.RegisterTower(newTower);
        _spawnedTowers.Add(newTower);

        // Apply existing session buffs to new tower
        foreach (Tower t in _spawnedTowers)
        {
            // Session buff state isn't stored globally — new tower starts neutral.
            // For a future enhancement, track active global buffs and reapply.
        }

        _mergesThisWave++;
    }

    public void UseFreeBuild() => _freeBuild = false;

    // ─────────────────────────────────────────────────────────────────────────
    // Enemy / bullet pools
    // ─────────────────────────────────────────────────────────────────────────

    public Bullet GetBulletFromPool(Bullet prefab)
    {
        GameObject obj = _spawnedBullets.Find(
            b => !b.gameObject.activeSelf && b.name.Contains(prefab.name))?.gameObject;
        if (obj == null) obj = Instantiate(prefab.gameObject);

        Bullet bullet = obj.GetComponent<Bullet>();
        if (!_spawnedBullets.Contains(bullet)) _spawnedBullets.Add(bullet);
        return bullet;
    }

    public void ExplodeAt(Vector2 point, float radius, int damage, AttackType attackType)
    {
        foreach (Enemy e in _spawnedEnemies)
            if (e.gameObject.activeSelf && Vector2.Distance(e.transform.position, point) <= radius)
                e.ReduceEnemyHealth(damage, attackType);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Economy
    // ─────────────────────────────────────────────────────────────────────────

    public bool CanAfford(int cost) => _gold >= cost;

    public bool SpendGold(int cost)
    {
        if (_gold < cost) return false;
        SetGold(_gold - cost);
        return true;
    }

    public void AddGold(int amount)
    {
        int actual = Mathf.RoundToInt(amount * _goldBonusMultiplier);
        SetGold(_gold + actual);
    }

    private void SetGold(int value)
    {
        _gold = Mathf.Max(0, value);
        if (_goldInfo != null) _goldInfo.text = $"Золото: {_gold}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lives
    // ─────────────────────────────────────────────────────────────────────────

    public void ReduceLives(int amount)
    {
        SetCurrentLives(_currentLives - amount);
        if (_currentLives <= 0) SetGameOver(false);
    }

    public void SetCurrentLives(int lives)
    {
        _currentLives = Mathf.Max(0, lives);
        if (_livesInfo != null) _livesInfo.text = $"Жизни: {_currentLives}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Game over / UI
    // ─────────────────────────────────────────────────────────────────────────

    public void SetGameOver(bool isWin)
    {
        IsOver = true;
        if (_statusInfo != null) _statusInfo.text = isWin ? "Победа!" : "Поражение!";
        if (_panel != null) _panel.SetActive(true);
    }

    private void UpdateWaveUI()
    {
        if (_waveInfo == null) return;
        if (_useFallbackSpawning)
            _waveInfo.text = $"Враги: {Mathf.Max(_enemyCounter, 0)}";
        else
            _waveInfo.text = $"Волна: {_currentWaveIndex + 1} / {_waves.Length}";
    }
}
