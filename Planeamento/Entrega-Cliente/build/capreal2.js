const puppeteer = require('puppeteer-core');
const out = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/img-real/';
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const b = await puppeteer.connect({ browserURL: 'http://localhost:9222', defaultViewport: null });
  const p = (await b.pages()).find(x => x.url().includes('0.0.0.1'));
  const go = async path => { await p.evaluate(u => { history.pushState(null, '', u); dispatchEvent(new PopStateEvent('popstate')); }, path); await wait(3000); };
  const shot = async n => { await p.screenshot({ path: out + n + '.png' }); console.log(n, p.url()); };
  const clickRow = async () => { const ok = await p.evaluate(() => { const r = document.querySelector('tbody tr'); if (r) { r.click(); return true; } return false; }); await wait(3000); return ok; };
  await go('/Manutencao/Equipamentos'); console.log('row eq', await clickRow()); await shot('m-5-equipamento-ficha');
  await go('/Manutencao/Fichas'); console.log('row ficha', await clickRow()); await shot('m-6-ficha-intervencao');
  await go('/configuracoes');
  const t = await p.evaluate(() => { const e = [...document.querySelectorAll('button,a,div,span')].find(x => x.children.length < 3 && /^\s*Manuten[cç][aã]o\s*$/.test(x.textContent)); if (e) { e.click(); return true; } return false; });
  console.log('tab manut', t); await wait(3000); await shot('m-7-configuracao');
  await b.disconnect();
})().catch(e => { console.error(e); process.exit(1); });
