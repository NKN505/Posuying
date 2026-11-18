// Posuying: canjea un codigo de un solo uso y apunta el juego en la cuenta.
// Devuelve { ok, mensaje }.
// El modulo de Cloud Save se carga DENTRO de la funcion: al subir el script, Unity
// lo ejecuta en el PC para leer sus parametros y ese modulo solo existe en el servidor.

const COLECCION = "codigos";
const CARACTERES = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

// PSY-XXXX-XXXX (letras mayusculas y numeros)
function formatoValido(codigo) {
  const partes = codigo.split("-");
  if (partes.length !== 3 || partes[0] !== "PSY") return false;
  for (const trozo of [partes[1], partes[2]]) {
    if (trozo.length !== 4) return false;
    for (const c of trozo) if (CARACTERES.indexOf(c) < 0) return false;
  }
  return true;
}

module.exports = async ({ params, context, logger }) => {
  const { DataApi } = require("@unity-services/cloud-save-1.4");
  const api = new DataApi(context);
  const playerId = context.playerId;
  const codigo = String(params.codigo || "").trim().toUpperCase();
  if (!formatoValido(codigo))
    return { ok: false, mensaje: "Ese codigo no tiene el formato correcto (PSY-XXXX-XXXX)." };

  const res = await api.getCustomItems(context.projectId, COLECCION, [codigo]);
  const item = (res.data.results || [])[0];
  if (!item) return { ok: false, mensaje: "Ese codigo no existe." };

  const v = item.value || {};
  if (v.anulado) return { ok: false, mensaje: "Ese codigo esta anulado." };
  if (v.usado && v.cuenta !== playerId) return { ok: false, mensaje: "Ese codigo ya lo ha usado otra cuenta." };

  if (!v.usado) {
    // writeLock: si dos personas lo canjean a la vez, solo una lo consigue
    try {
      await api.setCustomItem(context.projectId, COLECCION, {
        key: codigo,
        value: Object.assign({}, v, { usado: true, cuenta: playerId, canjeado: new Date().toISOString() }),
        writeLock: item.writeLock,
      });
    } catch (e) {
      logger.warning("Canje simultaneo de " + codigo + ": " + e.message);
      return { ok: false, mensaje: "Ese codigo ya lo ha usado otra cuenta." };
    }
  }

  await api.setProtectedItem(context.projectId, playerId, {
    key: "licencia",
    value: { posuying: true, origen: "codigo", codigo, fecha: new Date().toISOString() },
  });
  return { ok: true, mensaje: "Codigo canjeado: ya tienes Posuying en esta cuenta." };
};

module.exports.params = { codigo: { type: "STRING", required: true } };
