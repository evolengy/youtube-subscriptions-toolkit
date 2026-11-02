"""Serve current extension UI against synthetic, in-memory Chrome APIs."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import mimetypes

HERE = Path(__file__).resolve().parent
EXTENSION = HERE.parents[1] / 'extension'
FILES = {
    'dashboard.html', 'dashboard.js', 'dashboard.css', 'storage.js', 'logger.js',
    'toast.js', 'feedFilter.js', 'groupCounts.js', 'icons.js', 'videoPresentation.js',
    'content-groups.js', 'content-groups.css', 'guideDeclutter.js', 'emojiData.js',
    'emojiPicker.js', 'emojiPicker.css', 'groups.html', 'groups.js', 'groups.css',
    'groupEditor.js', 'settings.html', 'settings.js', 'settings.css',
    'logs.html', 'logs.js', 'logs.css', 'channels.html', 'channels.js',
    'channels.css', 'channelHealth.js',
}
OVERLAY = '''<!doctype html><html lang="en" dark><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>YouTube overlay test stand</title><link rel="stylesheet" href="content-groups.css">
<link rel="stylesheet" href="emojiPicker.css"><style>
body{margin:0;background:#0f0f0f;color:#f1f1f1;font:14px Arial}header{padding:16px;border-bottom:1px solid #333}
ytd-guide-renderer{display:block;width:240px;margin-top:12px}#sections{padding:12px 24px}.yst-overlay{top:88px}
</style></head><body><header>YouTube · synthetic shell</header>
<ytd-guide-renderer><div id="sections">Subscriptions</div><div id="footer"></div></ytd-guide-renderer>
<script src="mockChrome.js"></script>'''
for script in ['feedFilter.js', 'groupCounts.js', 'guideDeclutter.js', 'emojiData.js', 'emojiPicker.js', 'icons.js', 'videoPresentation.js', 'content-groups.js']:
    OVERLAY += f'<script src="{script}"></script>'
OVERLAY += '</body></html>'


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        name = self.path.split('?', 1)[0].lstrip('/') or 'dashboard.html'
        if name == 'overlay.html':
            data, kind = OVERLAY.encode(), 'text/html'
        elif name == 'mockChrome.js':
            data, kind = (HERE / name).read_bytes(), 'text/javascript'
        elif name in FILES:
            data = (EXTENSION / name).read_bytes()
            kind = mimetypes.guess_type(name)[0] or 'application/octet-stream'
            if name.endswith('.html'):
                data = data.replace(b'<body>', b'<body><script src="mockChrome.js"></script>', 1)
        else:
            self.send_error(404)
            return
        self.send_response(200)
        self.send_header('Content-Type', kind + '; charset=utf-8')
        self.send_header('Cache-Control', 'no-store')
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', type=int, default=8766)
    args = parser.parse_args()
    print(f'Extension preview: http://localhost:{args.port}/dashboard.html', flush=True)
    ThreadingHTTPServer(('127.0.0.1', args.port), Handler).serve_forever()
