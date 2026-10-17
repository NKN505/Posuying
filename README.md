# Posuying - version web

Esta rama solo tiene la build web del juego. **No se edita a mano**: la genera
Unity con `Posuying > Multiplataforma > Publicar web (rama web para Vercel)`.

Vercel la publica sola en cada push a esta rama.

Configuracion del proyecto en Vercel (una sola vez):
- Framework Preset: Other
- Build Command: vacio (no hay que compilar nada)
- Output Directory: `.`
- Production Branch: `web`

Antes de publicar una version nueva, sube la version del juego en Unity
(Project Settings > Player > Version): asi los moviles que ya la instalaron
como app se actualizan, y solo juegan juntos jugadores con la misma version.
