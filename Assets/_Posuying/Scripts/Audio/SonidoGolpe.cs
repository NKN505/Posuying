using UnityEngine;

// Sonido del golpe cuerpo a cuerpo: un silbido de aire al lanzarlo y un golpe
// seco si da en un enemigo.
//
// No hay audios de esto en Wwise, asi que los dos sonidos se FABRICAN por codigo
// al arrancar (ruido filtrado y una onda grave que se apaga) y suenan por el
// audio de Unity, igual que el chat de voz. Cuando haya audios de verdad basta
// con sustituir lo que devuelven Aire() e Impacto().
public static class SonidoGolpe
{
    private const int Frecuencia = 22050;
    private static AudioClip _aire, _impacto;

    /// <summary>
    /// Hace sonar el golpe. 'propio' = lo da el jugador de esta maquina (suena sin
    /// posicion, en la cabeza); si no, suena en 'donde' y se pierde con la distancia.
    /// </summary>
    public static void Sonar(Vector3 donde, bool acierta, bool propio)
    {
        float volumen = GameSettings.SfxVolume;
        if (volumen <= 0.001f) return;

        Uno(Aire(), donde, volumen * 0.55f, propio, 1f);
        if (acierta) Uno(Impacto(), donde, volumen, propio, Random.Range(0.92f, 1.08f));
    }

    private static void Uno(AudioClip clip, Vector3 donde, float volumen, bool propio, float tono)
    {
        var go = new GameObject("Sonido_Golpe");
        go.transform.position = donde;
        var fuente = go.AddComponent<AudioSource>();
        fuente.clip = clip;
        fuente.volume = volumen;
        fuente.pitch = tono;
        fuente.spatialBlend = propio ? 0f : 1f;
        fuente.rolloffMode = AudioRolloffMode.Linear;
        fuente.minDistance = 2f;
        fuente.maxDistance = 22f;
        fuente.Play();
        Object.Destroy(go, clip.length / tono + 0.1f);
    }

    // Silbido: ruido blanco pasado por un filtro que se va abriendo y cerrando
    private static AudioClip Aire()
    {
        if (_aire != null) return _aire;

        int n = Mathf.RoundToInt(Frecuencia * 0.22f);
        var datos = new float[n];
        var azar = new System.Random(7);
        float filtrado = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float envolvente = Mathf.Sin(t * Mathf.PI);                     // sube y baja
            float apertura = Mathf.Lerp(0.04f, 0.45f, Mathf.Sin(t * Mathf.PI));
            float ruido = (float)(azar.NextDouble() * 2.0 - 1.0);
            filtrado += (ruido - filtrado) * apertura;
            datos[i] = filtrado * envolvente * 0.9f;
        }
        _aire = AudioClip.Create("Golpe_Aire", n, 1, Frecuencia, false);
        _aire.SetData(datos, 0);
        return _aire;
    }

    // Golpe seco: una nota grave que cae de tono y se apaga rapido, con un chasquido al principio
    private static AudioClip Impacto()
    {
        if (_impacto != null) return _impacto;

        int n = Mathf.RoundToInt(Frecuencia * 0.20f);
        var datos = new float[n];
        var azar = new System.Random(11);
        float fase = 0f, filtrado = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float hz = Mathf.Lerp(150f, 55f, t);
            fase += 2f * Mathf.PI * hz / Frecuencia;
            float grave = Mathf.Sin(fase) * Mathf.Exp(-t * 9f);

            float ruido = (float)(azar.NextDouble() * 2.0 - 1.0);
            filtrado += (ruido - filtrado) * 0.25f;
            float chasquido = filtrado * Mathf.Exp(-t * 38f);

            datos[i] = Mathf.Clamp(grave * 0.9f + chasquido * 0.7f, -1f, 1f);
        }
        _impacto = AudioClip.Create("Golpe_Impacto", n, 1, Frecuencia, false);
        _impacto.SetData(datos, 0);
        return _impacto;
    }
}
