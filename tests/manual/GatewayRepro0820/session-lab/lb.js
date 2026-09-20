const http = require('http');
const { URL } = require('url');
let next = 0;
const targets = [
  { host: '127.0.0.1', port: 53661, worker: 'worker1' },
  { host: '127.0.0.1', port: 53662, worker: 'worker2' }
];
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1:53660');
  let index;
  const forced = url.searchParams.get('worker');
  if (forced === '1' || forced === '2') index = Number(forced) - 1;
  else index = (next++) % targets.length;
  url.searchParams.delete('worker');
  const t = targets[index];
  const headers = {...req.headers, host: `127.0.0.1:${t.port}`, 'x-session-lab-worker': t.worker};
  const proxy = http.request({host:t.host, port:t.port, method:req.method, path:url.pathname+url.search, headers}, upstream => {
    res.writeHead(upstream.statusCode || 502, upstream.headers);
    upstream.pipe(res);
  });
  proxy.on('error', err => { res.statusCode=502; res.end(JSON.stringify({error:String(err),worker:t.worker})); });
  req.pipe(proxy);
});
server.listen(53660, '127.0.0.1', () => console.log('session-lab-lb listening on 53660'));
