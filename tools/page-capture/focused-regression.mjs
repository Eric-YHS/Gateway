import fs from 'node:fs/promises';
import fssync from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, spawnSync } from 'node:child_process';
import { chromium } from 'playwright';

const currentFile = fileURLToPath(import.meta.url);
const root = path.resolve(path.dirname(currentFile), '..', '..');
const timestamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\..+/, '').replace('T', '-');
const outputDir = path.join(root, 'artifacts', 'real-browser-gateway-regression', timestamp);
const screenshotDir = path.join(outputDir, 'screenshots');
const backupDir = path.join(outputDir, 'backup');

const gatewayPort = 5180;
const adminPort = 5181;
const backendPort = 5185;
const httpsPort = 44367;
const parentCookieDomain = '.gwdemo.test';
const hosts = {
  erp: `a.gwdemo.test:${gatewayPort}`,
  wms: `b.gwdemo.test:${gatewayPort}`,
  finance: `c.gwdemo.test:${gatewayPort}`,
  erpHttps: `a.gwdemo.test:${httpsPort}`
};

const legacySrcDir = '_zip_verify_extract';
const gatewayWebConfigPath = path.join(root, legacySrcDir, 'src', 'GatewayDemo.Legacy.Web', 'Web.config');
const gatewaySitesPath = path.join(root, legacySrcDir, 'src', 'GatewayDemo.Legacy.Web', 'App_Data', 'gateway-sites.json');
const gatewayAppData = path.join(root, legacySrcDir, 'src', 'GatewayDemo.Legacy.Web', 'App_Data');
const gatewayDbFiles = [
  path.join(gatewayAppData, 'gateway-demo-legacy.db'),
  path.join(gatewayAppData, 'gateway-demo-legacy.db-shm'),
  path.join(gatewayAppData, 'gateway-demo-legacy.db-wal')
];
const apphostTemplatePath = path.join(root, 'tools', 'iisexpress', 'admin', 'IIS Express', 'AppServer', 'applicationhost.config');
const apphostPath = path.join(outputDir, 'applicationhost.config');
const iisExpressPath = path.join(root, 'tools', 'iisexpress', 'admin', 'IIS Express', 'iisexpress.exe');
const httpsKeyPath = path.join(outputDir, 'https-key.pem');
const httpsCertPath = path.join(outputDir, 'https-cert.pem');
const opensslConfigPath = path.join(outputDir, 'openssl.cnf');

const checks = [];
const children = [];
const servers = [];
let screenshotIndex = 1;

function xmlEscape(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
}

