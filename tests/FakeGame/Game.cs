// "Game code" of the fake game: behaviours with fields, properties, collections and methods that
// are called every frame, so inspection, live refresh and hooks have something to show.
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public int score;
    public Difficulty difficulty = Difficulty.Normal;
    public Dictionary<string, float> settings = new() { ["musicVolume"] = 0.8f, ["sfxVolume"] = 1f, ["mouseSensitivity"] = 2.5f };
    public List<string> unlockedLevels = new() { "Tutorial", "Forest", "Caves" };

    public GameManager() => Instance = this;

    public bool Paused { get; set; }

    public int AddScore(int points)
    {
        score += points;
        return score;
    }

    public override void Update()
    {
        if (Time.frameCount % 600 == 0)
            Debug.Log($"autosave: score {score}");
    }
}

public enum Difficulty
{
    Easy,
    Normal,
    Hard,
}

public sealed class PlayerController : MonoBehaviour
{
    public int health = 100;
    public int maxHealth = 100;
    public float speed = 5.5f;
    public bool invincible;
    public Color tint = new(0.4f, 0.7f, 1f, 1f);
    public List<string> inventory = new() { "Sword", "Potion", "Potion", "Map" };
    public Transform weapon;

    public int TakeDamage(int amount)
    {
        if (invincible)
            return health;
        health = Math.Max(0, health - amount);
        if (health == 0)
        {
            Debug.LogWarning("player died, respawning");
            health = maxHealth;
            OnRespawn();
        }

        return health;
    }

    /// <summary>Empty, like Enemy.OnRespawn: the fake runtime reports them as folded (ICF).</summary>
    public void OnRespawn()
    {
    }

    public void Heal(int amount) => health = Math.Min(maxHealth, health + amount);

    public override void Update()
    {
        var t = Time.time;
        transform.position = new Vector3(MathF.Cos(t) * 3, 0, MathF.Sin(t) * 3);
    }
}

public sealed class Enemy : MonoBehaviour
{
    private float _cooldown;

    public int hp = 30;
    public int damage = 7;
    public float attackInterval = 2f;
    public PlayerController target;

    public void OnRespawn()
    {
    }

    public override void Update()
    {
        _cooldown -= Time.deltaTime;
        if (_cooldown > 0 || target == null)
            return;
        _cooldown = attackInterval;
        target.TakeDamage(damage);
    }
}

public sealed class Spinner : MonoBehaviour
{
    public float degreesPerSecond = 90;

    public override void Update() => transform.rotation = Quaternion.Euler(0, Time.time * degreesPerSecond, 0);
}

/// <summary>Spawns pickups and destroys old ones, so handles to destroyed objects happen.</summary>
public sealed class Spawner : MonoBehaviour
{
    private readonly Queue<GameObject> _alive = new();
    private float _next;

    public float interval = 3;
    public int maxAlive = 3;
    public int spawned;

    public override void Update()
    {
        if (Time.time < _next)
            return;
        _next = Time.time + interval;

        var pickup = new GameObject($"Pickup {++spawned}", typeof(Pickup), typeof(MeshRenderer));
        pickup.transform.parent = transform;
        pickup.transform.position = new Vector3(spawned % 5, 0.5f, 2);
        _alive.Enqueue(pickup);
        if (_alive.Count > maxAlive)
            Object.Destroy(_alive.Dequeue());
    }
}

public sealed class Pickup : MonoBehaviour
{
    public int points = 10;
    public PickupKind kind = PickupKind.Coin;
}

public enum PickupKind
{
    Coin,
    Gem,
    Heart,
}

public sealed class HealthBar : MonoBehaviour
{
    public PlayerController player;
    public Sprite fill;

    public float Fraction => player == null ? 0 : player.health / (float)player.maxHealth;
}
