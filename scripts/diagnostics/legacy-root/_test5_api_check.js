const http = require('http');
const crypto = require('crypto');

const GATEWAY = 'http://100.113.71.64:15050';
const ADMIN = 'http://100.113.71.64:15051';
const ADMIN_USER = 'gateway-admin';
const ADMIN_PASS = 'GatewayDemo!2026';

function fetchText(url, opts = {}) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const req = http.request({
            hostname: u.hostname, port: u.port, path: u.pathname + u.search,
            method: opts.method || 'GET', headers: opts.headers || {}
        }, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, headers: res.headers, body: data }));
        });
        req.on('error', reject);
        if (opts.body) req.write(opts.body);
        req.end();
    });
}

function extractCookies(setCookie) {
    const jar = {};
    if (!setCookie) return jar;
    (Array.isArray(setCookie) ? setCookie : [setCookie]).forEach(c => {
        const m = c.match(/^([^=]+)=([^;]+)/);
        if (m) jar[m[1]] = m[2];
    });
    return jar;
}

function cookieHeader(jar) {
    return Object.keys(jar).map(k => `${k}=${jar[k]}`).join('; ');
}

function base64Url(buf) {
    return buf.toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/,'');
}

(async () => {
    // Login admin
    let jar = {};
    const loginResp = await fetchText(`${ADMIN}/Admin/Default.aspx`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: `action=login&username=${encodeURIComponent(ADMIN_USER)}&password=${encodeURIComponent(ADMIN_PASS)}`
    });
    jar = extractCookies(loginResp.headers['set-cookie']);
    console.log('Login status:', loginResp.status, 'cookies:', Object.keys(jar));

    // Get dashboard
    const dash = await fetchText(`${ADMIN}/Admin/Default.aspx`, { headers: { 'Cookie': cookieHeader(jar) } });
    console.log('Dashboard status:', dash.status);

    const csrfMatch = dash.body.match(/name="csrfToken" value="([^"]+)"/);
    const authIdMatch = dash.body.match(/name="authorizationId" value="([^"]+)"/);
    if (!csrfMatch || !authIdMatch) {
        console.log('No csrf or authId found');
        console.log(dash.body.substring(0, 500));
        return;
    }
    const csrf = csrfMatch[1];
    const authId = authIdMatch[1];
    console.log('csrf:', csrf.substring(0, 20), 'authId:', authId);

    // Issue credential
    const issueResp = await fetchText(`${ADMIN}/Admin/Default.aspx`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Cookie': cookieHeader(jar) },
        body: `action=issue-app-credential&authorizationId=${authId}&csrfToken=${encodeURIComponent(csrf)}`
    });
    console.log('Issue status:', issueResp.status);

    const keyMatch = issueResp.body.match(/APP Key\s+([a-zA-Z0-9_]+)/);
    const secretMatch = issueResp.body.match(/APP Secret\s+([a-zA-Z0-9_\-]+)/);
    if (!keyMatch || !secretMatch) {
        console.log('Credential not found in response');
        console.log(issueResp.body.substring(0, 800));
        return;
    }
    const key = keyMatch[1];
    const secret = secretMatch[1];
    console.log('Key:', key, 'Secret:', secret.substring(0, 10) + '...');

    // Call API
    const timestamp = Math.floor(Date.now() / 1000).toString();
    const nonce = crypto.randomUUID().replace(/-/g, '');
    const path = '/proxy/2027/api/ping';
    const bodyHash = crypto.createHash('sha256').update('').digest('hex');
    const payload = [timestamp, nonce, 'GET', path, bodyHash].join('\n');
    const signature = base64Url(crypto.createHmac('sha256', secret).update(payload).digest());

    const apiResp = await fetchText(`${GATEWAY}${path}`, {
        method: 'GET',
        headers: {
            'X-Gateway-Device-Key': key,
            'X-Gateway-Device-Timestamp': timestamp,
            'X-Gateway-Device-Nonce': nonce,
            'X-Gateway-Device-Body-SHA256': bodyHash,
            'X-Gateway-Device-Signature': signature
        }
    });
    console.log('API call status:', apiResp.status);
    console.log('API body preview:', apiResp.body.substring(0, 200));
})();