async function exists(filePath) {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

async function ensureDir(dir) {
  await fs.mkdir(dir, { recursive: true });
}

async function copyIfExists(source, destination) {
  if (await exists(source)) {
    await ensureDir(path.dirname(destination));
    await fs.copyFile(source, destination);
    return true;
  }

  return false;
}

async function restoreIfBackedUp(source, destination) {
  if (await exists(source)) {
    await fs.copyFile(source, destination);
  } else if (await exists(destination)) {
    await fs.rm(destination, { force: true });
  }
}

async function backupRuntimeFiles() {
  await ensureDir(backupDir);
  await copyIfExists(gatewayWebConfigPath, path.join(backupDir, 'Web.config'));
  await copyIfExists(gatewaySitesPath, path.join(backupDir, 'gateway-sites.json'));
  for (const dbFile of gatewayDbFiles) {
    await copyIfExists(dbFile, path.join(backupDir, path.basename(dbFile)));
  }
}

async function restoreRuntimeFiles() {
  await restoreIfBackedUp(path.join(backupDir, 'Web.config'), gatewayWebConfigPath);
  await restoreIfBackedUp(path.join(backupDir, 'gateway-sites.json'), gatewaySitesPath);
  for (const dbFile of gatewayDbFiles) {
    await restoreIfBackedUp(path.join(backupDir, path.basename(dbFile)), dbFile);
  }
}

async function resetGatewayDatabase() {
  for (const dbFile of gatewayDbFiles) {
    await fs.rm(dbFile, { force: true });
  }
}

async function patchWebConfig() {
  let xml = await fs.readFile(gatewayWebConfigPath, 'utf8');
  xml = setAppSetting(xml, 'Admin.ManagementPort', String(adminPort));
  xml = setAppSetting(xml, 'Gateway.DeviceCookieDomain', parentCookieDomain);
  xml = setAppSetting(xml, 'Gateway.TrustedProxyAddresses', '127.0.0.1,::1');
  xml = setAppSetting(xml, 'Gateway.AppDiagnostics.Enabled', 'true');
  await fs.writeFile(gatewayWebConfigPath, xml, 'utf8');
}

function setAppSetting(xml, key, value) {
  const escapedValue = value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
  const regex = new RegExp(`(<add\\s+key="${key}"\\s+value=")[^"]*("\\s*/>)`, 'i');
  if (regex.test(xml)) {
    return xml.replace(regex, `$1${escapedValue}$2`);
  }

  return xml.replace(/<appSettings>/i, `<appSettings>\n    <add key="${key}" value="${escapedValue}" />`);
}

function buildGatewaySitesConfig({ redirectSelf = false } = {}) {
  const anonymousAllowedPaths = [
    '/sys.ashx*',
    '/ashx/sys.ashx*',
    '/datacenter/*',
    '/default.aspx/datacenter/*'
  ];

  const erpUpstream = redirectSelf
    ? `http://${hosts.erp}/`
    : `http://localhost:${backendPort}/mock/erp-main/`;

  return [
    {
      Key: 'erp-main',
      Name: 'ERP Main',
      Description: 'ERP regression site',
      Accent: '#0f766e',
      UpstreamBaseUrl: erpUpstream,
      EntryPath: 'portal',
      ExposeLegacyAppAtRoot: true,
      HostNames: [
        hosts.erp,
        hosts.erpHttps,
        `localhost:${gatewayPort}`,
        `localhost:${httpsPort}`
      ],
      AnonymousAllowedPaths: anonymousAllowedPaths,
      RedirectToUpstream: redirectSelf
    },
    {
      Key: 'wms-east',
      Name: 'WMS East',
      Description: 'WMS regression site',
      Accent: '#b45309',
      UpstreamBaseUrl: `http://localhost:${backendPort}/mock/wms-east/`,
      EntryPath: 'portal',
      ExposeLegacyAppAtRoot: true,
      HostNames: [hosts.wms],
      AnonymousAllowedPaths: anonymousAllowedPaths,
      RedirectToUpstream: false
    },
    {
      Key: 'finance',
      Name: 'Finance',
      Description: 'Finance regression site',
      Accent: '#1d4ed8',
      UpstreamBaseUrl: `http://localhost:${backendPort}/mock/finance/`,
      EntryPath: 'portal',
      ExposeLegacyAppAtRoot: true,
      HostNames: [hosts.finance],
      AnonymousAllowedPaths: anonymousAllowedPaths,
      RedirectToUpstream: false
    }
  ];
}

async function writeGatewaySitesConfig(options) {
  const json = JSON.stringify(buildGatewaySitesConfig(options), null, 2);
  await fs.writeFile(gatewaySitesPath, `${json}\n`, 'utf8');
}

async function writeApplicationHostConfig() {
  let config = await fs.readFile(apphostTemplatePath, 'utf8');
  config = config
    .replace(/<section name="handlers" overrideModeDefault="Deny" \/>/i, '<section name="handlers" overrideModeDefault="Allow" />')
    .replace(/<section name="modules" allowDefinition="MachineToApplication" overrideModeDefault="Deny" \/>/i, '<section name="modules" allowDefinition="MachineToApplication" overrideModeDefault="Allow" />')
    .replace(/<section name="webSocket" overrideModeDefault="Deny" \/>/i, '<section name="webSocket" overrideModeDefault="Allow" />');

  const gatewayPath = xmlEscape(path.join(root, legacySrcDir, 'src', 'GatewayDemo.Legacy.Web'));
  const backendPath = xmlEscape(path.join(root, legacySrcDir, 'src', 'MockBusinessBackend.Legacy.Web'));
  const sitesXml = `
        <sites>
            <site name="GatewayRealBrowser" id="101" serverAutoStart="true">
                <application path="/" applicationPool="Clr4IntegratedAppPool">
                    <virtualDirectory path="/" physicalPath="${gatewayPath}" />
                </application>
                <bindings>
                    <binding protocol="http" bindingInformation="*:${gatewayPort}:" />
                    <binding protocol="http" bindingInformation="*:${adminPort}:" />
                </bindings>
            </site>
            <site name="MockBackendRealBrowser" id="102" serverAutoStart="true">
                <application path="/" applicationPool="Clr4IntegratedAppPool">
                    <virtualDirectory path="/" physicalPath="${backendPath}" />
                </application>
                <bindings>
                    <binding protocol="http" bindingInformation="*:${backendPort}:" />
                </bindings>
            </site>
            <siteDefaults>
                <logFile logFormat="W3C" directory="${xmlEscape(outputDir)}" enabled="true"/>
                <traceFailedRequestsLogging directory="${xmlEscape(outputDir)}" enabled="false" maxLogFileSizeKB="1024" />
            </siteDefaults>
            <applicationDefaults applicationPool="Clr4IntegratedAppPool" />
            <virtualDirectoryDefaults allowSubDirConfig="true" />
        </sites>`;

  config = config.replace(/<sites>[\s\S]*?<\/sites>/i, sitesXml);
  await fs.writeFile(apphostPath, config, 'utf8');
}

function spawnIisExpress(siteName, logBaseName) {
  const out = fssync.openSync(path.join(outputDir, `${logBaseName}.out.log`), 'w');
  const err = fssync.openSync(path.join(outputDir, `${logBaseName}.err.log`), 'w');
  const child = spawn(iisExpressPath, [
    `/config:${apphostPath}`,
    `/site:${siteName}`,
    '/systray:false'
  ], {
    cwd: root,
    detached: false,
    stdio: ['ignore', out, err],
    windowsHide: true
  });
  children.push(child);
  return child;
}

async function createHttpsCertificate() {
  await fs.writeFile(opensslConfigPath, [
    '[req]',
    'distinguished_name=req_distinguished_name',
    'prompt=no',
    '',
    '[req_distinguished_name]',
    'CN=a.gwdemo.test',
    ''
  ].join('\n'), 'utf8');

  const openssl = spawnSync('openssl', [
    'req',
    '-x509',
    '-newkey',
    'rsa:2048',
    '-nodes',
    '-config',
    opensslConfigPath,
    '-keyout',
    httpsKeyPath,
    '-out',
    httpsCertPath,
    '-days',
    '1',
    '-subj',
    '/CN=a.gwdemo.test',
    '-addext',
    'subjectAltName=DNS:a.gwdemo.test,DNS:localhost,IP:127.0.0.1'
  ], {
    cwd: outputDir,
    encoding: 'utf8',
    windowsHide: true
  });

  if (openssl.status !== 0) {
    throw new Error(`openssl failed: ${openssl.stderr || openssl.stdout}`);
  }
}

async function startHttpsTerminator() {
  await createHttpsCertificate();
  const options = {
    key: await fs.readFile(httpsKeyPath),
    cert: await fs.readFile(httpsCertPath)
  };

  const server = https.createServer(options, (clientReq, clientRes) => {
    const forwardedFor = clientReq.socket.remoteAddress || '127.0.0.1';
    const headers = {
      ...clientReq.headers,
      host: clientReq.headers.host || hosts.erpHttps,
      'x-forwarded-proto': 'https',
      'x-forwarded-scheme': 'https',
      'x-forwarded-ssl': 'on',
      'x-forwarded-host': clientReq.headers.host || hosts.erpHttps,
      'x-forwarded-for': forwardedFor
    };

    const upstream = http.request({
      host: '127.0.0.1',
      port: gatewayPort,
      method: clientReq.method,
      path: clientReq.url,
      headers
    }, upstreamRes => {
      clientRes.writeHead(upstreamRes.statusCode || 502, upstreamRes.statusMessage, upstreamRes.headers);
      upstreamRes.pipe(clientRes);
    });

    upstream.on('error', error => {
      clientRes.writeHead(502, { 'content-type': 'text/plain; charset=utf-8' });
      clientRes.end(`TLS terminator upstream error: ${error.message}`);
    });

    clientReq.pipe(upstream);
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(httpsPort, '127.0.0.1', resolve);
  });
  servers.push(server);
}

async function waitForUrl(url, options = {}) {
  const timeoutMs = options.timeoutMs ?? 45000;
  const deadline = Date.now() + timeoutMs;
  let lastError = '';
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url, { redirect: 'manual' });
      if (response.status > 0 && response.status < 500) {
        return response;
      }

      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error.message;
    }

    await new Promise(resolve => setTimeout(resolve, 750));
  }

  throw new Error(`Timed out waiting for ${url}. Last error: ${lastError}`);
}

