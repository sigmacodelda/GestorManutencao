// Auditoria de UI/UX da app Windows real (WebView2 por CDP), só de leitura: não cria nem altera dados.
// Percorre os ecrãs do técnico e do supervisor em tamanho de telemóvel e mede: transbordo horizontal,
// alvos de toque, nomes acessíveis, etiquetas de campos, imagens sem alt, tamanho de letra, contraste, título h1,
// erros de consola e pedidos falhados. Variáveis: API_URL, CDP_URL, PIN_TECNICO, PIN_SUPERVISOR.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9222';
const PIN_T = process.env.PIN_TECNICO;
const PIN_S = process.env.PIN_SUPERVISOR;
if (!PIN_T || !PIN_S) { console.error('Faltam PIN_TECNICO e PIN_SUPERVISOR.'); process.exit(2); }
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const PASTA = path.resolve('resultados-ux', new Date().toISOString().replace(/[:.]/g, '-'));
fs.mkdirSync(PASTA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);

async function http(token, rota) {
  const r = await fetch(API + rota, { headers: { Authorization: `Bearer ${token}` } });
  try { return await r.json(); } catch { return null; }
}
async function login(pin) {
  const r = await fetch(API + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ pin }) });
  const j = await r.json();
  if (!j?.token) throw new Error(`login recusado (${r.status})`);
  return j.token;
}

