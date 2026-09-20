const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const BASE_URL = process.env.GATEWAY_URL || 'http://127.0.0.1:24550';
const ADMIN_URL = process.env.ADMIN_URL || 'http://127.0.0.1:24551';
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD || (() => {
  try {
    return fs.readFileSync('E:\\验证页面\\deliverables\\GatewayDemo.Legacy-net472\\src\\GatewayDemo.Legacy.Web\\admin-password.txt', 'utf8').trim();
  } catch {
    return 'GatewayDemo!2026';
  }
})();
const OUTPUT_DIR = path.join(__dirname, 'browser-test-output');
if (!fs.existsSync(OUTPUT_DIR)) fs.mkdirSync(OUTPUT_DIR, { recursive: true });

function log(msg) {
  const line = `[${new Date().toISOString()}] ${msg}`;
  console.log(line);
  fs.appendFileSync(path.join(OUTPUT_DIR, 'test.log'), line + '\n');
}

async function screenshot(page, name) {
  const p = path.join(OUTPUT_DIR, `${name}.png`);
  await page.screenshot({ path: p, fullPage: true });
  log(`screenshot: ${p}`);
  return p;
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    locale: 'zh-CN',
    userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
    viewport: { width: 1280, height: 900 }
  });
  const page = await context.newPage();
  const errors = [];
  const consoleLogs = [];
  page.on('console', msg => consoleLogs.push(`[${msg.type()}] ${msg.text()}`));
  page.on('pageerror', err => errors.push(err.message));
  page.on('response', resp => {
    if (resp.status() >= 400) {
      log(`HTTP error ${resp.status()}: ${resp.url()}`);
    }
  });

  const issues = [];
  function recordIssue(title, detail) {
    issues.push({ title, detail });
    log(`ISSUE: ${title} - ${detail}`);
  }

  try {
    log(`=== Test start: BASE_URL=${BASE_URL}, ADMIN_URL=${ADMIN_URL} ===`);

    // ===== 场景1：浏览器申请 + 批准后访问 Vue 路径 =====
    log('Scenario 1: browser request access, approve, then visit Vue path');
    log('Step 1.1: visit /portal and submit access request');
    await page.goto(`${BASE_URL}/portal`, { waitUntil: 'networkidle', timeout: 20000 });
    await page.fill('input[name="companyName"]', '3093683');
    await page.fill('input[name="applicantName"]', 'browser-tester');
    await page.fill('input[name="phone"]', '13800138000');
    await page.fill('input[name="reason"]', '真实浏览器测试访问ERP门户');
    await page.click('button[type="submit"]');
    await page.waitForLoadState('networkidle', { timeout: 15000 });
    await screenshot(page, '11-request-submitted');
    const submitBody = await page.locator('body').innerText();
    log(`after submit body snippet: ${submitBody.slice(0, 200)}`);

    log('Step 1.2: login admin and approve');
    const adminPage = await context.newPage();
    await adminPage.goto(`${ADMIN_URL}/Admin/Default.aspx`, { waitUntil: 'networkidle', timeout: 20000 });
    await adminPage.fill('input[name="username"]', 'gateway-admin');
    await adminPage.fill('input[name="password"]', ADMIN_PASSWORD);
    await adminPage.click('button[type="submit"]');
    await adminPage.waitForLoadState('networkidle', { timeout: 15000 });
    await screenshot(adminPage, '12-admin-dashboard-pending');
    const adminBody = await adminPage.locator('body').innerText();
    if (adminBody.includes('当前没有待审核申请') || adminBody.includes('暂无')) {
      recordIssue('待审核申请未显示', '提交申请后管理后台未看到待审核条目');
    }

    // 查找并点击批准按钮
    const approveBtn = adminPage.locator('button:has-text("批准"), button[value="approve"]').first();
    if (await approveBtn.count() > 0) {
      await approveBtn.click();
      await adminPage.waitForLoadState('networkidle', { timeout: 15000 });
      await screenshot(adminPage, '13-admin-after-approve');
    } else {
      recordIssue('未找到批准按钮', '管理后台待审核区域没有批准按钮');
    }

    log('Step 1.3: visit Vue path /jvs-aps-ui/index.html after approval');
    await page.goto(`${BASE_URL}/jvs-aps-ui/index.html`, { waitUntil: 'networkidle', timeout: 20000 });
    await screenshot(page, '14-vue-after-approve');
    const vueUrl = page.url();
    const vueStatus = await page.evaluate(() => ({ title: document.title, body: document.body.innerText.slice(0, 300) }));
    log(`vue after approve url=${vueUrl}, title=${vueStatus.title}, body=${vueStatus.body}`);
    if (vueStatus.body.includes('404') || vueStatus.body.includes('找不到') || vueStatus.body.includes('页面不存在') || vueStatus.title.includes('404')) {
      recordIssue('Vue授权后访问404', `访问 ${BASE_URL}/jvs-aps-ui/index.html 出现404/页面不存在`);
    }
    // 检查是否仍是拦截页
    if (vueUrl.includes('Gateway/Default.aspx') || vueStatus.body.includes('需要复核') || vueStatus.body.includes('请先提交访问申请')) {
      recordIssue('Vue授权后仍被拦截', '设备已批准但访问Vue路径仍跳转到申请页');
    }

    // ===== 场景2：更新授权站点 =====
    log('Scenario 2: update authorized sites');
    await adminPage.reload({ waitUntil: 'networkidle' });
    await screenshot(adminPage, '21-admin-before-update-sites');
    const updateBtn = adminPage.locator('button:has-text("更新授权站点")').first();
    if (await updateBtn.count() > 0) {
      await updateBtn.click();
      await adminPage.waitForLoadState('networkidle', { timeout: 15000 });
      await screenshot(adminPage, '22-admin-after-update-sites');
      const afterUpdateBody = await adminPage.locator('body').innerText();
      if (afterUpdateBody.includes('错误') || afterUpdateBody.includes('异常') || afterUpdateBody.includes('失败')) {
        recordIssue('更新授权站点报错', '点击"更新授权站点"后出现错误/异常/失败提示');
      }
    } else {
      log('No "更新授权站点" button found; skipping explicit click');
    }

    // ===== 场景3：模拟移动请求授权，检查是否覆盖申请原因和移动端信息 =====
    log('Scenario 3: mobile request with full user info');
    const mobileContext = await browser.newContext({
      locale: 'zh-CN',
      userAgent: '快普移动/3.0 (Android 14; Mobile)',
      viewport: { width: 414, height: 896 }
    });
    const mobilePage = await mobileContext.newPage();

    // 模拟移动客户端访问受保护接口（/user/login），携带完整用户信息
    const mobileLoginUrl = `${BASE_URL}/user/login?CompanyId=3093683&UserId=dubx&UserName=dubx&DataCenterId=001&DeviceImei=88DF4F9B-6015-40F8-9485-F48C0608A9D7&MobileUserCount=1798&EntryUrl=http://10.188.188.249:15050`;
    await mobilePage.goto(mobileLoginUrl, { waitUntil: 'networkidle', timeout: 20000 });
    await screenshot(mobilePage, '31-mobile-login-intercept');
    const mobileBody = await mobilePage.locator('body').innerText();
    log(`mobile intercept body snippet: ${mobileBody.slice(0, 300)}`);

    // 提交申请（如果页面有表单）
    if (await mobilePage.locator('input[name="companyName"]').count() > 0) {
      await mobilePage.fill('input[name="companyName"]', '3093683');
      await mobilePage.fill('input[name="applicantName"]', 'dubx');
      await mobilePage.fill('input[name="phone"]', '13800138001');
      await mobilePage.fill('input[name="reason"]', '移动客户端真实浏览器测试');
      await mobilePage.click('button[type="submit"]');
      await mobilePage.waitForLoadState('networkidle', { timeout: 15000 });
      await screenshot(mobilePage, '32-mobile-request-submitted');
    }

    // 管理后台查看移动申请详情
    await adminPage.reload({ waitUntil: 'networkidle' });
    await screenshot(adminPage, '33-admin-mobile-pending');
    const detailLink = adminPage.locator('.js-authorization-item a, .js-authorization-item button').first();
    if (await detailLink.count() > 0) {
      await detailLink.click();
      await adminPage.waitForTimeout(1500);
      await screenshot(adminPage, '34-admin-mobile-detail');
      const detailBody = await adminPage.locator('body').innerText();
      log(`mobile request detail body: ${detailBody}`);

      // 检查是否被覆盖
      const hasOriginalReason = detailBody.includes('移动客户端真实浏览器测试') || detailBody.includes('快普移动客户端');
      const hasMobileInfo = detailBody.includes('88DF4F9B') || detailBody.includes('移动用户数') || detailBody.includes('入口地址');
      if (!hasOriginalReason) {
        recordIssue('移动申请原因被覆盖', '管理后台详情中没有保留原始申请原因');
      }
      if (!hasMobileInfo) {
        recordIssue('移动端信息丢失', '管理后台详情中没有显示移动设备/入口地址等信息');
      }
      // 如果同时有 UserId=dubx 且申请人被改成了其他，说明被覆盖
      if (detailBody.includes('移动APP') && detailBody.includes('dubx')) {
        recordIssue('移动端姓名信息冲突', '申请人同时出现"移动APP"和"dubx"，疑似移动端信息覆盖逻辑问题');
      }
    } else {
      log('No pending mobile request detail link found');
    }

    await mobileContext.close();
    await adminPage.close();

    // ===== 总结 =====
    log('=== Test finished ===');
    const report = {
      timestamp: new Date().toISOString(),
      baseUrl: BASE_URL,
      adminUrl: ADMIN_URL,
      issues,
      consoleLogs,
      pageErrors: errors
    };
    fs.writeFileSync(path.join(OUTPUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
    if (issues.length === 0) {
      log('No issues found in real browser test.');
    } else {
      log(`Found ${issues.length} issue(s):`);
      issues.forEach((i, idx) => log(`${idx + 1}. ${i.title}: ${i.detail}`));
    }
  } catch (e) {
    log(`ERROR: ${e.message}\n${e.stack}`);
    await screenshot(page, 'error');
  } finally {
    fs.writeFileSync(path.join(OUTPUT_DIR, 'console.log'), consoleLogs.join('\n'));
    fs.writeFileSync(path.join(OUTPUT_DIR, 'pageerrors.log'), errors.join('\n'));
    await browser.close();
  }
})();
