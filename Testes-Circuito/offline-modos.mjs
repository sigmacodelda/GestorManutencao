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
const PASTA = path.resolve('resultados-offline', `${new Date().toISOString().replace(/[:.]/g, '-')}_${RUN}`);
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
try {
  for (const modo of MODOS) {
    await modo.aplicar();
    const M = `${modo.nome}-${RUN}`;
    rotulo = modo.nome;
    console.log(`\n=== ${modo.nome} ===`);
    ligar();

    // Fichas preparadas pela API: duas por validar (validar / devolver) e uma já validada (Qualidade).
    const idV = await avariaConcluida(tokenT, equip.id, `TESTE-OFF-${M} validar`);
    const idD = await avariaConcluida(tokenT, equip.id, `TESTE-OFF-${M} devolver`);
    const idQ = await avariaConcluida(tokenT, equip.id, `TESTE-OFF-${M} qualidade`);
    afirma((await http(tokenS, 'POST', `/validacao/${idQ}`)).estado === 200, 'não consegui validar a ficha da Qualidade pela API');

    // ================= TÉCNICO
    await passo('Técnico: entra online e o sinal de ligação mostra "ligado"', async () => {
      await entrar(page, PIN_T);
      await ate(async () => (await sinalLigacao(page)).estado === 'ok', 15000, 'sinal verde');
      await foto(page, 'tecnico-online-inicio'); await semErro(page); await transbordo(page);
      return JSON.stringify(await sinalLigacao(page));
    });
    if (modo.nome === 'telemovel') await passo('Telemóvel: o sinal de ligação está no cartão do Início e, nas outras páginas, no canto superior direito', async () => {
      const larg = await page.evaluate(() => window.innerWidth);
      const noHero = await page.locator('.hero .ligacao').first().boundingBox();
      const hero = await page.locator('.hero').first().boundingBox();
      afirma(noHero && hero && noHero.x >= hero.x && noHero.x + noHero.width <= hero.x + hero.width && noHero.y >= hero.y && noHero.y + noHero.height <= hero.y + hero.height, 'o sinal não está dentro do cartão do Início');
      afirma(noHero.x + noHero.width / 2 > larg / 2, `no cartão o sinal devia estar do lado direito (x ${noHero.x}, largura ${larg})`);
      afirma(!(await page.locator('.ligacao-flutuante').first().isVisible()), 'o sinal flutuante não devia aparecer no Início');
      await ir(page, '/avaria'); await page.locator('#equipamento').waitFor({ timeout: 10000 });
      const f = await page.locator('.ligacao-flutuante .ligacao').first().boundingBox();
      afirma(f && f.x > larg / 2 && f.y < 60, `o sinal devia estar no canto superior direito (x ${f?.x}, y ${f?.y})`);
      await foto(page, 'sinal-canto-direito'); await ir(page, '/'); await page.locator('.hero-texto').waitFor({ timeout: 10000 });
    });
    await espera(6000); // deixa a app guardar equipamentos e fichas
    await passo('Técnico: online, regista a avaria 1 (vai direta à API)', async () => {
      await ir(page, '/avaria');
      await page.locator('#equipamento').waitFor({ timeout: 15000 });
      await page.locator('#equipamento').fill(equip.nome);
      const op = page.locator('.opcoes .opcao', { hasText: equip.nome }).first(); await op.waitFor({ timeout: 15000 }); await op.click();
      await page.locator('#anomalia').fill(`TESTE-OFF-${M} tecnico online`);
      await foto(page, 'tecnico-avaria-online');
      await botao(page, 'Feito').click();
      await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
      await ate(async () => !!(await procurar(tokenS, `TESTE-OFF-${M} tecnico online`)), 10000, 'a ficha aparecer na API');
    });

    cortar();
    await passo('Técnico: SEM REDE, a pesquisa de equipamentos devolve resultados da cópia guardada', async () => {
      await ir(page, '/avaria');
      await page.locator('#equipamento').waitFor({ timeout: 15000 });
      await page.locator('#equipamento').fill(equip.nome);
      const n = await ate(async () => (await page.locator('.opcoes .opcao', { hasText: equip.nome }).count()) || 0, 15000, 'equipamento na lista offline');
      await foto(page, 'tecnico-equipamentos-offline');
      return `${n} opção(ões)`;
    });
    await passo('Técnico: SEM REDE, regista a avaria 2 e fica "guardado, será enviado"', async () => {
      await page.locator('.opcoes .opcao', { hasText: equip.nome }).first().click();
      await page.locator('#anomalia').fill(`TESTE-OFF-${M} tecnico offline`);
      await botao(page, 'Feito').click();
      await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
      await semErro(page);
      afirma(!(await procurar(tokenS, `TESTE-OFF-${M} tecnico offline`)), 'a ficha já está na API com a rede cortada');
      await foto(page, 'tecnico-avaria-offline-lista');
      return `sinal: ${JSON.stringify(await sinalLigacao(page))}`;
    });
    await passo('Técnico: SEM REDE, "As minhas intervenções" mostra a ficha guardada (por enviar)', async () => {
      await page.locator('.chip', { hasText: 'Por enviar' }).first().waitFor({ timeout: 20000 });
      await foto(page, 'tecnico-lista-com-pendente');
    });
    ligar();
    await passo(`Técnico: a rede volta e a avaria 2 segue para a API (até ${ESPERA_ENVIO / 1000} s)`, async () => {
      const t0 = Date.now();
      await ate(async () => !!(await procurar(tokenS, `TESTE-OFF-${M} tecnico offline`)), ESPERA_ENVIO, 'a avaria offline chegar à API', 2000);
      await espera(1500); await foto(page, 'tecnico-depois-de-enviar');
      return `chegou em ${Math.round((Date.now() - t0) / 1000)} s; sinal: ${JSON.stringify(await sinalLigacao(page))}`;
    });
    await passo('Técnico: escreve a assinatura e fica guardada no utilizador (API)', async () => {
      await ir(page, '/assinatura');
      await page.locator('#assinatura-tela').waitFor({ timeout: 15000 });
      await botao(page, 'Limpar').waitFor({ timeout: 5000 });
      await desenhar(page);
      await foto(page, 'tecnico-assinatura');
      await botao(page, 'Guardar').click();
      await page.locator('.toast', { hasText: 'Assinatura guardada' }).first().waitFor({ timeout: 15000 });
      const r = await http(tokenT, 'GET', '/auth/assinatura');
      afirma(/^data:image\/png;base64,/.test(r.json?.assinatura ?? '') && r.json.assinatura.length > 400, `a API devolveu ${JSON.stringify(r.json).slice(0, 80)}`);
      await transbordo(page); await semErro(page);
    });
    await passo('Técnico: ficha EXTERNA — sem assinatura da empresa não conclui; SEM REDE assina, conclui e segue quando a rede volta', async () => {
      const id = await avariaExterna(tokenT, tokenS, equip.id, `TESTE-OFF-${M} externa`);
      await ir(page, '/intervencoes');
      await page.locator('a.intervencao', { hasText: equip.nome }).first().waitFor({ timeout: 15000 });
      await espera(2000);
      cortar();
      await ir(page, `/intervencoes/${id}`);
      await page.locator('#feito').waitFor({ timeout: 20000 });
      await page.locator('#assinatura-externa').waitFor({ timeout: 5000 });
      await page.locator('#feito').fill('Reparação feita pela empresa externa (teste)');
      await botao(page, 'Feito').click();
      await espera(1500);
      afirma(await page.locator('#assinante').isVisible(), 'sem assinatura a ficha externa não devia concluir');
      await page.locator('#assinante').fill('Manuel da Empresa Externa');
      await desenhar(page, '#assinatura-externa');
      await foto(page, 'tecnico-externa-assinatura');
      await botao(page, 'Feito').click();
      await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
      afirma((await detalhe(tokenS, id)).estado === 'EmCurso', 'a API já mudou com a rede cortada');
      ligar();
      const t0 = Date.now();
      await ate(async () => (await detalhe(tokenS, id))?.estado === 'Concluida', ESPERA_ENVIO, 'a conclusão externa chegar à API', 2000);
      const d = await detalhe(tokenS, id);
      afirma(d.assinaturaExtNome === 'Manuel da Empresa Externa' && /^data:image\/png;base64,/.test(d.assinaturaExt ?? ''), 'a assinatura externa não ficou na ficha');
      return `chegou em ${Math.round((Date.now() - t0) / 1000)} s`;
    });
    await sair(page);

    // ================= SUPERVISOR
    await passo('Supervisor: entra online e vê as fichas por validar', async () => {
      await entrar(page, PIN_S);
      await ir(page, '/validacao'); await page.locator('h1', { hasText: 'Validação' }).waitFor({ timeout: 15000 });
      await page.locator('li.intervencao', { hasText: equip.nome }).first().waitFor({ timeout: 15000 });
      await foto(page, 'supervisor-validacao-online'); await semErro(page); await transbordo(page);
    });
    await espera(6000);
    cortar();
    await passo('Supervisor: SEM REDE, a lista por validar continua visível (cópia guardada)', async () => {
      await ir(page, '/'); await page.locator('.hero-texto').waitFor({ timeout: 10000 });
      await ir(page, '/validacao'); await page.locator('h1', { hasText: 'Validação' }).waitFor({ timeout: 15000 });
      await page.locator('li.intervencao', { hasText: equip.nome }).first().waitFor({ timeout: 20000 });
      const n = await page.locator('li.intervencao').count();
      afirma(n >= 2, `só ${n} ficha(s) por validar`);
      await foto(page, 'supervisor-validacao-offline');
      return `${n} fichas`;
    });
    await passo('Supervisor: SEM REDE, valida uma ficha ("Guardado. Será enviado quando houver rede.")', async () => {
      const li = page.locator('li.intervencao', { hasText: `${M} validar` }).first();
      await li.getByRole('button', { name: /Validar/ }).first().click();
      await li.getByRole('button', { name: /Confirmar/ }).click();
      await page.locator('.toast', { hasText: /Guardado|enviado/i }).first().waitFor({ timeout: 15000 });
      await foto(page, 'supervisor-validou-offline');
      afirma((await page.locator('li.intervencao', { hasText: `${M} validar` }).count()) === 0, 'a ficha validada continua na lista');
      afirma((await detalhe(tokenS, idV)).estado === 'Concluida', 'a API já mudou com a rede cortada');
    });
    await passo('Supervisor: SEM REDE, devolve uma ficha com motivo', async () => {
      const li = page.locator('li.intervencao', { hasText: `${M} devolver` }).first();
      await li.getByRole('button', { name: /Devolver/ }).first().click();
      await li.locator('textarea').fill('Falta apertar a correia (teste offline)');
      await li.getByRole('button', { name: /^Devolver$/ }).click();
      await ate(async () => (await page.locator('li.intervencao', { hasText: `${M} devolver` }).count()) === 0, 15000, 'a ficha devolvida sair da lista');
      await foto(page, 'supervisor-devolveu-offline');
    });
    await passo('Supervisor: SEM REDE, o que exige rede avisa em vez de falhar calado (planeadas/equipa)', async () => {
      await ir(page, '/equipa'); await page.locator('h1', { hasText: 'Equipa' }).waitFor({ timeout: 10000 }); await espera(2500);
      await foto(page, 'supervisor-equipa-offline'); await semErro(page);
      for (const perfil of ['Operador', 'Qualidade', 'Supervisor']) afirma(await page.locator('.segmento', { hasText: perfil }).first().isVisible(), `falta o perfil ${perfil} em Equipa`);
      return (await page.locator('.campo-erro, .estado-lista, .alerta').allInnerTexts()).join(' | ').slice(0, 160);
    });
    ligar();
    await passo(`Supervisor: a rede volta e validar/devolver chegam à API (até ${ESPERA_ENVIO / 1000} s)`, async () => {
      const t0 = Date.now();
      await ate(async () => (await detalhe(tokenS, idV))?.estado === 'Validada' && (await detalhe(tokenS, idD))?.estado === 'EmCurso', ESPERA_ENVIO, 'a validação e a devolução chegarem à API', 2000);
      const d = await detalhe(tokenS, idD);
      const dev = (d.historico ?? []).find((e) => /devol/i.test(e.etapa));
      afirma(dev && /correia/.test(dev.texto ?? ''), 'o motivo da devolução não ficou no histórico');
      return `chegou em ${Math.round((Date.now() - t0) / 1000)} s`;
    });
    await sair(page);

    // ================= QUALIDADE
    await passo('Qualidade: entra online, vê o Início e a lista por concluir', async () => {
      await entrar(page, PIN_Q);
      await foto(page, 'qualidade-inicio');
      await ir(page, '/qualidade'); await page.locator('h1', { hasText: /Qualidade|concluir/i }).first().waitFor({ timeout: 15000 });
      await page.locator('li.intervencao', { hasText: equip.nome }).first().waitFor({ timeout: 15000 });
      await foto(page, 'qualidade-lista-online'); await semErro(page); await transbordo(page);
    });
    await espera(6000);
    cortar();
    await passo('Qualidade: SEM REDE, a lista continua visível e conclui com o nome de quem higienizou', async () => {
      await ir(page, '/'); await page.locator('.hero-texto').waitFor({ timeout: 10000 });
      await ir(page, '/qualidade'); await page.locator('li.intervencao', { hasText: equip.nome }).first().waitFor({ timeout: 20000 });
      const li = page.locator('li.intervencao', { hasText: `${M} qualidade` }).first();
      await li.getByRole('button', { name: /Concluir intervenção/ }).click();
      afirma(await botao(li, 'Concluir').isDisabled(), 'o botão Concluir devia estar bloqueado sem nome');
      await li.locator('input[type=text]').fill('Limpezas Silva (teste offline)');
      await foto(page, 'qualidade-higienizacao-offline');
      await botao(li, 'Concluir').click();
      await page.locator('.toast', { hasText: /Guardado|enviado/i }).first().waitFor({ timeout: 15000 });
      afirma((await page.locator('li.intervencao', { hasText: `${M} qualidade` }).count()) === 0, 'a ficha concluída continua na lista');
      afirma((await detalhe(tokenS, idQ)).fechada !== true, 'a API já fechou a ficha com a rede cortada');
      await foto(page, 'qualidade-concluiu-offline');
    });
    ligar();
    await passo(`Qualidade: a rede volta e a conclusão chega à API (até ${ESPERA_ENVIO / 1000} s)`, async () => {
      const t0 = Date.now();
      await ate(async () => (await detalhe(tokenS, idQ))?.fechada === true, ESPERA_ENVIO, 'a conclusão chegar à API', 2000);
      const d = await detalhe(tokenS, idQ);
      const h = (d.historico ?? []).find((e) => /higien/i.test(e.etapa));
      afirma(h && /Limpezas Silva/.test(h.quem ?? ''), 'a higienização não ficou no histórico');
      return `chegou em ${Math.round((Date.now() - t0) / 1000)} s`;
    });
    await sair(page);
  }
} finally {
  await cdp.send('Emulation.clearDeviceMetricsOverride').catch(() => {});
  ligar(); proxy.close();
}

// ------------------------------------------------------------ relatório
const falhas = resultados.filter((r) => !r.ok);
let md = `# Teste offline/online (app Windows real)\n\n${resultados.length - falhas.length}/${resultados.length} passos ok · ${falhas.length} falha(s)\n\n| Modo | Passo | Resultado | Detalhe |\n|---|---|---|---|\n`;
for (const r of resultados) md += `| ${r.modo} | ${r.nome} | ${r.ok ? 'ok' : 'FALHA'} | ${(r.ok ? r.detalhe : r.erro).replace(/\|/g, '/')} |\n`;
if (consola.length) md += `\n## Consola\n\n${[...new Set(consola)].slice(0, 30).map((c) => `- ${c}`).join('\n')}\n`;
fs.writeFileSync(path.join(PASTA, 'relatorio.md'), md);
fs.writeFileSync(path.join(PASTA, 'resultados.json'), JSON.stringify(resultados, null, 2));
console.log(`\n${resultados.length - falhas.length}/${resultados.length} ok · relatório em ${PASTA}`);
await browser.close().catch(() => {});
process.exit(falhas.length ? 1 : 0);
