const { chromium } = require('playwright');
const fs = require('fs');

const GATEWAY = 'http://localhost:5050';
const ADMIN = 'http://localhost:5051';
const BACKEND = 'http://localhost:5055';
const ISSUES = [];
let issueCounter = 210;

function issue(area, severity, title, detail, evidence = '') {
    const id = issueCounter++;
    ISSUES.push({ id, area, severity, title, detail, evidence });
    console.log(`[ISSUE #${id}] [${severity}] [${area}] ${title}`);
}

async function runFinalTests() {
    const browser = await chromium.launch({ headless: true });

    console.log('===== 第三轮：深度浏览器交互测试 =====\n');

    // =========== Test 1: Submit access request and verify flow ===========
    console.log('--- 1: 完整的访问申请提交流程 ---');
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true });
    const page = await ctx.newPage();

    try {
        // Navigate to gateway
        await page.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });

        // Fill form with real data
        await page.fill('input[name="companyName"]', '测试科技有限公司');
        await page.fill('input[name="applicantName"]', '张三');
        await page.fill('input[name="phone"]', '13800138000');
        await page.fill('input[name="reason"]', '业务测试需要访问ERP系统');

        // Submit
        await page.click('button[type="submit"]');
        await page.waitForTimeout(3000);

        const afterSubmit = page.url();
        const afterContent = await page.content();

        if (afterContent.includes('访问申请')) {
            issue('表单/流程', '高', '提交访问申请后仍停留在申请页面',
                '申请可能未被正确处理，或没有成功跳转',
                `URL: ${afterSubmit}`);
        }

        if (afterContent.includes('错误') || afterContent.includes('失败')) {
            issue('表单/流程', '严重', '提交访问申请后显示错误信息',
                '访问申请处理过程中发生服务器错误',
                afterContent.substring(afterContent.indexOf('错误') - 100, afterContent.indexOf('错误') + 200));
        }

        // Check if cookies updated
        const cookies = await ctx.cookies(GATEWAY);
        const devCred = cookies.find(c => c.name === 'gw_device_credential');
        if (!devCred) {
            issue('表单/流程', '高', '提交申请后设备凭据Cookie丢失',
                '后续请求将无法识别设备');
        }

    } catch (e) {
        issue('表单/流程', '严重', '申请提交流程异常: ' + e.message.substring(0, 200));
    }
    await ctx.close();

    console.log('');

    // =========== Test 2: Admin login and dashboard check ===========
    console.log('--- 2: 管理员完整登录流程 ---');
    const adminCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    const adminPage = await adminCtx.newPage();

    try {
        await adminPage.goto(ADMIN + '/Admin/', { waitUntil: 'networkidle' });

        // Login
        await adminPage.fill('input[name="username"]', 'gateway-admin');
        await adminPage.fill('input[name="password"]', 'GatewayDemo!2026');
        await adminPage.click('button[type="submit"]');
        await adminPage.waitForTimeout(3000);

        const adminUrl = adminPage.url();
        const adminContent = await adminPage.content();

        console.log(`  Admin URL after login: ${adminUrl}`);

        if (adminContent.includes('登录')) {
            issue('管理后台/认证', '严重', '默认管理员账号登录失败',
                'gateway-admin / GatewayDemo!2026 无法登录管理后台',
                `当前URL: ${adminUrl}`);

            // Check if password hash in Web.config matches
            issue('管理后台/配置', '高', 'Web.config中的密码哈希值可能与默认密码不匹配',
                'Admin.PasswordHash可能使用了不同salt或迭代次数重新生成');
        } else {
            console.log('  管理员登录成功');

            // Check dashboard features
            if (adminContent.includes('设备管理') || adminContent.includes('授权') || adminContent.includes('申请')) {
                console.log('  管理仪表盘正常渲染');
            } else {
                issue('管理后台/UI', '中', '登录后管理界面内容不完整',
                    '缺少设备管理、授权审核等核心管理功能',
                    adminContent.substring(0, 500));
            }

            // Check for audit log
            if (!adminContent.includes('审计') && !adminContent.includes('audit')) {
                issue('管理后台/功能', '低', '管理后台可能缺少审计日志入口',
                    '未找到审计日志相关链接或菜单');
            }

            // Check for device management section
            if (!adminContent.includes('设备') && !adminContent.includes('device')) {
                issue('管理后台/功能', '中', '管理后台缺少设备管理功能',
                    '无法查看和管理已注册设备');
            }
        }

    } catch (e) {
        issue('管理后台', '严重', '管理后台登录流程异常: ' + e.message.substring(0, 200));
    }
    await adminCtx.close();

    console.log('');

    // =========== Test 3: Access already-authorized flow ===========
    console.log('--- 3: 已授权设备访问行为测试 ---');
    const authCtx = await browser.newContext({ ignoreHTTPSErrors: true });
    const authPage = await authCtx.newPage();

    try {
        // Make multiple requests to the same path
        const responses = [];
        for (let i = 0; i < 5; i++) {
            const resp = await authPage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });
            responses.push(resp.status());
            await authPage.waitForTimeout(500);
        }

        // All should be consistent
        const allSame = responses.every(r => r === responses[0]);
        const all302 = responses.every(r => r === 302);
        const all200 = responses.every(r => r === 200);

        if (!allSame) {
            issue('会话/一致性', '中', '连续请求返回不一致的状态码',
                `状态码序列: ${responses.join(', ')}，可能存在会话状态抖动`);
        }

        if (all302) {
            issue('授权/流程', '中', '连续访问同一路径每次都触发302重定向到网关申请页',
                '即使设备已被识别，每次访问都需重新授权检查(可能正常但影响性能)');
        }

    } catch (e) {
        issue('会话', '中', '授权流程测试异常: ' + e.message.substring(0, 200));
    }
    await authCtx.close();

    console.log('');

    // =========== Test 4: JavaScript rendering check ===========
    console.log('--- 4: JavaScript功能完整性测试 ---');

    try {
        const jsPage = await browser.newPage();

        // Check if the gateway page JS works
        await jsPage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });

        // Test the "all sites" checkbox functionality
        const allSitesCheckbox = await jsPage.$('.js-all-sites');
        const siteCheckboxes = await jsPage.$$('input[name="siteKeys"]');

        if (allSitesCheckbox) {
            const wasChecked = await allSitesCheckbox.isChecked();
            await allSitesCheckbox.click();
            await jsPage.waitForTimeout(500);

            // Site checkboxes should be disabled
            for (const cb of siteCheckboxes) {
                const disabled = await cb.isDisabled();
                const checked = await cb.isChecked();
                if (!disabled && wasChecked === false) {
                    issue('前端/JS功能', '中', '"所有站点"复选框未正确禁用单个站点选择',
                        'JS交互逻辑可能未正确绑定');
                    break;
                }
            }
        }

        await jsPage.close();
    } catch (e) {
        issue('前端/JS', '中', 'JS功能测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Test 5: Cross-origin resource check ===========
    console.log('--- 5: 跨域资源引用测试 ---');

    try {
        const corsPage = await browser.newPage();

        // Check if gateway page loads any external resources
        const requests = [];
        corsPage.on('request', req => {
            if (req.url().startsWith('http') && !req.url().includes('localhost')) {
                requests.push(req.url());
            }
        });

        await corsPage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });

        if (requests.length > 0) {
            issue('安全/外部资源', '高', '网关页面加载了外部资源',
                `发现 ${requests.length} 个外部请求: ${requests.join(', ')}`);
        }

        await corsPage.close();
    } catch (e) {
        issue('安全', '低', '跨域测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Test 6: Iframe/CSP check ===========
    console.log('--- 6: 点击劫持与框架保护测试 ---');

    try {
        const framePage = await browser.newPage();

        // Create a page that tries to frame the gateway
        await framePage.setContent(`
            <html><body>
            <iframe src="${GATEWAY}/portal" width="800" height="600"></iframe>
            </body></html>
        `);
        await framePage.waitForTimeout(3000);

        // Check if the iframe loaded (no X-Frame-Options)
        const frameContent = await framePage.content();
        // If the page is framable, it's a security issue
        const frameHandle = await framePage.$('iframe');
        if (frameHandle) {
            try {
                const frameBody = await frameHandle.contentFrame();
                if (frameBody) {
                    const frameHtml = await frameBody.content();
                    if (frameHtml.includes('访问申请')) {
                        issue('安全/点击劫持', '严重', '网关页面可被iframe嵌入，存在点击劫持风险',
                            '缺少X-Frame-Options或CSP frame-ancestors响应头');
                    }
                }
            } catch (e) {
                // Frame blocked - good
                console.log('  页面已受框架保护(iframe被阻止)');
            }
        }

        await framePage.close();
    } catch (e) {
        issue('安全', '中', '框架保护测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Test 7: Performance check with resource timing ===========
    console.log('--- 7: 资源加载性能分析 ---');

    try {
        const perfPage = await browser.newPage();

        // Get performance metrics
        await perfPage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });

        const metrics = await perfPage.evaluate(() => {
            const perf = window.performance;
            const entries = perf.getEntriesByType('navigation');
            if (entries.length === 0) return null;
            const nav = entries[0];
            return {
                domContentLoaded: nav.domContentLoadedEventEnd - nav.startTime,
                loadComplete: nav.loadEventEnd - nav.startTime,
                firstPaint: perf.getEntriesByType('paint').find(p => p.name === 'first-contentful-paint')?.startTime,
                transferSize: nav.transferSize,
                decodedBodySize: nav.decodedBodySize,
            };
        });

        if (metrics) {
            console.log(`  DOMContentLoaded: ${Math.round(metrics.domContentLoaded)}ms`);
            console.log(`  Load: ${Math.round(metrics.loadComplete)}ms`);
            console.log(`  传输大小: ${Math.round(metrics.transferSize / 1024)}KB`);

            if (metrics.decodedBodySize > 50000) {
                issue('性能/页面体积', '中', `网关页面过大(${Math.round(metrics.decodedBodySize/1024)}KB)`,
                    '内联CSS和JS使页面体积膨胀，建议外部化以减少重复传输');
            }
        }

        await perfPage.close();
    } catch (e) {
        issue('性能', '低', '性能分析异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Test 8: Test unauthorized API access ===========
    console.log('--- 8: 未授权API访问行为 ---');

    try {
        const apiCtx = await browser.newContext({ ignoreHTTPSErrors: true });
        const apiPage = await apiCtx.newPage();

        // Access API-like paths without device cookie
        const apiPaths = [
            '/api/users',
            '/api/data',
            '/api/v2/orders',
            '/jvs-public/api/config',
            '/mgr/api/users',
        ];

        for (const path of apiPaths) {
            const resp = await apiPage.goto(GATEWAY + path, { waitUntil: 'networkidle' });
            const body = await apiPage.content();

            if (resp.status() === 403) {
                // Check if CORS headers present for blocked response
                const corsOrigin = resp.headers()['access-control-allow-origin'];
                if (corsOrigin) {
                    console.log(`  ${path}: 403 with CORS headers (good for AJAX clients)`);
                } else {
                    issue('API/错误响应', '低', `${path} 返回403但缺少CORS头`,
                        'AJAX客户端将无法读取错误响应');
                }
            } else if (resp.status() === 302) {
                issue('API/重定向', '高', `API路径 ${path} 返回302重定向而非403`,
                    'AJAX请求可能会跟随重定向导致意外行为');
            }
        }

        await apiCtx.close();
    } catch (e) {
        issue('API', '中', 'API访问测试异常: ' + e.message.substring(0, 200));
    }

    console.log('');

    // =========== Test 9: Cache behavior on repeated proxying ===========
    console.log('--- 9: 代理缓存行为测试 ---');

    try {
        const cachePage = await browser.newPage();

        // Test ETag/If-None-Match behavior
        const headers1 = (await cachePage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' })).headers();
        const etag = headers1['etag'];
        const lastMod = headers1['last-modified'];

        console.log(`  ETag: ${etag || 'none'}`);
        console.log(`  Last-Modified: ${lastMod || 'none'}`);

        if (!etag && !lastMod) {
            issue('缓存/性能', '中', '网关页面响应缺少ETag和Last-Modified',
                '浏览器无法使用条件请求，每次都需重新下载完整页面');
        }

        // Check for 304 support
        if (etag) {
            const condResp = await cachePage.request.get(GATEWAY + '/portal', {
                headers: { 'If-None-Match': etag }
            });
            if (condResp.status() === 200 && condResp.headers()['etag'] === etag) {
                issue('缓存/HTTP', '中', '网关不支持304 Not Modified条件响应',
                    '相同ETag的资源总是返回200，浪费带宽');
            }
        }

        await cachePage.close();
    } catch (e) {
        issue('缓存', '低', '缓存测试异常: ' + e.message.substring(0, 200));
    }

    // =========== Test 10: Cookie set on every request ===========
    console.log('\n--- 10: Cookie重复设置问题 ---');

    try {
        const cookiePage = await browser.newPage();
        cookiePage.on('response', resp => {
            const setCookie = resp.headers()['set-cookie'];
            if (setCookie && setCookie.includes('gw_device_credential')) {
                // Track if device credential is reset on every response
            }
        });

        let setCookieCount = 0;
        cookiePage.on('response', resp => {
            if (resp.headers()['set-cookie']) {
                setCookieCount++;
            }
        });

        await cookiePage.goto(GATEWAY + '/portal', { waitUntil: 'networkidle' });
        await cookiePage.waitForTimeout(500);

        // Navigate again
        await cookiePage.goto(GATEWAY + '/', { waitUntil: 'networkidle' });

        // Multiple navigations should not cause excessive cookie sets
        if (setCookieCount > 5) {
            issue('Cookie/性能', '中', `页面浏览过程中设置Cookie次数过多(${setCookieCount}次)`,
                '每次导航都重新设置设备凭据Cookie可能导致不必要的网络开销');
        }

        await cookiePage.close();
    } catch (e) {
        issue('Cookie', '低', 'Cookie检查异常: ' + e.message.substring(0, 200));
    }

    // =========== Summary ===========
    console.log('\n\n========== 第三轮测试完成 ==========');
    console.log(`本轮新发现问题: ${ISSUES.length}`);

    let existingIssues = [];
    try {
        existingIssues = JSON.parse(fs.readFileSync('E:/验证页面/gateway_test_report.json', 'utf-8')).issues;
    } catch (e) {}
    try {
        const deep = JSON.parse(fs.readFileSync('E:/验证页面/gateway_test_report_deep.json', 'utf-8')).issues;
        existingIssues = deep;
    } catch (e) {}

    const allIssues = [...existingIssues, ...ISSUES];
    console.log(`累计问题总数: ${allIssues.length}`);

    const bySeverity = {};
    for (const i of allIssues) {
        bySeverity[i.severity] = (bySeverity[i.severity] || 0) + 1;
    }
    console.log('\n累计按严重程度:');
    for (const [sev, count] of Object.entries(bySeverity)) {
        console.log(`  ${sev}: ${count}`);
    }

    fs.writeFileSync('E:/验证页面/gateway_test_report_final.json',
        JSON.stringify({ timestamp: new Date().toISOString(), totalIssues: allIssues.length, issues: allIssues, bySeverity }, null, 2));

    await browser.close();
    return { newIssues: ISSUES.length, totalIssues: allIssues.length };
}

runFinalTests().then(result => {
    console.log(`\n第三轮完成: 新增${result.newIssues}个, 合计${result.totalIssues}个`);
}).catch(err => {
    console.error('第三轮测试失败:', err.message);
});
