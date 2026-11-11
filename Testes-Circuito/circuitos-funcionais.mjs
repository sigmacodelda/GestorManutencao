// Circuitos funcionais, um por funcionalidade, na app Windows real (Playwright por CDP sobre o WebView2) com a API ligada à base de testes.
// Cada passo regista: o que se clicou, quanto demorou, os pedidos que a API recebeu (lidos do log da API) e os erros da consola da app.
// Variáveis: API_URL, CDP_URL, PIN_TECNICO, PIN_SUPERVISOR, GM_API_LOG (ficheiro de log da API). Os PINs nunca se imprimem nem se gravam.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9222';
const PIN_T = process.env.PIN_TECNICO;
const PIN_S = process.env.PIN_SUPERVISOR;
const LOG_API = process.env.GM_API_LOG;
if (!PIN_T || !PIN_S) { console.error('Faltam PIN_TECNICO e PIN_SUPERVISOR.'); process.exit(2); }
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const RUN = Math.random().toString(36).slice(2, 8);
const MARCA = `TESTE-E2E-${RUN}`;
const PASTA = path.resolve('resultados', `${new Date().toISOString().replace(/[:.]/g, '-')}_funcionais_${RUN}`);
fs.mkdirSync(PASTA, { recursive: true });

// ------------------------------------------------------------------ registo e depuração
const resultados = [];
const criados = { marca: MARCA, fichas: [], equipamentos: [], utilizadores: [] };
let page;
let circuitoAtual = '';
const consola = [];
let posLog = 0;

function lerLog() {
  if (!LOG_API) return '';
  try { const b = fs.readFileSync(LOG_API); const t = b.subarray(posLog).toString('utf8'); posLog = b.length; return t; } catch { return ''; }
}
// "Request finished HTTP/2 POST https://localhost:7257/auth/login - 200 - application/json 45.2ms"
function pedidosDoLog(texto) {
  const out = [];
  for (const m of texto.matchAll(/Request finished\s+\S+\s+(GET|POST|PUT|DELETE|PATCH)\s+https?:\/\/[^/\s]+(\/[^\s?]*)(?:\?\S*)?\s+-\s+(\d{3})[^\r\n]*?([\d.]+)ms/g)) out.push({ m: m[1], r: m[2].replace(/\/[A-Za-z0-9]{20,}/g, '/{id}'), s: Number(m[3]), ms: Number(m[4]) });
  return out;
}
const avisosDoLog = (texto) => [...texto.matchAll(/^(warn|fail|crit):\s*(.+)$/gm)].map((m) => `${m[1]}: ${m[2]}`.slice(0, 160));

async function passo(nome, fn) {
  const t0 = Date.now();
  consola.length = 0;
  lerLog();
  const r = { circuito: circuitoAtual, nome, ok: true, ms: 0, detalhe: '', chamadas: [], consola: [], avisos: [] };
  try { r.detalhe = (await fn()) ?? ''; } catch (e) {
    r.ok = false; r.erro = String(e.message ?? e).split('\n')[0];
    try { await page.screenshot({ path: path.join(PASTA, `falha-${resultados.length + 1}.png`) }); } catch { /* sem imagem */ }
  }
  r.ms = Date.now() - t0;
  await espera(400);
  const log = lerLog();
  r.chamadas = pedidosDoLog(log);
  r.avisos = avisosDoLog(log);
  r.consola = [...consola];
  resultados.push(r);
  const resumo = r.chamadas.map((c) => `${c.m} ${c.r} ${c.s}`).join(', ');
  console.log(`  ${r.ok ? 'ok   ' : 'FALHA'} [${circuitoAtual}] ${nome}${r.ok ? '' : ': ' + r.erro}${resumo ? `  <- ${resumo}` : ''}`);
  return r.ok;
}
async function circuito(nome, fn) { circuitoAtual = nome; console.log(`\n== ${nome}`); try { await fn(); } catch (e) { resultados.push({ circuito: nome, nome: 'circuito interrompido', ok: false, ms: 0, erro: String(e.message ?? e), chamadas: [], consola: [], avisos: [] }); console.log(`  FALHA circuito interrompido: ${e.message}`); } }
const afirma = (c, m) => { if (!c) throw new Error(m); };
const espera = (ms) => new Promise((r) => setTimeout(r, ms));
async function ate(fn, ms, desc, passoMs = 500) {
  const limite = Date.now() + ms; let ultimo;
  while (Date.now() < limite) { try { const v = await fn(); if (v) return v; } catch (e) { ultimo = e; } await espera(passoMs); }
  throw new Error(`Tempo esgotado: ${desc}${ultimo ? ` (${ultimo.message})` : ''}`);
}

// ------------------------------------------------------------------ API (só para preparar e confirmar)
async function http(token, metodo, rota, corpo) {
  const r = await fetch(API + rota, { method: metodo, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: corpo !== undefined ? JSON.stringify(corpo) : undefined });
  let json = null; try { json = await r.json(); } catch { /* sem corpo */ }
  return { estado: r.status, json };
}
const entrarApi = async (pin) => { const r = await http(null, 'POST', '/auth/login', { pin }); afirma(r.estado === 200 && r.json?.token, `login API ${r.estado}`); return r.json; };
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);

