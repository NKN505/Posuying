using UnityEngine;
using UnityEngine.UI;

public class EnemyHealthBar : MonoBehaviour
{
    public EnemyBehaviour enemy;
    public Image fillBar;
    public Image background;
    public float maxHealth = 1000f;

    private float _fullWidth;

    // Camara a la que miran TODAS las barras. Es compartida porque siempre es la
    // misma, y asi se resuelve una vez por frame en vez de una por enemigo.
    private static Camera _shared;
    private static int _resolvedFrame = -1;

    // OJO: LateUpdate, no Update.
    //
    // La barra es HIJA del enemigo, asi que hereda su rotacion. Unity ejecuta
    // primero todos los Update() y DESPUES mueve y rota a los personajes (la
    // navegacion y el CharacterController). Si orientaramos la barra en Update,
    // el movimiento posterior del padre desharia esa orientacion en el mismo
    // frame: unos pocos grados en los enemigos que giran suave (no se nota) y
    // un bandazo entero en el saltarin, que se mueve a saltos.
    //
    // LateUpdate corre cuando ya se ha movido todo, asi que la orientacion es
    // la ultima palabra y no depende del orden de ejecucion de los scripts.
    void LateUpdate()
    {
        if (enemy == null || !enemy.gameObject.activeSelf) return;

        Transform cam = ActiveCamera();
        if (cam == null) return;

        FaceCamera(cam);

        if (fillBar == null) return;

        EnsureFullWidth();

        // La vida maxima la manda el servidor (el director de hordas la fija al crear el enemigo)
        maxHealth = enemy.GetMaxHealth();
        float ratio = maxHealth > 0f ? Mathf.Clamp01(enemy.GetHealth() / maxHealth) : 0f;

        Vector2 size = fillBar.rectTransform.sizeDelta;
        size.x = _fullWidth * ratio;
        fillBar.rectTransform.sizeDelta = size;
        fillBar.color = Color.Lerp(Color.red, Color.green, ratio);

        // Centrar va DESPUES de cambiar la anchura, y cada frame en vez de una
        // sola vez al arrancar: hacerlo en Start dependia de cuando se activaba
        // cada enemigo, y por eso unos salian centrados y otros ladeados.
        Center(background);
        Center(fillBar.rectTransform);
    }

    // A donde tiene que mirar la barra.
    //
    // OJO: antes esto se guardaba UNA VEZ y no se volvia a mirar. El problema es
    // que al principio la camara activa es la del menu, y MenuCamera la APAGA en
    // cuanto aparece tu jugador... pero sin destruirla. Los enemigos que ya
    // existian se quedaban mirando a esa camara apagada para siempre (su
    // Transform sigue vivo, asi que nunca volvia a ser null) y su barra dejaba
    // de seguirte, mientras que los que aparecian despues si funcionaban.
    //
    // Ahora se comprueba que siga siendo valida y, si no, se busca otra.
    private static Transform ActiveCamera()
    {
        if (_resolvedFrame == Time.frameCount)
            return _shared != null ? _shared.transform : null;

        _resolvedFrame = Time.frameCount;

        if (IsUsable(_shared)) return _shared.transform;

        // La del jugador local es la buena; Camera.main solo como respaldo
        var local = NetworkPlayer.LocalPlayer;
        if (local != null)
        {
            Camera own = local.GetComponentInChildren<Camera>(true);
            if (IsUsable(own))
            {
                _shared = own;
                return own.transform;
            }
        }

        _shared = Camera.main;
        return _shared != null ? _shared.transform : null;
    }

    // Orienta la barra hacia la camara, pero SOLO girando sobre el eje Y.
    //
    // Antes se usaba LookAt, que apunta a la POSICION de la camara, altura
    // incluida. Con un enemigo a tu misma altura apenas se nota, pero con uno
    // que salta muy por encima de ti la barra tiene que inclinarse para
    // "mirarte": se escorza y se ve como una linea torcida. Ese era el caso del
    // saltarin, el unico enemigo que se eleva de verdad.
    //
    // Anulando la componente vertical, la barra siempre queda horizontal y
    // legible, este el enemigo por los aires o en un piso de arriba.
    private void FaceCamera(Transform cam)
    {
        // Del observador hacia la barra: asi la cara visible del Canvas queda
        // mirando a la camara y no hace falta el giro de 180 grados de antes.
        Vector3 away = transform.position - cam.position;
        away.y = 0f;

        // Justo encima o debajo de la camara no hay direccion horizontal valida
        if (away.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }

    private static bool IsUsable(Camera cam)
    {
        return cam != null && cam.enabled && cam.gameObject.activeInHierarchy;
    }

    private void EnsureFullWidth()
    {
        if (_fullWidth > 0f) return;

        // Del fondo mejor que del relleno: el fondo nunca encoge, asi que su
        // anchura es siempre la de la barra completa.
        RectTransform reference = background != null
            ? background.rectTransform
            : fillBar.rectTransform;

        _fullWidth = reference.rect.width;
    }

    private void Center(Image image)
    {
        if (image != null) Center(image.rectTransform);
    }

    // Deja la barra centrada sobre el eje del enemigo.
    //
    // El pivote esta en el borde izquierdo (hace falta para que la vida se vacie
    // hacia la izquierda al encoger la anchura), pero anclado al centro con
    // posicion 0 ese borde cae sobre el eje y la barra entera queda a la derecha.
    //
    // Lo que se fija es el BORDE IZQUIERDO, en -mitad de la anchura completa.
    // Asi el relleno mengua sin moverse y el conjunto queda centrado. La formula
    // vale para cualquier pivote, no solo para el 0.
    private void Center(RectTransform rt)
    {
        if (rt == null || _fullWidth <= 0f) return;

        Vector2 position = rt.anchoredPosition;
        position.x = -_fullWidth * 0.5f + rt.pivot.x * rt.rect.width;
        rt.anchoredPosition = position;
    }
}
