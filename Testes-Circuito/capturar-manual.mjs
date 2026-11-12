// Captura os ecrãs da app Windows real (WebView2 por CDP) em desktop e telemóvel, só de leitura, para o manual e o documento de design.
// Variáveis: CDP_URL, PIN_TECNICO, PIN_SUPERVISOR (em modo memória: 4829 e 7953), SAIDA (pasta das imagens).
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9222';
const PIN_T = process.env.PIN_TECNICO ?? '4829';
const PIN_S = process.env.PIN_SUPERVISOR ?? '7953';
const SAIDA = path.resolve(process.env.SAIDA ?? '../docs/manual-img');
fs.mkdirSync(SAIDA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));

const browser = await chromium.connectOverCDP(CDP);
const ctx = browser.contexts()[0];
const page = ctx.pages()[0];
const cdp = await ctx.newCDPSession(page);
await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });
const vista = (modo) => modo === 'mobile'
  ? cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true })
  : cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

async function entrar(pin) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }).first().click();
  const hero = page.locator('.hero-texto');
  if (!(await hero.waitFor({ timeout: 4000 }).then(() => true, () => false))) await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await hero.waitFor({ timeout: 30000 });
}
async function sair() {
  await page.locator('button[aria-label="Terminar sessão"]:visible').first().click();
  const c = page.getByRole('button', { name: 'Sair', exact: true });
  if (await c.isVisible().catch(() => false)) await c.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}
async function ir(rota) { await page.evaluate((r) => window.Blazor.navigateTo(r), rota); await espera(1200); }
const textos = {};
async function foto(modo, papel, nome, rota) {
  await vista(modo); await espera(500);
  if (rota) await ir(rota);
  const f = `${modo}-${papel}-${nome}.png`;
  await page.screenshot({ path: path.join(SAIDA, f) });
  textos[f] = { rota, url: page.url().replace(/^https?:\/\/[^/]+/, ''), texto: (await page.evaluate(() => document.body.innerText)).replace(/\n{2,}/g, '\n').slice(0, 1500) };
  console.log('ok', f);
}

await page.waitForLoadState('domcontentloaded');
await page.locator('.teclado, .hero-texto').first().waitFor({ timeout: 40000 });
if (await page.locator('.hero-texto').isVisible()) await sair();

for (const modo of ['desktop', 'mobile']) await foto(modo, 'sem', 'login');

const comuns = [['inicio', '/'], ['avaria', '/avaria'], ['intervencoes', '/intervencoes'], ['equipamentos', '/equipamentos'], ['mais', '/mais'], ['avisos', '/notificacoes'], ['pin', '/pin']];
const sup = [['validacao', '/validacao'], ['por-rever', '/por-rever'], ['fichas', '/fichas'], ['planeadas', '/planeadas'], ['equipa', '/equipa'], ['configuracao', '/configuracao'], ['periodicidades', '/periodicidades'], ['nova-ficha', '/nova-ficha'], ['equipamento-novo', '/equipamentos/novo']];

for (const [papel, pin, rotas] of [['tecnico', PIN_T, comuns], ['supervisor', PIN_S, [...comuns, ...sup]]]) {
  await vista('mobile'); await entrar(pin);
  for (const modo of ['desktop', 'mobile']) {
    for (const [nome, rota] of rotas) await foto(modo, papel, nome, rota);
    // detalhe do primeiro equipamento e da primeira ficha, se existirem
    await vista(modo); await ir('/equipamentos');
    const eq = page.locator('a[href*="/equipamentos/"]:visible').first();
    if (await eq.count()) { await eq.click(); await espera(1200); await foto(modo, papel, 'equipamento-ficha'); }
    await ir('/intervencoes');
    const fi = page.locator('a[href*="/intervencoes/"]:visible').first();
    if (await fi.count()) { await fi.click(); await espera(1200); await foto(modo, papel, 'ficha'); }
  }
  await ir('/'); await vista('mobile'); await sair();
}
fs.writeFileSync(path.join(SAIDA, 'textos.json'), JSON.stringify(textos, null, 1));
await browser.close().catch(() => {});
console.log('Concluído.');