// ------------------------------------------------------------------ app
const botao = (alvo, nome) => alvo.getByRole('button', { name: nome, exact: true });
const semErroBlazor = async () => afirma(!(await page.locator('#blazor-error-ui').isVisible().catch(() => false)), 'a app mostrou o aviso de erro do Blazor');
async function clicar(loc, desc) {
  await loc.first().waitFor({ timeout: 20000 });
  const n = await loc.count(); afirma(n === 1, `${desc}: esperava 1 elemento, achei ${n}`);
  await loc.click(); await semErroBlazor();
}
const escrever = async (sel, texto) => { const l = page.locator(sel); await l.waitFor({ timeout: 20000 }); await l.fill(String(texto)); await l.press('Tab').catch(() => {}); };
async function entrarApp(pin, erroEsperado = false) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await clicar(page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }), `tecla ${d}`);
  if (erroEsperado) return;
  const hero = page.locator('.hero-texto, input#pin-atual');
  await hero.first().waitFor({ timeout: 30000 });
}
async function sairApp() {
  await clicar(page.locator('header.topo button[aria-label="Terminar sessão"]'), 'Terminar sessão');
  const confirma = botao(page, 'Sair'); if (await confirma.isVisible().catch(() => false)) await confirma.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}
// Garante a sessão do papel pedido, mesmo que um passo anterior tenha falhado a meio.
async function sessaoComo(papel) {
  const pin = papel === 'Supervisor' ? PIN_S : PIN_T;
  for (let i = 0; i < 4; i++) {
    if (await page.locator('.hero-texto').isVisible().catch(() => false)) {
      if ((await page.locator('.hero-texto .sobretitulo').innerText()) === papel) return;
      await sairApp(); continue;
    }
    if (await page.locator('input#pin-atual').isVisible().catch(() => false)) { await page.reload(); await espera(3000); continue; }
    if (await page.locator('.teclado').isVisible().catch(() => false)) { await entrarApp(pin); continue; }
    try { await paraInicio(); } catch { await page.reload().catch(() => {}); await espera(3000); }
  }
  throw new Error(`não consegui ficar com a sessão de ${papel}`);
}
async function paraInicio() {
  for (let i = 0; i < 5; i++) {
    if (await page.locator('.hero-texto').isVisible().catch(() => false)) return;
    const voltar = page.locator('a.voltar').first();
    if (!(await voltar.isVisible().catch(() => false))) break;
    await voltar.click(); await page.locator('.hero-texto, a.voltar').first().waitFor({ timeout: 15000 });
  }
  await page.locator('.hero-texto').waitFor({ timeout: 15000 });
}
const mosaico = (titulo) => page.locator('a.mosaico-link', { has: page.locator('.mosaico-titulo', { hasText: new RegExp('^' + titulo + '([0-9]|[ \\u00a0]|$)') }) });
async function abrirMosaico(titulo, h1) { await paraInicio(); await clicar(mosaico(titulo), `mosaico ${titulo}`); await page.locator('h1', { hasText: h1 }).first().waitFor({ timeout: 20000 }); await semErroBlazor(); }
const sucesso = (texto) => page.locator('.alerta-pin, .sucesso, .alerta', { hasText: texto }).first().waitFor({ timeout: 20000 });

// ------------------------------------------------------------------ circuitos
const ctx = { eq: null, equipId: null, instrucaoId: null, planeadaId: null, fichaManual: null, avaria: null, loginT: null };
const NOME_EQ = `${MARCA} Compressor`;

async function ligar() {
  const browser = await chromium.connectOverCDP(CDP);
  const contexto = browser.contexts()[0];
  page = contexto.pages()[0] ?? await contexto.waitForEvent('page');
  const s = await contexto.newCDPSession(page);
  await s.send('Emulation.setFocusEmulationEnabled', { enabled: true });
  page.on('console', (m) => { if (m.type() === 'error') consola.push(m.text().slice(0, 160)); });
  page.on('pageerror', (e) => consola.push(`pageerror: ${String(e.message).slice(0, 160)}`));
  await page.waitForLoadState('domcontentloaded');
  await ate(async () => (await page.locator('.teclado').isVisible()) || (await page.locator('.hero-texto').isVisible()), 40000, 'login ou início');
  if (await page.locator('.hero-texto').isVisible()) await sairApp();
  return browser;
}

const browser = await (async () => { try { return await ligar(); } catch (e) { console.log(`FALHA ao ligar à app: ${e.message}`); process.exit(2); } })();
const T0 = Date.now();

await circuito('01 Login e PIN de 4 números', async () => {
  await passo('PIN errado (4 números) mostra o erro e não entra', async () => {
    await entrarApp('0000', true);
    await page.locator('.alerta[role=alert], .alerta').first().waitFor({ timeout: 15000 });
    afirma(!(await page.locator('.hero-texto').isVisible().catch(() => false)), 'entrou com PIN errado');
  });
  await passo('O teclado só aceita 4 números e entra sozinho ao 4.º (sem carregar em Entrar)', async () => {
    // Já com o PIN errado limpo pela app; confirma 4 pontos e que o técnico entra sem botão.
    afirma((await page.locator('.pin-ponto').count()) === 4, `pontos do PIN: ${await page.locator('.pin-ponto').count()}`);
    afirma((await page.locator('.login-sub').first().innerText()).includes('4 números'), 'o texto devia dizer 4 números');
    await entrarApp(PIN_T);
    afirma((await page.locator('.hero-texto .sobretitulo').innerText()) === 'Operador', 'devia entrar como Operador');
  });
  await passo('Técnico vê só o essencial e termina a sessão', async () => {
    afirma((await page.locator('a.mosaico-link .mosaico-titulo', { hasText: 'Nova ficha' }).count()) === 0, 'o técnico vê "Nova ficha"');
    afirma((await page.locator('a.mosaico-link .mosaico-titulo', { hasText: /^Equipa$/ }).count()) === 0, 'o técnico vê "Equipa"');
    await sairApp();
  });
});

