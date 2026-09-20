import json
import os
import sqlite3
from pathlib import Path

p = os.environ.get('DB_PATH', str(Path(__file__).resolve().parent / 'db-after-real-app-login.db'))
c=sqlite3.connect(p)
for t in ['GatewayManagedDevices','GatewayDeviceAuthorizations','GatewayLegacyAppIdentities','GatewayLegacyAppSessions','GatewayAuditLogs']:
 try:
  rows=c.execute(f'SELECT * FROM {t}').fetchall(); print('\nTABLE',t,'rows',len(rows))
  cols=[x[1] for x in c.execute(f'PRAGMA table_info({t})')]
  for row in rows[-10:]:
   d=dict(zip(cols,row))
   for k in list(d):
    if any(s in k.lower() for s in ['token','secret','cookie','hash','password']): d[k]='<redacted>'
   print(json.dumps(d,ensure_ascii=False,default=str)[:1800])
 except Exception as e: print(t,e)
