#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Construye el Animator Controller del jugador y los Override Controllers por
/// tipo de arma, sin tocar nada a mano.
///
/// Menu: NKN > Construir Animator del jugador
///
/// Genera un controller NUEVO (AC_Player_Generado). No sobrescribe el que ya
/// tienes: cuando el nuevo te convenza, lo asignas al Animator del Player.
/// </summary>
public static class NKNAnimatorBuilder
{
    const string CARPETA_ANIM = "Assets/NKN/Animations/Player";
    const string CARPETA_SALIDA = "Assets/NKN/Animations/Player";
    const string NOMBRE_CONTROLLER = "AC_Player_Generado";

    // Velocidades de Character.cs: speed = 5, sprintMultiplier = 1.8
    const float V_ANDAR = 5f;
    const float V_CORRER = 9f;

    // Ranuras del blend tree.
    // candidatos: nombres que se buscan, en orden de preferencia.
    // prohibidos: si el nombre del clip contiene alguno, se descarta. Evita que
    //             "Pistol Walk" acabe cogiendo "Pistol Walk Backward".
    static readonly (string clave, float x, float y, string[] candidatos, string[] prohibidos)[] LOCOMOCION =
    {
        ("Idle",     0,  0,        new[]{"Pistol Idle","Idle","Rifle Idle"},
                                   new[]{"kneel","crouch"}),
        ("Walk_F",   0,  V_ANDAR,  new[]{"Pistol Walk","Pistol Run","Walk","Walking","Run"},
                                   new[]{"back","backward","fast","strafe","left","right","rigth"}),
        ("Walk_B",   0, -V_ANDAR,  new[]{"Pistol Walk Backward","Walk Backward","Walking Backwards","Walk Back"},
                                   new[]{"fast","strafe"}),
        ("Walk_L",  -V_ANDAR, 0,   new[]{"Pistol Left","Pistol Strafe Left","Left Strafe Walking","Strafe Left","Left"},
                                   new[]{"fast","back","run"}),
        ("Walk_R",   V_ANDAR, 0,   new[]{"Pistol Rigth","Pistol Right","Pistol Strafe Right","Right Strafe Walking","Strafe Right","Right"},
                                   new[]{"fast","back","run"}),
        ("Run_F",    0,  V_CORRER, new[]{"Fast Run","Pistol Running","Running","Sprint"},
                                   new[]{"back","strafe","left","right","rigth"}),
        ("Run_B",    0, -V_CORRER, new[]{"Run Backward","Fast Run Backward","Running Backward"},
                                   new[]{"strafe"}),
        ("Run_L",   -V_CORRER, 0,  new[]{"Run Left","Fast Run Left","Run Strafe Left","Left Strafe"},
                                   new[]{"walk","back"}),
        ("Run_R",    V_CORRER, 0,  new[]{"Run Rigth","Run Right","Fast Run Right","Run Strafe Right","Right Strafe"},
                                   new[]{"walk","back"}),
    };

    // Menu desactivado a proposito. Regenerar el controller lo reescribe
    // entero y se lleva por delante los ajustes hechos a mano sobre
    // AC_Player_Generado (p.ej. la transicion Jump -> Locomotion).
    // Para volver a habilitarlo, descomenta la linea de abajo.
    //[MenuItem("NKN/Construir Animator del jugador")]
    public static void Construir()
    {
        var clips = CargarClips();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog("NKN",
                "No he encontrado ningun AnimationClip en " + CARPETA_ANIM, "Vale");
            return;
        }

        string ruta = Path.Combine(CARPETA_SALIDA, NOMBRE_CONTROLLER + ".controller");
        var ac = AnimatorController.CreateAnimatorControllerAtPath(ruta);