await circuito('02 Equipamento: criar, ver e editar (supervisor)', async () => {
  await passo('Supervisor entra e abre "Novo equipamento"', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Novo equipamento', 'Novo equipamento');
  });
  await passo('Passo 1: sem referência nem designação não avança (validações)', async () => {
    await clicar(botao(page, 'Seguinte'), 'Seguinte');
    const erros = await page.locator('p.campo-erro').allInnerTexts();
    afirma(erros.some((t) => /nº da infraestrutura/i.test(t)) || erros.some((t) => /designação/i.test(t)), `sem aviso de campos obrigatórios (${erros.join('|')})`);
  });
  await passo('Passos 1 a 3: preencher e Guardar equipamento', async () => {
    await escrever('#eq-ref', MARCA); await escrever('#eq-design', NOME_EQ);
    await escrever('#eq-tipo', 'Compressores'); await escrever('#eq-marca', 'E2E'); await escrever('#eq-modelo', 'M1'); await escrever('#eq-serie', `S-${RUN}`); await escrever('#eq-local', 'TESTE-E2E');
    await clicar(botao(page, 'Seguinte'), 'Seguinte 1');
    await page.locator('#eq-ano').waitFor({ timeout: 10000 });
    await escrever('#eq-ano', '2024'); await escrever('#eq-valor', '1500');
    await clicar(botao(page, 'Seguinte'), 'Seguinte 2');
    await page.locator('#eq-estado').waitFor({ timeout: 10000 });
    await clicar(botao(page, 'Guardar equipamento'), 'Guardar equipamento');
    await page.waitForURL(/equipamentos\/(?!novo)[^/]+$/, { timeout: 30000 });
    ctx.equipId = decodeURIComponent(page.url().split('/equipamentos/')[1].split(/[?#]/)[0]);
    criados.equipamentos.push(ctx.equipId);
    await page.locator('h1', { hasText: NOME_EQ }).waitFor({ timeout: 20000 });
  });
  await passo('Ficha do equipamento: dados gravados, botões do supervisor e histórico de estado', async () => {
    const txt = await page.locator('dl.ficha').innerText();
    afirma(txt.includes(MARCA) && txt.includes('Compressores') && txt.includes('TESTE-E2E'), `dados da ficha: ${txt.replace(/\s+/g, ' ').slice(0, 120)}`);
    for (const t of ['Nova ficha de intervenção', 'Editar equipamento', 'Instruções e tarefas']) afirma((await page.locator('a.btn', { hasText: t }).count()) === 1, `falta o botão "${t}"`);
    afirma((await page.getByText('Histórico de estado').count()) > 0, 'sem secção "Histórico de estado"');
  });
  await passo('Editar equipamento: mudar a marca e guardar', async () => {
    await clicar(page.locator('a.btn', { hasText: 'Editar equipamento' }), 'Editar equipamento');
    await page.locator('#eq-design').waitFor({ timeout: 20000 });
    afirma((await page.locator('#eq-ref').inputValue()) === MARCA, 'a referência não veio preenchida');
    await escrever('#eq-marca', 'E2E-Editada');
    await clicar(botao(page, 'Seguinte'), 'Seguinte 1'); await clicar(botao(page, 'Seguinte'), 'Seguinte 2');
    await clicar(botao(page, 'Guardar equipamento'), 'Guardar equipamento');
    await page.waitForURL(/equipamentos\/[^/]+$/, { timeout: 30000 });
    await page.locator('dl.ficha', { hasText: 'E2E-Editada' }).waitFor({ timeout: 20000 });
  });
  await passo('Referência repetida é recusada', async () => {
    await abrirMosaico('Novo equipamento', 'Novo equipamento');
    await escrever('#eq-ref', MARCA); await escrever('#eq-design', `${MARCA} duplicado`);
    await clicar(botao(page, 'Seguinte'), 'Seguinte 1'); await clicar(botao(page, 'Seguinte'), 'Seguinte 2');
    await clicar(botao(page, 'Guardar equipamento'), 'Guardar equipamento');
    await page.locator('p.campo-erro, .alerta').filter({ hasText: /\S/ }).first().waitFor({ timeout: 20000 });
    afirma(/equipamentos\/novo/.test(page.url()), 'não devia ter navegado');
    await page.goBack().catch(() => {});
  });
});

await circuito('02b Periodicidade Mensal: configurar como 1 mês (a base de testes tem-na sem intervalo)', async () => {
  await passo('Guardar o valor original da "Mensal" (API) para repor no fim', async () => {
    const s = await entrarApi(PIN_S);
    const lista = (await http(s.token, 'GET', '/gestao/periodicidades')).json ?? [];
    ctx.mensal = lista.find((x) => (x.periodo ?? '').trim() === 'Mensal'); afirma(ctx.mensal, 'a periodicidade "Mensal" não existe');
    return `original: ${ctx.mensal.unidade} ${ctx.mensal.intervalo}`;
  });
  await passo('Periodicidades: Mensal passa a "Meses" com intervalo 1 (unidade e intervalo pelos botões)', async () => {
    await sessaoComo('Supervisor'); await abrirMosaico('Periodicidades', 'Periodicidades');
    const linha = page.locator('li.intervencao').filter({ has: page.locator('.intervencao-titulo', { hasText: /^Mensal$/ }) });
    await clicar(linha.getByRole('button', { name: 'Editar', exact: true }), 'Editar Mensal');
    await page.locator('#pe-unidade').selectOption('meses');
    await escrever('#pe-intervalo', '1');
    await clicar(botao(page, 'Guardar'), 'Guardar');
    await ate(async () => (await linha.locator('.intervencao-desc').innerText()).includes('todos os meses'), 20000, 'a descrição "todos os meses"');
  });
  await passo('Intervalo vazio com unidade "Meses" é recusado', async () => {
    const linha = page.locator('li.intervencao').filter({ has: page.locator('.intervencao-titulo', { hasText: /^Trimestral$/ }) }).first();
    await clicar(linha.getByRole('button', { name: 'Editar', exact: true }), 'Editar Trimestral');
    await page.locator('#pe-unidade').selectOption('meses');
    await page.locator('#pe-intervalo').fill(''); await page.locator('#pe-intervalo').press('Tab');
    await clicar(botao(page, 'Guardar'), 'Guardar (sem intervalo)');
    await page.locator('p.campo-erro').filter({ hasText: /Indique o intervalo/ }).first().waitFor({ timeout: 15000 });
    await clicar(botao(page, 'Deixar de editar'), 'Deixar de editar');
  });
});

await circuito('03 Instruções e tarefas (supervisor)', async () => {
  await passo('Abrir "Instruções e tarefas" do equipamento e criar a versão', async () => {
    await sessaoComo('Supervisor');
    if (!(await page.locator('#iv-versao').isVisible().catch(() => false))) {
      await paraInicio(); await abrirMosaico('Equipamentos', 'Equipamentos');
      await escrever('#eq-texto', MARCA); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
      await clicar(page.locator(`a.intervencao[href$="${ctx.equipId}"]`), 'equipamento criado');
      await clicar(page.locator('a.btn', { hasText: 'Instruções e tarefas' }), 'Instruções e tarefas');
    }
    await page.locator('#iv-versao').waitFor({ timeout: 20000 });
    await escrever('#iv-versao', '1'); await escrever('#iv-obs', `${MARCA} instrução`);
    await clicar(botao(page, 'Criar versão'), 'Criar versão');
    await sucesso('Versão criada.');
    await page.locator('li.intervencao', { hasText: 'Versão 1' }).waitFor({ timeout: 20000 });
  });
  await passo('Nova tarefa Mensal: descrição obrigatória e gravar', async () => {
    await clicar(page.locator('a', { hasText: 'Ver tarefas' }).first(), 'Ver tarefas');
    await page.locator('#ta-descricao').waitFor({ timeout: 20000 });
    await clicar(botao(page, 'Guardar tarefa'), 'Guardar tarefa (vazia)');
    afirma((await page.locator('p.campo-erro').allInnerTexts()).join('').includes('Descreva a tarefa'), 'sem aviso "Descreva a tarefa."');
    await page.locator('#ta-periodo').selectOption({ label: 'Mensal' });
    await escrever('#ta-resp', 'Equipa interna'); await escrever('#ta-descricao', `${MARCA} verificar pressão`);
    await clicar(botao(page, 'Guardar tarefa'), 'Guardar tarefa');
    await sucesso('Tarefa criada.');
  });
  await passo('Editar a tarefa e guardar', async () => {
    await clicar(botao(page, 'Editar').first(), 'Editar tarefa');
    await escrever('#ta-descricao', `${MARCA} verificar pressão e óleo`);
    await clicar(botao(page, 'Guardar tarefa'), 'Guardar tarefa (edição)');
    await sucesso('Tarefa guardada.');
  });
  await passo('A tarefa gerou uma planeada para o equipamento (API do supervisor)', async () => {
    const s = await entrarApi(PIN_S);
    const r = await ate(async () => { const x = await http(s.token, 'GET', '/planeadas'); const p = itens(x.json).find((i) => JSON.stringify(i).includes(MARCA)); return p; }, 20000, 'a planeada do equipamento de teste');
    ctx.planeadaId = r.id; criados.fichas.push(r.id);
    return `planeada ${r.data ?? ''}`;
  });
});

await circuito('04 Planeadas (supervisor)', async () => {
  await passo('Abrir Planeadas, pesquisar o equipamento de teste e ver a planeada', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Planeadas', 'Planeadas');
    await escrever('#pl-texto', MARCA);
    await page.locator('.intervencao', { hasText: NOME_EQ }).first().waitFor({ timeout: 20000 });
  });
  await passo('Contadores-botão filtram e voltam a limpar', async () => {
    const kpis = page.locator('button.contador-btn');
    const n = await kpis.count(); afirma(n >= 3, `contadores: ${n}`);
    for (let i = 0; i < n; i++) { await kpis.nth(i).click(); await espera(300); await kpis.nth(i).click(); await espera(200); }
    await semErroBlazor();
  });
  await passo('Atribuir a planeada ao técnico de teste', async () => {
    await escrever('#pl-texto', MARCA);
    const sel = page.locator(`#tec-${ctx.planeadaId}`);
    await sel.waitFor({ timeout: 20000 });
    const opcoes = await sel.locator('option').allInnerTexts();
    const alvo = opcoes.find((o) => /Teste E2E Tecnico/i.test(o)); afirma(alvo, `técnico de teste não está na lista (${opcoes.length} opções)`);
    await sel.selectOption({ label: alvo });
    await page.getByText(/atribuída a/).first().waitFor({ timeout: 20000 });
  });
});

await circuito('05 Todas as fichas, Equipamentos e navegação (supervisor)', async () => {
  await passo('Todas as fichas: pesquisa, filtros e repor', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Todas as fichas', 'Todas as fichas');
    await escrever('#fi-texto', MARCA); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
    await page.locator('.lista-intervencoes, .estado-lista').first().waitFor({ timeout: 20000 });
    await escrever('#fi-texto', 'x'); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar (1 letra)');
    afirma((await page.locator('#fi-erro').innerText()).includes('pelo menos 2'), 'sem aviso de pesquisa curta');
    await page.locator('details summary', { hasText: 'Filtros' }).click();
    await clicar(botao(page, 'Repor filtros'), 'Repor filtros');
  });
  await passo('Equipamentos: pesquisa, KPIs e ordenação', async () => {
    await abrirMosaico('Equipamentos', 'Equipamentos');
    await escrever('#eq-texto', MARCA); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
    await page.locator(`a.intervencao[href$="${ctx.equipId}"]`).waitFor({ timeout: 20000 });
    for (const k of ['Ativos', 'Fora de serviço']) { const b = page.locator('button.contador-btn', { hasText: k }); if (await b.count()) { await b.first().click(); await espera(300); await b.first().click(); } }
    await semErroBlazor();
  });
  await passo('Barra inferior do supervisor: Planeadas, Validação, Máquinas, Mais, Avisos, Início', async () => {
    for (const [rotulo, h1] of [['Planeadas', 'Planeadas'], ['Validação', 'Validação'], ['Máquinas', 'Equipamentos'], ['Mais', 'Mais'], ['Avisos', 'Notificações'], ['Início', null]]) {
      await clicar(page.locator('nav.navegacao .nav-item', { has: page.locator('.nav-rotulo', { hasText: new RegExp('^[ \\n\\t]*' + rotulo) }) }), `nav ${rotulo}`);
      if (h1) await page.locator('h1', { hasText: h1 }).first().waitFor({ timeout: 15000 }); else await page.locator('.hero-texto').waitFor({ timeout: 15000 });
    }
  });
});

