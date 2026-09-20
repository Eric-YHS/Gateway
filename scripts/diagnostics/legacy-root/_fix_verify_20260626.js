const { chromium } = require('playwright');
const http = require('http');
const crypto = require('crypto');

const GATEWAY = 'http://100.113.71.64:15050';
const ADMIN = 'http://100.113.71.64:15051';
const ADMIN_USER = 'gateway-admin';
const ADMIN_PASS = 'GatewayDemo!2026';

function log(title, detail) {
    console.log(`[${new Date().toLocaleTimeString()}] ${title}: ${detail}`);
}

async function fetchText(url, opts = {}) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const req = http.request({
            hostname: u.hostname,
            port: u.port,
            path: u.pathname + u.search,
            method: opts.method || 'GET',
            headers: opts.headers || {}
        }, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        });
        req.on('error', reject);
        if (opts.body) req.write(opts.body);
        req.end();
    });
}

async function adminLogin(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    if (await adminPage.locator('input[name="password"]').count() > 0) {
        await adminPage.fill('input[name="username"]', ADMIN_USER);
        await adminPage.fill('input[name="password"]', ADMIN_PASS);
        await adminPage.click('button[type="submit"]');
        await adminPage.waitForTimeout(1500);
    }
}

async function getFirstPendingInfo(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    await adminPage.waitForTimeout(800);
    const first = adminPage.locator('section:has(h2:has-text("待审核申请")) article.item').first();
    if (await first.count() === 0) return null;
    return await first.innerText();
}

async function countPendingRequests(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    await adminPage.waitForTimeout(800);
    return await adminPage.locator('section:has(h2:has-text("待审核申请")) article.item').count();
}

async function approveFirstRequest(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    await adminPage.waitForTimeout(800);
    const first = adminPage.locator('section:has(h2:has-text("待审核申请")) article.item').first();
    if (await first.count() === 0) return false;
    await first.locator('button[value="approve-request"]').click();
    await adminPage.waitForTimeout(1200);
    return true;
}

