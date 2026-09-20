const { chromium } = require('playwright');
(async()=>{
  const browser = await chromium.launch({headless:true});
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();
  page.on('console', msg => console.log('console:', msg.type(), msg.text()));
  page.on('requestfailed', req => console.log('reqfail:', req.url(), req.failure()?.errorText));
  page.on('response', async r => { if (r.status() >= 400) console.log('response', r.status(), r.url()); });
  const url = 'http://127.0.0.1:39550/gateway';
  const resp = await page.goto(url, {waitUntil:'domcontentloaded', timeout:20000});
  console.log('goto', url, resp.status(), page.url());
  console.log((await page.content()).slice(0,1000));
  await page.waitForTimeout(1000);
  await browser.close();
})().catch(e=>{console.error('ERR',e);process.exit(1)});
