// Wwise "mudo" para las plataformas en las que todavia no esta instalado.
//
// La integracion de Wwise del proyecto solo trae Windows y Mac. En Android,
// navegador y Linux sus ensamblados estan excluidos (excludePlatforms en los
// .asmdef de Assets/Wwise) y, para que el juego compile igual, aqui se
// definen las piezas de Wwise que usa nuestro codigo, sin hacer nada: alli el
// juego funciona entero pero sin los sonidos de Wwise (la voz, el golpe y el
// video van por el audio de Unity y si suenan).
//
// CUANDO SE ANADAN ESAS PLATAFORMAS A WWISE (Wwise Launcher > Unity > Modify):
//   1. Quitar "Android", "WebGL" y "LinuxStandalone64" de excludePlatforms en
//      los .asmdef de Assets/Wwise (el Launcher puede hacerlo solo).
//   2. Quitar el simbolo POSUYING_SIN_WWISE de Android y WebGL
//      (Project Settings > Player > Scripting Define Symbols).
//   3. Este archivo puede borrarse.
//
// En el editor nunca se usa: alli siempre esta el Wwise de verdad.
#if !UNITY_EDITOR && (POSUYING_SIN_WWISE || UNITY_STANDALONE_LINUX)
using UnityEngine;

namespace AK.Wwise
{
    // OJO: los campos guardados tienen que ser EXACTAMENTE los del AK.Wwise.Event
    // de verdad (BaseType + Event), en el mismo orden. Si no, al cargar la escena
    // Unity lee mal los datos del script que lo contiene (PlayerController,
    // Handgun, MainMenuUI...) y le estropea los demas ajustes.
    [System.Serializable]
    public class Event
    {
#pragma warning disable 0414, 0649
        [SerializeField] private int idInternal;
        [SerializeField] private byte[] valueGuidInternal;
        public Object WwiseObjectReference;
#pragma warning restore 0414, 0649

        public bool IsValid() => false;
        public uint Post(GameObject gameObject) => 0;
        public void Stop(GameObject gameObject, int transitionDuration = 0) { }
    }
}

public class AkGameObj : MonoBehaviour { }
public class AkAudioListener : MonoBehaviour { }
public class AkInitializer : MonoBehaviour { }

public class AkAuxSendArray
{
    private int _cuantos;
    public void Reset() => _cuantos = 0;
    public bool Add(uint auxBusId, float value) { _cuantos++; return true; }
    public int Count() => _cuantos;
}

public static class AkUnitySoundEngine
{
    public static bool IsInitialized() => false;
    public static uint GetIDFromString(string name) => 0;
    public static ulong GetAkGameObjectID(GameObject gameObject) => 0;
    public static int SetSwitch(string group, string state, GameObject gameObject) => 0;
    public static int SetGameObjectAuxSendValues(GameObject gameObject, AkAuxSendArray sends, uint count) => 0;
    public static int SetOutputVolume(ulong deviceId, float volume) => 0;
    public static int SetGameObjectOutputBusVolume(ulong emitter, ulong listener, float volume) => 0;
}
#endif
