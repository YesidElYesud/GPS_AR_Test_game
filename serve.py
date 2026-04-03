#!/usr/bin/env python3
"""
Servidor WebGL para Unity — compatible con iOS Safari.
Sirve headers correctos de MIME type y Content-Encoding para .wasm y archivos Brotli/Gzip.
Uso: python3 serve.py [puerto]
"""
import sys
import http.server
import mimetypes

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8080

# MIME types requeridos por WebAssembly y Unity WebGL
EXTRA_TYPES = {
    ".wasm":       "application/wasm",
    ".js":         "application/javascript",
    ".data":       "application/octet-stream",
    ".unityweb":   "application/octet-stream",
    ".br":         "application/octet-stream",
    ".gz":         "application/octet-stream",
    ".mp4":        "video/mp4",
    ".webm":       "video/webm",
}

for ext, mime in EXTRA_TYPES.items():
    mimetypes.add_type(mime, ext)

class UnityWebGLHandler(http.server.SimpleHTTPRequestHandler):

    # Mapa de extensión de archivo → Content-Encoding que debe declararse al cliente
    ENCODING_MAP = {
        ".br":  "br",
        ".gz":  "gzip",
    }

    def end_headers(self):
        # Detectar si el archivo en sí es Brotli o Gzip por su extensión real
        import os
        path = self.translate_path(self.path)
        _, ext = os.path.splitext(path)
        encoding = self.ENCODING_MAP.get(ext.lower())
        if encoding:
            self.send_header("Content-Encoding", encoding)

        # Cabeceras necesarias para que Safari iOS acepte SharedArrayBuffer y WASM
        self.send_header("Cross-Origin-Opener-Policy",   "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")

        # Evitar que Safari cachee respuestas parciales o corruptas en primera carga
        self.send_header("Cache-Control", "no-cache")

        super().end_headers()

    def log_message(self, fmt, *args):
        # Formato más legible
        print(f"[{self.log_date_time_string()}] {self.address_string()} — {fmt % args}")


if __name__ == "__main__":
    with http.server.ThreadingHTTPServer(("0.0.0.0", PORT), UnityWebGLHandler) as httpd:
        print(f"Sirviendo en http://0.0.0.0:{PORT}/")
        print("Ctrl+C para detener.")
        try:
            httpd.serve_forever()
        except KeyboardInterrupt:
            print("\nServidor detenido.")
