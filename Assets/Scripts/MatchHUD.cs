using Unity.Netcode;
using UnityEngine;

// Interfaz de la partida: vidas del equipo, estado de abatido, barra de
// reanimacion y pantalla de derrota.
//
// Usa OnGUI (como el menu de Escape) porque son avisos puntuales que aparecen
// y desaparecen, y asi no hay que montar objetos de interfaz.
//
// Se pone en cualquier objeto de la escena (por ejemplo el NetworkManager).
public class MatchHUD : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !(nm.IsClient || nm.IsServer)) return;
        if (MatchManager.Instance == null) return;

        Matrix4x4 previous = GUI.matrix;
        float scale = Screen.height / ReferenceHeight;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

        float refWidth = ReferenceHeight * ((float)Screen.width / Screen.height);

        if (MatchManager.Instance.MatchOver)
        {
            DrawDefeat(refWidth);
        }
        else
        {
            DrawLives(refWidth);
            DrawLocalState(refWidth);
            DrawReviveBar(refWidth);
        }

        GUI.matrix = previous;
    }

    // ---------- Vidas del equipo ----------

    private void DrawLives(float refWidth)
    {
        int lives = MatchManager.Instance.Lives;

        string color = lives <= 1 ? "#ff6666" : lives <= 2 ? "#ffcc55" : "#ffffff";
        string text = "<color=" + color + "><b>VIDAS DE EQUIPO: " + lives + "</b></color>";

        GUI.Label(new Rect(refWidth / 2f - 200f, 16f, 400f, 30f), text, Centered(20));
    }

    // ---------- Estado del jugador local ----------

    private void DrawLocalState(float refWidth)
    {
        var state = LocalState();
        if (state == null) return;

        if (state.IsOut)
        {
            GUI.Label(new Rect(refWidth / 2f - 300f, ReferenceHeight / 2f - 40f, 600f, 80f),
                "<color=#ff8888><b>ESTAS FUERA DE COMBATE</b></color>\nEsperando a que termine la partida",
                Centered(26));
            return;
        }

        if (!state.IsDowned) return;

        int seconds = Mathf.CeilToInt(state.BleedRemaining);
        bool hasLives = MatchManager.Instance.Lives > 0;

        string message =
            "<color=#ff6666><b>ABATIDO</b></color>\n" +
            "Aguanta " + seconds + " s\n" +
            "Un companero puede levantarte manteniendo E\n";

        // Sin vidas no tiene sentido ofrecer gastar una
        message += hasLives
            ? "Pulsa <b>R</b> para gastar una vida del equipo"
            : "<color=#ff8888>No quedan vidas: solo pueden levantarte</color>";

        GUI.Label(new Rect(refWidth / 2f - 340f, ReferenceHeight / 2f - 90f, 680f, 180f),
            message, Centered(24));

        // Barra de la reanimacion que me estan haciendo
        if (state.ReviveProgress > 0f)
            DrawBar(refWidth / 2f - 170f, ReferenceHeight / 2f + 100f, 340f, 22f,
                    state.ReviveProgress, "Te estan levantando");
    }

    // ---------- Barra al levantar a otro ----------

    private void DrawReviveBar(float refWidth)
    {
        var state = LocalState();
        if (state == null || !state.CanAct) return;

        var target = state.FindNearbyDowned();
        if (target == null) return;

        if (target.ReviveProgress > 0f)
        {
            DrawBar(refWidth / 2f - 170f, ReferenceHeight / 2f + 140f, 340f, 22f,
                    target.ReviveProgress, "Levantando...");
        }
        else
        {
            GUI.Label(new Rect(refWidth / 2f - 250f, ReferenceHeight / 2f + 140f, 500f, 30f),
                "Manten <b>E</b> para levantar a tu companero", Centered(20));
        }
    }

    // ---------- Derrota ----------

    private void DrawDefeat(float refWidth)
    {
        float w = 560f, h = 300f;
        Rect panel = new Rect((refWidth - w) / 2f, (ReferenceHeight - h) / 2f, w, h);

        Color previousColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.9f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = previousColor;

        GUI.Label(new Rect(panel.x, panel.y + 30f, w, 60f),
            "<color=#ff6666><b>HABEIS CAIDO</b></color>", Centered(40));

        GUI.Label(new Rect(panel.x, panel.y + 95f, w, 40f),
            "No quedan vidas de equipo", Centered(20));

        GUIStyle button = new GUIStyle(GUI.skin.button) { fontSize = 18 };

        if (GUI.Button(new Rect(panel.x + 60f, panel.y + 165f, w - 120f, 46f),
                       "Reiniciar partida", button))
        {
            MatchManager.Instance.RestartMatchServerRpc();
        }

        if (GUI.Button(new Rect(panel.x + 60f, panel.y + 222f, w - 120f, 46f),
                       "Volver al menu", button))
        {
            var session = FindFirstObjectByType<OnlineSession>();
            if (session != null && session.HasSession) session.LeaveOnlineGame();
            else NetworkManager.Singleton.Shutdown();
        }
    }

    // ---------- Utilidades ----------

    private PlayerDownedState LocalState()
    {
        var player = NetworkPlayer.LocalPlayer;
        return player != null ? player.GetComponent<PlayerDownedState>() : null;
    }

    private void DrawBar(float x, float y, float w, float h, float progress, string label)
    {
        Color previousColor = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

        GUI.color = new Color(0.4f, 0.9f, 0.4f, 0.95f);
        GUI.DrawTexture(new Rect(x + 2f, y + 2f, (w - 4f) * Mathf.Clamp01(progress), h - 4f),
                        Texture2D.whiteTexture);

        GUI.color = previousColor;

        GUI.Label(new Rect(x, y - 26f, w, 24f), label, Centered(18));
    }

    private GUIStyle Centered(int size)
    {
        return new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
    }
}
