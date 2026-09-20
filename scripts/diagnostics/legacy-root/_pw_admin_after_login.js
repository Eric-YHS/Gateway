const { chromium } = require('playwright');
(async()=>{
 const browser = await chromium.launch({headless:true});
 const page = await browser.newPage();
 await page.goto('http://127.0.0.1:39551/Admin/Default.aspx', {waitUntil:'domcontentloaded'});
 await page.fill('input[name=username]', 'gateway-admin');
 await page.fill('input[name=password]', 'GatewayDemo!2026');
 await Promise.all([
   page.waitForNavigation({waitUntil:'domcontentloaded', timeout:15000}).catch(()=>{}),
   page.click('button[type=submit]')
 ]);
 await page.waitForTimeout(1000);
 console.log('URL', page.url());
 console.log('TITLE', await page.title());
 const text = await page.evaluate(() => document.body.innerText);
 console.log(text.slice(0,5000));
 await browser.close();
})().catch(e=>{console.error('ERR',e);process.exit(1)});
