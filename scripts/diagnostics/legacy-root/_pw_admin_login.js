const { chromium } = require('playwright');
(async()=>{
 const browser = await chromium.launch({headless:true});
 const page = await browser.newPage();
 await page.goto('http://127.0.0.1:39551/Admin/Default.aspx', {waitUntil:'domcontentloaded'});
 console.log(await page.title());
 console.log(await page.content());
 await browser.close();
})().catch(e=>{console.error(e);process.exit(1)});