// Corre dentro da página: devolve os problemas encontrados no ecrã atual.
function auditarPagina() {
  const out = [];
  const vis = (e) => { const d = e.closest('details:not([open])'); if (d && !e.closest('summary')) return false; const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none' && s.opacity !== '0'; };
  const desc = (e) => {
    const t = (e.getAttribute('aria-label') || e.innerText || e.value || e.getAttribute('title') || '').trim().replace(/\s+/g, ' ').slice(0, 40);
    return `${e.tagName.toLowerCase()}${e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).slice(0, 2).join('.') : ''}${t ? ` "${t}"` : ''}`;
  };
  const add = (tipo, gravidade, e, detalhe) => out.push({ tipo, gravidade, elemento: e ? desc(e) : '', detalhe });

  if (document.documentElement.scrollWidth > window.innerWidth + 1)
    add('transbordo-horizontal', 'alta', null, `scrollWidth ${document.documentElement.scrollWidth} > ${window.innerWidth}; culpados: ` +
      [...document.querySelectorAll('body *')].filter((x) => x.getBoundingClientRect().right > window.innerWidth + 1 && vis(x)).slice(0, 4).map(desc).join(' | '));
  if (!document.documentElement.lang) add('html-sem-lang', 'media', null, 'falta o atributo lang');
  const meta = document.querySelector('meta[name=viewport]')?.content ?? '';
  if (/user-scalable\s*=\s*no|maximum-scale\s*=\s*1(\.0)?\b/i.test(meta)) add('zoom-desativado', 'alta', null, meta);
  const h1 = [...document.querySelectorAll('h1')].filter(vis);
  if (h1.length !== 1) add('h1', 'baixa', null, `${h1.length} títulos h1 visíveis`);

  // alvos de toque e nomes acessíveis
  const alvos = [...document.querySelectorAll('a[href], button, input:not([type=hidden]), select, textarea, [role=button], [role=link], [tabindex]:not([tabindex="-1"])')].filter(vis);
  for (const e of alvos) {
    // Área de toque real: a caixa/rádio toca-se pela etiqueta; o título de um cartão-ligação cobre o cartão todo (::after).
    const alvoToque = (['checkbox', 'radio'].includes(e.type) && e.closest('label')) || (e.classList.contains('cartao-link') && e.closest('li, article, .cartao, .intervencao')) || e;
    const r = alvoToque.getBoundingClientRect();
    if (r.width < 44 || r.height < 44) {
      const pequeno = r.width < 32 || r.height < 32;
      add('alvo-pequeno', pequeno ? 'alta' : 'media', e, `${Math.round(r.width)}x${Math.round(r.height)}px (mínimo 44x44)`);
    }
    const nome = (e.getAttribute('aria-label') || e.getAttribute('aria-labelledby') || e.innerText || e.getAttribute('title') || '').trim();
    if (['A', 'BUTTON'].includes(e.tagName) && !nome && !e.querySelector('img[alt]:not([alt=""])'))
      add('sem-nome-acessivel', 'alta', e, 'botão/ligação sem texto nem aria-label');
    if (['INPUT', 'SELECT', 'TEXTAREA'].includes(e.tagName) && !['button', 'submit', 'checkbox', 'radio'].includes(e.type)) {
      const comLabel = e.getAttribute('aria-label') || e.getAttribute('aria-labelledby') || e.closest('label') || (e.id && document.querySelector(`label[for="${CSS.escape(e.id)}"]`));
      if (!comLabel) add('campo-sem-etiqueta', e.placeholder ? 'media' : 'alta', e, e.placeholder ? 'só tem placeholder' : 'sem etiqueta');
    }
  }
  // espaçamento entre alvos vizinhos (< 8px na horizontal ou vertical sem sobrepor)
  const caixas = alvos.map((e) => ({ e, r: e.getBoundingClientRect() })).filter((x) => x.r.width >= 20);
  let juntos = 0;
  for (let i = 0; i < caixas.length && juntos < 5; i++) for (let j = i + 1; j < caixas.length && juntos < 5; j++) {
    const a = caixas[i].r, b = caixas[j].r;
    if (caixas[i].e.contains(caixas[j].e) || caixas[j].e.contains(caixas[i].e)) continue;
    // A barra de navegação é fixa: o conteúdo desliza por baixo dela, não é uma sobreposição de alvos.
    const fixo = (e) => { for (let n = e; n; n = n.parentElement) { const p = getComputedStyle(n).position; if (p === 'fixed' || p === 'sticky') return true; } return false; };
    if (fixo(caixas[i].e) !== fixo(caixas[j].e)) continue;
    // Itens da barra de navegação são vizinhos por desenho (cada um ocupa a sua fatia inteira).
    if (caixas[i].e.closest('.navegacao') && caixas[j].e.closest('.navegacao')) continue;
    const dx = Math.max(b.left - a.right, a.left - b.right), dy = Math.max(b.top - a.bottom, a.top - b.bottom);
    const sobreposto = dx < -1 && dy < -1;
    if (sobreposto) { add('alvos-sobrepostos', 'alta', caixas[i].e, `sobrepõe-se a ${desc(caixas[j].e)}`); juntos++; }
    else if ((dx >= 0 && dx < 8 && dy < 0) || (dy >= 0 && dy < 8 && dx < 0)) { add('alvos-juntos', 'baixa', caixas[i].e, `a ${Math.round(Math.max(dx, dy))}px de ${desc(caixas[j].e)}`); juntos++; }
  }
  for (const img of [...document.querySelectorAll('img')].filter(vis)) if (!img.hasAttribute('alt')) add('img-sem-alt', 'media', img, 'falta alt');

  // letra pequena e contraste
  const fundo = (e) => {
    let r = 255, g = 255, b = 255; const pilha = [];
    for (let n = e; n; n = n.parentElement) pilha.push(n);
    for (const n of pilha.reverse()) {
      const m = getComputedStyle(n).backgroundColor.match(/rgba?\(([^)]+)\)/);
      if (!m) continue; const [R, G, B, A = 1] = m[1].split(/[ ,\/]+/).map(Number);
      if (A > 0) { r = r * (1 - A) + R * A; g = g * (1 - A) + G * A; b = b * (1 - A) + B * A; }
    }
    return [r, g, b];
  };
  const lum = ([r, g, b]) => { const f = (c) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; }; return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b); };
  const vistos = new Set();
  for (const e of document.querySelectorAll('body *')) {
    if (!vis(e)) continue;
    const temTexto = [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim().length > 0);
    if (!temTexto) continue;
    const s = getComputedStyle(e); const px = parseFloat(s.fontSize);
    const chave = `${e.tagName}|${s.color}|${px}|${e.className}`;
    if (vistos.has(chave)) continue; vistos.add(chave);
    if (px < 12) add('letra-pequena', 'media', e, `${px}px`);
    // Fundos com gradiente ou imagem não se medem por cor: ficam fora para não dar falsos positivos.
    let comImagem = false; for (let n = e; n; n = n.parentElement) if (getComputedStyle(n).backgroundImage !== 'none') { comImagem = true; break; }
    if (comImagem) continue;
    const m = s.color.match(/rgba?\(([^)]+)\)/); if (!m) continue;
    const [R, G, B, A = 1] = m[1].split(/[ ,\/]+/).map(Number);
    const bg = fundo(e); const fg = [R * A + bg[0] * (1 - A), G * A + bg[1] * (1 - A), B * A + bg[2] * (1 - A)];
    const l1 = lum(fg), l2 = lum(bg); const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
    const grande = px >= 24 || (px >= 18.66 && parseInt(s.fontWeight) >= 700);
    if (ratio < (grande ? 3 : 4.5)) add('contraste', ratio < 3 ? 'alta' : 'media', e, `${ratio.toFixed(2)}:1 (${s.color} sobre rgb(${bg.map(Math.round)}))`);
  }
  return out;
}

const relatorio = [];
const consola = [];
let paginaAtual = '';

