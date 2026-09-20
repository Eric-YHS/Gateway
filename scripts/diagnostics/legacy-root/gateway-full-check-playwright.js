const { chromium } = require('playwright');
const assert = require('assert');
const http = require('http');

const HOSTNAME = 'DESKTOP-79LK104';
const GATEWAY_PORT = 24150;
const ADMIN_PORT = 24151;
const BACKEND_PORT = 24155;
const GATEWAY_BASE = `http://${HOSTNAME}:${GATEWAY_PORT}`;
const ADMIN_BASE = `http://${HOSTNAME}:${ADMIN_PORT}`;
const BACKEND_BASE = `http://${HOSTNAME}:${BACKEND_PORT}`;

// 每次测试前清理 SQLite 数据库，避免历史待审核申请干扰
const dbPath = 'E:/验证页面/deliverables/GatewayDemo.Legacy-net472/src/GatewayDemo.Legacy.Web/App_Data/gateway-demo-legacy.db';
if (require('fs').existsSync(dbPath)) {
  try { require('fs').unlinkSync(dbPath); console.log('Cleaned old test database.'); } catch (e) { console.log('Could not clean DB:', e.message); }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function httpGetStatus(url, options = {}) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, { ...options, timeout: 10000 }, (res) => {
      let body = '';
      res.on('data', (chunk) => (body += chunk));
      res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body }));
    });
    req.on('error', reject);
    req.on('timeout', () => reject(new Error('timeout')));
  });
}

function log(step, detail) {
  console.log(`[${step}] ${detail}`);
}

