const { chromium } = require('playwright');
(async()=>{
  const browser = await chromium.launch({headless:true});
  const n = 40;
  const results = [];
  async function one(i){
    const ctx = await browser.newContext();
    const page = await ctx.newPage();
    try {
      const resp = await page.goto('http://127.0.0.1:39550/gateway', {waitUntil:'domcontentloaded', timeout:30000});
      results.push({i, status: resp.status(), url: page.url()});
    } catch(e) {
      results.push({i, error: e.message.split('\n')[0]});
    }
    await ctx.close();
  }
  await Promise.all(Array.from({length:n}, (_,i)=>one(i)));
  console.log(JSON.stringify(results, null, 2));
  await browser.close();
})().catch(e=>{console.error('ERR', e); process.exit(1)});
