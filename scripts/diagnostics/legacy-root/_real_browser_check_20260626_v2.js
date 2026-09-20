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

async function adminLogin(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    if (await adminPage.locator('input[name="password"]').count() > 0) {
        await adminPage.fill('input[name="username"]', ADMIN_USER);
        await adminPage.fill('input[name="password"]', ADMIN_PASS);
        await adminPage.click('button[type="submit"]');
        await adminPage.waitForTimeout(1500);
    }
}

async function getFirstRequestInfo(adminPage) {
    await adminPage.goto(`${ADMIN}/Admin/Default.aspx`, { waitUntil: 'networkidle' });
    await adminPage.waitForTimeout(800);
    const first = adminPage.locator('section:has(h2:has-text("待审核申请")) article.item').first();
    if (await first.count() === 0) return null;
    return await first.innerText();
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
        // ========== Test 1: Vue after authorization ==========
        log('TEST1', 'Open Vue page through gateway');
        const vueUrl = `${GATEWAY}/proxy/2027/jvs-aps-ui/index.html`;
        await page.goto(vueUrl, { waitUntil: 'networkidle', timeout: 15000 });
        await page.screenshot({ path: 'E:\\验证页面\\_test1_before_apply.png' });
        const title1 = await page.title();
        log('TEST1', `Page title: ${title1}`);

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

        await adminLogin(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test1_admin_dashboard.png' });
        const approved1 = await approveFirstRequest(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test1_after_approve.png' });
        results.push({ test: 'TEST1', step: 'approve', ok: approved1 });

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
        await adminLogin(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test2_before_update.png' });
        const authSection = adminPage.locator('section:has(h2:has-text("设备授权"))');
        const firstAuth = authSection.locator('article.item').first();
        if (await firstAuth.count() > 0) {
            const updateForm = firstAuth.locator('form:has(input[name="action"][value="update-authorization-sites"])');
            if (await updateForm.count() > 0) {
                await updateForm.locator('button[type="submit"]').click();
                await adminPage.waitForTimeout(1500);
                await adminPage.screenshot({ path: 'E:\\验证页面\\_test2_after_update.png' });
                const errorText = await adminPage.locator('body').innerText();
                results.push({ test: 'TEST2', step: 'update-sites', ok: !errorText.includes('服务器错误') && !errorText.includes('异常') && !errorText.includes('Error'), note: errorText.substring(0, 100) });
            } else {
                results.push({ test: 'TEST2', step: 'update-sites', ok: false, note: 'no update form found in auth section' });
            }
        } else {
            results.push({ test: 'TEST2', step: 'update-sites', ok: false, note: 'no authorizations' });
        }

        // ========== Test 3: Mobile app info override ==========
        const imei = crypto.randomUUID().replace(/-/g, '').toUpperCase();
        log('TEST3', `Simulate mobile app login with full info, IMEI=${imei}`);
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

        const loginInfo = await getFirstRequestInfo(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test3_after_login.png' });
        const loginHasName = loginInfo && (loginInfo.includes('杜同学') || loginInfo.includes('dubx'));
        results.push({ test: 'TEST3', step: 'login-request-has-name', ok: !!loginHasName, note: loginInfo ? loginInfo.substring(0, 160).replace(/\s+/g, ' ') : 'no request' });

        log('TEST3', 'Simulate subsequent mobile request with SAME IMEI but missing name/company');
        const partialResp = await fetchText(`${GATEWAY}/proxy/2027/BillForm/DoAccountSetConfig?isApp=1&DataCenterId=001&LocaleId=2052&device_imei=${imei}`, {
            method: 'GET',
            headers: {
                'User-Agent': 'kuaipu-mobile-app/1.0',
                'X-Requested-With': 'com.kuaipu.mobile'
            }
        });
        log('TEST3', `Partial response: ${partialResp.status}`);

        const partialInfo = await getFirstRequestInfo(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test3_after_partial.png' });
        const namePreserved = partialInfo && (partialInfo.includes('杜同学') || partialInfo.includes('dubx'));
        const noDuplicateIncomplete = partialInfo && !partialInfo.includes('申请人\n移动APP');
        results.push({ test: 'TEST3', step: 'name-preserved-after-partial', ok: !!namePreserved, note: partialInfo ? partialInfo.substring(0, 160).replace(/\s+/g, ' ') : 'no request' });
        results.push({ test: 'TEST3', step: 'no-incomplete-duplicate', ok: !!noDuplicateIncomplete, note: partialInfo ? partialInfo.substring(0, 160).replace(/\s+/g, ' ') : 'no request' });

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
        await adminLogin(adminPage);
        await adminPage.screenshot({ path: 'E:\\验证页面\\_test5_before_issue.png' });
        const authSection5 = adminPage.locator('section:has(h2:has-text("设备授权"))');
        const firstAuth5 = authSection5.locator('article.item').first();
        if (await firstAuth5.count() > 0) {
            const issueBtn = firstAuth5.locator('form:has(input[name="action"][value="issue-app-credential"]) button[type="submit"]');
            if (await issueBtn.count() > 0) {
                await issueBtn.click();
                await adminPage.waitForTimeout(1000);
                await adminPage.screenshot({ path: 'E:\\验证页面\\_test5_credential.png' });
                const credText = await adminPage.locator('body').innerText();
                const keyMatch = credText.match(/APP Key\s+([a-zA-Z0-9_]+)/);
                const secretMatch = credText.match(/APP Secret\s+([a-zA-Z0-9_\-]+)/);
                results.push({ test: 'TEST5', step: 'issue-credential', ok: keyMatch && secretMatch, note: `key=${keyMatch ? keyMatch[1] : 'none'}` });

                if (keyMatch && secretMatch) {
                    // Try call API with credential
                    const key = keyMatch[1];
                    const secret = secretMatch[1];
                    const timestamp = Math.floor(Date.now() / 1000).toString();
                    const nonce = crypto.randomUUID().replace(/-/g, '');
                    const bodyHash = crypto.createHash('sha256').update('').digest('hex');
                    const payload = `${timestamp}\n${nonce}\nGET\n/proxy/2027/api/ping\n\n${bodyHash}`;
                    const signature = crypto.createHmac('sha256', secret).update(payload).digest('hex');
                    const apiResp = await fetchText(`${GATEWAY}/proxy/2027/api/ping`, {
                        method: 'GET',
                        headers: {
                            'X-Gateway-Device-Key': key,
                            'X-Gateway-Device-Timestamp': timestamp,
                            'X-Gateway-Device-Nonce': nonce,
                            'X-Gateway-Device-Body-SHA256': bodyHash,
                            'X-Gateway-Device-Signature': signature
                        }
                    });
                    log('TEST5', `API call response: ${apiResp.status}`);
                    results.push({ test: 'TEST5', step: 'api-call-with-credential', ok: apiResp.status === 200, note: `status=${apiResp.status}` });
                }
            } else {
                results.push({ test: 'TEST5', step: 'issue-credential', ok: false, note: 'no issue button in auth section' });
            }
        } else {
            results.push({ test: 'TEST5', step: 'issue-credential', ok: false, note: 'no authorizations' });
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
