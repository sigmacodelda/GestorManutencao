// Circuito do dia a dia: uma ficha percorre técnico, supervisor e Qualidade pela interface, com e sem rede no técnico. (Base: offline-modos.mjs)
// Teste do modo offline na app Windows real (WebView2 por CDP), em vista de computador e de telemóvel, com e sem ligação ao servidor.
// A app fala com a API por um proxy TCP local (GESTOR_API_URL=https://localhost:<PROXY_PORT>): "cortar" fecha as ligações (sem rede), "ligar" volta a passar.
// Cobre: técnico (avaria sem rede, lista de equipamentos guardada), supervisor (validar e devolver sem rede), Qualidade (concluir sem rede),
// e o envio à API quando a rede volta. Só em memória/teste: cria fichas com o marcador TESTE-OFF-.
// Variáveis: API_URL (https local), PROXY_PORT (7258), CDP_URL, PIN_TECNICO, PIN_SUPERVISOR, PIN_QUALIDADE, ESPERA_ENVIO (s, 100)
import { chromium } from 'playwright-core';
import net from 'node:net';
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9333';

const PROXY_PORT = Number(process.env.PROXY_PORT ?? 7258);
const PIN_T = process.env.PIN_TECNICO, PIN_S = process.env.PIN_SUPERVISOR, PIN_Q = process.env.PIN_QUALIDADE;
const ESPERA_ENVIO = Number(process.env.ESPERA_ENVIO ?? 100) * 1000;
if (!PIN_T || !PIN_S || !PIN_Q) { console.error('Faltam PIN_TECNICO, PIN_SUPERVISOR e PIN_QUALIDADE.'); process.exit(2); }
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const RUN = Math.random().toString(36).slice(2, 7);
const PASTA = path.resolve('resultados-dia-a-dia', `${new Date().toISOString().replace(/[:.]/g, '-')}_${RUN}`);
fs.mkdirSync(PASTA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));
const afirma = (c, m) => { if (!c) throw new Error(m); };
async function ate(fn, ms, desc, passo = 700) {
  const limite = Date.now() + ms; let ult;
  while (Date.now() < limite) { try { const v = await fn(); if (v) return v; } catch (e) { ult = e; } await espera(passo); }
  throw new Error(`Tempo esgotado: ${desc}${ult ? ` (${ult.message})` : ''}`);
}

// ------------------------------------------------------------ proxy com interruptor de rede
let cortada = false;
const ligacoes = new Set();
const alvo = new URL(API);
const proxy = net.createServer((cli) => {
  if (cortada) { cli.destroy(); return; }
  const srv = net.connect(Number(alvo.port), alvo.hostname);
  ligacoes.add(cli); ligacoes.add(srv);
  const fim = () => { cli.destroy(); srv.destroy(); ligacoes.delete(cli); ligacoes.delete(srv); };
  cli.on('error', fim); srv.on('error', fim); cli.on('close', fim); srv.on('close', fim);
  cli.pipe(srv); srv.pipe(cli);
});
await new Promise((r) => proxy.listen(PROXY_PORT, '127.0.0.1', r));
const cortar = () => { cortada = true; for (const s of ligacoes) s.destroy(); ligacoes.clear(); };
const ligar = () => { cortada = false; };

// ------------------------------------------------------------ API direta (preparar e verificar)
async function http(token, metodo, rota, corpo) {
  const r = await fetch(API + rota, { method: metodo, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: corpo !== undefined ? JSON.stringify(corpo) : undefined });
  let json = null; try { json = await r.json(); } catch { /* sem corpo */ }
  return { estado: r.status, json };
}
const login = async (pin) => { const r = await http(null, 'POST', '/auth/login', { pin }); afirma(r.estado === 200, `login ${r.estado}`); return r.json.token; };
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);
async function procurar(tokenS, texto) { return itens((await http(tokenS, 'GET', `/intervencoes/todas?q=${encodeURIComponent(texto)}`)).json).find((f) => (f.anomalia ?? '').includes(texto)); }
async function detalhe(tokenS, id) { return (await http(tokenS, 'GET', `/intervencoes/${id}/detalhe`)).json; }

