const puppeteer = require('puppeteer-core');
const fs = require('fs');
const base = 'C:/_SigmaRepos/GestorManutencao/Planeamento/';
const out = base + 'Entrega-Cliente/img/';
fs.mkdirSync(out, { recursive: true });
const pw = process.env.LOCALAPPDATA + '/ms-playwright';
const chrome = fs.readdirSync(pw).filter(x => x.startsWith('chromium-'))[0];
const wait = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const b = await puppeteer.launch({ executablePath: 'C:/Program Files/Google/Chrome/Application/chrome.exe', headless: true });
  const p = await b.newPage();
  await p.setViewport({ width: 1280, height: 900, deviceScaleFactor: 2 });

  await p.goto('file:///' + base + '02-App-Intervencoes/mockups/ecras-telemovel.html');
  const ph = async n => { const e = await p.$('#phone'); await e.screenshot({ path: out + n + '.png' }); };
  const clk = async s => { await p.click(s); await wait(250); };
  await ph('app-1-pin');
  for (const d of '1234') await clk('[data-act=dig][data-v="' + d + '"]');
  await clk('[data-act=entrarpin]');
  await ph('app-2-lista');
  await clk('[data-act=novo]');
  await ph('app-3-nova');
  await p.select('#eq', '3');
  await clk('[data-act=tipo][data-v=Curativa]');
  await p.type('#anom', 'Temperatura acima do normal');
  await clk('[data-act=agora][data-v=ini]');
  await clk('[data-act=agora][data-v=fim]');
  await ph('app-4-nova-preenchida');
  await clk('#swNet');
  await clk('[data-act=concluir]');
  await ph('app-5-offline');
  await clk('[data-act=pend]');
  await ph('app-6-pendentes');

  await p.goto('file:///' + base + '01-Backoffice-PHC/mockups/backoffice.html');
  console.log('menu', await p.$$eval('#menu button', a => a.map(x => x.textContent.trim())));
  const names = ['bo-1-dashboard', 'bo-2-equipamentos', 'bo-3-planos', 'bo-4-planeadas', 'bo-5-validacao', 'bo-6-config'];
  const btns = await p.$$('#menu button');
  for (let i = 0; i < Math.min(btns.length, 6); i++) {
    await btns[i].click(); await wait(300);
    await p.screenshot({ path: out + names[i] + '.png' });
  }

  await p.goto('file:///' + base + '03-Pecas-Armazem/mockups/armazem-e-etiquetas.html');
  for (const t of ['pecas', 'rec', 'saida', 'inv', 'etq', 'alt']) {
    await p.click('[data-t=' + t + ']'); await wait(300);
    await p.screenshot({ path: out + 'arm-' + t + '.png' });
  }
  await b.close();
})().catch(e => { console.error(e); process.exit(1); });
