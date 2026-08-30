using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Decide que modelo del jugador ve cada camara.
///
///   - El DUENO ve sus manos FP y NO ve su propio cuerpo TP.
///   - Los DEMAS jugadores ven su cuerpo TP y NUNCA sus manos FP.
///   - Las camaras de espectador / killcam ven el cuerpo TP de TODOS
///     (incluido el tuyo) y nunca las manos FP.
///
/// Como funciona:
///   - Las manos FP solo estan activas en la copia del dueno y viven en la capa
///     "FPArms", que las camaras de espectador excluyen de su culling mask.
///   - El cuerpo TP NUNCA se desactiva: en este prefab el cuerpo ES el objeto raiz
///     (lleva la camara, los scripts y el CharacterController), asi que apagarlo
///     apagaria al jugador entero. En su lugar, en la copia del dueno sus renderers
///     pasan a la capa "LocalPlayerBody": la camara del propio jugador la excluye,
///     pero la de espectador / killcam si la renderiza.
///
/// Capas necesarias (Project Settings > Tags and Layers):
///   FPArms, PlayerBody, LocalPlayerBody
/// </summary>
[DisallowMultipleComponent]
public class PlayerVisual : NetworkBehaviour
{
    // Nombres de capa. Si los renombras en el proyecto, cambialos aqui tambien.
    public const string FPArmsLayerName = "FPArms";
    public const string RemoteBodyLayerName = "PlayerBody";
    public const string LocalBodyLayerName = "LocalPlayerBody";

    [Header("Modelos")]
    [Tooltip("Modelo de manos de primera persona (cuelga de la camara).")]
    [SerializeField] private GameObject fpArms;

    [Tooltip("Objetos extra que solo debe ver el dueno, p.ej. el arma en primera persona. " +
             "Se desactivan en las copias remotas igual que las manos. Puede quedar vacio.")]
    [SerializeField] private GameObject[] fpExtras;

    [Tooltip("Modelo de cuerpo completo de tercera persona. Puede ser el propio objeto raiz del jugador.")]
    [SerializeField] private GameObject bodyTP;

    [Header("Camara")]
    [Tooltip("Camara de ESTE jugador. Si se deja vacia se busca en PlayerController o entre los hijos.")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("Ajusta automaticamente el culling mask de la camara del dueno.")]
    [SerializeField] private bool configureOwnerCamera = true;

    [Header("Opciones")]
    [Tooltip("Cambia la capa solo de los objetos con Renderer, para no tocar la capa de " +
             "colliders, hitboxes ni del objeto raiz.")]
    [SerializeField] private bool onlyRenderers = true;

    [Tooltip("No aplicar la capa del cuerpo a nada que cuelgue de la camara (manos, arma, linterna).")]
    [SerializeField] private bool excludeCameraHierarchy = true;

    private static bool _layersResolved;
    private static int _fpArmsLayer = -1;
    private static int _remoteBodyLayer = -1;
    private static int _localBodyLayer = -1;