async function run() {
  const results = [];
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  function record(name, ok, detail) {
    results.push({ name, ok, detail });
    console.log(`${ok ? 'PASS' : 'FAIL'}: ${name}${detail ? ' — ' + detail : ''}`);
  }

  try {
    // 1. 健康检查
    try {
      const r = await httpGetStatus(`${GATEWAY_BASE}/healthz.ashx`);
      record('健康检查', r.status === 200, `status=${r.status}`);
    } catch (e) {
      record('健康检查', false, e.message);
    }

    // 2. 网关首页访问
    try {
      await page.goto(`${GATEWAY_BASE}/gateway`, { waitUntil: 'networkidle' });
      const title = await page.title();
      const hasAppName = await page.locator('text=统一认证访问网关').count() > 0;
      const hasDeviceCode = await page.locator('text=设备编号').count() > 0;
      record('网关首页', title.includes('访问申请') && hasAppName && hasDeviceCode, `title=${title}`);
    } catch (e) {
      record('网关首页', false, e.message);
    }

    // 3. 访问受保护入口被拦截并重定向到申请页
    try {
      await page.goto(`${GATEWAY_BASE}/portal`, { waitUntil: 'networkidle' });
      await sleep(500);
      const url = page.url();
      const hasForm = await page.locator('form[action="/Gateway/Default.aspx"]').count() > 0;
      const hasApplyButton = await page.locator('button:has-text("提交申请")').count() > 0;
      record('未授权访问/portal被拦截', hasForm && hasApplyButton, `url=${url}`);
    } catch (e) {
      record('未授权访问/portal被拦截', false, e.message);
    }

    // 4. 提交设备申请
    let deviceCode = '';
    let deviceId = '';
    try {
      deviceCode = await page.locator('.grid div:has-text("设备编号") strong').first().textContent();
      deviceId = await page.locator('.grid div:has-text("设备标识") strong').first().textContent();
      await page.fill('input[name="companyName"]', '测试企业');
      await page.fill('input[name="applicantName"]', '测试申请人');
      await page.fill('input[name="phone"]', '13800138000');
      await page.fill('input[name="reason"]', '真实浏览器全流程测试');
      await page.click('button:has-text("提交申请")');
      await page.waitForLoadState('networkidle');
      await sleep(500);
      const url = page.url();
      const hasPending = await page.locator('text=待审核').count() > 0 || await page.locator('text=申请已提交').count() > 0;
      record('提交设备申请', hasPending, `url=${url}, deviceCode=${deviceCode}, deviceId=${deviceId}`);
    } catch (e) {
      record('提交设备申请', false, e.message);
    }

    // 5. 管理后台端口隔离：从业务端口访问 /admin 最终应 404
    try {
      const isolatePage = await context.newPage();
      await isolatePage.goto(`${GATEWAY_BASE}/admin`, { waitUntil: 'networkidle' });
      await sleep(500);
      const body = await isolatePage.content();
      const url = isolatePage.url();
      const urlMatches = url.toLowerCase().startsWith(`${GATEWAY_BASE}/admin`.toLowerCase());
      const is404 = urlMatches && (body.includes('页面不存在') || body.includes('404') || body.includes('Not Found'));
      record('业务端口隔离/admin', is404, `url=${url}, title=${await isolatePage.title()}`);
      await isolatePage.close();
    } catch (e) {
      record('业务端口隔离/admin', false, e.message);
    }

    // 6. 管理后台登录（在独立管理端口）
    const adminPage = await context.newPage();
    try {
      await adminPage.goto(`${ADMIN_BASE}/admin`, { waitUntil: 'networkidle' });
      await adminPage.fill('input[name="username"]', 'gateway-admin');
      await adminPage.fill('input[name="password"]', 'GatewayDemo!2026');
      await adminPage.click('button:has-text("进入后台")');
      await adminPage.waitForLoadState('networkidle');
      await sleep(500);
      const url = adminPage.url();
      const hasDashboard = await adminPage.locator('text=网关管理台').count() > 0;
      const hasPendingSection = await adminPage.locator('text=待审核申请').count() > 0;
      record('管理后台登录', hasDashboard && hasPendingSection, `url=${url}`);
    } catch (e) {
      record('管理后台登录', false, e.message);
    }

    // 7. 审批设备申请
    try {
      await adminPage.waitForSelector('button[name="action"][value="approve-request"]', { timeout: 5000 });
      await adminPage.screenshot({ path: 'E:/验证页面/_debug_before_approve.png', fullPage: true });
      const pendingDeviceCode = await adminPage.locator('.item-head h3').first().textContent();
      console.log(`Pending request device: ${pendingDeviceCode}, current device: ${deviceCode} (${deviceId})`);
      const checkbox = adminPage.locator('input[name="siteKeys"][value="2027"]').first();
      if (await checkbox.count() > 0) {
        await checkbox.check();
      }
      const allowAll = adminPage.locator('input[name="allowAllSites"]').first();
      if (await allowAll.count() > 0 && await allowAll.isChecked()) {
        await allowAll.uncheck();
      }
      await adminPage.click('button[name="action"][value="approve-request"]');
      await adminPage.waitForLoadState('networkidle');
      await sleep(1000);
      const body = await adminPage.content();
      await adminPage.screenshot({ path: 'E:/验证页面/_debug_after_approve.png', fullPage: true });
      require('fs').writeFileSync('E:/验证页面/_debug_after_approve.html', body);
      const noPending = body.includes('当前没有待审核申请');
      const hasApprovedAuth = body.includes('已授权');
      const hasError = body.includes('登录失败') || body.includes('操作失败') || body.includes('令牌无效') || body.includes('请至少选择一个授权站点');
      record('审批设备申请', hasApprovedAuth && !hasError, `url=${adminPage.url()}, hasPending=${body.includes('待审核')}, hasApprovedAuth=${hasApprovedAuth}, hasError=${hasError}`);
    } catch (e) {
      record('审批设备申请', false, e.message);
    }

    // 8. 授权后访问网关申请页，应自动重定向到目标入口并被代理到 mock 后端
    const verifyPage = await context.newPage();
    try {
      await verifyPage.goto(`${GATEWAY_BASE}/Gateway/Default.aspx`, { waitUntil: 'networkidle' });
      await sleep(800);
      const url = verifyPage.url();
      const body = await verifyPage.content();
      require('fs').writeFileSync('E:/验证页面/_debug_after_auth_gateway.html', `url=${url}\n` + body);
      const reachedBackend = url.toLowerCase().includes('/portal') || url.toLowerCase().includes('/login');
      const isErpContent = body.includes('ERP 主站') || body.includes('门户') || body.includes('mock');
      record('授权后网关页重定向到后端', reachedBackend && isErpContent, `url=${url}, title=${await verifyPage.title()}`);
    } catch (e) {
      record('授权后网关页重定向到后端', false, e.message);
    }

    // 9. 直接访问 /portal，应被放行到 mock 后端
    try {
      await verifyPage.goto(`${GATEWAY_BASE}/portal`, { waitUntil: 'networkidle' });
      await sleep(800);
      const url = verifyPage.url();
      const body = await verifyPage.content();
      const isErpContent = body.includes('ERP 主站') || body.includes('门户') || body.includes('mock');
      record('授权后访问/portal放行', isErpContent, `url=${url}, title=${await verifyPage.title()}`);
    } catch (e) {
      record('授权后访问/portal放行', false, e.message);
    }

    // 9b. 登录 mock 后端，验证可进入真正的 ERP 门户
    try {
      if (verifyPage.url().toLowerCase().includes('/login')) {
        await verifyPage.fill('input[name="username"]', 'mock-user');
        await verifyPage.fill('input[name="password"]', 'MockDemo!2026');
        await verifyPage.click('button:has-text("登录系统")');
        await verifyPage.waitForLoadState('networkidle');
        await sleep(800);
      }
      const portalBody = await verifyPage.content();
      const portalTitle = await verifyPage.title();
      const isPortal = portalBody.includes('订单') && portalBody.includes('库存') && portalTitle.includes('门户');
      record('Mock后端登录后进入门户', isPortal, `url=${verifyPage.url()}, title=${portalTitle}`);
    } catch (e) {
      record('Mock后端登录后进入门户', false, e.message);
    }

    // 10. 访问静态资源（mock.css）应被放行（使用已授权上下文的 cookie）
    try {
      const res = await context.request.get(`${GATEWAY_BASE}/assets/mock.css`);
      const body = await res.text();
      record('静态资源mock.css放行', res.status() === 200 && body.includes('body'), `status=${res.status()}`);
    } catch (e) {
      record('静态资源mock.css放行', false, e.message);
    }

    // 10. 访问匿名路径 /proxy/2027/sys.ashx（不存在但按规则应尝试匿名代理， mock 后端无此文件会 404，但不应被网关拦截页拦截）
    try {
      const res = await context.request.get(`${GATEWAY_BASE}/proxy/2027/sys.ashx`);
      const body = await res.text();
      const isNotGatewayPage = !body.includes('统一认证访问网关');
      record('匿名路径sys.ashx不返回网关拦截页', isNotGatewayPage, `status=${res.status()}`);
    } catch (e) {
      record('匿名路径sys.ashx不返回网关拦截页', false, e.message);
    }

    // 11. 访问不存在的站点应 404
    try {
      const r = await httpGetStatus(`${GATEWAY_BASE}/proxy/nonexistent/test`);
      record('不存在站点返回404', r.status === 404, `status=${r.status}`);
    } catch (e) {
      record('不存在站点返回404', false, e.message);
    }

    // 12. 检查设备 Cookie 已设置
    try {
      const cookies = await context.cookies(`${GATEWAY_BASE}`);
      const deviceCookie = cookies.find((c) => c.name === 'gw_device_credential');
      record('设备Cookie已设置', !!deviceCookie, deviceCookie ? `name=${deviceCookie.name}` : 'missing');
    } catch (e) {
      record('设备Cookie已设置', false, e.message);
    }

    // 13. 管理后台撤销授权后再次访问应被拦截
    try {
      const revokeButton = adminPage.locator('button:has-text("撤销授权")').first();
      if (await revokeButton.count() > 0) {
        await revokeButton.click();
        await adminPage.waitForLoadState('networkidle');
        await sleep(500);
      }
      await page.goto(`${GATEWAY_BASE}/portal`, { waitUntil: 'networkidle' });
      await sleep(500);
      const reblocked = await page.locator('button:has-text("提交申请")').count() > 0;
      record('撤销授权后重新被拦截', reblocked, `url=${page.url()}`);
    } catch (e) {
      record('撤销授权后重新被拦截', false, e.message);
    }

    // 14. 直接访问后端 mock 站点确认其独立可用
    try {
      const r = await httpGetStatus(`${BACKEND_BASE}/mock/erp-main/portal`);
      record('Mock后端独立可用', r.status === 302, `status=${r.status}`);
    } catch (e) {
      record('Mock后端独立可用', false, e.message);
    }
  } finally {
    await browser.close();
  }

  console.log('\n=== 测试结果汇总 ===');
  const passed = results.filter((r) => r.ok).length;
  const failed = results.filter((r) => !r.ok).length;
  console.log(`通过: ${passed}/${results.length}, 失败: ${failed}/${results.length}`);
  if (failed > 0) {
    console.log('\n失败项:');
    results.filter((r) => !r.ok).forEach((r) => console.log(`  - ${r.name}: ${r.detail}`));
  }
  process.exit(failed > 0 ? 1 : 0);
}

run().catch((e) => {
  console.error('测试运行异常:', e);
  process.exit(1);
});
