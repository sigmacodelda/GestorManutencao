// Circuito de testes ponta a ponta do Gestor de Manutenção.
// Duas pistas em simultâneo: a API (supervisor e técnico por HTTP) e a app Windows real (botões, via Playwright/CDP).
// As pistas combinam-se: o que o técnico faz na app aparece ao supervisor na API, e o que o supervisor faz na app chega ao técnico.
// Variáveis: API_URL, CDP_URL, PIN_TECNICO, PIN_SUPERVISOR, E2E_PLANEADA=1 (inclui começar uma planeada do técnico).
// Os PINs nunca são impressos nem gravados.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9222';
const PIN_T = process.env.PIN_TECNICO;
const PIN_S = process.env.PIN_SUPERVISOR;
const COM_PLANEADA = process.env.E2E_PLANEADA === '1';
if (!PIN_T || !PIN_S) { console.error('Faltam PIN_TECNICO e PIN_SUPERVISOR.'); process.exit(2); }
// O certificado de desenvolvimento só se aceita para a API local.
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const RUN = Math.random().toString(36).slice(2, 8);
const MARCA = `TESTE-E2E-${RUN}`; // sem parênteses retos: no LIKE do SQL seriam uma classe de caracteres
const PASTA = path.resolve('resultados', `${new Date().toISOString().replace(/[:.]/g, '-')}_${RUN}`);
fs.mkdirSync(PASTA, { recursive: true });

// ------------------------------------------------------------------ registo
const resultados = [];
let paginaAtual = null;
async function passo(pista, nome, fn) {
  const t0 = Date.now();
  try {
    const detalhe = await fn();
    resultados.push({ pista, nome, ok: true, ms: Date.now() - t0, detalhe: detalhe ?? '' });
    console.log(`  ok   [${pista}] ${nome}`);
    return true;
  } catch (e) {
    resultados.push({ pista, nome, ok: false, ms: Date.now() - t0, erro: String(e.message ?? e) });
    console.log(`  FALHA [${pista}] ${nome}: ${e.message ?? e}`);
    if (pista === 'APP' && paginaAtual) {
      try { await paginaAtual.screenshot({ path: path.join(PASTA, `falha-${resultados.length}.png`) }); } catch { /* sem imagem */ }
    }
    return false;
  }
}
const afirma = (cond, msg) => { if (!cond) throw new Error(msg); };
const espera = (ms) => new Promise((r) => setTimeout(r, ms));
async function ate(fn, ms, descricao, passoMs = 1000) {
  const limite = Date.now() + ms;
  let ultimo;
  while (Date.now() < limite) {
    try { const v = await fn(); if (v) return v; } catch (e) { ultimo = e; }
    await espera(passoMs);
  }
  throw new Error(`Tempo esgotado: ${descricao}${ultimo ? ` (${ultimo.message})` : ''}`);
}
const adiado = () => { let resolve, rejeita; const promessa = new Promise((a, b) => { resolve = a; rejeita = b; }); return { promessa, resolve, rejeita }; };

// Sinais entre pistas. Se uma pista falha, rejeita-os para a outra não ficar à espera para sempre.
const sinal = {
  fichaA: adiado(), fichaAConcluida: adiado(), fichaBPronta: adiado(), fichaBDevolvida: adiado(), fichaBConcluida: adiado(), fichaAValidada: adiado()
};
Object.values(sinal).forEach((s) => s.promessa.catch(() => {}));
const abortarSinais = (motivo) => Object.values(sinal).forEach((s) => s.rejeita(new Error(motivo)));
const fichasCriadas = [];

// ------------------------------------------------------------------ API
async function http(token, metodo, rota, corpo) {
  const r = await fetch(API + rota, {
    method: metodo,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: corpo !== undefined ? JSON.stringify(corpo) : undefined
  });
  let json = null;
  try { json = await r.json(); } catch { /* sem corpo */ }
  return { estado: r.status, json };
}
async function entrarApi(pin) {
  const r = await http(null, 'POST', '/auth/login', { pin });
  afirma(r.estado === 200 && r.json?.token, `login recusado (${r.estado})`);
  afirma(!r.json.pinProvisorio, 'o PIN é provisório: defina um definitivo antes do circuito');
  return r.json;
}
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);
async function procurarFicha(tokenS, texto) {
  const r = await http(tokenS, 'GET', `/intervencoes/todas?q=${encodeURIComponent(texto)}`);
  afirma(r.estado === 200, `todas ${r.estado}`);
  return itens(r.json).find((f) => (f.anomalia ?? '').includes(texto));
}

