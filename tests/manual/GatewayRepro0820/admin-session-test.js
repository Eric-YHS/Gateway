const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const outputDir = process.env.OUT_DIR || __dirname;
(async()=>{
 const browser=await chromium.launch({headless:true});
 const out=[];
 async function test(name){
  const ctx=await browser.newContext({viewport:{width:1280,height:900}});
  const p=await ctx.newPage(); const req=[]; const res=[];
  p.on('request',r=>req.push({method:r.method(),url:r.url()}));
  p.on('response',r=>res.push({status:r.status(),url:r.url()}));
  await p.goto('http://127.0.0.1:53651/admin',{waitUntil:'domcontentloaded'});
  await p.waitForTimeout(300);
  const before={url:p.url(),title:await p.title(),text:(await p.locator('body').innerText()).slice(0,1000),inputs:await p.locator('input').evaluateAll(es=>es.map(e=>({name:e.name,type:e.type}))),buttons:await p.locator('button').allTextContents()};
  let after=null;
  if(await p.locator('input[name="username"]').count()){
   await p.locator('input[name="username"]').fill('gateway-admin');
   await p.locator('input[name="password"]').fill('repro-admin-2026');
   await p.locator('button[type="submit"]').click();
   await p.waitForLoadState('domcontentloaded').catch(()=>{}); await p.waitForTimeout(300);
   after={url:p.url(),title:await p.title(),text:(await p.locator('body').innerText()).slice(0,1000)};
  }
  out.push({name,before,after,cookies:await ctx.cookies(),responses:res.slice(-20),requestCount:req.length});
  await p.screenshot({path:path.join(outputDir, `${name}.png`),fullPage:true});
  await ctx.close();
 }
 await test('admin-session-a'); await test('admin-session-b');
 fs.writeFileSync(path.join(outputDir, 'admin-session-test.json'),JSON.stringify(out,null,2)); console.log(JSON.stringify(out,null,2)); await browser.close();
})();
