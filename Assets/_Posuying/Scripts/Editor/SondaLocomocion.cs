using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// HERRAMIENTA DE DIAGNOSTICO, TEMPORAL. Solo editor, no entra en la build.
///
/// Al entrar en Play engancha al primer enemigo con EnemyLocomotionAnimator y
/// vuelca a un CSV, frame a frame, la velocidad medida frente al estado real del
/// Animator. Sirve para ver POR QUE la animacion salta entre andar y correr en
/// vez de adivinarlo.
///
/// Se puede borrar entera cuando el problema este cerrado.
/// </summary>
[InitializeOnLoad]
public static class SondaLocomocion
{
    public const string Ruta = "Temp/sonda-locomocion.csv";
    private const float Duracion = 8f;

    private static readonly List<string> _lineas = new List<string>();
    private static EnemyLocomotionAnimator _loco;
    private static Animator _anim;
    private static NavMeshAgent _agente;
    private static float _t0;
    private static bool _midiendo;
    private static EnemyBehaviour _eb;
    private static Vector3 _posAnterior;
    private static System.Reflection.FieldInfo _campoPlayer;

    static SondaLocomocion()
    {
        EditorApplication.playModeStateChanged += EnCambioDePlay;
    }

    private static void EnCambioDePlay(PlayModeStateChange estado)
    {
        if (estado == PlayModeStateChange.EnteredPlayMode)
        {
            _lineas.Clear();
            _loco = null;
            _midiendo = true;
            _t0 = -1f;
            EditorApplication.update += Muestrear;
        }
        else if (estado == PlayModeStateChange.ExitingPlayMode)
        {
            Volcar();
        }
    }