// Cria uma avaria da Ana (API) e conclui-a: fica "por validar".
async function avariaConcluida(tokenT, equipId, texto) {
  const c = await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: equipId, anomalia: texto });
  afirma(c.estado === 200 || c.estado === 201, `avaria ${c.estado}`);
  const id = c.json.id;
  await http(tokenT, 'POST', `/intervencoes/${id}/iniciar`);
  const f = await http(tokenT, 'POST', `/intervencoes/${id}/concluir`, { feito: 'Feito no teste offline', inicio: '08:00:00', fim: '08:20:00' });
  afirma(f.estado === 200, `concluir ${f.estado}`);
  return id;
}

// A Ana abre uma avaria (API) e o supervisor passa-a a manutenção externa (o técnico só cria internas).
async function avariaExterna(tokenT, tokenS, equipId, texto) {
  const c = await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: equipId, anomalia: texto });
  afirma(c.estado === 200 || c.estado === 201, `avaria ${c.estado}`);
  const id = c.json.id;
  const e = await http(tokenS, 'PUT', `/intervencoes/${id}`, { tipoManut: 'Externa' });
  afirma(e.estado === 200, `externa ${e.estado}`);
  return id;
}

// ------------------------------------------------------------ registo
const resultados = [], achados = [];
let paginaAtual = null, rotulo = '';
async function passo(nome, fn) {
  const t0 = Date.now();
  try { const d = await fn(); resultados.push({ modo: rotulo, nome, ok: true, ms: Date.now() - t0, detalhe: d ?? '' }); console.log(`  ok    [${rotulo}] ${nome}${d ? ' · ' + d : ''}`); return true; }
  catch (e) {
    resultados.push({ modo: rotulo, nome, ok: false, ms: Date.now() - t0, erro: String(e.message ?? e) }); console.log(`  FALHA [${rotulo}] ${nome}: ${e.message ?? e}`);
    try { await paginaAtual?.screenshot({ path: path.join(PASTA, `falha-${rotulo}-${resultados.length}.png`) }); } catch { /* sem imagem */ }
    return false;
  }
}
const foto0 = () => page.screenshot({ path: path.join(PASTA, 'arranque.png') }).catch(() => {});
const foto = (page, nome) => page.screenshot({ path: path.join(PASTA, `${rotulo}-${nome}.png`) }).catch(() => {});

// ------------------------------------------------------------ app
const botao = (alvoLoc, nome) => alvoLoc.getByRole('button', { name: nome, exact: true });
async function entrar(page, pin) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }).first().click();
  const hero = page.locator('.hero-texto');
  // Depois do PIN a app entra sozinha (início ou, sem assinatura, a página da assinatura); se não entrar, carrega-se em Entrar.
  const ass = page.locator('#assinatura-tela');
  const aguardar = (ms) => Promise.race([hero.waitFor({ timeout: ms }).then(() => false), ass.waitFor({ timeout: ms }).then(() => true)]);
  let pede;
  try { pede = await aguardar(5000); } catch { await botao(page, 'Entrar').click(); pede = await aguardar(30000); }
  if (!pede) pede = await ass.waitFor({ timeout: 3500 }).then(() => true).catch(() => false); // a verificação da assinatura pode demorar um pouco
  if (pede) {
    afirma((await page.locator('nav.navegacao:visible').count()) === 0, 'a navegação devia estar escondida até haver assinatura');
    afirma(await botao(page, 'Guardar').isDisabled(), 'Guardar devia estar bloqueado sem texto');
    await foto(page, `assinatura-obrigatoria-${pin}`);
    await desenhar(page);
    await botao(page, 'Guardar').click();
    await hero.waitFor({ timeout: 20000 });
    assinaturasPedidas++;
  }
}
let assinaturasPedidas = 0;
// Desenha um traço no canvas com o rato (como o dedo no telemóvel).
async function desenhar(page, sel = '#assinatura-tela') {
  const c = page.locator(sel); await c.scrollIntoViewIfNeeded({ timeout: 3000 }).catch(() => {});
  const b = await c.boundingBox();
  await page.mouse.move(b.x + b.width * 0.15, b.y + b.height * 0.6); await page.mouse.down();
  for (let i = 1; i <= 12; i++) await page.mouse.move(b.x + b.width * (0.15 + 0.06 * i), b.y + b.height * (0.6 - 0.25 * Math.sin(i / 2)), { steps: 3 });
  await page.mouse.up();
}
async function sair(page) {
  // Em telemóvel o cabeçalho está escondido: sai-se pelo botão do início (hero) ou, se tapado, pelo botão visível.
  const hero = page.locator('.hero-texto');
  await page.evaluate(() => window.Blazor.navigateTo('/'));
  await hero.waitFor({ timeout: 15000 });
  const b = page.locator('button[aria-label="Terminar sessão"]:visible').first();
  await b.click({ force: true });
  const confirma = botao(page, 'Sair');
  if (await confirma.isVisible().catch(() => false)) await confirma.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}
