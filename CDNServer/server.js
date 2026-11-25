'use strict';

const http = require('http');
const fs = require('fs');
const fsp = fs.promises;
const path = require('path');

const PORT = Number(process.env.PORT || 8080);
const HOST = process.env.HOST || '127.0.0.1';
const ROOT = path.resolve(process.env.CDN_ROOT || path.join(__dirname, 'wwwroot'));

const MIME_TYPES = {
  '.json': 'application/json; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.htm': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.wasm': 'application/wasm',
  '.svg': 'image/svg+xml',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.ico': 'image/x-icon',
  '.mp4': 'video/mp4',
  '.mp3': 'audio/mpeg',
  '.wav': 'audio/wav',
  '.ogg': 'audio/ogg',
  '.zip': 'application/zip',
  '.gz': 'application/gzip',
  '.unityweb': 'application/octet-stream',
  '.bundle': 'application/octet-stream',
  '.bytes': 'application/octet-stream',
  '.bin': 'application/octet-stream',
  '.dll': 'application/octet-stream',
  '.hash': 'application/octet-stream',
};

function contentType(filePath) {
  return MIME_TYPES[path.extname(filePath).toLowerCase()] || 'application/octet-stream';
}

function timestamp() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  return `${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function humanSize(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 'B';
  for (const u of units) {
    if (value < 1024) break;
    value /= 1024;
    unit = u;
  }
  return `${value.toFixed(unit === 'KB' ? 0 : 2)} ${unit}`;
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

// 仅支持单段 Range（bytes=start-end / bytes=start- / bytes=-suffix），不符返回 null
function parseRange(header, size) {
  const m = /^bytes=(\d*)-(\d*)$/.exec(header.trim());
  if (!m || (m[1] === '' && m[2] === '')) return null;
  let start;
  let end;
  if (m[1] === '') {
    const suffix = Number(m[2]);
    if (!suffix) return null;
    start = Math.max(size - suffix, 0);
    end = size - 1;
  } else {
    start = Number(m[1]);
    end = m[2] === '' ? size - 1 : Math.min(Number(m[2]), size - 1);
  }
  if (start > end || start >= size) return null;
  return { start, end };
}

function sendText(res, status, text, extraHeaders) {
  res.writeHead(status, {
    'Content-Type': 'text/plain; charset=utf-8',
    'Cache-Control': 'no-store',
    'Access-Control-Allow-Origin': '*',
    ...extraHeaders,
  });
  res.end(text);
}

function serveFile(req, res, filePath, stat) {
  const size = stat.size;
  const headers = {
    'Content-Type': contentType(filePath),
    'Accept-Ranges': 'bytes',
    'Cache-Control': 'no-store',
    'Access-Control-Allow-Origin': '*',
    'Last-Modified': stat.mtime.toUTCString(),
  };

  const rangeHeader = req.headers.range;
  if (!rangeHeader) {
    headers['Content-Length'] = size;
    res.writeHead(200, headers);
    if (req.method === 'HEAD') return res.end();
    pipeFile(filePath, res);
    return;
  }

  const range = parseRange(rangeHeader, size);
  if (!range) {
    headers['Content-Range'] = `bytes */${size}`;
    res.writeHead(416, headers);
    return res.end();
  }

  headers['Content-Range'] = `bytes ${range.start}-${range.end}/${size}`;
  headers['Content-Length'] = range.end - range.start + 1;
  res.writeHead(206, headers);
  if (req.method === 'HEAD') return res.end();
  pipeFile(filePath, res, range);
}

function pipeFile(filePath, res, range) {
  const stream = fs.createReadStream(filePath, range ? { start: range.start, end: range.end } : undefined);
  stream.on('error', () => res.destroy());
  stream.pipe(res);
}

async function serveListing(req, res, dirPath, urlPath) {
  let entries;
  try {
    entries = await fsp.readdir(dirPath, { withFileTypes: true });
  } catch {
    return sendText(res, 500, '500 无法读取目录');
  }

  const items = await Promise.all(
    entries.map(async (entry) => {
      let size = null;
      if (entry.isFile()) {
        try {
          size = (await fsp.stat(path.join(dirPath, entry.name))).size;
        } catch {
          size = null;
        }
      }
      return { entry, size };
    })
  );

  items.sort((a, b) => {
    const dirDiff = Number(b.entry.isDirectory()) - Number(a.entry.isDirectory());
    return dirDiff || a.entry.name.localeCompare(b.entry.name);
  });

  const rows = items
    .map(({ entry, size }) => {
      const isDir = entry.isDirectory();
      const href = encodeURIComponent(entry.name) + (isDir ? '/' : '');
      const label = escapeHtml(entry.name) + (isDir ? '/' : '');
      const meta = isDir ? '目录' : size === null ? '-' : humanSize(size);
      return `<li><a href="${href}">${label}</a><span>${meta}</span></li>`;
    })
    .join('\n');

  const parentRow = urlPath !== '/' ? '<li><a href="../">../</a><span>上级目录</span></li>\n' : '';
  const html = `<!doctype html>
