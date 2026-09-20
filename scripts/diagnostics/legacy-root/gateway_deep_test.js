const { chromium } = require('playwright');
const fs = require('fs');

const GATEWAY = 'http://localhost:5050';
const ADMIN = 'http://localhost:5051';
const BACKEND = 'http://localhost:5055';
const ISSUES = [];

let issueCounter = 200;

function issue(area, severity, title, detail, evidence = '') {
    const id = issueCounter++;
    ISSUES.push({ id, area, severity, title, detail, evidence });
    console.log(`[ISSUE #${id}] [${severity}] [${area}] ${title}`);
}

async function runDeepTests() {
    const browser = await chromium.launch({ headless: true });

    console.log('===== GatewayDemo.Legacy-net472 深度补充测试 =====\n');

    // =========== Part A: Admin Panel Deep Test ===========
    console.log('--- A: 管理后台深度测试 ---');
    const adminCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    const adminPage = await adminCtx.newPage();

    try {
        await adminPage.goto(ADMIN + '/admin', { waitUntil: 'networkidle' });
        const adminHtml = await adminPage.content();

        // Check admin form elements
        const formElements = await adminPage.$$('input, button, select, textarea');
        console.log(`  管理后台表单元素数量: ${formElements.length}`);

        // Check if there's a username field
        const usernameSelectors = ['input[name="username"]', 'input[name="UserName"]', '#username', '#UserName', 'input[type="text"]'];
        let userField = null;
        for (const sel of usernameSelectors) {
            userField = await adminPage.$(sel);
            if (userField) {
                console.log(`  找到用户名输入框: ${sel}`);
                break;
            }
        }

        let passField = null;
        const passSelectors = ['input[name="password"]', 'input[name="Password"]', '#password', '#Password', 'input[type="password"]'];
        for (const sel of passSelectors) {
            passField = await adminPage.$(sel);
            if (passField) {
                console.log(`  找到密码输入框: ${sel}`);
                break;
            }
        }

        // Check if admin page is ASP.NET Forms auth page
        if (!adminHtml.includes('input') && !adminHtml.includes('form')) {
            issue('管理后台', '高', '管理后台页面缺少表单元素',
                '可能被Forms认证重定向到不正确的页面',
                adminHtml.substring(0, 500));
        }

        // Try accessing admin with different paths
        const adminPaths = ['/Admin/', '/Admin/Default.aspx', '/admin/', '/Admin/Default'];
        for (const path of adminPaths) {
            const resp = await adminPage.goto(ADMIN + path, { waitUntil: 'networkidle' });
            console.log(`  ${path}: ${resp.status()}`);
        }

        // Check if Forms auth redirect is happening
        const currentUrl = adminPage.url();
        if (currentUrl.includes('ReturnUrl')) {
            issue('管理后台', '中', '管理后台Forms认证跳转URL中包含ReturnUrl参数',
                '可能导致开放重定向风险', currentUrl);
        }

    } catch (e) {
        issue('管理后台', '严重', '管理后台深度测试异常: ' + e.message.substring(0, 200));
    }
    await adminCtx.close();

    console.log('');

    // =========== Part B: Detailed Mock Backend Analysis ===========
    console.log('--- B: Mock后端深度分析 ---');
    const backendCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    const backendPage = await backendCtx.newPage();

    try {
        // Check mock backend paths
        const mockPaths = [
            '/',
            '/mock/',
            '/mock/erp-main/',
            '/mock/erp-main/login',
            '/mock/erp-main/portal',
            '/mock/erp-main/api/ping',
            '/mock/erp-main/api/data',
            '/default.aspx',
            '/assets/spa.css',
            '/assets/spa.js',
            '/assets/mock.css',
            '/assets/mock.js',
            '/assets/grid.svg',
            '/jvs-aps-ui/index.html',
        ];

        for (const path of mockPaths) {
            try {
                const resp = await backendPage.goto(BACKEND + path, { timeout: 10000 });
                const status = resp.status();
                const ct = resp.headers()['content-type'] || '';

                if (status >= 400 && status !== 401 && status !== 403) {
                    issue('Mock后端/资源', '中', `Mock后端路径 ${path} 返回 ${status}`,
                        `期望正常响应，Content-Type: ${ct}`);
                }

                if (status === 200) {
                    const body = await backendPage.content();
                    // Check for ASP.NET errors
                    if (body.includes('Server Error') || body.includes('运行时错误')) {
                        issue('Mock后端/错误', '高', `Mock后端路径 ${path} 返回ASP.NET错误页`,
                            '模拟后端存在运行时错误');
                    }
                }
            } catch (e) {
                if (!e.message.includes('ERR_ABORTED')) {
                    issue('Mock后端/连接', '高', `Mock后端路径 ${path} 无法访问: ${e.message.substring(0, 100)}`);
                }
            }
        }

        // Test mock login flow
        await backendPage.goto(BACKEND + '/mock/erp-main/login?returnUrl=portal', { waitUntil: 'networkidle' });
        const loginBody = await backendPage.content();

        // Look for any form
        const anyForm = await backendPage.$('form');
        if (!anyForm) {
            issue('Mock后端/功能', '高', 'Mock后端登录页缺失form元素',
                '无法提交登录表单，后端模拟功能不完整');
        } else {
            const formAction = await anyForm.getAttribute('action');
            const formMethod = await anyForm.getAttribute('method');
            console.log(`  Login form: ${formMethod || 'GET'} ${formAction || '(none)'}`);
        }

    } catch (e) {
        issue('Mock后端', '严重', 'Mock后端深度测试异常: ' + e.message.substring(0, 200));
    }
    await backendCtx.close();

    console.log('');

    // =========== Part C: Content Rewriting and URL Handling ===========
    console.log('--- C: URL重写与内容处理测试 ---');
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    try {
        // Test double encoding
        const doubleEncoded = await page.goto(GATEWAY + '/portal%252ftest', { timeout: 10000 });
        console.log(`  双重编码URL: ${doubleEncoded.status()}`);

        // Test unicode in URL
        const unicodeUrl = await page.goto(GATEWAY + '/portal?name=' + encodeURIComponent('测试中文'),
            { timeout: 10000 });
        console.log(`  中文参数URL: ${unicodeUrl.status()}`);

        // Test with fragment
        await page.goto(GATEWAY + '/portal#section1', { waitUntil: 'networkidle' });
        const fragUrl = page.url();
        if (!fragUrl.includes('#section1')) {
            issue('URL/路由', '中', 'URL中的fragment标识符在重定向后丢失',
                'SPA的路由状态可能无法保持', fragUrl);
        }

        // Test trailing slash behavior
        const withSlash = await page.goto(GATEWAY + '/portal/', { timeout: 10000 });
        const withoutSlash = await page.goto(GATEWAY + '/portal', { timeout: 10000 });
        if (withSlash.status() !== withoutSlash.status()) {
            issue('URL/路由', '低', '带/不带尾部斜杠的相同路径返回不同状态码',
                `/portal/: ${withSlash.status()}, /portal: ${withoutSlash.status()}`);
        }

        // Test for incorrect content-type on gateway pages
        const gwResp = await page.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });
        const gwCt = gwResp.headers()['content-type'] || '';
        if (gwCt.includes('text/html') && !gwCt.includes('charset')) {
            issue('编码/响应', '高', 'HTML响应Content-Type缺少charset声明',
                `Content-Type: ${gwCt}，可能导致非ASCII字符显示异常`);
        }

    } catch (e) {
        issue('URL/路由', '中', 'URL测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part D: Error Handling & Edge Cases ===========
    console.log('--- D: 错误处理与边界情况测试 ---');

    try {
        // Test domain names that don't match
        const hostPage = await ctx.newPage();
        await hostPage.setExtraHTTPHeaders({ 'Host': 'unknown-domain.com' });
        try {
            const hostResp = await hostPage.goto('http://localhost:5050/portal', { timeout: 10000 });
            console.log(`  未知Host头访问: ${hostResp.status()}`);
            const hostBody = await hostPage.content();
            if (hostBody.includes('Server Error') || hostBody.includes('Exception')) {
                issue('错误处理', '高', '未知Host头触发服务器异常页',
                    '应返回404而不是服务器错误');
            }
        } catch (e) {
            // Expected if host doesn't match
        }
        await hostPage.close();

        // Test very large request body
        try {
            const largeData = 'x'.repeat(100000);
            const largeResp = await page.request.post(GATEWAY + '/Gateway/Default.aspx', {
                data: 'action=request-access&companyName=' + largeData + '&applicantName=test&phone=123&reason=test',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                timeout: 30000
            });
            if (largeResp.status() >= 500) {
                issue('输入验证', '高', '超大POST数据导致500错误',
                    '应返回413或进行输入长度限制');
            }
        } catch (e) {
            issue('输入验证', '中', '超大POST请求处理异常: ' + e.message.substring(0, 100));
        }

        // Test multiple content types
        const contentTypes = [
            'application/xml',
            'text/plain',
            'multipart/form-data',
            'application/octet-stream',
        ];
        for (const ct of contentTypes) {
            try {
                const resp = await page.request.post(GATEWAY + '/api/data', {
                    headers: { 'Content-Type': ct },
                    data: '<test>data</test>',
                    timeout: 10000
                });
                if (resp.status() >= 500) {
                    issue('API/内容类型', '中', `Content-Type ${ct} 的POST请求返回${resp.status()}`,
                        '网关可能无法正确处理所有标准Content-Type');
                }
            } catch (e) {}
        }

    } catch (e) {
        issue('错误处理', '中', '错误处理测试异常: ' + e.message.substring(0, 200));
    }
    await ctx.close();

    console.log('');

    // =========== Part E: Gateway Configuration Inspection ===========
    console.log('--- E: 网关配置检查 ---');

    try {
        const cfgPage = await browser.newPage();

        // Check if gateway-sites.json is exposed
        const configPaths = [
            '/gateway-sites.json',
            '/App_Data/gateway-sites.json',
            '/web.config',
            '/Web.config',
            '/appsettings.json',
            '/../web.config',
            '/../App_Data/gateway-sites.json',
        ];

        for (const path of configPaths) {
            try {
                const resp = await cfgPage.goto(GATEWAY + path, { timeout: 10000 });
                const body = await cfgPage.content();
                if ((resp.status() === 200 && (body.includes('UpstreamBaseUrl') || body.includes('connectionString') || body.includes('PasswordHash'))) ||
                    resp.status() === 200 && (path.includes('.config') || path.includes('.json'))) {
                    issue('安全/配置泄露', '严重', `配置文件可能可被公开访问: ${path}`,
                        `状态码: ${resp.status()}, 响应长度: ${body.length}`);
                }
            } catch (e) {}
        }
        await cfgPage.close();

    } catch (e) {
        issue('配置安全', '中', '配置检查异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part F: Specific SPA/Vite behavior tests ===========
    console.log('--- F: SPA/Vite特定行为测试 ---');

    try {
        const spaCtx = await browser.newContext({ ignoreHTTPSErrors: true });
        const spaPage = await spaCtx.newPage();

        // Vite dev server specific patterns
        const vitePaths = [
            '/@vite/client',
            '/@vite/env',
            '/@id/__x00__',
            '/@fs/C:/windows/win.ini',
            '/src/main.js',
            '/src/App.vue',
            '/__vite_ping',
        ];

        for (const path of vitePaths) {
            try {
                const resp = await spaPage.goto(GATEWAY + path, { timeout: 10000 });
                const body = await spaPage.content();

                if (body.includes('访问申请')) {
                    issue('Vite/SPA', '高', `Vite特定路径 ${path} 被网关授权拦截`,
                        '开发环境下Vite HMR和ESM导入将无法工作');
                }
            } catch (e) {}
        }

        // Check if __vite_ping is handled
        const pingResp = await spaPage.goto(GATEWAY + '/__vite_ping', { waitUntil: 'networkidle', timeout: 5000 });
        console.log(`  /__vite_ping: ${pingResp.status()}`);

        await spaCtx.close();
    } catch (e) {
        issue('Vite/SPA', '中', 'Vite测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part G: Login Brute Force / Rate Limiting ===========
    console.log('--- G: 登录限制测试 ---');

    try {
        const adminCtx2 = await browser.newContext({ ignoreHTTPSErrors: true });
        const adminPage2 = await adminCtx2.newPage();

        // Check if admin login has rate limiting
        await adminPage2.goto(ADMIN + '/admin', { waitUntil: 'networkidle' });

        // Try multiple login attempts
        let rateLimited = false;
        for (let i = 0; i < 10; i++) {
            try {
                const resp = await adminPage2.request.post(ADMIN + '/Admin/Default.aspx', {
                    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                    data: `username=gateway-admin&password=wrong${i}`,
                    timeout: 10000
                });
                if (resp.status() === 429 || resp.status() === 403) {
                    rateLimited = true;
                    console.log(`  第${i + 1}次登录被限流: ${resp.status()}`);
                    break;
                }
            } catch (e) {}
        }

        if (!rateLimited) {
            issue('管理后台/安全', '高', '管理后台登录未实现有效的频率限制',
                '10次连续错误密码尝试未被阻止，存在暴力破解风险');
        } else {
            console.log('  登录频率限制已生效');
        }

        await adminCtx2.close();
    } catch (e) {
        issue('管理后台', '中', '登录限制测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part H: Device Identification Tests ===========
    console.log('--- H: 设备识别与指纹测试 ---');

    try {
        const ctx1 = await browser.newContext({ ignoreHTTPSErrors: true });
        const ctx2 = await browser.newContext({ ignoreHTTPSErrors: true });

        const page1 = await ctx1.newPage();
        const page2 = await ctx2.newPage();

        await page1.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });
        await page2.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });

        const cookies1 = await ctx1.cookies(GATEWAY);
        const cookies2 = await ctx2.cookies(GATEWAY);

        const devCred1 = cookies1.find(c => c.name === 'gw_device_credential');
        const devCred2 = cookies2.find(c => c.name === 'gw_device_credential');

        if (devCred1 && devCred2 && devCred1.value === devCred2.value) {
            issue('设备识别', '高', '不同浏览器上下文被分配了相同的设备凭据',
                '设备指纹识别可能不够精确，不同设备被识别为同一设备');
        } else {
            console.log('  不同上下文有不同的设备凭据(正常)');
        }

        await ctx1.close();
        await ctx2.close();
    } catch (e) {
        issue('设备识别', '中', '设备识别测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part I: Redirect Loop Detection ===========
    console.log('--- I: 重定向环路详细测试 ---');

    try {
        const loopCtx = await browser.newContext({ ignoreHTTPSErrors: true });
        const loopPage = await loopCtx.newPage();

        // Track redirects
        let redirectChain = [];
        loopPage.on('response', resp => {
            if (resp.status() >= 300 && resp.status() < 400) {
                redirectChain.push({
                    status: resp.status(),
                    location: resp.headers()['location'],
                    url: resp.url()
                });
            }
        });

        // Test paths that might cause loops
        const suspectPaths = [
            '/',
            '/gateway',
            '/gateway/',
            '/Gateway/Default.aspx',
            '/Gateway/Default.aspx?target=/',
            '/proxy/2027',
            '/proxy/2027/',
        ];

        for (const path of suspectPaths) {
            redirectChain = [];
            try {
                await loopPage.goto(GATEWAY + path, { timeout: 10000 });
                if (redirectChain.length > 3) {
                    issue('重定向/路由', '高', `路径 ${path} 可能触发重定向链(${redirectChain.length}次)`,
                        JSON.stringify(redirectChain));
                }
            } catch (e) {
                if (e.message.includes('ERR_TOO_MANY_REDIRECTS')) {
                    issue('重定向/路由', '严重', `路径 ${path} 触发重定向死循环`,
                        '浏览器因过多重定向而停止');
                }
            }
        }

        await loopCtx.close();
    } catch (e) {
        issue('重定向', '中', '重定向环路测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Part J: File Extension Handling ===========
    console.log('--- J: 文件扩展名与MIME类型测试 ---');

    try {
        const extCtx = await browser.newContext({ ignoreHTTPSErrors: true });
        const extPage = await extCtx.newPage();

        const extensions = {
            'js': 'application/javascript',
            'css': 'text/css',
            'json': 'application/json',
            'png': 'image/png',
            'svg': 'image/svg+xml',
            'woff2': 'font/woff2',
            'html': 'text/html',
        };

        for (const [ext, expectedMime] of Object.entries(extensions)) {
            try {
                const resp = await extPage.goto(GATEWAY + '/assets/test.' + ext, { timeout: 10000 });
                const ct = resp.headers()['content-type'] || '';
                const status = resp.status();

                if (status === 200) {
                    if (!ct.includes(expectedMime) && !ct.includes('text/html')) {
                        issue('MIME/资源', '中', `.${ext}文件返回错误的Content-Type: "${ct}"`,
                            `期望: ${expectedMime}`);
                    }
                }
            } catch (e) {}
        }

        await extCtx.close();
    } catch (e) {
        issue('MIME', '低', 'MIME测试异常: ' + e.message.substring(0, 200));
    }

    // =========== Summary ===========
    console.log('\n\n========== 深度测试完成总结 ==========');
    console.log(`本轮新发现问题: ${ISSUES.length} 个`);

    // Merge with existing report if any
    let existingIssues = [];
    try {
        existingIssues = JSON.parse(fs.readFileSync('E:/验证页面/gateway_test_report.json', 'utf-8')).issues;
    } catch (e) {}

    const allIssues = [...existingIssues, ...ISSUES];
    console.log(`累计问题总数: ${allIssues.length}`);

    const bySeverity = {};
    const byArea = {};
    for (const i of allIssues) {
        bySeverity[i.severity] = (bySeverity[i.severity] || 0) + 1;
        byArea[i.area] = (byArea[i.area] || 0) + 1;
    }

    console.log('\n累计按严重程度:');
    for (const [sev, count] of Object.entries(bySeverity)) {
        console.log(`  ${sev}: ${count}`);
    }

    fs.writeFileSync('E:/验证页面/gateway_test_report_deep.json',
        JSON.stringify({ timestamp: new Date().toISOString(), totalIssues: allIssues.length, issues: allIssues, bySeverity, byArea }, null, 2));

    await browser.close();
    return { newIssues: ISSUES.length, totalIssues: allIssues.length };
}

runDeepTests().then(result => {
    console.log(`\n深度测试完成: 新增${result.newIssues}个, 总计${result.totalIssues}个`);
    if (result.totalIssues < 50) {
        console.log(`还需要 ${50 - result.totalIssues} 个问题达到50`);
    }
}).catch(err => {
    console.error('深度测试失败:', err.message);
});
