const puppeteer = require('puppeteer-core');
const base = 'C:/_SigmaRepos/GestorManutencao/Planeamento/';
const out = base + 'Entrega-Cliente/img/';
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const b = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const p = await b.newPage();
  await p.setViewport({ width: 1100, height: 900, deviceScaleFactor: 2 });
  await p.goto('file:///' + base + 'Entrega-Cliente/build/fluxos.html', { waitUntil: 'networkidle2' });
  await wait(800);
  for (const id of ['d1', 'd2', 'd3', 'd4', 'd5', 'd6']) {
    const e = await p.$('#' + id);
    await e.screenshot({ path: out + 'fluxo-' + id + '.png' });
  }
  // backoffice validação com uma ficha escolhida
  await p.setViewport({ width: 1280, height: 760, deviceScaleFactor: 2 });
  await p.goto('file:///' + base + '01-Backoffice-PHC/mockups/backoffice.html');
  const btns = await p.$$('#menu button');
  await btns[4].click(); await wait(300);
  await p.click('#va-corpo tr[data-f]'); await wait(300);
  await p.screenshot({ path: out + 'bo-5-validacao.png' });
  await btns[3].click(); await wait(300);
  await p.screenshot({ path: out + 'bo-4-planeadas.png' });
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
