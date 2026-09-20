const fs = require('fs');
const path = require('path');
const cp = require('child_process');
const { chromium } = require('playwright');

const root = process.env.GW_TEST_ROOT || 'E:\\验证页面\\_fix_20260713_logging';
const gateway = process.env.GW_TEST_BASE || 'http://127.0.0.1:5250';
const admin = process.env.GW_TEST_ADMIN || 'http://127.0.0.1:5251';
const appcmd = path.join(process.env.WINDIR, 'System32', 'inetsrv', 'appcmd.exe');
const appPool = process.env.GW_TEST_APPPOOL || 'GatewayDemoLoggingFixPool';
const backendPool = process.env.GW_TEST_BACKENDPOOL || 'MockBusinessLoggingFixPool';
const dbPath = path.join(root, 'src', 'GatewayDemo.Legacy.Web', 'App_Data', 'gateway-demo-legacy.db');
const logsPath = path.join(root, 'src', 'GatewayDemo.Legacy.Web', 'App_Data', 'logs');
const backupPath = dbPath + '.browser-backup';
const output = path.join(root, 'browser-check-output');
fs.mkdirSync(output, { recursive: true });

function runAppcmd(args) {
  try {
    cp.execFileSync(appcmd, args, { stdio: 'ignore' });
  } catch (error) {
    // Stop/start is intentionally idempotent for the cleanup path.
  }
}

