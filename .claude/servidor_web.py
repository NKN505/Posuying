# Servidor local para probar la build web de Posuying (Builds/Web).
# Igual que "python -m http.server", pero con las cabeceras COOP/COEP: sin ellas
# el navegador no da SharedArrayBuffer y el audio de Wwise no arranca.
import http.server, sys, functools

class Manejador(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()

puerto = int(sys.argv[1]) if len(sys.argv) > 1 else 5180
carpeta = sys.argv[2] if len(sys.argv) > 2 else "."
http.server.ThreadingHTTPServer(("127.0.0.1", puerto), functools.partial(Manejador, directory=carpeta)).serve_forever()
