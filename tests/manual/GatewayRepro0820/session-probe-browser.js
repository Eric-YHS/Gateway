const { chromium } = require('playwright');
(async()=>{
 const b=await chromium.launch({headless:true});
 const c=await b.newContext(); const p=await c.newPage();
 const url='http://127.0.0.1:8080/session-probe.aspx';
 const sequential=[];
 for(let i=0;i<20;i++){ const r=await p.goto(url,{waitUntil:'domcontentloaded'}); sequential.push({status:r.status(),text:(await p.locator('body').innerText()).slice(0,500)}); }
 const parallel=await Promise.all(Array.from({length:40},async(_,i)=>{const q=await c.newPage(); try{const r=await q.goto(url,{waitUntil:'domcontentloaded'}); return {i,status:r.status(),text:(await q.locator('body').innerText()).slice(0,500)};}catch(e){return {i,error:String(e)}}finally{await q.close()}}));
 const c2=await b.newContext(); const p2=await c2.newPage(); const fresh=[]; for(let i=0;i<3;i++){const r=await p2.goto(url);fresh.push({status:r.status(),text:await p2.locator('body').innerText()});}
 console.log(JSON.stringify({sequential,parallel,fresh,cookies:await c.cookies()},null,2)); await b.close();
})();
