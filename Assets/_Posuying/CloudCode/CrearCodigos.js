// Posuying: genera codigos de un solo uso. Solo para las cuentas de ADMINS
// (las mismas que en ComprobarLicencia.js). Devuelve { ok, codigos, mensaje }.
// El modulo de Cloud Save se carga DENTRO de la funcion: al subir el script, Unity
// lo ejecuta en el PC para leer sus parametros y ese modulo solo existe en el servidor.

// Cuentas que pueden crear codigos (Player ID de Unity Cloud > Player Management)
const ADMINS = ["B8Kf98YQqzaQwU3a9fNBFIsULD8j"];   // test123456@
const COLECCION = "codigos";
// Sin letras que se confundan (0/O, 1/I)
const LETRAS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

function trozo() {
  let s = "";
  for (let i = 0; i < 4; i++) s += LETRAS[Math.floor(Math.random() * LETRAS.length)];
  return s;
}

module.exports = async ({ params, context }) => {
  const { DataApi } = require("@unity-services/cloud-save-1.4");
  if (!ADMINS.includes(context.playerId))
    return { ok: false, codigos: [], mensaje: "Esta cuenta no puede crear codigos." };

  const cantidad = Math.max(1, Math.min(50, Number(params.cantidad) || 1));
  const nota = String(params.nota || "").slice(0, 60);
  const api = new DataApi(context);
  const codigos = [];

  while (codigos.length < cantidad) {
    const codigo = "PSY-" + trozo() + "-" + trozo();
    const res = await api.getCustomItems(context.projectId, COLECCION, [codigo]);
    if ((res.data.results || []).length > 0) continue;   // ya existe: otro
    await api.setCustomItem(context.projectId, COLECCION, {
      key: codigo,
      value: { usado: false, creado: new Date().toISOString(), por: context.playerId, nota },
    });
    codigos.push(codigo);
  }
  return { ok: true, codigos, mensaje: cantidad + " codigos creados." };
};

module.exports.params = {
  cantidad: { type: "NUMERIC", required: true },
  nota: { type: "STRING", required: false },
};
