using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

// La cuenta del jugador en los servicios de Unity: usuario y contrasena.
//
// Es la base del futuro sistema de licencias: lo que compre un jugador (Steam,
// App Store, tienda propia) se apuntara en su cuenta, asi que la misma compra
// vale en todas las plataformas y no sirve a otra persona. Usuario y contrasena
// funciona en PC, movil y navegador (Unity Player Accounts no funciona en web).
// Mas adelante se podran vincular Steam y Apple a esta misma cuenta.
//
// La sesion se recuerda: al volver a abrir el juego se entra solo en la ultima
// cuenta (Unity guarda un token en el dispositivo).
//
// Para jugar online hace falta cuenta. En el editor y en las builds de
// desarrollo se puede seguir entrando como anonimo, para las pruebas.
public static class Cuenta
{
    /// <summary>Hay que tener cuenta para jugar online (fuera del editor y de builds de desarrollo).</summary>
    public static bool Obligatoria => !(Application.isEditor || Debug.isDebugBuild);

    /// <summary>Hay sesion iniciada con una cuenta de verdad (no anonima).</summary>
    public static bool Conectada => SesionIniciada && !string.IsNullOrEmpty(Usuario);

    /// <summary>Nombre de usuario de la cuenta, o vacio si es anonima o no hay sesion.</summary>
    public static string Usuario => SesionIniciada ? (AuthenticationService.Instance.PlayerInfo?.Username ?? "") : "";

    private static bool SesionIniciada =>
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    // ---------- Arranque ----------

    /// <summary>
    /// Deja los servicios de Unity listos con el perfil indicado y, si hay una sesion
    /// guardada en este dispositivo, entra en ella. No crea cuentas anonimas.
    /// </summary>
    public static Task Preparar(string perfil)
    {
        // Lo llaman a la vez la pestana CUENTA, crear/buscar partida y ENTRAR. Dos
        // inicios de sesion a la vez hacen fallar al segundo ("already signing in"):
        // todos esperan al mismo.
        if (_preparando == null || _preparando.IsCompleted) _preparando = PrepararAhora(perfil);
        return _preparando;
    }

    private static Task _preparando;

    private static async Task PrepararAhora(string perfil)
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn) return;

        // El perfil separa la identidad del editor y la de la build en el mismo PC
        if (!string.IsNullOrEmpty(perfil) && auth.Profile != perfil) auth.SwitchProfile(perfil);

        // Con un token guardado, "anonimo" en realidad vuelve a entrar en esa cuenta
        if (auth.SessionTokenExists) await auth.SignInAnonymouslyAsync();
    }

    // ---------- Entrar, crear, salir ----------

    /// <summary>Entra con usuario y contrasena. Devuelve null si todo bien, o el error para el jugador.</summary>
    public static async Task<string> Entrar(string perfil, string usuario, string contrasena)
    {
        string error = ValidarUsuario(usuario) ?? (string.IsNullOrEmpty(contrasena) ? "Escribe la contrasena." : null);
        if (error != null) return error;

        try
        {
            await Preparar(perfil);
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn) auth.SignOut(true);   // la sesion anterior (anonima u otra cuenta)
            await auth.SignInWithUsernamePasswordAsync(usuario.Trim(), contrasena);
            return null;
        }
        catch (System.Exception e)
        {
            return Explicar(e);
        }
    }

    /// <summary>
    /// Crea una cuenta nueva. Si ya se estaba jugando como anonimo, esa identidad
    /// pasa a ser la cuenta (no se pierde nada). Devuelve null si todo bien, o el error.
    /// </summary>
    public static async Task<string> Crear(string perfil, string usuario, string contrasena)
    {
        string error = ValidarUsuario(usuario) ?? ValidarContrasena(contrasena);
        if (error != null) return error;

        try
        {
            await Preparar(perfil);
            var auth = AuthenticationService.Instance;
            if (auth.IsSignedIn && string.IsNullOrEmpty(Usuario))
                await auth.AddUsernamePasswordAsync(usuario.Trim(), contrasena);
            else
            {
                if (auth.IsSignedIn) auth.SignOut(true);
                await auth.SignUpWithUsernamePasswordAsync(usuario.Trim(), contrasena);
            }
            return null;
        }
        catch (System.Exception e)
        {
            return Explicar(e);
        }
    }

    /// <summary>Cierra la sesion y olvida la cuenta en este dispositivo.</summary>
    public static void Salir()
    {
        if (SesionIniciada) AuthenticationService.Instance.SignOut(true);
    }

    // ---------- Reglas de Unity ----------

    // Usuario: 3-20 caracteres; letras, numeros y . - @ _
    public static string ValidarUsuario(string u)
    {
        u = (u ?? "").Trim();
        if (u.Length < 3 || u.Length > 20) return "El usuario debe tener entre 3 y 20 caracteres.";
        if (!Regex.IsMatch(u, @"^[A-Za-z0-9.\-@_]+$")) return "El usuario solo admite letras sin tilde, numeros y . - @ _";
        return null;
    }

    // Contrasena: 8-30 caracteres con mayuscula, minuscula, numero y simbolo
    public static string ValidarContrasena(string c)
    {
        c = c ?? "";
        if (c.Length < 8 || c.Length > 30) return "La contrasena debe tener entre 8 y 30 caracteres.";
        if (!Regex.IsMatch(c, "[A-Z]") || !Regex.IsMatch(c, "[a-z]") || !Regex.IsMatch(c, "[0-9]") ||
            !Regex.IsMatch(c, "[^A-Za-z0-9]"))
            return "La contrasena necesita una mayuscula, una minuscula, un numero y un simbolo.";
        return null;
    }

    private static string Explicar(System.Exception e)
    {
        string m = e.Message ?? "";
        Debug.LogWarning("[Cuenta] " + e);
        if (m.Contains("WRONG_USERNAME_PASSWORD") || m.Contains("Invalid username or password"))
            return "Usuario o contrasena incorrectos.";
        if (m.Contains("USERNAME_PASSWORD_INVALID") || m.Contains("already exists") || m.Contains("ENTITY_EXISTS"))
            return "Ese usuario ya existe. Elige otro o entra con el tuyo.";
        if (m.Contains("not enabled") || m.Contains("PROVIDER") || m.Contains("Forbidden"))
            return "El inicio de sesion con usuario no esta activado en el proyecto de Unity Cloud.";
        if (m.Contains("network") || m.Contains("Network") || m.Contains("Cannot connect"))
            return "Sin conexion con Unity. Revisa tu conexion a internet.";
        return "No se pudo: " + m;
    }
}
