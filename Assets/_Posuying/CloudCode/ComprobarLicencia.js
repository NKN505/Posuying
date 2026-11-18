// Posuying: ¿tiene esta cuenta el juego? (ver ComunLicencias.txt)
// Devuelve { tiene, ticket, caduca, esAdmin }.
// El modulo de Cloud Save se carga DENTRO de la funcion: al subir el script, Unity
// lo ejecuta en el PC para leer sus parametros y ese modulo solo existe en el servidor.

// Clave para firmar los tickets: la MISMA que en VerificarTicket.js
const CLAVE = "posuying-cambiar-antes-de-publicar-7f3a91c2e8b54d06";
// Cuentas que pueden crear codigos (Player ID de Unity Cloud > Player Management)
const ADMINS = ["B8Kf98YQqzaQwU3a9fNBFIsULD8j"];   // test123456@
const MINUTOS_TICKET = 15;

// --- HMAC-SHA256 sin modulos (Cloud Code no deja usar "crypto" de Node) ---
function sha256(bytes) {
  const K = [0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
    0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
    0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
    0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
    0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
    0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
    0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
    0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
  const H = [0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19];
  const l = bytes.length, total = ((l + 9 + 63) >> 6) << 6;
  const m = new Uint8Array(total); m.set(bytes); m[l] = 0x80;
  const bits = l * 8; for (let i = 0; i < 8; i++) m[total - 1 - i] = Math.floor(bits / Math.pow(2, 8 * i)) & 0xff;
  const w = new Array(64), r = (x, n) => (x >>> n) | (x << (32 - n));
  for (let o = 0; o < total; o += 64) {
    for (let i = 0; i < 16; i++) w[i] = (m[o+4*i] << 24) | (m[o+4*i+1] << 16) | (m[o+4*i+2] << 8) | m[o+4*i+3];
    for (let i = 16; i < 64; i++) {
      const s0 = r(w[i-15], 7) ^ r(w[i-15], 18) ^ (w[i-15] >>> 3), s1 = r(w[i-2], 17) ^ r(w[i-2], 19) ^ (w[i-2] >>> 10);
      w[i] = (w[i-16] + s0 + w[i-7] + s1) | 0;
    }
    let [a, b, c, d, e, f, g, h] = H;
    for (let i = 0; i < 64; i++) {
      const t1 = (h + (r(e, 6) ^ r(e, 11) ^ r(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + w[i]) | 0;
      const t2 = ((r(a, 2) ^ r(a, 13) ^ r(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
      h = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
    }
    H[0] = (H[0] + a) | 0; H[1] = (H[1] + b) | 0; H[2] = (H[2] + c) | 0; H[3] = (H[3] + d) | 0;
    H[4] = (H[4] + e) | 0; H[5] = (H[5] + f) | 0; H[6] = (H[6] + g) | 0; H[7] = (H[7] + h) | 0;
  }
  const out = new Uint8Array(32);
  for (let i = 0; i < 8; i++) for (let j = 0; j < 4; j++) out[4*i+j] = (H[i] >>> (24 - 8*j)) & 0xff;
  return out;
}
function utf8(s) { const t = unescape(encodeURIComponent(s)); const b = new Uint8Array(t.length); for (let i = 0; i < t.length; i++) b[i] = t.charCodeAt(i); return b; }
function hmacHex(clave, texto) {
  let k = utf8(clave); if (k.length > 64) k = sha256(k);
  const ki = new Uint8Array(64), ko = new Uint8Array(64);
  for (let i = 0; i < 64; i++) { const b = k[i] || 0; ki[i] = b ^ 0x36; ko[i] = b ^ 0x5c; }
  const msg = utf8(texto), dentro = new Uint8Array(64 + msg.length); dentro.set(ki); dentro.set(msg, 64);
  const fuera = new Uint8Array(96); fuera.set(ko); fuera.set(sha256(dentro), 64);
  return Array.from(sha256(fuera), b => b.toString(16).padStart(2, "0")).join("");
}
// --- fin HMAC ---

module.exports = async ({ context }) => {
  const { DataApi } = require("@unity-services/cloud-save-1.4");
  const api = new DataApi(context);
  const playerId = context.playerId;

  const res = await api.getProtectedItems(context.projectId, playerId, ["licencia"]);
  const item = (res.data.results || [])[0];
  const tiene = !!(item && item.value && item.value.posuying === true);

  // Ticket: "playerId|caduca|firma". Solo el servidor conoce la clave para firmarlo
  let ticket = null, caduca = 0;
  if (tiene) {
    caduca = Date.now() + MINUTOS_TICKET * 60 * 1000;
    const datos = playerId + "|" + caduca;
    ticket = datos + "|" + hmacHex(CLAVE, datos);
  }

  return { tiene, ticket, caduca, esAdmin: ADMINS.includes(playerId) };
};