await circuito('06 Periodicidades e Configuração (supervisor, repõe o original)', async () => {
  await passo('Periodicidades: editar o aviso de uma e voltar ao valor original', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Periodicidades', 'Periodicidades');
    const editar = botao(page, 'Editar').first(); await editar.waitFor({ timeout: 20000 }); await editar.click();
    const aviso = page.locator('#pe-aviso'); await aviso.waitFor({ timeout: 10000 });
    const antes = await aviso.isChecked();
    await aviso.setChecked(!antes);
    await clicar(botao(page, 'Guardar'), 'Guardar');
    await ate(async () => !(await page.locator('#pe-aviso').isVisible().catch(() => false)), 20000, 'sair da edição');
    await botao(page, 'Editar').first().click(); await page.locator('#pe-aviso').waitFor({ timeout: 10000 });
    afirma((await page.locator('#pe-aviso').isChecked()) === !antes, 'o aviso não ficou gravado');
    await page.locator('#pe-aviso').setChecked(antes);
    await clicar(botao(page, 'Guardar'), 'Guardar (repor)');
    await ate(async () => !(await page.locator('#pe-aviso').isVisible().catch(() => false)), 20000, 'sair da edição (repor)');
  });
  await passo('Configuração: antecedência inválida é recusada, válida grava e repõe', async () => {
    await abrirMosaico('Configuração', 'Configuração');
    const campo = page.locator('#cfg-antecedencia'); await campo.waitFor({ timeout: 20000 });
    const original = await campo.inputValue();
    await escrever('#cfg-antecedencia', '999'); await clicar(botao(page, 'Guardar'), 'Guardar (999)');
    await page.locator('p.campo-erro, .alerta').filter({ hasText: /entre 0 e 90/ }).first().waitFor({ timeout: 15000 });
    await escrever('#cfg-antecedencia', '9'); await clicar(botao(page, 'Guardar'), 'Guardar (9)');
    await sucesso('Configuração guardada.');
    await escrever('#cfg-antecedencia', original || '7'); await clicar(botao(page, 'Guardar'), 'Guardar (repor)');
    await sucesso('Configuração guardada.');
  });
});