async function stopIisExpressProcesses() {
  const processes = children.splice(0);
  await Promise.all(processes.map(child => new Promise(resolve => {
    if (child.exitCode !== null || child.killed) {
      resolve();
      return;
    }

    child.once('exit', resolve);
    child.kill();
    setTimeout(() => {
      if (child.exitCode === null && !child.killed) {
        child.kill('SIGKILL');
      }
      resolve();
    }, 3500);
  })));
}

async function stopChildren() {
  await Promise.all(servers.splice(0).map(server => new Promise(resolve => server.close(resolve))));
  await stopIisExpressProcesses();
}

function recordCheck(name, ok, details = '') {
  checks.push({ name, ok, details });
  const prefix = ok ? 'PASS' : 'FAIL';
  console.log(`${prefix} ${name}${details ? ` - ${details}` : ''}`);
  if (!ok) {
    throw new Error(`${name}${details ? `: ${details}` : ''}`);
  }
}

async function screenshot(page, name) {
  const fileName = `${String(screenshotIndex).padStart(2, '0')}-${name}.png`;
  screenshotIndex += 1;
  const fullPath = path.join(screenshotDir, fileName);
  await page.screenshot({ path: fullPath, fullPage: true });
  return fullPath;
}

async function launchBrowser() {
  const args = [
    '--proxy-server=direct://',
    '--proxy-bypass-list=*',
    '--ignore-certificate-errors',
    '--host-resolver-rules=MAP a.gwdemo.test 127.0.0.1,MAP b.gwdemo.test 127.0.0.1,MAP c.gwdemo.test 127.0.0.1'
  ];

  try {
    return await chromium.launch({
      channel: 'msedge',
      headless: true,
      args
    });
  } catch {
    return await chromium.launch({
      headless: true,
      args
    });
  }
}