const ir = async (page, rota) => page.evaluate((r) => window.Blazor.navigateTo(r), rota);
const sinalLigacao = async (page) => page.evaluate(() => {
  const vis = [...document.querySelectorAll('.ligacao')].filter((e) => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden'; });
  const e = vis[0]; if (!e) return { visivel: false };
  return { visivel: true, estado: /ligacao-sem/.test(e.className) ? 'sem' : /ligacao-sinc/.test(e.className) ? 'sincronizar' : 'ok', texto: e.getAttribute('aria-label') ?? '' };
});
const semErro = async (page) => afirma(!(await page.locator('#blazor-error-ui').isVisible().catch(() => false)), 'a app mostrou o aviso de erro do Blazor');
async function transbordo(page) {
  const t = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, iw: window.innerWidth }));
  afirma(t.sw <= t.iw + 1, `transbordo horizontal (${t.sw} > ${t.iw})`);
}
async function esperarPendentesZero(page, tokenS, verificar, ms) {
  return ate(async () => (await verificar()) === true, ms, 'a API receber o que ficou na fila');
}

// ------------------------------------------------------------ execução
const browser = await chromium.connectOverCDP(CDP);
const contexto = browser.contexts()[0];
const page = contexto.pages()[0] ?? await contexto.waitForEvent('page');
paginaAtual = page;
const cdp = await contexto.newCDPSession(page);
await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });
const consola = [];
page.on('pageerror', (e) => consola.push(`${rotulo} excecao: ${String(e.message).slice(0, 200)}`));
page.on('console', (m) => { if (m.type() === 'error') consola.push(`${rotulo} consola: ${m.text().slice(0, 200)}`); });
await page.waitForLoadState('domcontentloaded');
// A app arrancou antes do proxy existir: se não chegou ao login, recarrega-se agora que o proxy está a passar.
const noLogin = () => ate(async () => (await page.locator('.teclado').isVisible()) || (await page.locator('.hero-texto').isVisible()) || (await page.locator('#assinatura-tela').isVisible()), 25000, 'login, início ou assinatura');
try { await noLogin(); } catch { await foto0(); await page.reload(); await noLogin(); } // só na página da app Gestor (porta própria)
// Sessão guardada de uma execução anterior: se a API nova não tem a assinatura dela, a app pede-a; desenha-se e sai-se.
if (await page.locator('#assinatura-tela').isVisible()) { await desenhar(page); await botao(page, 'Guardar').click(); await page.locator('.hero-texto').waitFor({ timeout: 20000 }); }
if (await page.locator('.hero-texto').isVisible()) await sair(page);

const tokenT = await login(PIN_T), tokenS = await login(PIN_S);
const eqs = itens((await http(tokenS, 'GET', '/equipamentos')).json).filter((e) => (e.nome ?? '').trim().length >= 3 && !/[%_[\]\\]/.test(e.nome));
afirma(eqs.length, 'sem equipamentos na API');
const equip = eqs[0];

const MODOS_TODOS = [
  { nome: 'computador', aplicar: () => cdp.send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false }) },
  { nome: 'telemovel', aplicar: () => cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true }) }
];
// A app é só para telemóvel: por defeito corre só em vista de telemóvel. SO_MODO=computador para a vista larga (só diagnóstico).
const MODOS = MODOS_TODOS.filter((m) => m.nome === (process.env.SO_MODO ?? 'telemovel'));

