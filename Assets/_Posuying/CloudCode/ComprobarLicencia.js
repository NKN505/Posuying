// Posuying: ¿tiene esta cuenta el juego? (ver ComunLicencias.txt)
// Devuelve { tiene, ticket, caduca, quedanMs, esAdmin }.
// El modulo de Cloud Save se carga DENTRO de la funcion: al subir el script, Unity
// lo ejecuta en el PC para leer sus parametros y ese modulo solo existe en el servidor.
//
// El ticket es "playerId|codigo": un codigo al azar que se guarda en el dato
// PROTEGIDO "ticket" de la cuenta. VerificarTicket lo compara con el guardado.
// Asi no hay ninguna clave secreta en el script (el repositorio es publico: una
// clave escrita aqui la podria leer cualquiera y fabricar tickets).

// Cuentas que pueden crear codigos (Player ID de Unity Cloud > Player Management)
const ADMINS = ["B8Kf98YQqzaQwU3a9fNBFIsULD8j"];   // test123456@
const MINUTOS_TICKET = 30;
// Si al ticket guardado le quedan mas minutos que estos, se devuelve el mismo
// (el juego pide uno nuevo cuando le quedan menos de 6)
const MINUTOS_REUSO = 10;

function codigoAlAzar() {
  const bytes = new Uint8Array(16);
  const c = globalThis.crypto;
  if (c && typeof c.getRandomValues === "function") c.getRandomValues(bytes);
  else for (let i = 0; i < bytes.length; i++) bytes[i] = Math.floor(Math.random() * 256);
  return Array.from(bytes, b => b.toString(16).padStart(2, "0")).join("");
}

module.exports = async ({ context, logger }) => {
  const { DataApi } = require("@unity-services/cloud-save-1.4");
  const api = new DataApi(context);
  const { projectId, playerId } = context;
  const esAdmin = ADMINS.includes(playerId);

  const res = await api.getProtectedItems(projectId, playerId, ["licencia", "ticket"]);
  const items = res.data.results || [];
  const licencia = items.find(i => i.key === "licencia");
  const guardado = items.find(i => i.key === "ticket");

  const tiene = !!(licencia && licencia.value && licencia.value.posuying === true);
  if (!tiene) return { tiene, ticket: null, caduca: 0, quedanMs: 0, esAdmin };

  const ahora = Date.now();
  const respuesta = t => ({ tiene, ticket: playerId + "|" + t.codigo, caduca: t.caduca, quedanMs: t.caduca - ahora, esAdmin });

  if (guardado && guardado.value && guardado.value.caduca - ahora > MINUTOS_REUSO * 60 * 1000)
    return respuesta(guardado.value);

  const nuevo = { codigo: codigoAlAzar(), caduca: ahora + MINUTOS_TICKET * 60 * 1000 };
  try {
    const cuerpo = { key: "ticket", value: nuevo };
    if (guardado) cuerpo.writeLock = guardado.writeLock;   // si otra llamada lo cambia a la vez, falla
    await api.setProtectedItem(projectId, playerId, cuerpo);
  } catch (e) {
    // Otra llamada de esta misma cuenta lo ha renovado a la vez: vale el suyo
    logger.warning("Ticket renovado a la vez para " + playerId + ": " + e.message);
    const otra = await api.getProtectedItems(projectId, playerId, ["ticket"]);
    const t = (otra.data.results || [])[0];
    if (!t || !t.value) throw e;
    return respuesta(t.value);
  }
  return respuesta(nuevo);
};