async function assertAllSitesState(scope, expectedChecked, expectedConcreteDisabled, expectedConcreteCheckedCount, label) {
  const state = await scope.evaluate(element => {
    const all = element.querySelector('input.js-all-sites');
    const sites = [...element.querySelectorAll('input[name="siteKeys"]')];
    return {
      allChecked: all ? all.checked : null,
      disabledCount: sites.filter(item => item.disabled).length,
      checkedCount: sites.filter(item => item.checked).length,
      count: sites.length
    };
  });

  recordCheck(`${label}: all-sites checkbox`, state.allChecked === expectedChecked, JSON.stringify(state));
  recordCheck(`${label}: concrete site disabled state`, expectedConcreteDisabled ? state.disabledCount === state.count : state.disabledCount === 0, JSON.stringify(state));
  recordCheck(`${label}: concrete checked count`, state.checkedCount === expectedConcreteCheckedCount, JSON.stringify(state));
}

async function createStaticAssets() {
  const gatewayRoot = path.join(root, legacySrcDir, 'src', 'GatewayDemo.Legacy.Web');
  await ensureDir(path.join(gatewayRoot, 'jvs-ui-public'));
  await ensureDir(path.join(gatewayRoot, 'jvs-aps-ui'));
  await fs.writeFile(path.join(gatewayRoot, 'jvs-ui-public', 'app.js'), 'console.log("jvs-ui-public-app");', 'utf8');
  await fs.writeFile(path.join(gatewayRoot, 'jvs-aps-ui', 'index.html'), '<!DOCTYPE html><html><body>jvs-aps-ui-spa</body></html>', 'utf8');
  await fs.writeFile(path.join(gatewayRoot, 'jvs-aps-ui', 'app.css'), 'body{color:red}', 'utf8');
}

async function localStaticFetch(requestPath, hostHeader) {
  const url = `http://127.0.0.1:${gatewayPort}${requestPath}`;
  const response = await fetch(url, {
    redirect: 'manual',
    headers: { Host: hostHeader }
  });
  const body = await response.text();
  return { status: response.status, body };
}

