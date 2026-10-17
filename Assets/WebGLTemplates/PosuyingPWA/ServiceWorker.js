// Guarda el juego en el dispositivo para que, instalado como app, se abra
// rapido sin volver a descargar cientos de MB.
//
// OJO: solo se guardan los archivos del propio juego. Las llamadas a los
// servicios de Unity (iniciar sesion, lista de partidas, Relay) van SIEMPRE a
// la red: si se guardaran, la lista de partidas se quedaria congelada.
//
// El nombre lleva la version: al publicar una version nueva se descarga de nuevo
// y la vieja se borra.
const cacheName = {{{ JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-" + PRODUCT_VERSION) }}};

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

  e.respondWith((async function () {
    const cache = await caches.open(cacheName);
    const guardado = await cache.match(e.request);
    if (guardado) return guardado;

    const respuesta = await fetch(e.request);
    if (respuesta.ok) cache.put(e.request, respuesta.clone());
    return respuesta;
  })());
});