async function visitar(page, papel, rota, largura) {
  const nome = `${papel} ${rota} @${largura}`;
  try {
    await page.evaluate((r) => window.Blazor.navigateTo(r), rota);
    await espera(1800);
    if (await page.locator('#blazor-error-ui').isVisible().catch(() => false)) {
      relatorio.push({ papel, rota, largura, problemas: [{ tipo: 'erro-blazor', gravidade: 'alta', elemento: '', detalhe: 'a app mostrou o aviso de erro' }] });
      return;
    }
    const problemas = await page.evaluate(`(${auditarPagina.toString()})()`);
    if (largura === 390) await page.screenshot({ path: path.join(PASTA, `${papel}-${rota.replace(/[^a-z0-9]+/gi, '_') || 'inicio'}.png`) }).catch(() => {});
    relatorio.push({ papel, rota, largura, url: await page.evaluate(() => location.pathname), problemas });
    console.log(`  ${nome}: ${problemas.length} problema(s)`);
  } catch (e) {
    relatorio.push({ papel, rota, largura, problemas: [{ tipo: 'nao-abriu', gravidade: 'alta', elemento: '', detalhe: String(e.message).split('\n')[0] }] });
    console.log(`  ${nome}: não abriu (${String(e.message).split('\n')[0]})`);
  }
}

async function entrar(page, pin) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }).first().click();
  const hero = page.locator('.hero-texto');
  if (!(await hero.waitFor({ timeout: 4000 }).then(() => true, () => false))) await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await hero.waitFor({ timeout: 30000 });
}
async function sair(page) {
  // Em vista de telemóvel o botão está no cabeçalho do início (hero-sair); noutros ecrãs, no topo.
  await page.locator('button[aria-label="Terminar sessão"]:visible').first().click();
  const c = page.getByRole('button', { name: 'Sair', exact: true });
  if (await c.isVisible().catch(() => false)) await c.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}

const tokenS = await login(PIN_S);
const equips = itens(await http(tokenS, '/equipamentos')).filter((e) => (e.nome ?? e.descricao ?? '').trim());
const equipId = equips[0]?.id ?? equips[0]?.equipamentoId ?? equips[0]?.stamp;
const fichas = itens(await http(tokenS, '/intervencoes/todas'));
const fichaId = fichas[0]?.id ?? fichas[0]?.stamp;
console.log(`Dados para os ecrãs de detalhe: equipamento ${equipId ? 'ok' : 'não encontrado'}, ficha ${fichaId ? 'ok' : 'não encontrada'}.`);

const browser = await chromium.connectOverCDP(CDP);
const contexto = browser.contexts()[0];
const page = contexto.pages()[0] ?? await contexto.waitForEvent('page');
const cdp = await contexto.newCDPSession(page);
await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });
page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') consola.push({ tipo: m.type(), texto: m.text().slice(0, 300), onde: paginaAtual }); });
page.on('pageerror', (e) => consola.push({ tipo: 'excecao', texto: String(e.message).slice(0, 300), onde: paginaAtual }));
page.on('requestfailed', (r) => consola.push({ tipo: 'pedido-falhado', texto: `${r.method()} ${r.url().replace(/\?.*/, '')} ${r.failure()?.errorText ?? ''}`, onde: paginaAtual }));
page.on('response', (r) => { if (r.status() >= 400) consola.push({ tipo: `http-${r.status()}`, texto: `${r.request().method()} ${r.url().replace(/\?.*/, '')}`, onde: paginaAtual }); });

const emular = (largura) => cdp.send('Emulation.setDeviceMetricsOverride', { width: largura, height: 844, deviceScaleFactor: 2, mobile: true });
await page.waitForLoadState('domcontentloaded');
await page.locator('.teclado, .hero-texto').first().waitFor({ timeout: 40000 });
if (await page.locator('.hero-texto').isVisible()) await sair(page);

const comuns = ['/', '/avaria', '/intervencoes', '/equipamentos', '/mais', '/notificacoes', '/pin'];
const tecnico = [...comuns, ...(equipId ? [`/equipamentos/${equipId}`] : []), '/nova-ficha'];
const supervisor = [...comuns, '/validacao', '/por-rever', '/fichas', '/planeadas', '/equipa', '/configuracao', '/periodicidades', '/nova-ficha', '/equipamentos/novo',
  ...(equipId ? [`/equipamentos/${equipId}`, `/equipamentos/${equipId}/editar`, `/equipamentos/${equipId}/instrucoes`] : []), ...(fichaId ? [`/intervencoes/${fichaId}`] : [])];

