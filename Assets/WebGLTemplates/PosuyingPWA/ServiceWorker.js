// Lo necesita la web para poder instalarse como app (PWA).
//
// SIEMPRE PRIMERO LA RED. Antes servia lo guardado en el dispositivo sin preguntar,
// y cada jugador acababa con una build vieja, o con archivos mezclados de builds
// distintas (pantalla negra, "no tiene el juego"...). No hace falta guardar el juego
// aqui: el navegador ya lo tiene en su cache normal y, como Vercel manda
// "no-cache", solo vuelve a descargar lo que haya cambiado (lo demas, el servidor
// contesta "sin cambios" en un momento).
//
// Lo guardado solo se usa sin conexion, y solo la pagina (no los cientos de MB del
// juego: sin internet no se puede jugar online de todas formas).
//
// OJO: las llamadas a los servicios de Unity (iniciar sesion, partidas, Relay) no
// pasan por aqui: son de otro dominio y van a la red tal cual.
//
// El nombre lleva la version y la fecha de la build (la pone Multiplataforma.Compilar);
// al cambiar, lo guardado de antes se borra.
const cacheName = {{{ JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-" + PRODUCT_VERSION) }}} + "-__BUILD__";

self.addEventListener("install", function (e) {
  self.skipWaiting();
});

self.addEventListener("activate", function (e) {
  e.waitUntil((async function () {
    for (const nombre of await caches.keys())
      if (nombre !== cacheName) await caches.delete(nombre);
    await self.clients.claim();
  })());
});

self.addEventListener("fetch", function (e) {
  const url = new URL(e.request.url);
  const delJuego = e.request.method === "GET" && url.origin === self.location.origin;
  if (!delJuego) return;   // todo lo demas, a la red tal cual

  // Los archivos grandes del juego no se guardan (ver arriba)
  const pesado = url.pathname.indexOf("/Build/") >= 0 || url.pathname.indexOf("/StreamingAssets/") >= 0;

  e.respondWith((async function () {
    try {
      const respuesta = await fetch(e.request);
      if (respuesta.ok && !pesado) {
        const copia = respuesta.clone();
        caches.open(cacheName).then(function (cache) { return cache.put(e.request, copia); });
      }
      return respuesta;
    } catch (err) {
      const guardado = await caches.match(e.request);
      if (guardado) return guardado;
      throw err;
    }
  })());
});
