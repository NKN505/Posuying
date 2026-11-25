using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using UnityEngine;

// ¿Tiene esta cuenta el juego? Lo decide el servidor de Unity (Cloud Code, ver
// Assets/_Posuying/CloudCode), nunca el propio juego:
//
//   - La licencia se guarda en la cuenta, en un dato que solo el servidor puede
//     escribir. Vale en todas las plataformas y no sirve a otra persona.
//   - Quien la tiene recibe un "ticket" firmado (dura 15 min) que enseña al
//     entrar en una partida; el anfitrion lo comprueba con el servidor.
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
    private static long _caduca;

    // Respuestas de los scripts (los nombres coinciden con el JSON que devuelven)
    private class RespComprobar { public bool tiene; public string ticket; public long caduca; public bool esAdmin; }
    private class RespCanjear { public bool ok; public string mensaje; }
    private class RespVerificar { public bool ok; public string playerId; public string motivo; }
    private class RespCrear { public bool ok; public List<string> codigos; public string mensaje; }

    /// <summary>Pregunta al servidor. Devuelve null si ha ido bien, o el error.</summary>
    public static async Task<string> Comprobar()
    {
        try
        {
            var r = await CloudCodeService.Instance.CallEndpointAsync<RespComprobar>(
                "ComprobarLicencia", new Dictionary<string, object>());
            Tiene = r.tiene;
            EsAdmin = r.esAdmin;
            _ticket = r.ticket;
            _caduca = r.caduca;
            Comprobada = true;
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Licencia] " + e);
            return "No se pudo comprobar la licencia: " + e.Message;
        }
    }

    /// <summary>Ticket para entrar en una partida (se renueva solo si va a caducar).</summary>
    public static async Task<string> TicketVigente()
    {
        long ahora = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // Se renueva si le quedan menos de 6 min (OnlineSession lo pide cada 5 durante la partida)
        if (!Comprobada || (Tiene && ahora > _caduca - 6 * 60000)) await Comprobar();
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
    /// Devuelve si vale, de que cuenta es y, si no vale, por que.
    /// </summary>
    public static async Task<(bool ok, string playerId, string motivo)> Verificar(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return (false, "", "no tiene el juego");
        try
        {
            var r = await CloudCodeService.Instance.CallEndpointAsync<RespVerificar>(
                "VerificarTicket", new Dictionary<string, object> { { "ticket", ticket } });
            return (r.ok, r.playerId ?? "", r.motivo ?? "");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Licencia] " + e);
            return (false, "", "no se pudo comprobar su licencia");
        }
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
        Comprobada = Tiene = EsAdmin = false;
        _ticket = null;
        _caduca = 0;
    }
}