function runIcacls(args) {
  try {
    cp.execFileSync('icacls.exe', args, { stdio: 'ignore' });
  } catch (error) {
    // Cleanup below still attempts to remove the deny rule.
  }
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function assertReadableChinese(text, label) {
  assert(!/(?:鏈|锛|璇|€|�)/.test(text), `${label} contains mojibake`);
}

async function main() {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const result = { checks: [], requests: [], screenshots: [] };
  let logsDenied = false;
  page.on('response', response => {
    if (response.url().startsWith(gateway) || response.url().startsWith(admin)) {
      result.requests.push({ url: response.url(), status: response.status(), type: response.headers()['content-type'] || '' });
    }
  });

  try {
    let response = await page.goto(`${gateway}/portal`, { waitUntil: 'domcontentloaded' });
    assert(response && response.status() === 200, `public page status ${response && response.status()}`);
    let body = await page.locator('body').innerText();
    assertReadableChinese(body, 'public page');
    assert(body.includes('提交访问申请'), 'public application form missing');
    await page.screenshot({ path: path.join(output, '01-public-application.png'), fullPage: true });
    result.screenshots.push('01-public-application.png');
    result.checks.push('public application and UTF-8 response');

    const site = page.locator('input[name="siteKeys"]').first();
    if (await site.count() && !(await site.isChecked())) await site.check();
    await page.locator('input[name="companyName"]').fill('真实浏览器日志验证企业');
    await page.locator('input[name="applicantName"]').fill('真实浏览器验证人');
    await page.locator('input[name="phone"]').fill('13800138000');
    await page.locator('input[name="reason"]').fill('验证错误日志与数据库故障恢复');
    await page.getByRole('button', { name: '提交申请' }).click();
    await page.waitForLoadState('domcontentloaded');
    body = await page.locator('body').innerText();
    assertReadableChinese(body, 'submitted application');
    assert(body.includes('申请') || body.includes('待审核'), 'application result missing');
    await page.screenshot({ path: path.join(output, '02-application-submitted.png'), fullPage: true });
    result.screenshots.push('02-application-submitted.png');
    result.checks.push('public application submission');

    const adminPage = await context.newPage();
    response = await adminPage.goto(`${admin}/Admin/Default.aspx`, { waitUntil: 'domcontentloaded' });
    assert(response && response.status() === 200, `admin login status ${response && response.status()}`);
    await adminPage.locator('input[name="username"]').fill('gateway-admin');
    await adminPage.locator('input[name="password"]').fill('Admin-Logging-20260713!');
    await adminPage.getByRole('button', { name: '进入后台' }).click();
    await adminPage.waitForLoadState('domcontentloaded');
    body = await adminPage.locator('body').innerText();
    assertReadableChinese(body, 'admin dashboard');
    assert(body.includes('网关管理台') && body.includes('错误日志'), 'admin error log section missing');
    await adminPage.screenshot({ path: path.join(output, '03-admin-dashboard.png'), fullPage: true });
    result.screenshots.push('03-admin-dashboard.png');
    result.checks.push('admin login and error log section');

    const approve = adminPage.locator('button[value="approve-request"]').first();
    if (await approve.count()) {
      await approve.click();
      await adminPage.waitForLoadState('domcontentloaded');
      body = await adminPage.locator('body').innerText();
      assert(body.includes('错误日志'), 'admin dashboard lost error log section after approval');
      result.checks.push('admin approval workflow');
    }

    response = await page.goto(`${gateway}/portal`, { waitUntil: 'domcontentloaded' });
    assert(response && response.status() < 500, `authorized upstream status ${response && response.status()}`);
    body = await page.locator('body').innerText();
    assertReadableChinese(body, 'upstream portal');
    result.checks.push('authorized upstream proxy');

    runIcacls([logsPath, '/deny', `IIS AppPool\\${appPool}:(OI)(CI)(M)`]);
    logsDenied = true;
    runAppcmd(['stop', 'apppool', `/apppool.name:${backendPool}`]);
    response = await page.goto(`${gateway}/portal`, { waitUntil: 'domcontentloaded' });
    const badGatewayStatus = response ? response.status() : 0;
    body = await page.locator('body').innerText();
    assert(badGatewayStatus >= 500, `stopped upstream did not return 5xx: ${badGatewayStatus}`);
    assertReadableChinese(body, 'upstream failure page');
    await page.screenshot({ path: path.join(output, '04-upstream-500.png'), fullPage: true });
    result.screenshots.push('04-upstream-500.png');
    if (logsDenied) {
      runIcacls([logsPath, '/remove:d', `IIS AppPool\\${appPool}`]);
      logsDenied = false;
    }
    runAppcmd(['start', 'apppool', `/apppool.name:${backendPool}`]);
    result.checks.push(`upstream failure returned ${badGatewayStatus} and was logged`);

    await adminPage.reload({ waitUntil: 'domcontentloaded' });
    body = await adminPage.locator('body').innerText();
    assert(body.includes('错误日志'), 'admin error log section missing after upstream failure');
    assert(body.includes('ManagedReverseProxy') || body.includes('500') || body.includes('502'), 'upstream failure not visible in admin error logs');
    await adminPage.screenshot({ path: path.join(output, '05-admin-upstream-error-log.png'), fullPage: true });
    result.screenshots.push('05-admin-upstream-error-log.png');
    result.checks.push('admin displays upstream error log');
    assert(body.includes('备用日志'), 'fallback error log storage was not visible in admin');
    result.checks.push('read-only primary log directory used fallback storage');

    runAppcmd(['stop', 'apppool', `/apppool.name:${appPool}`]);
    if (fs.existsSync(backupPath)) fs.unlinkSync(backupPath);
    fs.copyFileSync(dbPath, backupPath);
    fs.writeFileSync(dbPath, 'not a sqlite database', 'utf8');
    runAppcmd(['start', 'apppool', `/apppool.name:${appPool}`]);
    const healthPage = await context.newPage();
    response = await healthPage.goto(`${gateway}/healthz.ashx`, { waitUntil: 'domcontentloaded' });
    const healthStatus = response ? response.status() : 0;
    const healthBody = await healthPage.locator('body').innerText();
    assert(healthStatus === 503, `invalid database health status ${healthStatus}`);
    assert(healthBody.includes('unavailable'), 'invalid database health body missing');
    result.checks.push('invalid SQLite returned 503');

    response = await page.goto(`${gateway}/portal`, { waitUntil: 'domcontentloaded' });
    const publicDatabaseStatus = response ? response.status() : 0;
    const publicDatabaseBody = await page.locator('body').innerText();
    assert(publicDatabaseStatus === 500, `invalid database public status ${publicDatabaseStatus}`);
    assertReadableChinese(publicDatabaseBody, 'public database error page');
    assert(publicDatabaseBody.includes('服务器处理请求时发生错误'), 'public database error page missing');
    await page.screenshot({ path: path.join(output, '06-public-database-error.png'), fullPage: true });
    result.screenshots.push('06-public-database-error.png');
    result.checks.push('public invalid-database request returned UTF-8 500 error page');

    await adminPage.reload({ waitUntil: 'domcontentloaded' });
    body = await adminPage.locator('body').innerText();
    assert(body.includes('数据库暂时不可用') || body.includes('错误日志'), 'admin did not remain available during database failure');
    assert(body.includes('runtime.database-initialization') || body.includes('healthz.database'), 'database failure not visible in admin error logs');
    await adminPage.screenshot({ path: path.join(output, '06-admin-database-error-log.png'), fullPage: true });
    result.screenshots.push('06-admin-database-error-log.png');
    result.checks.push('admin remains usable and displays database error');

    runAppcmd(['stop', 'apppool', `/apppool.name:${appPool}`]);
    fs.copyFileSync(backupPath, dbPath);
    fs.unlinkSync(backupPath);
    runAppcmd(['start', 'apppool', `/apppool.name:${appPool}`]);
    response = await healthPage.goto(`${gateway}/healthz.ashx`, { waitUntil: 'domcontentloaded' });
    assert(response && response.status() === 200, `database recovery health status ${response && response.status()}`);
    assert((await healthPage.locator('body').innerText()).includes('"ok":true'), 'database recovery body missing');
    result.checks.push('SQLite restore and recovery returned 200');

    await adminPage.setViewportSize({ width: 390, height: 844 });
    await adminPage.reload({ waitUntil: 'domcontentloaded' });
    const box = await adminPage.locator('body').boundingBox();
    assert(!box || box.width <= 390, `mobile page horizontal overflow: ${box && box.width}`);
    await adminPage.screenshot({ path: path.join(output, '07-admin-mobile.png'), fullPage: true });
    result.screenshots.push('07-admin-mobile.png');
    result.checks.push('admin mobile viewport has no horizontal overflow');
  } finally {
    if (logsDenied) {
      runIcacls([logsPath, '/remove:d', `IIS AppPool\\${appPool}`]);
    }
    runAppcmd(['start', 'apppool', `/apppool.name:${backendPool}`]);
    if (fs.existsSync(backupPath)) {
      runAppcmd(['stop', 'apppool', `/apppool.name:${appPool}`]);
      fs.copyFileSync(backupPath, dbPath);
      fs.unlinkSync(backupPath);
      runAppcmd(['start', 'apppool', `/apppool.name:${appPool}`]);
    }
    await browser.close();
  }

  fs.writeFileSync(path.join(output, 'results.json'), JSON.stringify(result, null, 2), 'utf8');
  console.log(JSON.stringify(result, null, 2));
}

main().catch(error => {
  console.error(error.stack || error.message || error);
  process.exitCode = 1;
});