        // ---------------------------------------------------------- parametros
        ac.AddParameter("MoveX", AnimatorControllerParameterType.Float);
        ac.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
        ac.AddParameter("IsAiming", AnimatorControllerParameterType.Bool);
        ac.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        ac.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);
        ac.AddParameter("Grip", AnimatorControllerParameterType.Int);
        ac.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Vault", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
        ac.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        var maquina = ac.layers[0].stateMachine;
        var faltan = new List<string>();

        // ------------------------------------------------------- locomocion
        var arbol = new BlendTree
        {
            name = "Locomotion",
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveZ",
            useAutomaticThresholds = false,
        };
        // Sin esto el arbol aparece como un asset suelto en el Project.
        arbol.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(arbol, ac);

        var hijos = new List<ChildMotion>();
        var usados = new HashSet<AnimationClip>();
        foreach (var (clave, x, y, candidatos, prohibidos) in LOCOMOCION)
        {
            var clip = Buscar(clips, candidatos, prohibidos, usados);
            if (clip == null) { faltan.Add(clave + "  (" + string.Join(" / ", candidatos) + ")"); continue; }
            usados.Add(clip);
            hijos.Add(new ChildMotion
            {
                motion = clip,
                position = new Vector2(x, y),
                timeScale = 1f,
                directBlendParameter = "MoveX",
            });
        }
        arbol.children = hijos.ToArray();

        var locomocion = maquina.AddState("Locomotion", new Vector3(300, 0, 0));
        locomocion.motion = arbol;
        locomocion.writeDefaultValues = false;
        maquina.defaultState = locomocion;

        // ------------------------------------------------------------ salto
        var salto = Estado(ac, maquina, clips, "Jump",
               new[]{"Pisto Jump","Pistol Jump","Jump","Jumping"},
               new[]{"over","vault"},
               "Jump", new Vector3(620, -120, 0), faltan);
        // Sin salida, el Animator se quedaria en Jump para siempre.
        Volver(salto, locomocion, 0.85f);

        var vault = Estado(ac, maquina, clips, "Vault",
               new[]{"Pistol Jump over","Jump Over","Vault"},
               null,
               "Vault", new Vector3(620, -40, 0), faltan);
        Volver(vault, locomocion, 0.90f);

        // --------------------------------------------------------- agachado
        var agachado = Estado(ac, maquina, clips, "Kneeling",
               new[]{"Pistol Kneeling Idle","Pistol Kneeling","Kneeling","Crouch Idle"},
               null, null,
               new Vector3(620, 40, 0), faltan);
        if (agachado != null)
        {
            var aCrouch = locomocion.AddTransition(agachado);
            aCrouch.hasExitTime = false; aCrouch.duration = 0.15f;
            aCrouch.AddCondition(AnimatorConditionMode.If, 0, "Crouch");

            var deCrouch = agachado.AddTransition(locomocion);
            deCrouch.hasExitTime = false; deCrouch.duration = 0.15f;
            deCrouch.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
        }

        // ----------------------------------------------------------- muerte
        var muerte = Estado(ac, maquina, clips, "Death",
               new[]{"Death 1","Death","Falling Back Death"},
               null, "Die",
               new Vector3(620, 140, 0), faltan);
        if (muerte != null)
        {
            muerte.writeDefaultValues = false;
            // la muerte no vuelve: a partir de aqui manda el ragdoll
            // sin transicion de salida: a partir de aqui manda el ragdoll
        }

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();

        // ------------------------------------------- overrides por tipo de arma
        // OneHanded no lleva override: el controller base YA es el set de
        // pistola, asi que PlayerAnimation cae a el cuando la ranura esta vacia.
        string[] sets = { "Unarmed", "TwoHanded", "Melee" };

        // Si quedaba un OC_OneHanded de una ejecucion anterior, sobra.
        string rutaSobra = Path.Combine(CARPETA_SALIDA, "OC_OneHanded.overrideController");
        if (AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(rutaSobra) != null)
        {
            AssetDatabase.DeleteAsset(rutaSobra);
            Debug.Log("[NKN] Eliminado OC_OneHanded: el controller base ya es el set de pistola.");
        }
        foreach (var s in sets)
        {
            string rutaOv = Path.Combine(CARPETA_SALIDA, "OC_" + s + ".overrideController");
            var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(rutaOv);
            if (ov == null)
            {
                ov = new AnimatorOverrideController(ac);
                AssetDatabase.CreateAsset(ov, rutaOv);
            }
            else
            {
                ov.runtimeAnimatorController = ac;   // conserva los clips ya asignados
            }
            EditorUtility.SetDirty(ov);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string msg = "Creado " + ruta + "\n\nBlend tree con " + hijos.Count + " de 9 clips.\n" +
                     "Overrides: OC_Unarmed, OC_TwoHanded, OC_Melee.\n" +
                     "La pistola usa el controller base directamente (sin override).";
        if (faltan.Count > 0)
            msg += "\n\nClips que no he encontrado:\n  - " + string.Join("\n  - ", faltan);

        Debug.Log("[NKN] " + msg);
        EditorUtility.DisplayDialog("NKN - Animator construido", msg, "Vale");
        Selection.activeObject = ac;
    }

    // ------------------------------------------------------------- utilidades

    /// <summary>Transicion de vuelta a locomocion cuando el clip termina.</summary>
    static void Volver(AnimatorState desde, AnimatorState hacia, float exitTime)
    {
        if (desde == null || hacia == null) return;
        var t = desde.AddTransition(hacia);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.duration = 0.15f;
    }

    static AnimatorState Estado(AnimatorController ac, AnimatorStateMachine maquina,
                                List<AnimationClip> clips, string nombre,
                                string[] candidatos, string[] prohibidos, string trigger,
                                Vector3 pos, List<string> faltan)
    {
        var clip = Buscar(clips, candidatos, prohibidos, null);
        if (clip == null) { faltan.Add(nombre + "  (" + string.Join(" / ", candidatos) + ")"); return null; }

        var st = maquina.AddState(nombre, pos);
        st.motion = clip;
        st.writeDefaultValues = false;

        if (!string.IsNullOrEmpty(trigger))
        {
            var t = maquina.AddAnyStateTransition(st);
            t.hasExitTime = false;
            t.duration = 0.1f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
        }
        return st;
    }

    static List<AnimationClip> CargarClips()
    {
        var lista = new List<AnimationClip>();
        if (!AssetDatabase.IsValidFolder(CARPETA_ANIM)) return lista;

        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { CARPETA_ANIM }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
            {
                var c = o as AnimationClip;
                // los clips de preview del importador empiezan por "__"
                if (c != null && !c.name.StartsWith("__")) lista.Add(c);
            }
        }
        return lista;
    }

    /// <summary>Normaliza para comparar: minusculas, sin espacios ni signos,
    /// y corrigiendo erratas frecuentes.</summary>
    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString()
                 .Replace("rigth", "right")
                 .Replace("pisto1", "pistol");
    }

    static bool Prohibido(AnimationClip c, string[] prohibidos)
    {
        if (prohibidos == null) return false;
        string n = Norm(c.name);
        foreach (var p in prohibidos)
            if (n.Contains(Norm(p))) return true;
        return false;
    }

    static AnimationClip Buscar(List<AnimationClip> clips, string[] candidatos,
                                string[] prohibidos, HashSet<AnimationClip> usados)
    {
        var libres = clips.Where(c => !Prohibido(c, prohibidos) &&
                                      (usados == null || !usados.Contains(c))).ToList();

        // 1) coincidencia exacta normalizada
        foreach (var nombre in candidatos)
        {
            string n = Norm(nombre);
            var m = libres.FirstOrDefault(c => Norm(c.name) == n);
            if (m != null) return m;
        }
        // 2) el nombre del clip contiene el candidato
        foreach (var nombre in candidatos)
        {
            string n = Norm(nombre);
            var m = libres.FirstOrDefault(c => Norm(c.name).Contains(n));
            if (m != null) return m;
        }
        return null;
    }
}
#endif
