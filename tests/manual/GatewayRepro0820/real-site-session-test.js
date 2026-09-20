const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const outputDir = process.env.OUT_DIR || __dirname;
(async()=>{
 const browser=await chromium.launch({headless:false});
 const ctx=await browser.newContext({viewport:{width:1440,height:1000}});
 const p=await ctx.newPage(); const req=[],res=[];
 p.on('request',r=>req.push({method:r.method(),url:r.url(),postData:r.postData()}));
 p.on('response',r=>res.push({status:r.status(),url:r.url(),headers:r.headers()}));
 await p.goto('http://127.0.0.1:8080/default.aspx',{waitUntil:'domcontentloaded',timeout:30000});
 await p.waitForTimeout(1000);
 const state1={url:p.url(),title:await p.title(),text:(await p.locator('body').innerText()).slice(0,1200),cookies:await ctx.cookies(),inputs:await p.locator('input').evaluateAll(es=>es.map(e=>({id:e.id,name:e.name,type:e.type,value:e.value,src:e.src}))),images:await p.locator('img').evaluateAll(es=>es.map(e=>({id:e.id,src:e.src,alt:e.alt}))) };
 await p.screenshot({path:path.join(outputDir, 'real-site-login.png'),fullPage:true});
 const captcha = p.locator('input').filter({has:undefined});
 const codeInput = p.locator('#txtValidate');
 let wrong=null;
 if(await codeInput.count()) {
   await codeInput.fill('0000');
   const login = p.locator('#btnLogin');
   if(await login.count()) {
     await login.click();
     await p.waitForTimeout(1500);
     wrong={url:p.url(),text:(await p.locator('body').innerText()).slice(0,1200),dialogs:'handled',cookies:await ctx.cookies()};
   }
 }
 const state2={wrong,requestTail:req.slice(-30),responseTail:res.slice(-30)};
 fs.writeFileSync(path.join(outputDir, 'real-site-session-test.json'),JSON.stringify({state1,state2},null,2));
 console.log(JSON.stringify({state1,state2},null,2));
 await ctx.close(); await browser.close();
})();