await circuito('07 Ficha manual e ficha completa (supervisor)', async () => {
  await passo('Nova ficha preventiva para o equipamento de teste', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Nova ficha', 'Nova ficha');
    await escrever('#nf-equipamento', MARCA);
    await clicar(page.locator('.opcoes .opcao', { hasText: NOME_EQ }), 'equipamento de teste');
    await page.locator('#nf-tipo').selectOption({ label: 'Preventiva' });
    await escrever('#nf-problema', `${MARCA} ficha manual`);
    await clicar(botao(page, 'Criar ficha'), 'Criar ficha');
    await page.waitForURL(/intervencoes\/[^/]+$/, { timeout: 30000 });
    ctx.fichaManual = page.url().split('/intervencoes/')[1].split(/[?#]/)[0]; criados.fichas.push(ctx.fichaManual);
    await page.locator('#fc-tipo').waitFor({ timeout: 20000 });
  });
  await passo('Nova ficha sem equipamento é recusada', async () => {
    await paraInicio().catch(() => {}); await abrirMosaico('Nova ficha', 'Nova ficha');
    await clicar(botao(page, 'Criar ficha'), 'Criar ficha (vazia)');
    afirma((await page.locator('#nf-erro').innerText()).includes('Escolha o equipamento'), 'sem aviso "Escolha o equipamento."');
  });
  await passo('Ficha completa: editar o problema e a resolução e guardar', async () => {
    await paraInicio().catch(() => {}); await abrirMosaico('Todas as fichas', 'Todas as fichas');
    await escrever('#fi-texto', `${MARCA} ficha manual`); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
    await clicar(page.locator(`a[href$="${ctx.fichaManual}"]`), 'abrir ficha manual');
    await page.locator('#fc-problema').waitFor({ timeout: 20000 });
    await escrever('#fc-resolucao', `${MARCA} resolução`);
    await clicar(botao(page, 'Guardar alterações'), 'Guardar alterações');
    await ate(async () => { const d = (await http((await entrarApi(PIN_S)).token, 'GET', `/intervencoes/${encodeURIComponent(ctx.fichaManual)}/detalhe`)).json; return (d?.resolucao ?? '').includes(MARCA); }, 20000, 'a resolução gravada');
  });
  await passo('Ficha completa: juntar horas de mão de obra do técnico de teste', async () => {
    const sel = page.locator('#mo-tecnico'); await sel.waitFor({ timeout: 20000 });
    const ops = await sel.locator('option').allInnerTexts(); const alvo = ops.find((o) => /Teste E2E Tecnico/i.test(o));
    afirma(alvo, `técnico de teste não está em "Mão de obra" (${ops.length} opções)`);
    await sel.selectOption({ label: alvo }); await escrever('#mo-horas', '1.5');
    await clicar(botao(page, 'Juntar horas'), 'Juntar horas');
    await page.locator('button[aria-label^="Apagar horas de"]').first().waitFor({ timeout: 20000 });
  });
  await passo('Cancelar a ficha com motivo (motivo é obrigatório)', async () => {
    await clicar(botao(page, 'Cancelar intervenção'), 'Cancelar intervenção');
    await clicar(botao(page, 'Confirmar'), 'Confirmar (sem motivo)');
    afirma((await page.locator('#fc-acoes .campo-erro, p.campo-erro').allInnerTexts()).join('').includes('motivo do cancelamento'), 'sem aviso do motivo');
    await escrever('#fc-motivo', 'Teste E2E'); await clicar(botao(page, 'Confirmar'), 'Confirmar');
    await page.getByText('Esta ficha já não se altera.').waitFor({ timeout: 20000 });
  });
});

await circuito('08 Técnico: planeada atribuída e avaria com material', async () => {
  await passo('Supervisor sai e o técnico entra', async () => { await sessaoComo('Operador'); });
  await passo('Técnico vê a planeada atribuída e começa a ficha (Começar → Feito)', async () => {
    await abrirMosaico('As minhas intervenções', 'As minhas intervenções');
    await clicar(page.locator(`a.intervencao[href$="${ctx.planeadaId}"]`), 'planeada atribuída');
    await clicar(botao(page, 'Começar'), 'Começar');
    await page.locator('#feito').waitFor({ timeout: 20000 });
    await clicar(botao(page, 'Feito'), 'Feito (vazio)');
    afirma((await page.locator('p.campo-erro').allInnerTexts()).join('').includes('Escreva o que foi feito'), 'sem aviso "Escreva o que foi feito."');
    await escrever('#feito', `${MARCA} planeada feita`);
    await clicar(botao(page, 'Feito'), 'Feito');
    await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
  });
  await passo('Técnico regista uma avaria no equipamento de teste', async () => {
    await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções');
    await abrirMosaico('Registar avaria', 'Registar avaria');
    await clicar(botao(page, 'Guardar avaria'), 'Guardar avaria (vazia)');
    afirma((await page.locator('#erro-avaria').innerText()).includes('Escolha o equipamento'), 'sem aviso "Escolha o equipamento."');
    await escrever('#equipamento', MARCA);
    await clicar(page.locator('.opcoes .opcao', { hasText: NOME_EQ }), 'equipamento de teste');
    await escrever('#anomalia', `${MARCA} avaria`);
    await clicar(botao(page, 'Guardar avaria'), 'Guardar avaria');
    await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
    const s = await entrarApi(PIN_S);
    const f = await ate(async () => itens((await http(s.token, 'GET', `/intervencoes/todas?q=${encodeURIComponent(MARCA + ' avaria')}`)).json).find((x) => (x.anomalia ?? '').includes(`${MARCA} avaria`)), 20000, 'a avaria aparecer');
    ctx.avaria = f.id; criados.fichas.push(f.id);
    afirma(f.tipo === 'Curativa' && f.estado === 'EmCurso', `avaria: ${f.tipo}/${f.estado}`);
  });
  await passo('Material: juntar, editar, apagar e juntar de novo; depois concluir', async () => {
    await page.locator(`a.intervencao[href$="${ctx.avaria}"]`).first().click({ timeout: 30000 });
    await escrever('#material', 'Filtro E2E'); await escrever('#qtt', '2'); await escrever('#epv', '3.5');
    await clicar(botao(page, 'Juntar material'), 'Juntar material');
    await page.locator('.material-linha', { hasText: 'Filtro E2E' }).waitFor({ timeout: 20000 });
    await clicar(page.locator('button[aria-label="Editar Filtro E2E"]'), 'Editar material');
    await escrever('#e-qtt', '3'); await clicar(botao(page, 'Guardar material'), 'Guardar material');
    await ate(async () => (await page.locator('.material-linha', { hasText: 'Filtro E2E' }).innerText()).includes('3'), 15000, 'quantidade editada');
    await clicar(page.locator('button[aria-label="Tirar Filtro E2E"]'), 'Tirar material');
    await ate(async () => (await page.locator('.material-linha', { hasText: 'Filtro E2E' }).count()) === 0, 15000, 'material apagado');
    await escrever('#material', 'Retentor E2E'); await escrever('#qtt', '1');
    await clicar(botao(page, 'Juntar material'), 'Juntar material 2');
    await page.locator('.material-linha', { hasText: 'Retentor E2E' }).waitFor({ timeout: 20000 });
    await escrever('#feito', `${MARCA} avaria resolvida`); await clicar(botao(page, 'Feito'), 'Feito');
    await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
  });
  await passo('Barra inferior do técnico e Notificações', async () => {
    await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções');
    for (const [rotulo, h1] of [['Avaria', 'Registar avaria'], ['Máquinas', 'Equipamentos'], ['Avisos', 'Notificações'], ['Intervenções', 'As minhas intervenções'], ['Início', null]]) {
      await clicar(page.locator('nav.navegacao .nav-item', { has: page.locator('.nav-rotulo', { hasText: new RegExp('^[ \\n\\t]*' + rotulo) }) }), `nav ${rotulo}`);
      if (h1) await page.locator('h1', { hasText: h1 }).first().waitFor({ timeout: 15000 }); else await page.locator('.hero-texto').waitFor({ timeout: 15000 });
    }
    await sairApp();
  });
});

await circuito('09 Validação: devolver, reconcluir e validar', async () => {
  await passo('Supervisor valida a planeada feita pelo técnico', async () => {
    await sessaoComo('Supervisor'); await abrirMosaico('Validação', 'Validação');
    const c = page.locator('li.ficha-validar', { hasText: `${MARCA} planeada feita` }); await c.waitFor({ timeout: 30000 });
    await clicar(botao(c, 'Validar'), 'Validar'); await clicar(botao(c, 'Confirmar'), 'Confirmar');
    await ate(async () => (await c.count()) === 0, 20000, 'sair da lista');
  });
  await passo('Supervisor devolve a avaria (o botão só ativa com motivo)', async () => {
    const c = page.locator('li.ficha-validar', { hasText: `${MARCA} avaria resolvida` }); await c.waitFor({ timeout: 30000 });
    await clicar(botao(c, 'Devolver'), 'Devolver (abrir)');
    afirma(await botao(c, 'Devolver').isDisabled(), 'o botão Devolver devia estar desativado sem motivo');
    await c.locator('textarea').fill('Falta limpar (E2E)');
    await clicar(botao(c, 'Devolver'), 'Devolver (confirmar)');
    await ate(async () => (await c.count()) === 0, 20000, 'sair da lista');
  });
  await passo('Técnico vê a devolução com o motivo e volta a concluir', async () => {
    await sessaoComo('Operador');
    await abrirMosaico('As minhas intervenções', 'As minhas intervenções');
    await clicar(page.locator(`a.intervencao[href$="${ctx.avaria}"]`), 'avaria devolvida');
    const a = page.locator('.alerta-devolvida'); await a.waitFor({ timeout: 20000 });
    afirma((await a.innerText()).includes('Falta limpar'), 'o motivo não aparece ao técnico');
    await escrever('#feito', `${MARCA} limpo`); await clicar(botao(page, 'Feito'), 'Feito');
    await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
    await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções'); await paraInicio(); await sairApp();
  });
  await passo('Supervisor valida a avaria pela ficha completa ("Validar e fechar")', async () => {
    await sessaoComo('Supervisor'); await abrirMosaico('Todas as fichas', 'Todas as fichas');
    await escrever('#fi-texto', `${MARCA} avaria`); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
    await clicar(page.locator(`a[href$="${ctx.avaria}"]`), 'abrir a avaria');
    await clicar(botao(page, 'Validar e fechar'), 'Validar e fechar');
    await page.getByText('Esta ficha já não se altera.').waitFor({ timeout: 20000 });
  });
});

await circuito('10 Equipamento obsoleto e Equipa/PIN (supervisor)', async () => {
  await passo('Marcar o equipamento obsoleto pede confirmação e cancela as planeadas', async () => {
    await sessaoComo('Supervisor');
    await abrirMosaico('Equipamentos', 'Equipamentos');
    await escrever('#eq-texto', MARCA); await clicar(page.locator('button[aria-label="Pesquisar"]'), 'Pesquisar');
    await clicar(page.locator(`a.intervencao[href$="${ctx.equipId}"]`), 'equipamento de teste');
    await clicar(page.locator('a.btn', { hasText: 'Editar equipamento' }), 'Editar equipamento');
    await clicar(botao(page, 'Seguinte'), 'Seguinte 1'); await clicar(botao(page, 'Seguinte'), 'Seguinte 2');
    await page.locator('#eq-obsoleto').check();
    await clicar(botao(page, 'Guardar equipamento'), 'Guardar equipamento');
    await page.getByText('Marcar como obsoleto?').first().waitFor({ timeout: 20000 });
    await clicar(botao(page, 'Confirmar e guardar'), 'Confirmar e guardar');
    await page.waitForURL(/equipamentos\/[^/]+$/, { timeout: 30000 });
    await page.getByText(/Obsoleto/i).first().waitFor({ timeout: 20000 });
  });
  await passo('Equipa: criar operador e receber o PIN provisório de 4 números', async () => {
    await abrirMosaico('Equipa', 'Equipa');
    const codigo = `e2e.n${RUN}`; criados.utilizadores.push(codigo);
    await escrever('#nome', `Teste E2E Novo ${RUN}`); await escrever('#codigo', codigo); await escrever('#tecno', '99005');
    await clicar(botao(page, 'Criar e dar PIN'), 'Criar e dar PIN');
    const pin = await page.locator('.pin-grande').first().innerText({ timeout: 20000 });
    afirma(/^\d{4}$/.test(pin.trim()), `o PIN provisório devia ter 4 números (tem ${pin.trim().length})`);
    ctx.pinNovo = pin.trim();
    await clicar(botao(page, 'Já anotei, fechar'), 'Já anotei, fechar');
  });
  await passo('Equipa: dados repetidos e códigos inválidos são recusados', async () => {
    await escrever('#nome', 'X'); await escrever('#codigo', 'a b'); await clicar(botao(page, 'Criar e dar PIN'), 'Criar (inválido)');
    afirma((await page.locator('p.campo-erro').allInnerTexts()).join('').length > 0, 'sem aviso de validação');
  });
  await passo('O operador novo entra com o PIN provisório e é obrigado a definir um PIN de 4 números', async () => {
    await paraInicio().catch(() => {}); await sairApp(); await entrarApp(ctx.pinNovo);
    await page.locator('#pin-atual').waitFor({ timeout: 20000 });
    afirma((await page.locator('nav.navegacao').count()) === 0, 'a navegação devia estar escondida até mudar o PIN');
    await escrever('#pin-atual', ctx.pinNovo);
    await escrever('#pin-novo', '1234'); await escrever('#pin-repetir', '1234'); await clicar(botao(page, 'Guardar PIN'), 'Guardar PIN (fraco)');
    await page.locator('p.campo-erro').filter({ hasText: /fácil de adivinhar/ }).first().waitFor({ timeout: 15000 });
    await escrever('#pin-novo', '5927'); await escrever('#pin-repetir', '5928'); await clicar(botao(page, 'Guardar PIN'), 'Guardar PIN (diferentes)');
    await page.locator('p.campo-erro').filter({ hasText: /não são iguais/ }).first().waitFor({ timeout: 15000 });
    const maxlen = await page.locator('#pin-novo').getAttribute('maxlength');
    afirma(maxlen === '4', `o campo do PIN novo aceita ${maxlen} caracteres em vez de 4`);
  });
  await passo('Define o PIN 5927, entra no início e volta a entrar com ele', async () => {
    await escrever('#pin-repetir', '5927'); await clicar(botao(page, 'Guardar PIN'), 'Guardar PIN');
    await page.locator('.hero-texto').waitFor({ timeout: 30000 });
    await sairApp(); await entrarApp('5927');
    await page.locator('.hero-texto').waitFor({ timeout: 30000 }); await sairApp();
  });
});

// Repõe a periodicidade "Mensal" como estava, mesmo que os circuitos tenham falhado.
if (ctx.mensal) {
  const r = await http((await entrarApi(PIN_S)).token, 'PUT', `/gestao/periodicidades/${encodeURIComponent(ctx.mensal.id)}`, { unidade: ctx.mensal.unidade || 'nenhum', intervalo: ctx.mensal.intervalo, aviso: ctx.mensal.aviso, concorrencia: (await http((await entrarApi(PIN_S)).token, 'GET', '/gestao/periodicidades')).json?.find((x) => x.id === ctx.mensal.id)?.concorrencia });
  console.log(`Periodicidade Mensal reposta (${r.estado}).`);
  resultados.push({ circuito: 'limpeza', nome: 'Repor a periodicidade "Mensal" original', ok: r.estado === 200, ms: 0, erro: `PUT devolveu ${r.estado}`, chamadas: [], consola: [], avisos: [] });
}
await browser?.close().catch(() => {});

// ------------------------------------------------------------------ relatório
const falhas = resultados.filter((r) => !r.ok);
const circuitos = [...new Set(resultados.map((r) => r.circuito))];
const md = [`# Circuitos funcionais ${RUN}`, '',
  `Duração: ${Math.round((Date.now() - T0) / 1000)} s · passos: ${resultados.length} · falhas: ${falhas.length}`, '',
  '| Circuito | Passos | Falhas |', '|---|---|---|',
  ...circuitos.map((c) => { const p = resultados.filter((r) => r.circuito === c); return `| ${c} | ${p.length} | ${p.filter((r) => !r.ok).length} |`; }), ''];
for (const c of circuitos) {
  md.push(`## ${c}`, '', '| Passo | Resultado | ms | Pedidos à API | Consola / avisos |', '|---|---|---|---|---|');
  for (const r of resultados.filter((x) => x.circuito === c)) {
    const ch = r.chamadas.map((x) => `${x.m} ${x.r} → ${x.s}`).join('<br>') || '—';
    const ex = [...r.consola.map((t) => `consola: ${t}`), ...r.avisos].map((t) => t.replace(/\|/g, '/')).join('<br>') || '—';
    md.push(`| ${r.nome} | ${r.ok ? 'ok' : 'FALHA: ' + String(r.erro).replace(/\|/g, '/')} | ${r.ms} | ${ch} | ${ex} |`);
  }
  md.push('');
}
fs.writeFileSync(path.join(PASTA, 'relatorio.md'), md.join('\n'));
fs.writeFileSync(path.join(PASTA, 'resultados.json'), JSON.stringify(resultados, null, 2));
fs.writeFileSync(path.join(PASTA, 'criados.json'), JSON.stringify(criados, null, 2));
fs.writeFileSync(path.join(PASTA, 'fichas-criadas.json'), JSON.stringify({ marca: MARCA, ids: [...new Set(criados.fichas)] }, null, 2));
console.log(`\n${resultados.length - falhas.length}/${resultados.length} passos ok. Relatório: ${PASTA}`);
process.exit(falhas.length ? 1 : 0);