async function runBrowserScenario() {
  await createStaticAssets();
  const browser = await launchBrowser();
  const company = `TwoSite-${Date.now()}`;
  const context = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    locale: 'zh-CN',
    ignoreHTTPSErrors: true
  });
  const adminContext = await browser.newContext({
    viewport: { width: 1440, height: 1000 },
    locale: 'zh-CN',
    ignoreHTTPSErrors: true
  });

  const page = await context.newPage();
  const admin = await adminContext.newPage();

  try {
    await page.goto(`http://${hosts.erp}/portal`, { waitUntil: 'networkidle' });
    await screenshot(page, 'initial-erp-request');
    recordCheck('initial ERP visit shows request form', await page.locator('input[name="companyName"]').count() === 1);

    await page.fill('input[name="companyName"]', company);
    await page.fill('input[name="applicantName"]', 'Two Site Tester');
    await page.fill('input[name="phone"]', '13800138000');
    await page.fill('input[name="reason"]', 'two-site-test');

    const applicantForm = page.locator('form').filter({ has: page.locator('input[name="companyName"]') }).first();
    await applicantForm.locator('input.js-all-sites').uncheck();
    await applicantForm.locator('input[name="siteKeys"][value="erp-main"]').check();
    await applicantForm.locator('input[name="siteKeys"][value="wms-east"]').check();
    await assertAllSitesState(applicantForm, false, false, 2, 'applicant selected two concrete sites');
    await screenshot(page, 'applicant-two-sites-checked');

    await Promise.all([
      page.waitForLoadState('networkidle'),
      page.click('form button[type="submit"]')
    ]);
    await screenshot(page, 'applicant-two-sites-submitted');

    await admin.goto(`http://localhost:${adminPort}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    await admin.fill('input[name="username"]', 'gateway-admin');
    await admin.fill('input[name="password"]', 'GatewayDemo!2026');
    await Promise.all([
      admin.waitForLoadState('networkidle'),
      admin.click('button[type="submit"]')
    ]);
    await screenshot(admin, 'admin-dashboard-pending');

    const pending = admin.locator('article.item').filter({ hasText: company }).first();
    await pending.waitFor({ state: 'visible', timeout: 15000 });
    await assertAllSitesState(pending, false, false, 2, 'admin pending shows both requested sites');
    await screenshot(admin, 'admin-two-sites-before-approve');

    await pending.locator('input[name="note"]').fill('approve two sites');
    await Promise.all([
      admin.waitForLoadState('networkidle'),
      pending.locator('button[name="action"][value="approve-request"]').click()
    ]);
    await screenshot(admin, 'admin-two-sites-approved');

    await page.goto(`http://${hosts.erp}/portal`, { waitUntil: 'networkidle' });
    await screenshot(page, 'erp-authorized');
    recordCheck('ERP is authorized after two-site approval', await page.locator('input[name="companyName"]').count() === 0);
    recordCheck('ERP upstream rendered', (await page.textContent('body')).includes('ERP'));

    await page.goto(`http://${hosts.wms}/portal`, { waitUntil: 'networkidle' });
    await screenshot(page, 'wms-authorized');
    recordCheck('WMS is authorized after two-site approval', await page.locator('input[name="companyName"]').count() === 0);
    recordCheck('WMS upstream rendered', (await page.textContent('body')).includes('WMS'));

    const anonStatic = await localStaticFetch('/jvs-ui-public/app.js', hosts.erp);
    recordCheck('anonymous static js served locally from gateway root', anonStatic.status === 200 && anonStatic.body.includes('jvs-ui-public-app'), `status=${anonStatic.status}, body=${anonStatic.body.slice(0, 100)}`);

    const staticPage = await context.newPage();
    await staticPage.goto(`http://${hosts.erp}/jvs-aps-ui/index.html`, { waitUntil: 'networkidle' });
    const staticBody = await staticPage.textContent('body');
    recordCheck('authorized static index served locally from gateway root', staticBody.includes('jvs-aps-ui-spa'), staticBody.slice(0, 100));
    await staticPage.close();

    await fs.writeFile(path.join(outputDir, 'report.json'), JSON.stringify({
      company,
      ports: { gatewayPort, adminPort, backendPort, httpsPort },
      checks,
      artifactDir: outputDir
    }, null, 2), 'utf8');
  } finally {
    await adminContext.close();
    await context.close();
    await browser.close();
  }
}

async function main() {
  await ensureDir(outputDir);
  await ensureDir(screenshotDir);
  await backupRuntimeFiles();

  try {
    if (!(await exists(iisExpressPath))) {
      throw new Error(`IIS Express not found: ${iisExpressPath}`);
    }

    await patchWebConfig();
    await resetGatewayDatabase();
    await writeGatewaySitesConfig();
    await writeApplicationHostConfig();

    spawnIisExpress('GatewayRealBrowser', 'gateway');
    spawnIisExpress('MockBackendRealBrowser', 'mock-backend');
    await startHttpsTerminator();
    await waitForUrl(`http://localhost:${gatewayPort}/healthz.ashx`);
    await waitForUrl(`http://localhost:${backendPort}/mock/erp-main/login`);

    await runBrowserScenario();
    console.log(`ARTIFACT_DIR=${outputDir}`);
  } finally {
    await stopChildren();
    await restoreRuntimeFiles();
  }
}

main().catch(async error => {
  await fs.writeFile(path.join(outputDir, 'failure.txt'), `${error.stack || error.message}\n`, 'utf8').catch(() => {});
  console.error(error.stack || error.message);
  process.exitCode = 1;
});
