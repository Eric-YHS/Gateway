import json, time, urllib.request, websocket, sys
from pathlib import Path
pages=json.load(urllib.request.urlopen('http://127.0.0.1:9222/json'))
page=next((p for p in pages if 'webSocketDebuggerUrl' in p and p.get('type')=='page'), None)
if not page: raise SystemExit('no webview page')
ws=websocket.create_connection(page['webSocketDebuggerUrl'], timeout=2)
seq=0
def send(method, params=None):
 global seq
 seq+=1; ws.send(json.dumps({'id':seq,'method':method,'params':params or {}})); return seq
send('Network.enable')
send('Runtime.evaluate', {'expression': "location.href='http://10.0.2.2:53650/user/login'"})
events=[]; started=time.time()
while time.time()-started < 10:
 try:
  m=json.loads(ws.recv())
  if m.get('method','').startswith('Network.'):
   p=m.get('params',{})
   if m['method'] in ('Network.requestWillBeSent','Network.responseReceived'):
    r=p.get('request',{}) or {}; resp=p.get('response',{}) or {}
    events.append({'method':m['method'],'url':r.get('url') or resp.get('url'),'httpMethod':r.get('method'),'status':resp.get('status'),'mimeType':resp.get('mimeType'),'headers':resp.get('headers') if resp else None})
 except Exception: pass
(Path(__file__).resolve().parent / 'app-login-network.json').write_text(json.dumps(events,ensure_ascii=False,indent=2), encoding='utf-8')
print(json.dumps(events,ensure_ascii=False,indent=2))
ws.close()
