using UnityEngine;

public class MeteorShowerSpawner : MonoBehaviour
{
    [Header("Meteor Prefab")]
    [Tooltip("Prefab that has MoveToTarget attached")]
    public GameObject meteorPrefab;

    [Header("Target")]
    [Tooltip("The player/target — meteors will spawn at the same Z depth. Leave empty to auto-find by tag 'Player'")]
    public Transform target;

    [Header("Spawn Area")]
    [Tooltip("Extra distance above the top of the screen to spawn")]
    public float spawnYOffset = 1f;    // Distance above the top of the screen to spawn

    [Header("Spawn Rate")]
    public float spawnInterval = 0.8f;
    public int   maxMeteors    = 30;

    [Header("Meteor Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 6f;

    [Header("Meteor Scale")]
    public float minScale = 0.5f;
    public float maxScale = 1.5f;

    [Header("Auto Destroy")]
    [Tooltip("Destroy a meteor after this many seconds (0 = never)")]
    public float meteorLifetime = 8f;

    // ── internals ───────────────────────────────────────────────────
    private float _timer;
    private int   _activeMeteors;

    void Start()
    {
        if (target == null)
        {
            GameObject found = GameObject.FindWithTag("Player");
            if (found != null)
                target = found.transform;
            else
                Debug.LogWarning("[MeteorShowerSpawner] No target assigned and none found with tag 'Player'");
        }
    }

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= spawnInterval)
        {
            _timer = 0f;
            SpawnMeteor();
        }
    }

    void SpawnMeteor()
    {
        if (_activeMeteors >= maxMeteors) return;
        if (meteorPrefab == null)
        {
            Debug.LogWarning("[MeteorShowerSpawner] No meteorPrefab assigned!");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[MeteorShowerSpawner] No Main Camera found!");
            return;
        }

        // Use the target's Z so the meteor is on the same depth plane
        float spawnZ = target != null ? target.position.z : 0f;

        // Convert screen edges to world space at the correct Z depth
        float distToTarget = Mathf.Abs(cam.transform.position.z - spawnZ);
        Vector3 worldLeft  = cam.ScreenToWorldPoint(new Vector3(0f,            Screen.height, distToTarget));
        Vector3 worldRight = cam.ScreenToWorldPoint(new Vector3(Screen.width,  Screen.height, distToTarget));
        Vector3 worldTop   = cam.ScreenToWorldPoint(new Vector3(Screen.width / 2f, Screen.height, distToTarget));

        // Spread randomly across the full screen width on the X axis
        float   randomX  = Random.Range(worldLeft.x, worldRight.x);
        Vector3 spawnPos = new Vector3(randomX, worldTop.y + spawnYOffset, spawnZ);

        GameObject meteor = Instantiate(meteorPrefab, spawnPos, Quaternion.identity);

        // Pass target reference so the mover already has it (skips the Start() FindWithTag call)
        MoveToTarget mover = meteor.GetComponent<MoveToTarget>();
        if (mover != null)
        {
            mover.target    = target;
            mover.moveSpeed = Random.Range(minSpeed, maxSpeed);
        }

        // Randomise size
        float scale = Random.Range(minScale, maxScale);
        meteor.transform.localScale = Vector3.one * scale;

        _activeMeteors++;
        if (meteorLifetime > 0f)
            Destroy(meteor, meteorLifetime);

        meteor.AddComponent<MeteorDeathNotifier>().Init(this);
    }

    public void OnMeteorDestroyed() => _activeMeteors--;
}

// ── tiny helper component ────────────────────────────────────────────
public class MeteorDeathNotifier : MonoBehaviour
{
    private MeteorShowerSpawner _spawner;

    public void Init(MeteorShowerSpawner spawner) => _spawner = spawner;

    void OnDestroy()
    {
        if (_spawner != null)
            _spawner.OnMeteorDestroyed();
    }
}