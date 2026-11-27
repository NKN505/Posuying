// Posuying: el anfitrion comprueba el ticket de quien quiere entrar en su partida.
// Devuelve { ok, playerId, motivo }.
// El modulo de Cloud Save se carga DENTRO de la funcion: al subir el script, Unity
// lo ejecuta en el PC para leer sus parametros y ese modulo solo existe en el servidor.
//
// El ticket es "playerId|codigo" (ver ComprobarLicencia.js): vale si el codigo es
// el que esa cuenta tiene guardado, no ha caducado y la cuenta tiene el juego.

module.exports = async ({ params, context }) => {
  const { DataApi } = require("@unity-services/cloud-save-1.4");
  const partes = String(params.ticket || "").split("|");

  // Los tickets de antes (playerId|caduca|firma) ya no valen
  if (partes.length === 3) return { ok: false, playerId: "", motivo: "su juego esta desactualizado" };
  if (partes.length !== 2 || !partes[0] || !partes[1])
    return { ok: false, playerId: "", motivo: "ticket mal formado" };

  const [playerId, codigo] = partes;
  const api = new DataApi(context);
  const res = await api.getProtectedItems(context.projectId, playerId, ["licencia", "ticket"]);
  const items = res.data.results || [];
  const licencia = items.find(i => i.key === "licencia");
  const guardado = items.find(i => i.key === "ticket");

  // Primero el codigo: sin el, no se dice nada mas de esa cuenta
  if (!(guardado && guardado.value && guardado.value.codigo === codigo))
    return { ok: false, playerId: "", motivo: "ticket no valido" };
  if (Number(guardado.value.caduca) < Date.now())
    return { ok: false, playerId, motivo: "ticket caducado" };
  // Por si la licencia se ha retirado (devolucion) despues de emitir el ticket
  if (!(licencia && licencia.value && licencia.value.posuying === true))
    return { ok: false, playerId, motivo: "sin licencia" };

  return { ok: true, playerId, motivo: "" };
};

module.exports.params = { ticket: { type: "STRING", required: true } };