    // -----------------------------------------------------------------------
    // Ciclo de vida
    // -----------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Apply(IsOwner);
    }

    public override void OnGainedOwnership()
    {
        base.OnGainedOwnership();
        Apply(IsOwner);
    }

    public override void OnLostOwnership()
    {
        base.OnLostOwnership();
        Apply(IsOwner);
    }

    private void Start()
    {
        // Escena de pruebas sin red: tratamos al jugador como local.
        // Si el objeto si esta spawneado, ya lo resolvio OnNetworkSpawn.
        if (!IsSpawned)
            Apply(true);
    }

    // -----------------------------------------------------------------------
    // Visibilidad
    // -----------------------------------------------------------------------

    private void Apply(bool isOwner)
    {
        ResolveLayers();
        ResolveCamera();

        // 1) Cuerpo TP: siempre activo, lo que cambia es su capa.
        if (bodyTP != null)
        {
            int bodyLayer = isOwner ? _localBodyLayer : _remoteBodyLayer;

            if (bodyLayer >= 0)
                SetBodyLayer(bodyLayer);

            if (!bodyTP.activeSelf)
                bodyTP.SetActive(true);
        }
        else
        {
            Debug.LogWarning("[PlayerVisual] 'bodyTP' sin asignar en " + name, this);
        }

        // 2) Manos FP y extras: solo existen en tu copia, y en la capa FPArms
        //    para que la killcam nunca los renderice.
        ApplyFirstPerson(fpArms, isOwner, warnIfNull: true);

        if (fpExtras != null)
        {
            for (int i = 0; i < fpExtras.Length; i++)
                ApplyFirstPerson(fpExtras[i], isOwner, warnIfNull: false);
        }

        // 3) Culling mask de tu camara.
        if (isOwner && configureOwnerCamera)
            ConfigureOwnerCamera();
    }

    private void ApplyFirstPerson(GameObject go, bool isOwner, bool warnIfNull)
    {
        if (go == null)
        {
            if (warnIfNull)
                Debug.LogWarning("[PlayerVisual] 'fpArms' sin asignar en " + name, this);
            return;
        }

        if (_fpArmsLayer >= 0)
            SetLayer(go, _fpArmsLayer, onlyRenderers);

        go.SetActive(isOwner);
    }

    /// <summary>
    /// Aplica la capa del cuerpo saltandose todo lo que sea de primera persona.
    /// Necesario porque el cuerpo TP suele ser el objeto raiz, y las manos y el arma
    /// cuelgan de la camara, que tambien es hija de esa raiz.
    /// </summary>
    private void SetBodyLayer(int layer)
    {
        var renderers = bodyTP.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Transform t = renderers[i].transform;
            if (IsFirstPerson(t)) continue;

            if (onlyRenderers)
            {
                t.gameObject.layer = layer;
            }
            else
            {
                var all = t.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                    all[j].gameObject.layer = layer;
            }
        }
    }

    private bool IsFirstPerson(Transform t)
    {
        if (fpArms != null && t.IsChildOf(fpArms.transform)) return true;

        if (excludeCameraHierarchy && playerCamera != null && t.IsChildOf(playerCamera.transform))
            return true;

        if (fpExtras != null)
        {
            for (int i = 0; i < fpExtras.Length; i++)
                if (fpExtras[i] != null && t.IsChildOf(fpExtras[i].transform)) return true;
        }

        return false;
    }

    // -----------------------------------------------------------------------
    // Camaras
    // -----------------------------------------------------------------------

    private void ResolveCamera()
    {
        if (playerCamera != null) return;

        var controller = GetComponent<PlayerController>();
        if (controller != null)
            playerCamera = controller.playerCamera;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
    }

    /// <summary>
    /// Camara del jugador: ve sus manos FP y los cuerpos de los demas,
    /// pero no su propio cuerpo.
    /// </summary>
    private void ConfigureOwnerCamera()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("[PlayerVisual] No se encontro la camara del jugador en " + name, this);
            return;
        }

        int mask = playerCamera.cullingMask;

        if (_fpArmsLayer >= 0) mask |= 1 << _fpArmsLayer;          // ver mis manos
        if (_remoteBodyLayer >= 0) mask |= 1 << _remoteBodyLayer;  // ver a los demas
        if (_localBodyLayer >= 0) mask &= ~(1 << _localBodyLayer); // ocultar mi cuerpo

        playerCamera.cullingMask = mask;
    }

    /// <summary>
    /// Camara de espectador o killcam: ve el cuerpo TP de todos los jugadores
    /// (incluido el local) y nunca las manos FP.
    /// Llamala al activar una camara de este tipo, o usa el componente
    /// SpectatorCameraVisual.
    /// </summary>
    public static void ConfigureSpectatorCamera(Camera cam)
    {
        if (cam == null) return;

        ResolveLayers();

        int mask = cam.cullingMask;

        if (_fpArmsLayer >= 0) mask &= ~(1 << _fpArmsLayer);      // nunca manos FP
        if (_remoteBodyLayer >= 0) mask |= 1 << _remoteBodyLayer; // cuerpos remotos
        if (_localBodyLayer >= 0) mask |= 1 << _localBodyLayer;   // y el cuerpo local

        cam.cullingMask = mask;
    }

    /// <summary>
    /// Pone los renderers de un objeto en la capa de "cuerpo de otro":
    /// el que SI ve la camara del jugador local.
    ///
    /// Lo usan los supervivientes NPC, que reaprovechan el modelo del jugador
    /// pero no llevan este componente. Sin esto heredarian la capa guardada en
    /// el prefab y, si esa fuera LocalPlayerBody, serian invisibles.
    /// </summary>
    public static void ApplyRemoteBodyLayer(GameObject root)
    {
        if (root == null) return;

        ResolveLayers();
        if (_remoteBodyLayer < 0) return;

        SetLayer(root, _remoteBodyLayer, true);
    }

    // -----------------------------------------------------------------------
    // Utilidades de capas
    // -----------------------------------------------------------------------

    private static void ResolveLayers()
    {
        if (_layersResolved) return;
        _layersResolved = true;

        _fpArmsLayer = FindLayer(FPArmsLayerName);
        _remoteBodyLayer = FindLayer(RemoteBodyLayerName);
        _localBodyLayer = FindLayer(LocalBodyLayerName);

        if (_fpArmsLayer < 0)
            Debug.LogError("[PlayerVisual] Falta la capa '" + FPArmsLayerName +
                           "'. Creala en Project Settings > Tags and Layers.");

        if (_remoteBodyLayer < 0)
            Debug.LogError("[PlayerVisual] Falta la capa '" + RemoteBodyLayerName +
                           "'. Creala en Project Settings > Tags and Layers.");

        if (_localBodyLayer < 0)
            Debug.LogError("[PlayerVisual] Falta la capa '" + LocalBodyLayerName +
                           "'. Creala en Project Settings > Tags and Layers; sin ella " +
                           "el jugador vera su propio cuerpo de tercera persona.");
    }

    private static int FindLayer(string layerName)
    {
        if (string.IsNullOrEmpty(layerName)) return -1;

        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0) return layer;

        // Algunas capas del proyecto estan guardadas con un espacio al final
        // ("FPArms ", "PlayerBody "), y NameToLayer no las encuentra.
        string wanted = layerName.Trim();
        for (int i = 0; i < 32; i++)
        {
            string name = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(name) &&
                string.Equals(name.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void SetLayer(GameObject root, int layer, bool renderersOnly)
    {
        if (renderersOnly)
        {
            // Solo los objetos que se dibujan: asi no movemos de capa los colliders
            // de hitbox ni el objeto raiz (romperia los LayerMask de disparo).
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].gameObject.layer = layer;

            return;
        }

        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
            transforms[i].gameObject.layer = layer;
    }
}