    private static void Muestrear()
    {
        if (!Application.isPlaying || !_midiendo) return;

        // El enemigo puede tardar en aparecer (lo instancia el servidor)
        if (_loco == null)
        {
            var todos = Object.FindObjectsByType<EnemyLocomotionAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (todos.Length == 0) return;

            // El mas cercano a la camara: es el que el jugador esta mirando y del
            // que se queja. Coger todos[0] podria pillar uno parado al otro lado
            // del mapa y no medir nada util.
            Camera cam = Camera.main;
            if (cam == null) return;
            _loco = todos[0];
            float mejor = float.MaxValue;
            foreach (var c in todos)
            {
                float d = (c.transform.position - cam.transform.position).sqrMagnitude;
                if (d < mejor) { mejor = d; _loco = c; }
            }
            _anim = _loco.GetComponentInChildren<Animator>(true);
            _agente = _loco.GetComponent<NavMeshAgent>();
            _t0 = Time.realtimeSinceStartup;
            _lineas.Add("objetivo;" + _loco.gameObject.name + ";de;" + todos.Length);
            _lineas.Add("t;dt;agentVel;agentDeseada;transformVel;restante;pathStatus;hasPath;pending;isStopped;distObj;objMapeable;vecinos;saltoPos;estado");
            var rb = _loco.GetComponent<Rigidbody>();
            var cc = _loco.GetComponent<CharacterController>();
            var nt = _loco.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            var no = _loco.GetComponent<Unity.Netcode.NetworkObject>();
            _lineas.Add("info;rigidbody=" + (rb != null ? (rb.isKinematic ? "kinematico" : "DINAMICO") : "no") +
                        ";charctrl=" + (cc != null) +
                        ";netTransform=" + (nt != null) +
                        ";esServidor=" + (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer) +
                        ";agenteActivo=" + (_agente != null && _agente.enabled) +
                        ";avoidance=" + (_agente != null ? _agente.obstacleAvoidanceType.ToString() : "-") +
                        ";prioridad=" + (_agente != null ? _agente.avoidancePriority.ToString() : "-"));
            _posAnterior = _loco.transform.position;
            _campoPlayer = typeof(EnemyBehaviour).GetField("player",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _eb = _loco.GetComponent<EnemyBehaviour>();
        }

        float t = Time.realtimeSinceStartup - _t0;
        if (t > Duracion || _anim == null) { Volcar(); return; }

        var si = _anim.GetCurrentAnimatorStateInfo(0);
        string nombre = si.IsName("idle") ? "idle" : si.IsName("walk") ? "walk"
                      : si.IsName("run") ? "run" : si.IsName("sprint") ? "sprint" : "otro";

        float restante = -1f;
        string ps = "-";
        float agv = 0f, agd = 0f;
        if (_agente != null && _agente.enabled && _agente.isOnNavMesh)
        {
            restante = _agente.remainingDistance > 1e6f ? -1f : _agente.remainingDistance;
            ps = _agente.pathStatus.ToString();
            agv = _agente.velocity.magnitude;
            agd = _agente.desiredVelocity.magnitude;
        }

        // Velocidad REAL del transform, para contrastarla con la que cree el agente.
        // Si el agente va suave y el transform da tirones, el culpable no es la
        // navegacion sino algo que mueve o corrige al objeto por detras.
        Vector3 delta = _loco.transform.position - _posAnterior;
        _posAnterior = _loco.transform.position;
        float transformVel = Time.deltaTime > 0f ? new Vector3(delta.x, 0f, delta.z).magnitude / Time.deltaTime : 0f;
        float saltoPos = delta.magnitude;   // un salto grande delata correccion de red

        // Cuantos otros agentes tiene pegados: la evitacion entre agentes tambien
        // produce este arranque y parada cuando se amontonan
        int vecinos = 0;
        foreach (var otro in Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (otro != _agente && (otro.transform.position - _loco.transform.position).sqrMagnitude < 9f) vecinos++;

        // A quien persigue ahora mismo, y si ese punto se puede mapear al NavMesh.
        // Si SetDestination recibe un punto no mapeable, el agente TIRA el camino
        // y se para en seco: es el sospechoso principal del tiron.
        string objNombre = "-"; string objPos = "-"; int mapeable = -1; float distObj = -1f;
        Transform obj = _campoPlayer != null && _eb != null ? _campoPlayer.GetValue(_eb) as Transform : null;
        if (obj != null)
        {
            objNombre = obj.name;
            objPos = obj.position.ToString("F1").Replace(";", ",");
            NavMeshHit mh;
            mapeable = NavMesh.SamplePosition(obj.position, out mh, 1f, NavMesh.AllAreas) ? 1 : 0;
            distObj = Vector3.Distance(_loco.transform.position, obj.position);
        }

        bool hasPath = _agente != null && _agente.enabled && _agente.isOnNavMesh && _agente.hasPath;
        bool pending = _agente != null && _agente.enabled && _agente.isOnNavMesh && _agente.pathPending;
        string dest = hasPath || pending ? _agente.destination.ToString("F1").Replace(";", ",") : "-";

        _lineas.Add(string.Format(CultureInfo.InvariantCulture,
            "{0:F3};{1:F4};{2:F3};{3:F3};{4:F3};{5:F2};{6};{7};{8};{9};{10:F2};{11};{12};{13:F4};{14}",
            t, Time.deltaTime, agv, agd, transformVel, restante, ps,
            hasPath ? 1 : 0, pending ? 1 : 0,
            _agente != null && _agente.enabled && _agente.isOnNavMesh && _agente.isStopped ? 1 : 0,
            distObj, mapeable, vecinos, saltoPos, nombre));
    }

    private static void Volcar()
    {
        EditorApplication.update -= Muestrear;
        if (!_midiendo) return;
        _midiendo = false;
        if (_lineas.Count == 0) return;

        Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
        File.WriteAllLines(Ruta, _lineas);
        Debug.Log("SondaLocomocion: " + (_lineas.Count - 2) + " muestras en " + Path.GetFullPath(Ruta));
    }
}