// A ficha percorre o circuito todo pela interface: técnico (avaria, material, conclui) → supervisor (valida) → Qualidade (conclui).
// Em cada passo confirma-se na API o que ficou gravado. `semRedeNoTecnico` faz o passo do técnico com a rede cortada.
async function vidaDaFicha(page, modo, M, semRedeNoTecnico) {
  const marca = `TESTE-DIA-${M}${semRedeNoTecnico ? '-off' : ''}`;
  const tag = semRedeNoTecnico ? 'técnico sem rede' : 'tudo online';
  let id;

  await passo(`[${tag}] Técnico: regista a avaria pela app`, async () => {
    await entrar(page, PIN_T);
    await ir(page, '/avaria');
    await page.locator('#equipamento').waitFor({ timeout: 15000 });
    await page.locator('#equipamento').fill(equip.nome);
    const op = page.locator('.opcoes .opcao', { hasText: equip.nome }).first(); await op.waitFor({ timeout: 15000 }); await op.click();
    await page.locator('#anomalia').fill(`${marca} avaria`);
    await botao(page, 'Feito').click();
    await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
    const f = await ate(async () => procurar(tokenS, `${marca} avaria`), 15000, 'a ficha aparecer na API');
    id = f.id;
    afirma(f.estado === 'Aberta' || f.estado === 'Planeada' || f.estado === 'EmCurso', `estado inicial inesperado: ${f.estado}`);
    await foto(page, `${semRedeNoTecnico ? 'off' : 'on'}-1-avaria-registada`);
  });
  if (!id) return;

  if (semRedeNoTecnico) cortar();
  await passo(`[${tag}] Técnico: abre a ficha, junta material e conclui`, async () => {
    await page.locator(`a.intervencao[href$="${id}"]`).first().click({ timeout: 30000 });
    await page.locator('dl.ficha').waitFor({ timeout: 20000 });
    const comecar = botao(page, 'Começar');
    if (await comecar.isVisible().catch(() => false)) await comecar.click();
    await page.locator('#feito').waitFor({ timeout: 20000 });
    await page.locator('#material').fill('Parafuso M8 (dia a dia)');
    await page.locator('#qtt').fill('2'); await page.locator('#qtt').press('Tab');
    // O técnico não vê o preço (só o supervisor).
    if (await page.locator('#epv').count()) { await page.locator('#epv').fill('1.5'); await page.locator('#epv').press('Tab'); }
    await botao(page, 'Juntar material').click();
    await page.locator('.material-linha', { hasText: 'Parafuso M8 (dia a dia)' }).waitFor({ timeout: 20000 });
    await page.locator('#feito').fill(`${marca} resolvido`);
    await foto(page, `${semRedeNoTecnico ? 'off' : 'on'}-2-tecnico-a-concluir`);
    await botao(page, 'Feito').click();
    await page.locator('.toast', { hasText: /Intervenção concluída|Guardado|enviado/i }).first().waitFor({ timeout: 20000 });
    await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 20000 });
    await semErro(page);
    if (semRedeNoTecnico) afirma((await detalhe(tokenS, id)).estado !== 'Concluida', 'a API já tem a conclusão com a rede cortada');
  });
  if (semRedeNoTecnico) {
    ligar();
    await passo(`[${tag}] A rede volta e a conclusão do técnico chega à API (até ${ESPERA_ENVIO / 1000} s)`, async () => {
      const t0 = Date.now();
      await ate(async () => ['Concluida', 'Validada'].includes((await detalhe(tokenS, id))?.estado), ESPERA_ENVIO, 'a conclusão chegar à API', 2000);
      return `chegou em ${Math.round((Date.now() - t0) / 1000)} s`;
    });
  }
  await passo(`[${tag}] API: a ficha está concluída com o material e as horas`, async () => {
    const d = await ate(async () => { const x = await detalhe(tokenS, id); return x?.estado === 'Concluida' ? x : null; }, 20000, 'a ficha ficar concluída');
    afirma(JSON.stringify(d).includes('Parafuso M8 (dia a dia)'), 'o material não ficou na ficha');
    afirma((d.historico ?? []).length >= 1, 'a ficha não tem histórico');
  });
  await sair(page);

  await passo(`[${tag}] Supervisor: vê a ficha por validar e valida pela app`, async () => {
    await entrar(page, PIN_S);
    await ir(page, '/validacao'); await page.locator('h1', { hasText: 'Validação' }).waitFor({ timeout: 15000 });
    const cartao = page.locator('li.ficha-validar, li.intervencao', { hasText: `${marca} avaria` }).first();
    await cartao.waitFor({ timeout: 30000 });
    await foto(page, `${semRedeNoTecnico ? 'off' : 'on'}-3-supervisor-por-validar`);
    await botao(cartao, 'Validar').click();
    const confirmar = botao(cartao, 'Confirmar');
    if (await confirmar.isVisible().catch(() => false)) await confirmar.click();
    await page.locator('.toast', { hasText: /Validada|Guardado/i }).first().waitFor({ timeout: 20000 });
    await ate(async () => (await page.locator('li', { hasText: `${marca} avaria` }).count()) === 0, 20000, 'a ficha sair da lista de validação');
    await ate(async () => (await detalhe(tokenS, id))?.estado === 'Validada', 20000, 'a API marcar Validada');
    await semErro(page);
  });
  await sair(page);

  await passo(`[${tag}] Qualidade: vê a ficha validada e conclui com o nome de quem higienizou`, async () => {
    await entrar(page, PIN_Q);
    await ir(page, '/qualidade'); await page.locator('h1', { hasText: /Qualidade|concluir/i }).first().waitFor({ timeout: 15000 });
    const li = page.locator('li.intervencao', { hasText: `${marca} avaria` }).first();
    await li.waitFor({ timeout: 30000 });
    await li.getByRole('button', { name: /Concluir intervenção/ }).click();
    await li.locator('input[type=text]').fill('Limpezas Silva (dia a dia)');
    await foto(page, `${semRedeNoTecnico ? 'off' : 'on'}-4-qualidade-a-concluir`);
    await botao(li, 'Concluir').click();
    await page.locator('.toast', { hasText: /Concluída|concluída|Guardado/i }).first().waitFor({ timeout: 20000 });
    await ate(async () => (await detalhe(tokenS, id))?.fechada === true, 30000, 'a API fechar a ficha');
    const d = await detalhe(tokenS, id);
    afirma((d.historico ?? []).some((e) => /Limpezas Silva/.test(e.quem ?? '')), 'a higienização não ficou no histórico');
    await semErro(page);
  });
  await sair(page);

  await passo(`[${tag}] Fim: a ficha percorreu todas as etapas e já não aparece nas listas de trabalho`, async () => {
    const d = await detalhe(tokenS, id);
    afirma(d.fechada === true, 'a ficha não está fechada');
    const etapas = (d.historico ?? []).map((e) => e.etapa).join(' > ');
    return etapas.slice(0, 160);
  });
}

