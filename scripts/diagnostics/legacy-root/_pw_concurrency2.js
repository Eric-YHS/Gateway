const { chromium } = require('playwright');
(async()=>{
  const browser = await chromium.launch({headless:true});
  const n = 50;
  const results = [];
  async function one(i){
    const ctx = await browser.newContext({ userAgent: 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.'+i+' Safari/537.36' });
    const page = await ctx.newPage();
    try {
      const resp = await page.goto(i%2===0 ? 'http://127.0.0.1:39550/gateway' : 'http://127.0.0.1:39550/proxy/2027/portal', {waitUntil:'domcontentloaded', timeout:20000});
      results.push({i, status: resp.status(), url: page.url()});
    } catch(e) { results.push({i, error: e.message.split('\n')[0]}); }
    await ctx.close();
  }
  await Promise.all(Array.from({length:n}, (_,i)=>one(i)));
  console.log(JSON.stringify(results));
  await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
