// Posuying: copiar texto al portapapeles en el navegador (ver Portapapeles.cs).
// GUIUtility.systemCopyBuffer de Unity no llega al portapapeles del navegador.
mergeInto(LibraryManager.library, {
  PosuyingCopiar: function (ptr) {
    var texto = UTF8ToString(ptr);

    // Metodo antiguo: textarea oculto + execCommand (Safari y navegadores viejos)
    function copiarViejo() {
      var t = document.createElement("textarea");
      t.value = texto;
      t.setAttribute("readonly", "");
      t.style.position = "fixed";
      t.style.top = "0";
      t.style.opacity = "0";
      document.body.appendChild(t);
      t.select();
      t.setSelectionRange(0, texto.length);
      var ok = false;
      try { ok = document.execCommand("copy"); } catch (e) {}
      document.body.removeChild(t);
      return ok;
    }

    function intentar() {
      if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(texto).then(function () { pendiente = false; }, copiarViejo);
      } else if (copiarViejo()) {
        pendiente = false;
      }
    }

    // El navegador solo deja copiar justo despues de un toque o clic. Unity procesa
    // el boton un fotograma despues, asi que se intenta ya y, si no se pudo, se
    // repite en el siguiente toque (durante 10 s).
    var pendiente = true;
    intentar();
    var fin = Date.now() + 10000;
    var alSoltar = function () {
      if (pendiente && Date.now() < fin) intentar();
      if (!pendiente || Date.now() >= fin) {
        document.removeEventListener("pointerup", alSoltar, true);
        document.removeEventListener("touchend", alSoltar, true);
      }
    };
    document.addEventListener("pointerup", alSoltar, true);
    document.addEventListener("touchend", alSoltar, true);
  }
});