try {
  for (const modo of MODOS) {
    await modo.aplicar();
    rotulo = modo.nome;
    console.log(`\n=== ${modo.nome} ===`);
    const M = `${modo.nome}-${RUN}`;
    await vidaDaFicha(page, modo, M, false);
    await espera(3000);
    await vidaDaFicha(page, modo, M, true);
  }
} finally {
  await cdp.send('Emulation.clearDeviceMetricsOverride').catch(() => {});
  ligar(); proxy.close();
}

// ------------------------------------------------------------ relatório
const falhas = resultados.filter((r) => !r.ok);
let md = `# Circuito do dia a dia (app Windows real)\n\n${resultados.length - falhas.length}/${resultados.length} passos ok · ${falhas.length} falha(s)\n\n| Modo | Passo | Resultado | Detalhe |\n|---|---|---|---|\n`;
for (const r of resultados) md += `| ${r.modo} | ${r.nome} | ${r.ok ? 'ok' : 'FALHA'} | ${(r.ok ? r.detalhe : r.erro).replace(/\|/g, '/')} |\n`;
if (consola.length) md += `\n## Consola\n\n${[...new Set(consola)].slice(0, 30).map((c) => `- ${c}`).join('\n')}\n`;
fs.writeFileSync(path.join(PASTA, 'relatorio.md'), md);
fs.writeFileSync(path.join(PASTA, 'resultados.json'), JSON.stringify(resultados, null, 2));
console.log(`\n${resultados.length - falhas.length}/${resultados.length} ok · relatório em ${PASTA}`);
await browser.close().catch(() => {});
process.exit(falhas.length ? 1 : 0);