async function runTest() {
    const browser = await chromium.launch({ headless: false });
    const context = await browser.newContext();
    const page = await context.newPage();
    const adminPage = await context.newPage();
    const results = [];

    try {
        // ========== Regression: Vue after authorization ==========
        log('REGRESSION', 'Vue project authorization');
        const vueUrl = `${GATEWAY}/proxy/2027/jvs-aps-ui/index.html`;
        await page.goto(vueUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.screenshot({ path: 'E:\\验证页面\\_fix_test1_before_apply.png' });

        if (await page.locator('input[name="companyName"]').count() > 0) {
            await page.fill('input[name="companyName"]', '测试企业Vue');
            await page.fill('input[name="applicantName"]', '测试员Vue');
            await page.fill('input[name="phone"]', '13800000001');
            await page.fill('input[name="reason"]', 'Vue项目授权访问测试');
            await page.click('button[type="submit"]');
            await page.waitForTimeout(1000);
        }

        await adminLogin(adminPage);
        const approved1 = await approveFirstRequest(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_fix_test1_after_approve.png' });

        await page.goto(vueUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.waitForTimeout(1000);
        await page.screenshot({ path: 'E:\\验证页面\\_fix_test1_revisit_vue.png' });
        const vueTitle = await page.title();
        const vueBody = await page.locator('body').innerText();
        const notFound = vueBody.includes('404') || vueBody.includes('找不到') || vueTitle.includes('页面不存在');
        results.push({ test: 'REGRESSION-Vue', step: 'vue-access-after-auth', ok: !notFound, note: `title=${vueTitle}` });

        // ========== Regression: Update authorization sites ==========
        log('REGRESSION', 'Update authorization sites');
        await adminLogin(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_fix_test2_before_update.png' });
        const authSection = adminPage.locator('section:has(h2:has-text("设备授权"))');
        const firstAuth = authSection.locator('article.item').first();
        if (await firstAuth.count() > 0) {
            const updateForm = firstAuth.locator('form:has(input[name="action"][value="update-authorization-sites"])');
            if (await updateForm.count() > 0) {
                await updateForm.locator('button[type="submit"]').click();
                await adminPage.waitForTimeout(1500);
                await adminPage.screenshot({ path: 'E:\\验证页面\\_fix_test2_after_update.png' });
                const errorText = await adminPage.locator('body').innerText();
                results.push({ test: 'REGRESSION-Update', step: 'update-sites', ok: !errorText.includes('服务器错误') && !errorText.includes('异常') && !errorText.includes('Error'), note: errorText.substring(0, 100) });
            } else {
                results.push({ test: 'REGRESSION-Update', step: 'update-sites', ok: false, note: 'no update form' });
            }
        } else {
            results.push({ test: 'REGRESSION-Update', step: 'update-sites', ok: false, note: 'no authorizations' });
        }

        // ========== FIX TEST: Mobile identity preservation ==========
        const imei = crypto.randomUUID().replace(/-/g, '').toUpperCase();
        log('FIX-TEST3', `Mobile login with full info, IMEI=${imei}`);
        const loginBody = JSON.stringify({
            Action: 'Login',
            UserId: 'dubx',
            UserName: '杜同学',
            CompanyId: '3093683',
            DataCenterId: '001',
            DeviceImei: imei,
            UserPwd: 'mockpwd'
        });
        const loginResp = await fetchText(`${GATEWAY}/proxy/2027/user/login`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'User-Agent': 'kuaipu-mobile-app/1.0',
                'X-Requested-With': 'com.kuaipu.mobile'
            },
            body: loginBody
        });
        log('FIX-TEST3', `Login response: ${loginResp.status}`);

        const loginInfo = await getFirstPendingInfo(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_fix_test3_after_login.png' });
        const loginHasName = loginInfo && (loginInfo.includes('杜同学') || loginInfo.includes('dubx'));
        results.push({ test: 'FIX-TEST3', step: 'login-request-has-name', ok: !!loginHasName, note: loginInfo ? loginInfo.substring(0, 160).replace(/\s+/g, ' ') : 'no request' });

        const pendingAfterLogin = await countPendingRequests(adminPage);
        results.push({ test: 'FIX-TEST3', step: 'pending-count-after-login', ok: pendingAfterLogin === 1, note: `count=${pendingAfterLogin}` });

        log('FIX-TEST3', 'Subsequent mobile request with SAME IMEI but missing name/company');
        const partialResp = await fetchText(`${GATEWAY}/proxy/2027/BillForm/DoAccountSetConfig?isApp=1&DataCenterId=001&LocaleId=2052&device_imei=${imei}`, {
            method: 'GET',
            headers: {
                'User-Agent': 'kuaipu-mobile-app/1.0',
                'X-Requested-With': 'com.kuaipu.mobile'
            }
        });
        log('FIX-TEST3', `Partial response: ${partialResp.status}`);

        const pendingAfterPartial = await countPendingRequests(adminPage);
        const partialInfo = await getFirstPendingInfo(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_fix_test3_after_partial.png' });

        // 核心断言：不应新增待审核申请
        const noNewRequest = pendingAfterPartial === pendingAfterLogin;
        results.push({ test: 'FIX-TEST3', step: 'no-new-pending-request', ok: noNewRequest, note: `before=${pendingAfterLogin}, after=${pendingAfterPartial}` });

        // 核心断言：申请人/企业名称应保持完整
        const namePreserved = partialInfo && (partialInfo.includes('杜同学') || partialInfo.includes('dubx'));
        results.push({ test: 'FIX-TEST3', step: 'name-preserved', ok: !!namePreserved, note: partialInfo ? partialInfo.substring(0, 200).replace(/\s+/g, ' ') : 'no request' });

        const companyPreserved = partialInfo && partialInfo.includes('3093683');
        results.push({ test: 'FIX-TEST3', step: 'company-preserved', ok: !!companyPreserved, note: partialInfo ? partialInfo.substring(0, 200).replace(/\s+/g, ' ') : 'no request' });

        const reasonPreserved = partialInfo && partialInfo.includes('UserName:杜同学');
        results.push({ test: 'FIX-TEST3', step: 'reason-summary-preserved', ok: !!reasonPreserved, note: partialInfo ? partialInfo.substring(0, 200).replace(/\s+/g, ' ') : 'no request' });

        // ========== Regression: External system no user-agent ==========
        log('REGRESSION', 'External system call without User-Agent');
        const externalResp = await fetchText(`${GATEWAY}/proxy/2027/api/ping`, {
            method: 'GET',
            headers: {}
        });
        log('REGRESSION', `External response: ${externalResp.status}`);
        results.push({ test: 'REGRESSION-External', step: 'no-user-agent', ok: externalResp.status !== 500, note: `status=${externalResp.status}` });

        await adminPage.close();
        await page.close();
    } catch (err) {
        console.error('Test error:', err);
        results.push({ test: 'GLOBAL', step: 'error', ok: false, note: err.message });
    } finally {
        await browser.close();
    }

    console.log('\n========== RESULTS ==========');
    results.forEach(r => {
        console.log(`${r.test} / ${r.step}: ${r.ok ? 'PASS' : 'FAIL'} ${r.note || ''}`);
    });
}

runTest().catch(console.error);
