using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

// ¿Tiene esta cuenta el juego? Lo decide el servidor de Unity (Cloud Code, ver
// Assets/_Posuying/CloudCode), nunca el propio juego:
//
//   - La licencia se guarda en la cuenta, en un dato que solo el servidor puede
//     escribir. Vale en todas las plataformas y no sirve a otra persona.
//   - Quien la tiene recibe un "ticket" (dura 30 min) que ensena al entrar en una
//     partida; el anfitrion lo comprueba con el servidor.
//   - Se consigue canjeando un codigo de un solo uso (tienda propia, regalos,
//     equipo). Mas adelante: compras de Steam y App Store.
//
// Como la cuenta, es obligatoria fuera del editor y de las builds de desarrollo.
public static class Licencia
{
    public static bool Obligatoria => Cuenta.Obligatoria;

    /// <summary>Ya se ha preguntado al servidor en esta sesion.</summary>
    public static bool Comprobada { get; private set; }
    public static bool Tiene { get; private set; }
    /// <summary>La cuenta puede crear codigos (lista ADMINS de los scripts).</summary>
    public static bool EsAdmin { get; private set; }

    private static string _ticket;
    // Cuando caduca el ticket. Se cuenta con el reloj interno del juego, no con la
    // hora del dispositivo: con un reloj atrasado el ticket caducaba sin renovarse.
    private static float _caducaLocal = -1f;
    private static long _caduca;   // hora del servidor (solo si el script no manda quedanMs)

    // Una sola pregunta a la vez: si se piden dos tickets a la vez, uno anula al otro
    private static Task<string> _comprobando;
    // Sube al cerrar sesion: lo que llegue despues de la cuenta anterior se descarta
    private static int _generacion;

    // Respuestas de los scripts (los nombres coinciden con el JSON que devuelven)
    private class RespComprobar { public bool tiene; public string ticket; public long caduca; public long quedanMs; public bool esAdmin; }
    private class RespCanjear { public bool ok; public string mensaje; }
    private class RespVerificar { public bool ok; public string playerId; public string motivo; }
    private class RespCrear { public bool ok; public List<string> codigos; public string mensaje; }

    /// <summary>Pregunta al servidor. Devuelve null si ha ido bien, o el error.</summary>
    public static Task<string> Comprobar()
    {
        if (_comprobando == null || _comprobando.IsCompleted) _comprobando = ComprobarAhora();
        return _comprobando;
    }

    private static async Task<string> ComprobarAhora()
    {
        int generacion = _generacion;
        try
        {
            var r = await CloudCodeService.Instance.CallEndpointAsync<RespComprobar>(
                "ComprobarLicencia", new Dictionary<string, object>());
            if (generacion != _generacion) return null;   // se cerro la sesion mientras tanto

            Tiene = r.tiene;
            EsAdmin = r.esAdmin;
            _ticket = r.ticket;
            _caduca = r.caduca;
            _caducaLocal = r.quedanMs > 0 ? Time.realtimeSinceStartup + r.quedanMs / 1000f : -1f;
            Comprobada = true;
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Licencia] " + e);
            return "No se pudo comprobar la licencia: " + e.Message;
        }
    }

    private static float SegundosQueLeQuedan()
    {
        if (_caducaLocal > 0f) return _caducaLocal - Time.realtimeSinceStartup;
        // Scripts antiguos (sin quedanMs): con la hora del dispositivo
        return (_caduca - System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000f;
    }

    /// <summary>Ticket para entrar en una partida (se renueva si le quedan menos de 6 min).</summary>
    public static async Task<string> TicketVigente()
    {
        if (!Comprobada || (Tiene && SegundosQueLeQuedan() < 6 * 60)) await Comprobar();
        return Tiene ? _ticket ?? "" : "";
    }

    /// <summary>Canjea un codigo. Devuelve el mensaje para el jugador y si ha ido bien.</summary>
    public static async Task<(bool ok, string mensaje)> Canjear(string codigo)
    {
        // Vale pegar el codigo con espacios, en minusculas o junto a otros (se coge el primero)
        var hallado = System.Text.RegularExpressions.Regex.Match((codigo ?? "").ToUpperInvariant(),
                                                                 "PSY-[A-Z0-9]{4}-[A-Z0-9]{4}");
        if (!hallado.Success) return (false, "Escribe un codigo con el formato PSY-XXXX-XXXX.");

        try
        {
            var r = await CloudCodeService.Instance.CallEndpointAsync<RespCanjear>(
                "CanjearCodigo", new Dictionary<string, object> { { "codigo", hallado.Value } });
            if (r.ok) await Comprobar();
            return (r.ok, r.mensaje);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Licencia] " + e);
            return (false, "No se pudo canjear: " + e.Message);
        }
    }

    /// <summary>
    /// Lo usa el anfitrion: comprueba con el servidor el ticket de quien quiere entrar.
    /// Devuelve si vale, de que cuenta es, el motivo si no vale y si el fallo ha sido
    /// de conexion con el servidor (no un "no" de verdad).
    /// </summary>
    public static async Task<(bool ok, string playerId, string motivo, bool sinRespuesta)> Verificar(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return (false, "", "no tiene el juego", false);

        // Un fallo suelto del servidor no debe dejar a nadie fuera: se reintenta.
        // Sin pasarse: Netcode espera la respuesta como mucho 10 s.
        float inicio = Time.realtimeSinceStartup;
        for (int intento = 1; ; intento++)
        {
            try
            {
                var r = await CloudCodeService.Instance.CallEndpointAsync<RespVerificar>(
                    "VerificarTicket", new Dictionary<string, object> { { "ticket", ticket } });
                return (r.ok, r.playerId ?? "", r.motivo ?? "", false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Licencia] Intento " + intento + ": " + e);
                if (intento >= 3 || Time.realtimeSinceStartup - inicio > 5f)
                    return (false, "", "no se pudo comprobar su licencia", true);
                await Esperar(0.5f);
            }
        }
    }

    // Espera con el reloj real (no se para si el juego esta en pausa) y sin hilos (web)
    private static async Task Esperar(float segundos)
    {
        float fin = Time.realtimeSinceStartup + segundos;
        while (Time.realtimeSinceStartup < fin) await Awaitable.NextFrameAsync();
    }

    /// <summary>Solo admins: crea codigos nuevos.</summary>
    public static async Task<(List<string> codigos, string mensaje)> CrearCodigos(int cantidad, string nota)
    {
        try
        {
            var r = await CloudCodeService.Instance.CallEndpointAsync<RespCrear>(
                "CrearCodigos", new Dictionary<string, object> { { "cantidad", cantidad }, { "nota", nota ?? "" } });
            return (r.codigos ?? new List<string>(), r.mensaje);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Licencia] " + e);
            return (new List<string>(), "No se pudieron crear: " + e.Message);
        }
    }

    /// <summary>Al cerrar sesion: lo de la cuenta anterior ya no vale.</summary>
    public static void Olvidar()
    {
        _generacion++;
        _comprobando = null;
        Comprobada = Tiene = EsAdmin = false;
        _ticket = null;
        _caduca = 0;
        _caducaLocal = -1f;
    }
}
