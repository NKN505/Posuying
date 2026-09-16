using UnityEngine;

public class Handgun : Weapon
{
    protected override void Awake()
    {
        // La clase base captura el desfase del holder y el FOV de reposo.
        base.Awake();

        // Municion
        SetCapacity(15.0f);
        SetCurrentAmmo(15.0f);
        SetReserveAmmo(0.0f);

        // Estadisticas
        SetDamage(100.0f);
        SetCadency(0.3f);
        SetScope(100.0f);

        // Estados iniciales
        SetIsEmpty(false);
        SetIsFull(true);
        SetIsShooting(false);
        SetIsReloading(false);
        SetIsSwitchingWeapon(false);
        SetNoAmmo(true);
    }

    protected override void Update()
    {
        // La clase base lee el boton de apuntar y aplica el zoom.
        base.Update();

        if (Input.GetButtonDown("Fire"))
        {
            Fire();
        }

        if (Input.GetButtonDown("Reload-Interact"))
        {
            if (GetReserveAmmo() <= 0.0f)
            {
                Debug.Log("Sin municion de reserva");
            }
            else if (GetIsFull())
            {
                Debug.Log("El cargador ya esta lleno");
            }
            else
            {
                Reload();
                Debug.Log("RELOADING");
            }
        }
    }

    public override void Fire()
    {
        if (GetIsShooting() ||
            GetIsReloading() ||
            GetIsSwitchingWeapon())
        {
            return;
        }

        if (GetCurrentAmmo() <= 0.0f)
        {
            SetIsEmpty(true);
            SetIsFull(false);
            SetIsShooting(false);

            Debug.Log("Cargador vacio");
            return;
        }

        SetIsShooting(true);

        SetCurrentAmmo(GetCurrentAmmo() - 1.0f);

        SetCurrentAmmo(GetCurrentAmmo() - 1.0f);

        SetIsEmpty(GetCurrentAmmo() <= 0.0f);
        SetIsFull(GetCurrentAmmo() >= GetCapacity());
        SetNoAmmo(GetReserveAmmo() <= 0.0f);

        // ---- EL DISPARO ----
        if (ShootRay(GetCurrentSpread(), out RaycastHit hit))
        {
            Debug.Log($"Impacto: {hit.collider.name} a {hit.distance:0.00} m");
        }
        else
        {
            Debug.Log("Fallo: el rayo no toco nada");
        }
        // --------------------

        Debug.Log(
            $"Disparo realizado. Balas en el cargador: {GetCurrentAmmo():0}"
        );

        SetIsEmpty(GetCurrentAmmo() <= 0.0f);
        SetIsFull(GetCurrentAmmo() >= GetCapacity());
        SetNoAmmo(GetReserveAmmo() <= 0.0f);

        Debug.Log(
            $"Disparo realizado. Balas en el cargador: {GetCurrentAmmo():0}"
        );

        CancelInvoke(nameof(StopShooting));
        Invoke(nameof(StopShooting), GetCadency());
    }
}