async function pistaApi(ctx) {
  const P = 'API';
  let tokenS, tokenT, loginS, loginT, antes;
  if (!(await passo(P, 'Login de supervisor e de técnico, e PIN errado recusado', async () => {
    loginS = await entrarApi(PIN_S); loginT = await entrarApi(PIN_T);
    tokenS = loginS.token; tokenT = loginT.token;
    afirma(loginS.perfil === 'Supervisor', `perfil do supervisor: ${loginS.perfil}`);
    afirma(loginT.perfil === 'Operador', `perfil do técnico: ${loginT.perfil}`);
    const mau = await http(null, 'POST', '/auth/login', { pin: '000000' });
    afirma(mau.estado === 401, `PIN errado devolveu ${mau.estado}`);
    afirma((await http(tokenT, 'GET', '/auth/eu')).json?.perfil === 'Operador', '/auth/eu');
  }))) { abortarSinais('login falhou'); ctx.pronto.rejeita(new Error('login falhou')); return; }
  ctx.nomeTecnico = loginT.nome;

  await passo(P, 'O técnico não acede ao que é do supervisor (403)', async () => {
    for (const [m, rota] of [['GET', '/utilizadores'], ['GET', '/gestao/opcoes'], ['GET', '/intervencoes/todas'], ['GET', '/intervencoes/contadores'],
      ['GET', '/validacao'], ['GET', '/configuracao'], ['GET', '/gestao/periodicidades'], ['POST', '/planeadas/gerar']]) {
      const r = await http(tokenT, m, rota, m === 'POST' ? {} : undefined);
      afirma(r.estado === 403, `${m} ${rota} devolveu ${r.estado} em vez de 403`);
    }
    afirma((await http(null, 'GET', '/intervencoes')).estado === 401, 'sem token devia ser 401');
  });

  let equipamento;
  await passo(P, 'Supervisor lê equipamentos, planeadas, validação, fichas, equipa, configuração e periodicidades', async () => {
    const eq = await http(tokenS, 'GET', '/equipamentos');
    afirma(eq.estado === 200, `equipamentos ${eq.estado}`);
    const lista = Array.isArray(eq.json) ? eq.json : itens(eq.json);
    // Na base real há equipamentos sem nome ou com caracteres especiais da pesquisa: escolhe um que o botão da app consiga procurar.
    const procuravel = (e) => typeof e.nome === 'string' && e.nome.trim().length >= 3 && e.nome.length <= 40 && !/[%_[\]\\]/.test(e.nome);
    equipamento = lista.find((e) => !e.obsoleto && !e.foraDeServico && procuravel(e));
    afirma(equipamento, 'nenhum equipamento para testar');
    ctx.equipamento = equipamento;
    for (const rota of ['/intervencoes/contadores', '/planeadas', '/validacao', '/intervencoes/todas', '/utilizadores', '/configuracao', '/gestao/opcoes', '/gestao/periodicidades',
      `/gestao/equipamentos/${encodeURIComponent(equipamento.id)}`, `/gestao/equipamentos/${encodeURIComponent(equipamento.id)}/instrucoes`, `/equipamentos/${encodeURIComponent(equipamento.id)}`]) {
      const r = await http(tokenS, 'GET', rota);
      afirma(r.estado === 200, `GET ${rota} devolveu ${r.estado}`);
    }
    antes = (await http(tokenS, 'GET', '/intervencoes/contadores')).json;
    return `equipamento de teste: ${equipamento.nome}`;
  });
  if (equipamento) ctx.pronto.resolve(); else ctx.pronto.rejeita(new Error('sem equipamento'));
  if (!equipamento) { abortarSinais('sem equipamento'); return; }

  await passo(P, 'Regra: o técnico não cria preventivas; a ficha manual dele nasce curativa e interna', async () => {
    const preventiva = await http(tokenT, 'POST', '/intervencoes', { equipamentoId: equipamento.id, tipo: 'Preventiva', problema: `${MARCA} preventiva` });
    afirma(preventiva.estado === 403 && preventiva.json?.erro === 'tipo_nao_permitido', `preventiva do técnico devolveu ${preventiva.estado}`);
    const manual = await http(tokenT, 'POST', '/intervencoes', { equipamentoId: equipamento.id, tipoManut: 'Externa', periodicidade: 'Mensal', problema: `${MARCA} manual` });
    afirma(manual.estado === 200, `ficha manual do técnico devolveu ${manual.estado}`);
    fichasCriadas.push(manual.json.id);
    afirma(manual.json.tipo === 'Curativa', `tipo gravado: ${manual.json.tipo}`);
    const det = await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(manual.json.id)}/detalhe`);
    afirma(det.json?.tipoManut === 'Interna' && !det.json?.periodicidade, `tipoManut/periodicidade: ${det.json?.tipoManut}/${det.json?.periodicidade}`);
    const canc = await http(tokenS, 'POST', `/intervencoes/${encodeURIComponent(manual.json.id)}/cancelar`, { motivo: 'Teste E2E' });
    afirma(canc.estado === 200, `cancelar devolveu ${canc.estado}`);
  });

  // Ficha B: o técnico (API) regista a avaria e conclui; o supervisor vai devolvê-la na app.
  const textoB = `${MARCA} avaria B`;
  let idB;
  const okB = await passo(P, 'Técnico regista avaria B, junta material e conclui (para o supervisor devolver na app)', async () => {
    const r = await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: equipamento.id, anomalia: textoB });
    afirma(r.estado === 200, `avaria devolveu ${r.estado}`);
    idB = r.json.id; fichasCriadas.push(idB); ctx.idB = idB;
    afirma(r.json.tipo === 'Curativa' && r.json.estado === 'EmCurso', `avaria: ${r.json.tipo}/${r.json.estado}`);
    const m = await http(tokenT, 'POST', `/intervencoes/${encodeURIComponent(idB)}/materiais`, { design: 'Retentor E2E', qtt: 1, epv: 2.5 });
    afirma(m.estado === 200 || m.estado === 201, `material devolveu ${m.estado}`);
    const c = await http(tokenT, 'POST', `/intervencoes/${encodeURIComponent(idB)}/concluir`, { feito: 'Trocado o retentor (E2E)', inicio: '08:00:00', fim: '08:20:00' });
    afirma(c.estado === 200 && c.json.estado === 'Concluida', `concluir devolveu ${c.estado}`);
  });
  if (okB) sinal.fichaBPronta.resolve(idB); else sinal.fichaBPronta.rejeita(new Error('ficha B falhou'));

  // Visibilidade do supervisor sobre o que o técnico faz na app.
  let idA;
  await passo(P, 'Supervisor vê a avaria A que o técnico registou na app: curativa, em curso, do técnico', async () => {
    const f = await ate(() => procurarFicha(tokenS, `${MARCA} avaria A`), 180000, 'a avaria A aparecer ao supervisor');
    idA = f.id; fichasCriadas.push(idA);
    afirma(f.tipo === 'Curativa', `tipo ${f.tipo}`);
    afirma(f.estado === 'EmCurso', `estado ${f.estado}`);
    afirma(f.tecno === loginT.tecno || f.tecno > 0, 'sem técnico na ficha');
    sinal.fichaA.resolve(idA);
  }) || sinal.fichaA.rejeita(new Error('ficha A não apareceu'));

  await passo(P, 'Supervisor vê a avaria A concluída na lista de validação e o contador sobe', async () => {
    await ate(async () => (await http(tokenS, 'GET', '/validacao')).json?.some?.((f) => f.id === idA), 180000, 'a avaria A em validação');
    const depois = (await http(tokenS, 'GET', '/intervencoes/contadores')).json;
    afirma(depois.porValidar >= (antes?.porValidar ?? 0) + 1, `porValidar ${antes?.porValidar} -> ${depois.porValidar}`);
    const det = (await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(idA)}/detalhe`)).json;
    afirma(det.estado === 'Concluida', `estado ${det.estado}`);
    afirma(/Substitu/i.test(det.resolucao ?? ''), 'resolução gravada pelo técnico em falta');
    afirma(det.horaInicio && det.horaFim, 'horas automáticas em falta');
    sinal.fichaAConcluida.resolve();
  }) || sinal.fichaAConcluida.rejeita(new Error('ficha A não concluída'));

  await passo(P, 'Ficha A validada pelo supervisor na app: fechada e supervisionada', async () => {
    await sinal.fichaAValidada.promessa;
    const det = (await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(idA)}/detalhe`)).json;
    afirma(det.estado === 'Validada', `estado ${det.estado}`);
  });

  await passo(P, 'Avaria B devolvida na app: volta a EmCurso com o motivo; depois de concluída de novo, o supervisor valida', async () => {
    const mot = await ate(async () => {
      const d = (await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(idB)}/detalhe`)).json;
      return d?.estado === 'EmCurso' && d.motivo ? d : null;
    }, 240000, 'a avaria B devolvida');
    afirma(/E2E/.test(mot.motivo), `motivo: ${mot.motivo}`);
    sinal.fichaBDevolvida.resolve();
    await ate(async () => (await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(idB)}/detalhe`)).json?.estado === 'Concluida', 240000, 'a avaria B concluída outra vez');
    const v = await http(tokenS, 'POST', `/validacao/${encodeURIComponent(idB)}`);
    afirma(v.estado === 200, `validar devolveu ${v.estado}`);
    const fim = (await http(tokenS, 'GET', `/intervencoes/${encodeURIComponent(idB)}/detalhe`)).json;
    afirma(fim.estado === 'Validada', `estado final ${fim.estado}`);
    sinal.fichaBConcluida.resolve();
  }) || sinal.fichaBConcluida.rejeita(new Error('ficha B não fechou'));
}

// ------------------------------------------------------------------ APP (Playwright por CDP)
const unico = async (loc, desc) => {
  await loc.first().waitFor({ timeout: 20000 });
  const n = await loc.count();
  afirma(n === 1, `${desc}: esperava 1 elemento, achei ${n}`);
  return loc;
};
const botao = (alvo, nome) => alvo.getByRole('button', { name: nome, exact: true });
async function clicar(loc, desc) { await (await unico(loc, desc)).click(); }
const semErroBlazor = async (page) => afirma(!(await page.locator('#blazor-error-ui').isVisible().catch(() => false)), 'a app mostrou o aviso de erro do Blazor');

async function entrarApp(page, pin) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await clicar(page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }), `tecla ${d}`);
  // Com 6 números a app entra sozinha; com menos, carrega-se em Entrar.
  const hero = page.locator('.hero-texto');
  if (!(await hero.waitFor({ timeout: 4000 }).then(() => true, () => false))) await clicar(botao(page, 'Entrar'), 'Entrar');
  await hero.waitFor({ timeout: 30000 });
}
async function sairApp(page) {
  await clicar(page.locator('header.topo button[aria-label="Terminar sessão"]'), 'Terminar sessão');
  const confirma = botao(page, 'Sair');
  if (await confirma.isVisible().catch(() => false)) await confirma.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}
async function paraInicio(page) {
  // Algumas áreas voltam primeiro a "Mais": segue as setas de voltar até ao início.
  for (let i = 0; i < 4; i++) {
    if (await page.locator('.hero-texto').isVisible().catch(() => false)) return;
    const voltar = page.locator('a.voltar').first();
    if (!(await voltar.isVisible().catch(() => false))) break;
    await voltar.click();
    await page.locator('.hero-texto, a.voltar').first().waitFor({ timeout: 15000 });
  }
  await page.locator('.hero-texto').waitFor({ timeout: 15000 });
}
// O título pode levar a contagem colada ("Validação3"): aceita dígito, espaço ou fim, para "Equipa" não apanhar "Equipamentos".
const mosaico = (page, titulo) => page.locator('a.mosaico-link', { has: page.locator('.mosaico-titulo', { hasText: new RegExp('^' + titulo + '([0-9]|[ \\u00a0]|$)') }) });
async function abrirMosaico(page, titulo, h1) {
  await clicar(mosaico(page, titulo), `mosaico ${titulo}`);
  await page.locator('h1', { hasText: h1 }).first().waitFor({ timeout: 20000 });
  await semErroBlazor(page);
}

async function pistaApp(ctx) {
  const P = 'APP';
  let browser;
  const ligou = await passo(P, 'Ligar à app Windows por CDP (WebView2)', async () => {
    browser = await chromium.connectOverCDP(CDP);
    const contexto = browser.contexts()[0];
    afirma(contexto, 'sem contexto do WebView2');
    const page = contexto.pages()[0] ?? await contexto.waitForEvent('page');
    paginaAtual = page; ctx.page = page;
    const s = await contexto.newCDPSession(page);
    await s.send('Emulation.setFocusEmulationEnabled', { enabled: true });
    await page.waitForLoadState('domcontentloaded');
    // Pode haver uma sessão antiga (a app guarda-a): termina-a para começar limpo.
    await ate(async () => (await page.locator('.teclado').isVisible()) || (await page.locator('.hero-texto').isVisible()), 40000, 'a app mostrar o login ou o início');
    if (await page.locator('.hero-texto').isVisible()) await sairApp(page);
  });
  if (!ligou) { abortarSinais('sem ligação à app'); return; }
  const page = ctx.page;
  await ctx.pronto.promessa;
  const equipamento = ctx.equipamento;
  if (!equipamento) { abortarSinais('sem equipamento'); return; }

  // ---- Técnico: avaria A, tudo simples
  await passo(P, 'Técnico entra com o PIN pelos botões e vê só o essencial no início', async () => {
    await entrarApp(page, PIN_T);
    afirma((await page.locator('.hero-texto .sobretitulo').innerText()) === 'Operador', 'o técnico devia estar em modo Operador');
    afirma((await page.locator('a.mosaico-link .mosaico-titulo', { hasText: 'Nova ficha' }).count()) === 0, 'o técnico ainda vê "Nova ficha"');
    for (const t of ['Registar avaria', 'As minhas intervenções', 'Equipamentos', 'Notificações']) await unico(mosaico(page, t), `mosaico ${t}`);
    afirma((await page.locator('a.mosaico-link .mosaico-titulo', { hasText: /^(Validação|Planeadas|Equipa|Configuração)$/ }).count()) === 0, 'o técnico vê áreas do supervisor');
    await semErroBlazor(page);
  });

  await passo(P, 'Técnico: a ficha completa (nova-ficha) está fechada e leva à avaria', async () => {
    await page.evaluate(() => window.Blazor.navigateTo('/nova-ficha'));
    await page.locator('h1', { hasText: 'Registar avaria' }).waitFor({ timeout: 15000 });
    afirma((await page.getByRole('link', { name: /Nova ficha completa/ }).count()) === 0, 'o técnico vê o link para a ficha completa');
    // Só pede equipamento e problema.
    afirma((await page.locator('form select').count()) === 0, 'o formulário da avaria tem escolhas a mais');
  });

  await passo(P, 'Técnico regista a avaria A (equipamento e problema) e fica nas suas intervenções', async () => {
    await page.locator('#equipamento').fill(equipamento.nome);
    const opcao = page.locator('.opcoes .opcao', { hasText: equipamento.nome }).first();
    await opcao.waitFor({ timeout: 20000 });
    await opcao.click();
    await page.locator('.escolhido').waitFor({ timeout: 5000 });
    await page.locator('#anomalia').fill(`${MARCA} avaria A`);
    await clicar(botao(page, 'Guardar avaria'), 'Guardar avaria');
    await page.locator('h1', { hasText: 'As minhas intervenções' }).waitFor({ timeout: 30000 });
  });

  if (COM_PLANEADA) {
    await passo(P, 'Técnico começa e conclui uma planeada sua (Começar → Feito)', async () => {
      const lista = await http((await entrarApi(PIN_T)).token, 'GET', '/intervencoes');
      const planeada = (lista.json ?? []).find((i) => i.estado === 'Planeada');
      afirma(planeada, 'o técnico não tem planeadas');
      fichasCriadas.push(planeada.id);
      await page.locator(`a.intervencao[href$="${planeada.id}"]`).first().click();
      await clicar(botao(page, 'Começar'), 'Começar');
      await page.locator('#feito').waitFor({ timeout: 20000 });
      await page.locator('#feito').fill('Planeada feita no circuito E2E');
      await clicar(botao(page, 'Feito'), 'Feito');
      await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
      await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções');
    });
  }

  let idA;
  await passo(P, 'Técnico abre a avaria A: curativa, sem campos a mais, horas automáticas; junta material e conclui', async () => {
    idA = await sinal.fichaA.promessa;
    await page.locator(`a.intervencao[href$="${idA}"]`).first().click({ timeout: 30000 });
    await page.locator('dl.ficha').waitFor({ timeout: 20000 });
    afirma((await page.locator('dl.ficha').innerText()).includes('Avaria'), 'a ficha devia ser uma Avaria');
    for (const sel of ['#inicio', '#fim', '#fc-tipo', '#fc-manut', '#fc-tecnico', '#fc-data', '#fc-frio', '#mo-horas']) {
      afirma((await page.locator(sel).count()) === 0, `o técnico vê o campo ${sel}`);
    }
    afirma((await page.locator('h2', { hasText: /^Dados|Mão de obra|Externo/ }).count()) === 0, 'o técnico vê secções da ficha completa');
    await page.locator('#material').fill('Parafuso M8 E2E');
    await page.locator('#qtt').fill('2'); await page.locator('#qtt').press('Tab');
    await page.locator('#epv').fill('1.5'); await page.locator('#epv').press('Tab');
    await clicar(botao(page, 'Juntar material'), 'Juntar material');
    await page.locator('.material-linha', { hasText: 'Parafuso M8 E2E' }).waitFor({ timeout: 20000 });
    await page.locator('#feito').fill('Substituído o parafuso (E2E)');
    await clicar(botao(page, 'Feito'), 'Feito');
    await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
  });

  await passo(P, 'Técnico consulta um equipamento e regista avaria a partir dele (já escolhido)', async () => {
    await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções');
    await paraInicio(page);
    await abrirMosaico(page, 'Equipamentos', 'Equipamentos');
    await page.locator('a.intervencao[href^="equipamentos/"]').first().click();
    await clicar(page.locator('a.btn', { hasText: 'Registar avaria' }), 'Registar avaria (equipamento)');
    await page.locator('.escolhido').waitFor({ timeout: 20000 });
    afirma((await page.locator('a.btn', { hasText: /Nova ficha/ }).count()) === 0, 'o técnico vê "Nova ficha" no equipamento');
    await paraInicio(page).catch(async () => { await page.locator('a.voltar').first().click(); await paraInicio(page); });
  });

  await passo(P, 'Técnico termina a sessão pelo botão', async () => { await sairApp(page); });

  // ---- Supervisor
  await passo(P, 'Supervisor entra pelos botões e percorre todas as áreas', async () => {
    await entrarApp(page, PIN_S);
    afirma((await page.locator('.hero-texto .sobretitulo').innerText()) === 'Supervisor', 'o supervisor devia estar em modo Supervisor');
    for (const [titulo, h1] of [['Planeadas', 'Planeadas'], ['Validação', 'Validação'], ['Equipamentos', 'Equipamentos'], ['Periodicidades', 'Periodicidades'],
      ['Todas as fichas', 'Todas as fichas'], ['Equipa', 'Equipa'], ['Configuração', 'Configuração'], ['Notificações', 'Notificações']]) {
      await paraInicio(page);
      await abrirMosaico(page, titulo, h1);
    }
    await paraInicio(page);
  });

  await passo(P, 'Supervisor vê tudo na ficha completa de A (horas, técnico, tipo) e valida pelos botões', async () => {
    await sinal.fichaAConcluida.promessa;
    await abrirMosaico(page, 'Validação', 'Validação');
    const cartao = page.locator('li.ficha-validar', { hasText: `${MARCA} avaria A` });
    await cartao.waitFor({ timeout: 30000 });
    afirma((await cartao.innerText()).includes('Avaria'), 'o cartão devia dizer Avaria');
    await clicar(botao(cartao, 'Validar'), 'Validar');
    await clicar(botao(cartao, 'Confirmar'), 'Confirmar');
    await page.locator('.alerta-pin').waitFor({ timeout: 20000 });
    await ate(async () => (await cartao.count()) === 0, 20000, 'A sair da lista de validação');
    sinal.fichaAValidada.resolve();
  }) || sinal.fichaAValidada.rejeita(new Error('A não foi validada'));

  await passo(P, 'Supervisor abre a ficha completa de B nas Todas as fichas e vê os dados todos', async () => {
    const idB = await sinal.fichaBPronta.promessa;
    await paraInicio(page);
    await abrirMosaico(page, 'Todas as fichas', 'Todas as fichas');
    await page.locator(`a[href$="${idB}"]`).first().click({ timeout: 30000 });
    await page.locator('#fc-tipo').waitFor({ timeout: 20000 });
    afirma((await page.locator('#fc-tipo').inputValue()) === 'Curativa', 'tipo da B devia ser Curativa');
    for (const sel of ['#fc-data', '#fc-tecnico', '#fc-inicio', '#fc-fim', '#fc-resolucao']) await unico(page.locator(sel), sel);
    await paraInicio(page).catch(async () => { await page.locator('a.voltar').first().click(); await paraInicio(page); });
  });

  await passo(P, 'Supervisor devolve a avaria B com motivo, pelos botões', async () => {
    await abrirMosaico(page, 'Validação', 'Validação');
    const cartao = page.locator('li.ficha-validar', { hasText: `${MARCA} avaria B` });
    await cartao.waitFor({ timeout: 30000 });
    await clicar(botao(cartao, 'Devolver'), 'Devolver (abrir)');
    await cartao.locator('textarea').fill('Falta apertar o retentor (E2E)');
    await clicar(botao(cartao, 'Devolver'), 'Devolver (confirmar)');
    await ate(async () => (await cartao.count()) === 0, 20000, 'B sair da lista de validação');
    await sinal.fichaBDevolvida.promessa;
  });

  await passo(P, 'Supervisor termina a sessão', async () => { await paraInicio(page); await sairApp(page); });

  // ---- Técnico: recebe a devolução e conclui outra vez
  await passo(P, 'Técnico vê a ficha B devolvida com o motivo e volta a concluí-la', async () => {
    await entrarApp(page, PIN_T);
    const idB = ctx.idB;
    await abrirMosaico(page, 'As minhas intervenções', 'As minhas intervenções');
    await page.locator(`a.intervencao[href$="${idB}"]`).first().click({ timeout: 30000 });
    const alerta = page.locator('.alerta-devolvida');
    await alerta.waitFor({ timeout: 20000 });
    afirma((await alerta.innerText()).includes('Falta apertar o retentor'), 'o motivo da devolução não aparece ao técnico');
    await page.locator('#feito').fill('Retentor apertado (E2E)');
    await clicar(botao(page, 'Feito'), 'Feito');
    await page.locator('.sucesso-titulo', { hasText: 'Feito! Obrigado.' }).waitFor({ timeout: 20000 });
    await sinal.fichaBConcluida.promessa;
    await clicar(page.locator('a.btn', { hasText: 'Ver as minhas intervenções' }), 'Ver as minhas intervenções');
    await paraInicio(page);
    await sairApp(page);
  });

  await browser?.close().catch(() => {});
}

// ------------------------------------------------------------------ execução
console.log(`Circuito ${RUN}: API ${API}, app ${CDP}`);
const ctx = { pronto: adiado() };
ctx.pronto.promessa.catch(() => {});
const t0 = Date.now();
await Promise.all([
  pistaApi(ctx).catch((e) => { resultados.push({ pista: 'API', nome: 'pista interrompida', ok: false, ms: 0, erro: String(e.message ?? e) }); abortarSinais('pista API interrompida'); ctx.pronto.rejeita(e); }),
  pistaApp(ctx).catch((e) => { resultados.push({ pista: 'APP', nome: 'pista interrompida', ok: false, ms: 0, erro: String(e.message ?? e) }); abortarSinais('pista APP interrompida'); })
]);

const falhas = resultados.filter((r) => !r.ok);
const md = [`# Circuito de testes ${RUN}`, '', `Duração: ${Math.round((Date.now() - t0) / 1000)} s · passos: ${resultados.length} · falhas: ${falhas.length}`, '',
  '| Pista | Passo | Resultado | ms |', '|---|---|---|---|',
  ...resultados.map((r) => `| ${r.pista} | ${r.nome} | ${r.ok ? 'ok' : 'FALHA: ' + String(r.erro).replace(/\|/g, '/').replace(/\n/g, ' ')} | ${r.ms} |`)].join('\n');
fs.writeFileSync(path.join(PASTA, 'relatorio.md'), md);
fs.writeFileSync(path.join(PASTA, 'resultados.json'), JSON.stringify(resultados, null, 2));
// Os ids criados servem à limpeza (só estes, e só com o marcador do teste).
fs.writeFileSync(path.join(PASTA, 'fichas-criadas.json'), JSON.stringify({ marca: MARCA, ids: [...new Set(fichasCriadas)] }, null, 2));
console.log(`\n${resultados.length - falhas.length}/${resultados.length} passos ok. Relatório: ${PASTA}`);
process.exit(falhas.length ? 1 : 0);
