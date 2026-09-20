import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from 'playwright';

const currentFile = fileURLToPath(import.meta.url);
const root = path.resolve(path.dirname(currentFile), '..', '..');
const outputDir = path.join(root, 'artifacts', 'page-screenshots', '2026-03-25');

const companyName = `体验站截图企业-${Date.now()}`;
const applicantName = '截图测试用户';
const phone = '13800000000';
const reason = '截图留档';

const ensureDir = async (dir) => {
  await fs.mkdir(dir, { recursive: true });
};

const saveMeta = async (items) => {
  const lines = ['# 页面截图清单', '', `生成时间：${new Date().toISOString()}`, ''];
  for (const item of items) {
    lines.push(`- \`${item.file}\`：${item.desc}`);
  }

  await fs.writeFile(path.join(outputDir, 'README.md'), `${lines.join('\n')}\n`, 'utf8');
};

const screenshot = async (page, file, desc, options = {}) => {
  const target = path.join(outputDir, file);
  await page.screenshot({
    path: target,
    fullPage: options.fullPage ?? true,
    ...(options.clip ? { clip: options.clip } : {})
  });
  return { file, desc };
};

await ensureDir(outputDir);

const browser = await chromium.launch({
  channel: 'msedge',
  headless: true
});

const userContext = await browser.newContext({
  viewport: { width: 1440, height: 1100 },
  locale: 'zh-CN'
});

const adminContext = await browser.newContext({
  viewport: { width: 1440, height: 1100 },
  locale: 'zh-CN'
});

const userPage = await userContext.newPage();
const adminPage = await adminContext.newPage();

const captures = [];

try {
  await userPage.goto('http://localhost:5050/proxy/erp-main/default.aspx', { waitUntil: 'networkidle' });
  captures.push(await screenshot(userPage, '01-gateway-access-request.png', '浏览器首次访问网关时的设备申请页。'));

  await userPage.fill('input[name="companyName"]', companyName);
  await userPage.fill('input[name="applicantName"]', applicantName);
  await userPage.fill('input[name="phone"]', phone);
  await userPage.fill('input[name="reason"]', reason);
  await userPage.click('button[type="submit"]');
  await userPage.waitForLoadState('networkidle');
  captures.push(await screenshot(userPage, '02-gateway-request-submitted.png', '提交访问申请后的前台页面。'));

  await adminPage.goto('http://localhost:5051/Admin/Default.aspx', { waitUntil: 'networkidle' });
  captures.push(await screenshot(adminPage, '03-admin-login.png', '管理后台登录页。'));

  await adminPage.fill('input[name="username"]', 'gateway-admin');
  await adminPage.fill('input[name="password"]', 'GatewayDemo!2026');
  await Promise.all([
    adminPage.waitForURL('**/Admin/Default.aspx'),
    adminPage.click('button[type="submit"]')
  ]);
  await adminPage.waitForLoadState('networkidle');
  captures.push(await screenshot(adminPage, '04-admin-dashboard-before-approval.png', '管理后台中待审批申请列表。'));

  const requestCard = adminPage.locator('article.item').filter({ hasText: companyName }).first();
  await requestCard.scrollIntoViewIfNeeded();
  await requestCard.locator('input[name="note"]').fill('截图测试审批通过');
  await Promise.all([
    adminPage.waitForURL('**/Admin/Default.aspx'),
    requestCard.locator('button[name="action"][value="approve-request"]').click()
  ]);
  await adminPage.waitForLoadState('networkidle');
  captures.push(await screenshot(adminPage, '05-admin-dashboard-after-approval.png', '审批通过后的管理后台页面。'));

  await userPage.goto('http://localhost:5050/proxy/erp-main/default.aspx', { waitUntil: 'networkidle' });
  captures.push(await screenshot(userPage, '06-experience-login-via-gateway.png', '审批通过后，经网关进入的体验站登录页。'));

  const authCard = adminPage.locator('article.item').filter({ hasText: companyName }).first();
  await authCard.scrollIntoViewIfNeeded();
  await Promise.all([
    adminPage.waitForLoadState('networkidle'),
    authCard.locator('button', { hasText: '签发 APP 凭据' }).click()
  ]);
  await adminPage.waitForLoadState('networkidle');
  captures.push(await screenshot(adminPage, '07-issued-app-credential.png', '管理后台签发 APP 凭据后的页面。'));

  await saveMeta(captures);
} finally {
  await userContext.close();
  await adminContext.close();
  await browser.close();
}