try {
  await emular(390);
  // ecrã de login (antes de entrar)
  paginaAtual = 'login';
  const problemasLogin = await page.evaluate(`(${auditarPagina.toString()})()`);
  await page.screenshot({ path: path.join(PASTA, 'login.png') }).catch(() => {});
  relatorio.push({ papel: 'sem sessão', rota: '/login', largura: 390, problemas: problemasLogin });
  for (const [papel, pin, rotas] of [['técnico', PIN_T, tecnico], ['supervisor', PIN_S, supervisor]]) {
    console.log(`\n${papel}`);
    await entrar(page, pin);
    for (const largura of [390, 320]) {
      await emular(largura);
      for (const rota of rotas) { paginaAtual = `${papel} ${rota}`; await visitar(page, papel, rota, largura); }
    }
    await emular(390);
    await page.evaluate(() => window.Blazor.navigateTo('/'));
    await page.locator('.hero-texto').waitFor({ timeout: 20000 });
    await page.evaluate(() => window.scrollTo(0, 0));
    await espera(900); // deixa acabar a animação de entrada do cabeçalho
    // O botão de sair do início fica tapado pelo bloco de boas-vindas em vista de telemóvel: regista-se e sai-se por JS.
    try { await sair(page); } catch (e) { console.log('DBG ' + JSON.stringify(String(e.message).slice(0, 1200)));
      relatorio.push({ papel, rota: '/ (terminar sessão)', largura: 390, problemas: [{ tipo: 'alvo-tapado', gravidade: 'alta', elemento: 'button "Terminar sessão" (hero-sair)', detalhe: 'não clicável em vista de telemóvel: ' + (/intercepts pointer events/.test(e.message) ? 'o bloco .boas-vindas.hero intercepta os cliques' : String(e.message).split('\n')[0]) }] });
      await cdp.send('Emulation.clearDeviceMetricsOverride').catch(() => {});
      await sair(page);
    }
  }
} finally {
  await cdp.send('Emulation.clearDeviceMetricsOverride').catch(() => {});
}

// ---- relatório
const gravs = { alta: 0, media: 0, baixa: 0 };
const porTipo = new Map();
for (const p of relatorio) for (const x of p.problemas) {
  gravs[x.gravidade]++;
  const normal = x.tipo === 'alvo-pequeno' ? { ...x, elemento: x.elemento.replace(/ ".*$/, ''), detalhe: x.detalhe.replace(/\d+x\d+px/, 'pequeno') } : x;
  const k = `${normal.gravidade}|${normal.tipo}|${normal.elemento}|${normal.detalhe}`.slice(0, 400);
  const g = porTipo.get(k) ?? { ...x, ecras: new Set() }; g.ecras.add(`${p.papel} ${p.rota}@${p.largura}`); porTipo.set(k, g);
}
const ordem = { alta: 0, media: 1, baixa: 2 };
const lista = [...porTipo.values()].sort((a, b) => ordem[a.gravidade] - ordem[b.gravidade] || a.tipo.localeCompare(b.tipo));
let md = `# Auditoria UI/UX (app Windows real, vista de telemóvel)\n\n`;
md += `Ecrãs auditados: ${relatorio.length} · problemas: ${gravs.alta} altos, ${gravs.media} médios, ${gravs.baixa} baixos (com repetições entre ecrãs)\n\n`;
md += `## Problemas distintos (${lista.length})\n\n| Gravidade | Tipo | Elemento | Detalhe | Onde |\n|---|---|---|---|---|\n`;
for (const x of lista) md += `| ${x.gravidade} | ${x.tipo} | ${x.elemento.replace(/\|/g, '/')} | ${x.detalhe.replace(/\|/g, '/')} | ${[...x.ecras].slice(0, 3).join('; ')}${x.ecras.size > 3 ? ` (+${x.ecras.size - 3})` : ''} |\n`;
md += `\n## Consola e rede (${consola.length})\n\n`;
const cons = new Map(); for (const c of consola) { const k = `${c.tipo}|${c.texto}`; const g = cons.get(k) ?? { ...c, n: 0 }; g.n++; cons.set(k, g); }
for (const c of cons.values()) md += `- ${c.tipo} ×${c.n} (${c.onde}): ${c.texto}\n`;
md += `\n## Ecrãs sem problemas\n\n${relatorio.filter((p) => p.problemas.length === 0).map((p) => `- ${p.papel} ${p.rota} @${p.largura}`).join('\n') || '—'}\n`;
fs.writeFileSync(path.join(PASTA, 'ux-relatorio.md'), md, 'utf8');
fs.writeFileSync(path.join(PASTA, 'ux.json'), JSON.stringify({ relatorio, consola }, null, 1), 'utf8');
console.log(`\nAuditoria: ${relatorio.length} ecrãs, ${lista.length} problemas distintos (${gravs.alta} altos). Relatório: ${path.join(PASTA, 'ux-relatorio.md')}`);
await browser.close().catch(() => {});