<html lang="zh-CN">
<meta charset="utf-8">
<title>CDN ${escapeHtml(urlPath)}</title>
<style>
  body { font-family: Consolas, "Microsoft YaHei", monospace; background: #15171c; color: #d7dae0; margin: 32px; }
  h1 { font-size: 16px; font-weight: normal; color: #8ab4f8; }
  ul { list-style: none; padding: 0; max-width: 860px; }
  li { display: flex; justify-content: space-between; padding: 6px 10px; border-bottom: 1px solid #262a33; }
  a { color: #d7dae0; text-decoration: none; }
  a:hover { color: #8ab4f8; }
  span { color: #7a8290; font-size: 12px; }
</style>
<h1>CDN 目录: ${escapeHtml(urlPath)}</h1>
<ul>
${parentRow}${rows}
</ul>
</html>`;

  res.writeHead(200, {
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': 'no-store',
    'Access-Control-Allow-Origin': '*',
  });
  if (req.method === 'HEAD') return res.end();
  res.end(html);
}

async function handle(req, res) {
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    return sendText(res, 405, '405 Method Not Allowed', { Allow: 'GET, HEAD' });
  }

  let pathname;
  try {
    pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
  } catch {
    return sendText(res, 400, '400 Bad Request');
  }

  const target = path.resolve(ROOT, '.' + pathname);
  if (target !== ROOT && !target.startsWith(ROOT + path.sep)) {
    return sendText(res, 403, '403 Forbidden');
  }

  let stat;
  try {
    stat = await fsp.stat(target);
  } catch {
    return sendText(res, 404, `404 Not Found: ${pathname}`);
  }

  if (stat.isDirectory()) {
    if (!pathname.endsWith('/')) {
      res.writeHead(301, { Location: pathname + '/' });
      return res.end();
    }
    const indexPath = path.join(target, 'index.html');
    try {
      const indexStat = await fsp.stat(indexPath);
      if (indexStat.isFile()) return serveFile(req, res, indexPath, indexStat);
    } catch {
      // 无 index.html 时回落到目录列表
    }
    return serveListing(req, res, target, pathname);
  }

  return serveFile(req, res, target, stat);
}

const server = http.createServer((req, res) => {
  const startedAt = process.hrtime.bigint();
  let loggedSize;
  const originalWriteHead = res.writeHead.bind(res);
  res.writeHead = (statusCode, headers) => {
    if (headers && typeof headers === 'object' && headers['Content-Length'] !== undefined) {
      loggedSize = Number(headers['Content-Length']);
    }
    return originalWriteHead(statusCode, headers);
  };

  res.on('finish', () => {
    const ms = Number(process.hrtime.bigint() - startedAt) / 1e6;
    let shownUrl = req.url;
    try {
      shownUrl = decodeURIComponent(req.url);
    } catch {
      // 保留原始 URL
    }
    console.log(
      `${timestamp()} ${req.method.padEnd(4)} ${shownUrl} -> ${res.statusCode} ` +
        `${loggedSize === undefined ? '-' : humanSize(loggedSize)} ${ms.toFixed(1)}ms`
    );
  });

  handle(req, res).catch((err) => {
    console.error(`${timestamp()} 请求处理异常: ${err && err.stack ? err.stack : err}`);
    if (!res.headersSent) sendText(res, 500, '500 Internal Server Error');
    else res.destroy();
  });
});

fs.mkdirSync(ROOT, { recursive: true });

server.listen(PORT, HOST, () => {
  console.log('CDN 文件服务器已启动');
  console.log(`  监听:     http://${HOST}:${PORT}`);
  console.log(`  根目录:   ${ROOT}`);
  console.log(`  访问约定: http://${HOST}:${PORT}/<平台>/<包名>/...`);
  console.log(`  例如:     http://${HOST}:${PORT}/Windows/MainPackage/`);
  console.log('  按 Ctrl+C 停止');
});

server.on('error', (err) => {
  if (err.code === 'EADDRINUSE') {
    console.error(`端口 ${PORT} 已被占用，请先关闭占用进程，或用 PORT=8081 node server.js 换端口`);
  } else {
    console.error(`服务器启动失败: ${err.message}`);
  }
  process.exit(1);
});
