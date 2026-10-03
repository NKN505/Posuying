using System.IO;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Herramienta de pruebas de red. NO hace nada en una partida normal: solo se
// activa si el juego se arranca con argumentos en la linea de comandos.
//
//   -autohost            crea una partida local (127.0.0.1) nada mas arrancar
//   -autocliente         se une a esa partida local
//   -sonda <fichero>     escribe dos veces por segundo lo que ESTA maquina ve de
//                        los demas jugadores: animacion, manos, pasos y disparos
//
// Sirve para comprobar el multijugador con dos copias del juego en el mismo PC
// sin tener que jugar con las dos a la vez: una se arranca con estos argumentos
// y deja escrito lo que ve.
public class SondaPruebas : MonoBehaviour
{
    public static int PasosOidos;      // los suma PasosRemotos
    public static int DisparosOidos;   // los suma NetworkPlayer

    private string _fichero;
    private bool _autoHost, _autoCliente, _arrancado;
    private float _siguiente;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Arrancar()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        bool host = System.Array.IndexOf(args, "-autohost") >= 0;
        bool cliente = System.Array.IndexOf(args, "-autocliente") >= 0;
        int i = System.Array.IndexOf(args, "-sonda");
        string fichero = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

        if (!host && !cliente && fichero == null) return;   // partida normal: nada

        var go = new GameObject("SondaPruebas");
        DontDestroyOnLoad(go);
        var sonda = go.AddComponent<SondaPruebas>();
        sonda._autoHost = host;
        sonda._autoCliente = cliente;
        sonda._fichero = fichero;
        Application.runInBackground = true;
    }

    void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (!_arrancado && Time.unscaledTime > 3f && (_autoHost || _autoCliente))
        {
            _arrancado = true;
            var transporte = nm.GetComponent<UnityTransport>();
            if (transporte != null) transporte.SetConnectionData("127.0.0.1", 7777);
            if (_autoHost) nm.StartHost(); else nm.StartClient();
        }

        if (_fichero == null || Time.unscaledTime < _siguiente) return;
        _siguiente = Time.unscaledTime + 0.5f;

        var sb = new StringBuilder();
        sb.Append("t=").Append(Time.unscaledTime.ToString("0.0"))
          .Append(" servidor=").Append(nm.IsServer).Append(" conectado=").Append(nm.IsConnectedClient || nm.IsServer)
          .Append(" jugadores=").Append(NetworkPlayer.AllPlayers.Count)
          .Append(" pasosOidos=").Append(PasosOidos).Append(" disparosOidos=").Append(DisparosOidos).Append('\n');

        foreach (var p in NetworkPlayer.AllPlayers)
        {
            if (p == null || p.IsOwner) continue;

            var anim = p.GetComponent<Animator>();
            var cam = p.GetComponentInChildren<Camera>(true);

            // Lo que se dibuja de sus manos de primera persona (deberia ser 0)
            int manos = 0;
            if (cam != null)
                foreach (var r in cam.GetComponentsInChildren<Renderer>(false))
                    if (r.enabled) manos++;

            sb.Append("  remoto ").Append(p.OwnerClientId)
              .Append(" pos=").Append(p.transform.position.ToString("0.0"))
              .Append(" MoveX=").Append(anim.GetFloat("MoveX").ToString("0.00"))
              .Append(" MoveZ=").Append(anim.GetFloat("MoveZ").ToString("0.00"))
              .Append(" IsAiming=").Append(anim.GetBool("IsAiming"))
              .Append(" Crouch=").Append(anim.GetBool("Crouch"))
              .Append(" estado=").Append(anim.GetCurrentAnimatorStateInfo(0).shortNameHash)
              .Append(" manosVisibles=").Append(manos).Append('\n');
        }

        try { File.AppendAllText(_fichero, sb.ToString()); } catch (System.Exception) { }
    }
}
