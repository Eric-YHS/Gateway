const { chromium } = require('playwright');
const http = require('http');
const crypto = require('crypto');

const GATEWAY = 'http://100.113.71.64:15050';
const ADMIN = 'http://100.113.71.64:15051';
const MOCK = 'http://100.113.71.64:15055';
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

async function runTest() {
    const browser = await chromium.launch({ headless: false });
    const context = await browser.newContext();
    const page = await context.newPage();
    const results = [];

    try {
        // ========== Test 1: Vue after authorization ==========
        log('TEST1', 'Open Vue page through gateway');
        const vueUrl = `${GATEWAY}/proxy/2027/jvs-aps-ui/index.html`;
        await page.goto(vueUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.screenshot({ path: 'E:\\验证页面\\_test1_before_apply.png' });
        const title1 = await page.title();
        log('TEST1', `Page title: ${title1}`);

        // If redirected to gateway apply page, fill and submit
        if (await page.locator('input[name="companyName"]').count() > 0) {
            log('TEST1', 'Gateway apply form shown, submit application');
            await page.fill('input[name="companyName"]', '测试企业Vue');
            await page.fill('input[name="applicantName"]', '测试员Vue');
            await page.fill('input[name="phone"]', '13800000001');
            await page.fill('input[name="reason"]', 'Vue项目授权访问测试');
            await page.click('button[type="submit"]');
            await page.waitForTimeout(1000);
            await page.screenshot({ path: 'E:\\验证页面\\_test1_after_submit.png' });
        }

        // Approve in admin
        log('TEST1', 'Login admin and approve');
        const adminPage = await context.newPage();
        await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
        await adminPage.fill('input[name="username"]', ADMIN_USER);
        await adminPage.fill('input[name="password"]', ADMIN_PASS);
        await adminPage.click('button[type="submit"]');
        await adminPage.waitForTimeout(1500);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test1_admin_dashboard.png' });

        if (await adminPage.locator('article.item').count() > 0) {
            await adminPage.locator('article.item').first().locator('button[value="approve-request"]').click();
            await adminPage.waitForTimeout(1500);
            await adminPage.screenshot({ path: 'E:\\验证页面\\_test1_after_approve.png' });
            results.push({ test: 'TEST1', step: 'approve', ok: (await adminPage.locator('text=当前没有待审核申请').count()) > 0 });
        } else {
            results.push({ test: 'TEST1', step: 'approve', ok: false, note: 'no pending request' });
        }

        // Revisit Vue page
        log('TEST1', 'Revisit Vue page after approve');
        await page.goto(vueUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.waitForTimeout(1000);
        await page.screenshot({ path: 'E:\\验证页面\\_test1_revisit_vue.png' });
        const vueTitle = await page.title();
        const vueBody = await page.locator('body').innerText();
        const notFound = vueBody.includes('404') || vueBody.includes('找不到') || vueTitle.includes('页面不存在');
        results.push({ test: 'TEST1', step: 'vue-access-after-auth', ok: !notFound, note: `title=${vueTitle}` });

        // ========== Test 2: Update authorization sites ==========
        log('TEST2', 'Update authorization sites');
        await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
        await adminPage.waitForTimeout(1000);
        if (await adminPage.locator('article.item').count() > 0) {
            await adminPage.screenshot({ path: 'E:\\验证页面\\_test2_before_update.png' });
            const firstItem = adminPage.locator('article.item').first();
            // Find update-authorization-sites form and submit
            const updateForms = await firstItem.locator('form:has(input[name="action"][value="update-authorization-sites"])').count();
            if (updateForms > 0) {
                await firstItem.locator('form:has(input[name="action"][value="update-authorization-sites"]) button[type="submit"]').click();
                await adminPage.waitForTimeout(1500);
                await adminPage.screenshot({ path: 'E:\\验证页面\\_test2_after_update.png' });
                const errorText = await adminPage.locator('body').innerText();
                results.push({ test: 'TEST2', step: 'update-sites', ok: !errorText.includes('服务器错误') && !errorText.includes('异常'), note: errorText.substring(0, 80) });
            } else {
                results.push({ test: 'TEST2', step: 'update-sites', ok: false, note: 'no update form found' });
            }
        } else {
            results.push({ test: 'TEST2', step: 'update-sites', ok: false, note: 'no authorizations' });
        }

        // ========== Test 3: Mobile app info override ==========
        log('TEST3', 'Simulate mobile app login with full info');
        const imei = crypto.randomUUID().replace(/-/g, '').toUpperCase();
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
        log('TEST3', `Login response: ${loginResp.status}`);

        await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
        await adminPage.waitForTimeout(1000);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test3_after_login.png' });
        const loginRequestText = await adminPage.locator('article.item').first().innerText();
        const loginHasName = loginRequestText.includes('杜同学') || loginRequestText.includes('dubx');
        results.push({ test: 'TEST3', step: 'login-request-has-name', ok: loginHasName, note: loginRequestText.substring(0, 200) });

        log('TEST3', 'Simulate subsequent mobile request with partial info');
        const partialResp = await fetchText(`${GATEWAY}/proxy/2027/BillForm/DoAccountSetConfig?isApp=1&DataCenterId=001&LocaleId=2052`, {
            method: 'GET',
            headers: {
                'User-Agent': 'kuaipu-mobile-app/1.0',
                'X-Requested-With': 'com.kuaipu.mobile'
            }
        });
        log('TEST3', `Partial response: ${partialResp.status}`);

        await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
        await adminPage.waitForTimeout(1000);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test3_after_partial.png' });
        const partialRequestText = await adminPage.locator('article.item').first().innerText();
        const namePreserved = partialRequestText.includes('杜同学') || partialRequestText.includes('dubx');
        results.push({ test: 'TEST3', step: 'name-preserved-after-partial', ok: namePreserved, note: partialRequestText.substring(0, 200) });

        // ========== Test 4: External system without browser fingerprint ==========
        log('TEST4', 'External system call without User-Agent');
        const externalResp = await fetchText(`${GATEWAY}/proxy/2027/api/ping`, {
            method: 'GET',
            headers: {}
        });
        log('TEST4', `External response: ${externalResp.status}`);
        results.push({ test: 'TEST4', step: 'no-user-agent', ok: externalResp.status !== 500, note: `status=${externalResp.status}` });

        // ========== Test 5: Third-party API via gateway ==========
        log('TEST5', 'Issue app credential for third-party API');
        // Use admin to issue credential for the device from TEST3
        await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
        await adminPage.waitForTimeout(1000);
        const issueBtn = adminPage.locator('article.item').first().locator('button[type="submit"]:has-text("签发接口凭据")');
        if (await issueBtn.count() > 0) {
            await issueBtn.click();
            await adminPage.waitForTimeout(1000);
            await adminPage.screenshot({ path: 'E:\\验证页面\\_test5_credential.png' });
            const credText = await adminPage.locator('body').innerText();
            const keyMatch = credText.match(/APP Key\s+([a-zA-Z0-9_]+)/);
            const secretMatch = credText.match(/APP Secret\s+([a-zA-Z0-9_\-]+)/);
            results.push({ test: 'TEST5', step: 'issue-credential', ok: keyMatch && secretMatch, note: `key=${keyMatch ? keyMatch[1] : 'none'}` });
        } else {
            results.push({ test: 'TEST5', step: 'issue-credential', ok: false, note: 'no issue button' });
        }

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
