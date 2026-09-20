const { chromium } = require('playwright');
(async()=>{
  const browser = await chromium.launch({headless:true});
  const page = await browser.newPage();
  const resp = await page.goto('http://127.0.0.1:39550/healthz.ashx', {waitUntil:'domcontentloaded'});
  console.log('status', resp.status());
  console.log('text', await page.textContent('body'));
  await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
