using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Melee")]
    public float meleeDamage = 50f;
    public float meleeRange = 2f;
    public float meleeCooldown = 0.5f;

    [Header("Disparo")]
    public float shootDamage = 30f;
    public float shootRange = 50f;
    public float shootCooldown = 0.3f;

    private float _meleeTimer = 0f;
    private float _shootTimer = 0f;
    private Camera _cam;

    void Start()
    {
        _cam = Camera.main;
    }

    void Update()
    {
        if (_meleeTimer > 0f) _meleeTimer -= Time.deltaTime;
        if (_shootTimer > 0f) _shootTimer -= Time.deltaTime;

        if (Input.GetMouseButtonDown(0) && _meleeTimer <= 0f)
            MeleeAttack();

        if (Input.GetMouseButtonDown(1) && _shootTimer <= 0f)
            Shoot();
    }

    void MeleeAttack()
    {
        _meleeTimer = meleeCooldown;

        RaycastHit hit;
        if (Physics.Raycast(_cam.transform.position, _cam.transform.forward, out hit, meleeRange))
        {
            EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();
            if (enemy != null)
            {
                enemy.TakeDamage(meleeDamage);
                Debug.Log("Golpe melee a " + hit.collider.name);
            }
        }
    }

    void Shoot()
    {
        _shootTimer = shootCooldown;

        RaycastHit hit;
        if (Physics.Raycast(_cam.transform.position, _cam.transform.forward, out hit, shootRange))
        {
            EnemyBehaviour enemy = hit.collider.GetComponentInParent<EnemyBehaviour>();
            if (enemy != null)
            {
                enemy.TakeDamage(shootDamage);
                Debug.Log("Disparo a " + hit.collider.name);
            }
        }
    }
}
