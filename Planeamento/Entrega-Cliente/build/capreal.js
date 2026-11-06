const puppeteer = require('puppeteer-core');
const out = 'C:/_SigmaRepos/GestorManutencao/Planeamento/Entrega-Cliente/img-real/';
const wait = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const b = await puppeteer.connect({ browserURL: 'http://localhost:9222', defaultViewport: null });
  const pages = await b.pages();
  const p = pages.find(x => x.url().includes('0.0.0.1')) || pages[0];
  console.log('url', p.url(), JSON.stringify(await p.evaluate(() => [innerWidth, innerHeight])));
  const go = async (path, name) => {
    await p.evaluate(u => { history.pushState(null, '', u); dispatchEvent(new PopStateEvent('popstate')); }, path);
    await wait(3500);
    await p.screenshot({ path: out + name + '.png' });
    console.log(name, p.url());
  };
  await go('/Manutencao/Equipamentos', 'm-1-equipamentos');
  await go('/Manutencao/Planeadas', 'm-2-planeadas');
  await go('/Manutencao/Fichas', 'm-3-fichas');
  await go('/Manutencao/Validacao', 'm-4-validacao');
  await b.disconnect();
})().catch(e => { console.error(e); process.exit(1); });
