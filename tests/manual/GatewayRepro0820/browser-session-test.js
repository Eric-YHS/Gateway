const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const outputDir = process.env.OUT_DIR || __dirname;

(async () => {
  const browser = await chromium.launch({headless: true});
  const results = [];
  async function run(name) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    const requests = [];
    const responses = [];
    const consoleErrors = [];
    page.on('request', r => requests.push({method:r.method(), url:r.url()}));
    page.on('response', r => responses.push({status:r.status(), url:r.url()}));
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
    await page.goto('http://127.0.0.1:53650/portal', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.waitForTimeout(500);
    const first = { url: page.url(), title: await page.title(), text: (await page.locator('body').innerText()).slice(0,400) };
    const inputs = await page.locator('input').evaluateAll(es => es.map(e => ({name:e.name,type:e.type,placeholder:e.placeholder}))).catch(()=>[]);
    const buttons = await page.locator('button').allTextContents().catch(()=>[]);
    let login = null;
    if (await page.locator('input[name="username"]').count()) {
      await page.locator('input[name="username"]').fill('mock-user');
      await page.locator('input[name="password"]').fill('repro-mock-2026');
      await page.locator('button[type="submit"]').click();
      await page.waitForLoadState('domcontentloaded').catch(()=>{});
      await page.waitForTimeout(500);
      login = { url: page.url(), title: await page.title(), text: (await page.locator('body').innerText()).slice(0,400) };
    }
    const cookies = await context.cookies();
    const out = {name, first, inputs, buttons, login, cookies, requestCount:requests.length, responses:responses.slice(-20), consoleErrors};
    results.push(out);
    await page.screenshot({path:path.join(outputDir, `${name}.png`), fullPage:true});
    await context.close();
  }
  await run('browser-session-a');
  await run('browser-session-b');
  fs.writeFileSync(path.join(outputDir, 'browser-session-test.json'), JSON.stringify(results,null,2));
  console.log(JSON.stringify(results,null,2));
  await browser.close();
})();
