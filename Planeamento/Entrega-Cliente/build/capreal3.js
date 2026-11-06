const puppeteer = require('puppeteer-core');
const out = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/img-real/';
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const b = await puppeteer.connect({ browserURL: 'http://localhost:9222', defaultViewport: null });
  const p = (await b.pages()).find(x => x.url().includes('0.0.0.1'));
  const ok = await p.evaluate(() => {
    const el = [...document.querySelectorAll('*')].find(x => x.children.length === 0 && x.textContent.trim() === 'Manutenção');
    if (!el) return false; (el.closest('button,a,[onclick],.card,div') || el).click(); return true; });
  console.log('tile', ok); await wait(3000);
  await p.screenshot({ path: out + 'm-7-configuracao.png' });
  await p.evaluate(() => { document.querySelectorAll('*').forEach(e => { if (e.scrollHeight > e.clientHeight + 50 && getComputedStyle(e).overflowY !== 'visible') e.scrollTop = 99999; }); window.scrollTo(0, 99999); });
  await wait(800);
  await p.screenshot({ path: out + 'm-8-configuracao-periodicidades.png' });
  console.log(p.url());
  await b.disconnect();
})().catch(e => { console.error(e); process.exit(1); });
