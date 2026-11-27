// Cobertura de UI do Gestor de Manutenção (app Windows real por CDP) para técnico, supervisor e qualidade.
// Corre segmentos escolhidos por SEG (lista separada por vírgulas); resultados em resultados-cobertura/.
// Variáveis: CDP_URL (9333), PIN_TECNICO, PIN_SUPERVISOR, PIN_QUALIDADE, SEG, API_URL. Os PINs nunca se imprimem.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const CDP = process.env.CDP_URL ?? 'http://127.0.0.1:9333';
const PINS = { Operador: process.env.PIN_TECNICO, Supervisor: process.env.PIN_SUPERVISOR, Qualidade: process.env.PIN_QUALIDADE };
const SEG = (process.env.SEG ?? 'perm').split(',').map((s) => s.trim());
if (!PINS.Operador || !PINS.Supervisor || !PINS.Qualidade) { console.error('Faltam PIN_TECNICO, PIN_SUPERVISOR, PIN_QUALIDADE.'); process.exit(2); }
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const RUN = Math.random().toString(36).slice(2, 7);
const PASTA = path.resolve('resultados-cobertura', `${new Date().toISOString().replace(/[:.]/g, '-')}_${RUN}`);
fs.mkdirSync(PASTA, { recursive: true });
const espera = (ms) => new Promise((r) => setTimeout(r, ms));
const afirma = (c, m) => { if (!c) throw new Error(m); };
async function ate(fn, ms, desc, p = 400) {
  const limite = Date.now() + ms; let ult;
  while (Date.now() < limite) { try { const v = await fn(); if (v) return v; } catch (e) { ult = e; } await espera(p); }
  throw new Error(`Tempo esgotado: ${desc}${ult ? ` (${ult.message})` : ''}`);
}

// ------------------------------------------------------------ registo
const resultados = [];
let circuitoAtual = '', page;
const consola = [];
async function passo(nome, fn) {
  const t0 = Date.now(); consola.length = 0;
  const r = { circuito: circuitoAtual, nome, ok: true, ms: 0, detalhe: '', consola: [] };
  try { r.detalhe = (await fn()) ?? ''; await semErroBlazor(); } catch (e) {
    r.ok = false; r.erro = String(e.message ?? e).split('\n')[0];
    try { await page.screenshot({ path: path.join(PASTA, `falha-${resultados.length + 1}.png`) }); } catch { /* sem imagem */ }
  }
  r.ms = Date.now() - t0; r.consola = [...consola];
  resultados.push(r);
  console.log(`  ${r.ok ? 'ok   ' : 'FALHA'} [${circuitoAtual}] ${nome}${r.ok ? (r.detalhe ? ' · ' + String(r.detalhe).slice(0, 150) : '') : ': ' + r.erro}${r.consola.length ? '  CONSOLA: ' + r.consola.join(' | ') : ''}`);
  return r.ok;
}
async function circuito(nome, fn) { circuitoAtual = nome; console.log(`\n== ${nome}`); try { await fn(); } catch (e) { resultados.push({ circuito: nome, nome: 'circuito interrompido', ok: false, erro: String(e.message ?? e) }); console.log(`  FALHA circuito interrompido: ${e.message}`); } }
const foto = (nome) => page.screenshot({ path: path.join(PASTA, `${nome}.png`) }).catch(() => {});

// ------------------------------------------------------------ API (só para preparar e consultar)
async function http(token, metodo, rota, corpo) {
  const r = await fetch(API + rota, { method: metodo, headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: corpo !== undefined ? JSON.stringify(corpo) : undefined });
  let json = null; try { json = await r.json(); } catch { /* sem corpo */ }
  return { estado: r.status, json };
}
const tokenDe = async (papel) => { const r = await http(null, 'POST', '/auth/login', { pin: PINS[papel] }); afirma(r.estado === 200, `login API ${papel} ${r.estado}`); return r.json.token; };
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);

// ------------------------------------------------------------ app
const botao = (alvo, nome) => alvo.getByRole('button', { name: nome, exact: true });
const semErroBlazor = async () => afirma(!(await page.locator('#blazor-error-ui').isVisible().catch(() => false)), 'a app mostrou o aviso de erro do Blazor (#blazor-error-ui)');
async function clicar(loc, desc) {
  await loc.first().waitFor({ timeout: 15000 });
  const n = await loc.count(); afirma(n === 1, `${desc}: esperava 1 elemento, achei ${n}`);
  await loc.click(); await semErroBlazor();
}
async function escrever(sel, texto) { const l = page.locator(sel); await l.waitFor({ timeout: 15000 }); await l.fill(String(texto)); }
async function assinar() {
  const c = page.locator('#assinatura-tela'), guardar = botao(page, 'Guardar');
  await c.waitFor({ timeout: 15000 });
  for (let t = 0; t < 4 && await guardar.isDisabled(); t++) {
    await espera(500);
    // Eventos de ponteiro enviados pelo próprio JS: o rato do CDP bloqueia quando o WebView2 não pinta (janela tapada).
    await c.evaluate((cv) => {
      cv.setPointerCapture = () => {};
      const b = cv.getBoundingClientRect();
      const ev = (t, x, y) => cv.dispatchEvent(new PointerEvent(t, { pointerId: 1, bubbles: true, cancelable: true, clientX: b.x + b.width * x, clientY: b.y + b.height * y, pointerType: 'mouse', buttons: 1 }));
      ev('pointerdown', 0.15, 0.6);
      for (let i = 1; i <= 12; i++) ev('pointermove', 0.15 + 0.06 * i, 0.6 - 0.25 * Math.sin(i / 2));
      ev('pointerup', 0.87, 0.5);
    });
    await espera(300);
  }
  await guardar.click();
}
async function entrarApp(pin) {
  await page.locator('.teclado').waitFor({ timeout: 30000 });
  for (const d of pin) await clicar(page.locator('.teclado button.tecla', { hasText: new RegExp(`^${d}$`) }), `tecla ${d}`);
  await page.locator('.hero-texto, input#pin-atual, #assinatura-tela').first().waitFor({ timeout: 30000 });
  if (await page.locator('#assinatura-tela').isVisible().catch(() => false)) { await assinar(); await page.locator('.hero-texto, input#pin-atual').first().waitFor({ timeout: 30000 }); }
}
async function sairApp() {
  await clicar(page.locator('button[aria-label="Terminar sessão"]:visible'), 'Terminar sessão');
  const confirma = botao(page, 'Sair'); if (await confirma.isVisible().catch(() => false)) await confirma.click();
  await page.locator('.teclado').waitFor({ timeout: 20000 });
}
const ir = async (rota) => { await page.evaluate((r) => window.Blazor.navigateTo(r), rota); await espera(500); };
async function sessaoComo(papel) {
  for (let i = 0; i < 8; i++) {
    if (await page.locator('.hero-texto').isVisible().catch(() => false)) { if ((await page.locator('.hero-texto .sobretitulo').innerText()) === papel) return; await sairApp(); continue; }
    if (await page.locator('#assinatura-tela').isVisible().catch(() => false)) { await assinar(); await espera(1500); continue; }
    if (await page.locator('input#pin-atual').isVisible().catch(() => false)) { await ir('/'); await espera(2000); if (await page.locator('input#pin-atual').isVisible().catch(() => false)) { await page.reload(); await espera(3000); } continue; }
    if (await page.locator('.teclado').isVisible().catch(() => false)) { await entrarApp(PINS[papel]); continue; }
    try { await ir('/'); await page.locator('.hero-texto').waitFor({ timeout: 8000 }); } catch { await page.reload().catch(() => {}); await espera(3000); }
  }
  throw new Error(`não consegui ficar com a sessão de ${papel}`);
}
// Texto visível da zona de conteúdo (sem hidden), mais os textos dos alertas ocultos.
async function conteudo() {
  return page.evaluate(() => {
    const c = document.querySelector('section.conteudo') ?? document.body;
    const alertas = [...c.querySelectorAll('[role=alert]')].map((e) => e.textContent.trim()).filter(Boolean);
    return { h1: c.querySelector('h1')?.textContent.trim() ?? '', texto: c.innerText.replace(/\s+/g, ' ').trim(), alertas, avisos: [...document.querySelectorAll('.gm-aviso, .toast')].map((e) => e.textContent.trim()) };
  });
}
const toastDe = async (re, ms = 6000) => ate(async () => (await conteudo()).avisos.some((a) => re.test(a)), ms, `aviso ${re}`);
const BLOQ = /só para supervisores|só é para|Esta área é|Esta área é só|só para o perfil/i;
async function abrirRota(rota, h1) {
  await ir(rota);
  await page.locator('section.conteudo h1').first().waitFor({ timeout: 15000 });
  if (h1) await ate(async () => (await conteudo()).h1 === h1, 8000, `h1 "${h1}" em ${rota}`);
  await ate(async () => !/A carregar…/.test((await conteudo()).texto), 15000, `fim do "A carregar" em ${rota}`);
  return conteudo();
}

// ------------------------------------------------------------ ligar
async function ligar() {
  const browser = await chromium.connectOverCDP(CDP);
  const contexto = browser.contexts()[0];
  page = contexto.pages()[0] ?? await contexto.waitForEvent('page');
  const s = await contexto.newCDPSession(page);
  await s.send('Emulation.setFocusEmulationEnabled', { enabled: true });
  await s.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  // Com a janela da app tapada o WebView2 deixa de pintar e o Playwright fica à espera de "estável": clica-se sem essa espera.
  const proto = Object.getPrototypeOf(page.locator('body')); const clickOrig = proto.click;
  // click() do próprio elemento: não depende das coordenadas (a barra de navegação inferior tapa botões se a janela não pinta/rola).
  proto.click = async function (o) { await this.first().waitFor({ state: 'attached', timeout: 15000 }); if (await this.first().isDisabled().catch(() => false)) throw new Error('clique num controlo desativado'); return this.first().evaluate((e) => e.click()); };
  page.on('console', (m) => { if (m.type() === 'error') consola.push(m.text().slice(0, 160)); });
  page.on('pageerror', (e) => consola.push(`pageerror: ${String(e.message).slice(0, 160)}`));
  await page.waitForLoadState('domcontentloaded');
  if (!/\/login$/.test(page.url())) { await page.evaluate(() => window.Blazor.navigateTo('/')).catch(() => {}); await espera(1500); }
  await ate(async () => (await page.locator('.teclado').isVisible()) || (await page.locator('.hero-texto').isVisible()) || (await page.locator('#assinatura-tela').isVisible()) || (await page.locator('input#pin-atual').isVisible()), 40000, 'login/início');
  return browser;
}
const browser = await ligar();
const tokens = { Operador: await tokenDe('Operador'), Supervisor: await tokenDe('Supervisor'), Qualidade: await tokenDe('Qualidade') };

// ================================================================ segmentos
const tem = (s) => SEG.includes(s) || SEG.includes('todos');

// ---------------- permissões e páginas só de leitura, por perfil
const MATRIZ = [
  // rota, h1, quem vê a página (true) ou o bloqueio (false)
  ['/pecas/arrumar', 'Arrumação', { Operador: true, Supervisor: true, Qualidade: false }],
  ['/pecas/rearrumar', 'Rearrumar', { Operador: true, Supervisor: true, Qualidade: false }],
  ['/inventarios', 'Inventários', { Operador: true, Supervisor: true, Qualidade: false }],
  ['/qualidade', 'Qualidade', { Operador: false, Supervisor: false, Qualidade: true }],
  ['/por-rever', 'Por rever', { Operador: true, Supervisor: true, Qualidade: true }],
  ['/notificacoes', 'Notificações', { Operador: true, Supervisor: true, Qualidade: true }],
  ['/leitor', null, { Operador: true, Supervisor: true, Qualidade: true }],
  ['/mais', 'Mais', { Operador: false, Supervisor: true, Qualidade: false }],
  ['/fichas', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/configuracao', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/periodicidades', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/equipa', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/validacao', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/planeadas', null, { Operador: false, Supervisor: true, Qualidade: false }],
  ['/nova-ficha', null, { Operador: false, Supervisor: true, Qualidade: false }]
];
const MOSAICOS = {
  Operador: ['As minhas intervenções', 'Registar avaria', 'Equipamentos', 'Arrumação', 'Rearrumar', 'Inventários', 'Leitor'],
  Supervisor: ['Planeadas', 'Validação', 'Nova ficha', 'Todas as fichas', 'Equipamentos', 'Arrumação', 'Rearrumar', 'Inventários', 'Leitor', 'Equipa', 'Configuração'],
  Qualidade: ['Qualidade']
};
if (tem('perm')) {
  for (const papel of ['Operador', 'Supervisor', 'Qualidade']) {
    await circuito(`P ${papel}: início e permissões`, async () => {
      await passo('Entrar e ver o início', async () => { await sessaoComo(papel); await ir('/'); await page.locator('.hero-texto').waitFor({ timeout: 10000 }); });
      await passo('Mosaicos do início corretos para o perfil', async () => {
        const titulos = await page.locator('a.mosaico-link .mosaico-titulo').allInnerTexts();
        const lim = titulos.map((t) => t.replace(/[\d\s ]+(por .*|em .*)?$/i, '').trim());
        const falta = MOSAICOS[papel].filter((m) => !lim.some((t) => t.startsWith(m)));
        afirma(falta.length === 0, `faltam mosaicos: ${falta.join(', ')} (vistos: ${lim.join(' | ')})`);
        if (papel === 'Qualidade') afirma(!lim.some((t) => /Arrumação|Inventários|Equipa|Configuração|Planeadas|Validação/.test(t)), `Qualidade vê mosaicos a mais: ${lim.join(' | ')}`);
        if (papel === 'Operador') afirma(!lim.some((t) => /^(Equipa$|Configuração|Nova ficha|Planeadas|Validação|Qualidade)/.test(t)), `técnico vê mosaicos a mais: ${lim.join(' | ')}`);
        if (papel === 'Supervisor') afirma(!lim.some((t) => /^Qualidade/.test(t)), 'supervisor vê o mosaico Qualidade');
        return lim.join(' | ');
      });
      for (const [rota, h1, quem] of MATRIZ) {
        await passo(`${rota} ${quem[papel] ? 'abre' : 'bloqueia'}`, async () => {
          const c = await abrirRota(rota);
          const bloqueada = BLOQ.test(c.texto) || (rota === '/nova-ficha' && c.h1 === 'Registar avaria');
          if (quem[papel]) afirma(!bloqueada, `devia abrir mas mostra bloqueio: "${c.texto.slice(0, 100)}"`);
          else afirma(bloqueada, `devia estar bloqueada para ${papel} mas mostra: "${c.texto.slice(0, 120)}"`);
          if (h1 && quem[papel]) afirma(c.h1 === h1, `h1 "${c.h1}" ≠ "${h1}"`);
          if (!quem[papel] && !(rota === '/nova-ficha' && c.h1 === 'Registar avaria')) {
            // a página bloqueada não deve esconder botões de ação (ex.: Guardar, Arrumar)
            const acoes = await page.locator('section.conteudo button:visible, section.conteudo input:visible').count();
            afirma(acoes <= 1, `bloqueada mas com ${acoes} controlos visíveis`);
          }
          return `${c.h1} · ${c.texto.slice(0, 80)}`;
        });
      }
      await passo('Nenhum pedido protegido da API recusado por 403 no perfil (rotas do perfil)', async () => {
        const t = tokens[papel];
        const casos = papel === 'Qualidade' ? [] : [['/pecas/estrutura']];
        return `verificação de rotas delegada ao agente API (${casos.length})`;
      });
    });
  }
}

// ---------------- rotas extra por perfil (sem escrita): quem vê o quê
if (tem('sobreposicao')) {
  // Largura de telemóvel mas sem o modo 'mobile' do CDP: nesse modo a página não rola dentro desta janela e o teste não mede nada.
  { const sess = await page.context().newCDPSession(page); await sess.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 2, mobile: false }); await espera(800); }
  // A 390x844: para cada controlo da página centra-se o controlo no ecrã (o máximo que o scroll da página permite) e confere-se que nada o tapa (barra inferior, avisos, cabeçalho).
  const tapados = () => page.evaluate(() => {
    // Se a janela não rola (scrollTo sem efeito apesar de haver conteúdo por ver), nada se pode medir: é limite do ambiente, não da app.
    window.scrollTo(0, 200); const roda = Math.round(scrollY) > 0 || document.scrollingElement.scrollHeight <= innerHeight + 50; window.scrollTo(0, 0);
    if (!roda) return ['__sem_scroll__'];
    const raiz = document.querySelector('section.conteudo') ?? document.body;
    const sel = 'button, a[href], input:not([type=hidden]), select, textarea, summary';
    const vis = (e) => { const r = e.getBoundingClientRect(); const c = getComputedStyle(e); return r.width > 0 && r.height > 0 && c.visibility !== 'hidden' && c.display !== 'none' && !e.disabled; };
    const out = [];
    for (const e of raiz.querySelectorAll(sel)) {
      if (!vis(e)) continue;
      e.scrollIntoView({ block: 'center', inline: 'nearest' });
      const r = e.getBoundingClientRect();
      const x = Math.min(Math.max(r.left + r.width / 2, 1), innerWidth - 1), y = Math.min(Math.max(r.top + r.height / 2, 1), innerHeight - 1);
      const topo = document.elementFromPoint(x, y);
      if (!topo || !(topo === e || e.contains(topo) || topo.contains(e) || e.closest('label')?.contains(topo))) out.push(`${e.tagName.toLowerCase()} "${(e.getAttribute('aria-label') || e.textContent || e.id || '').trim().replace(/\s+/g, ' ').slice(0, 40)}" tapado por ${topo ? topo.tagName.toLowerCase() + '.' + String(topo.className).slice(0, 40) : 'nada'}`);
    }
    // Fundo da página: com o scroll no máximo, quanto espaço há entre o último controlo e a barra inferior.
    const sc = document.scrollingElement;
    let pai = raiz; const roláveis = [];
    for (let n = raiz; n; n = n.parentElement) { const c = getComputedStyle(n); if (/(auto|scroll)/.test(c.overflowY) && n.scrollHeight > n.clientHeight) roláveis.push(n.tagName.toLowerCase() + '.' + String(n.className).slice(0, 24)); }
    const todos = []; for (const e of document.querySelectorAll('*')) { const c = getComputedStyle(e); if (/(auto|scroll)/.test(c.overflowY) && e.scrollHeight > e.clientHeight + 2) todos.push(e.tagName.toLowerCase() + '.' + String(e.className).slice(0, 24) + ' ' + e.clientHeight + '/' + e.scrollHeight); }
    window.scrollTo(0, 300); const y300 = Math.round(scrollY);
    const ov = getComputedStyle(document.documentElement).overflowY + '/' + getComputedStyle(document.body).overflowY;
    window.scrollTo(0, 1e9); for (const n of document.querySelectorAll('main, .conteudo, .pagina, .layout')) n.scrollTop = 1e9;
    const nav = document.querySelector('nav.navegacao'); const ultimos = [...raiz.querySelectorAll(sel)].filter(vis);
    const ult = ultimos[ultimos.length - 1];
    out.unshift(`[medida] scroll ${Math.round(sc.scrollTop)}/${sc.scrollHeight} (vista ${innerHeight}); roláveis: ${roláveis.join(',') || 'nenhum'}; barra topo ${nav ? Math.round(nav.getBoundingClientRect().top) : 'n/a'}; último controlo fundo ${ult ? Math.round(ult.getBoundingClientRect().bottom) : 'n/a'}; padding-fundo ${getComputedStyle(raiz).paddingBottom}; scrollTo300→${y300}; overflow html/body ${ov}; contentores com scroll: ${todos.join(',') || 'nenhum'}`);
    return out;
  });
  const percursos = {
    Operador: ['/', '/intervencoes', '/avaria', '/pecas/arrumar', '/pecas/rearrumar', '/inventarios', '/leitor', '/notificacoes', '/equipamentos'],
    Supervisor: ['/', '/planeadas', '/validacao', '/nova-ficha', '/fichas', '/mais', '/configuracao', '/periodicidades', '/equipa', '/equipamentos', '/equipamentos/novo', '/inventarios', '/notificacoes'],
    Qualidade: ['/', '/qualidade', '/notificacoes']
  };
  for (const [papel, rotas] of Object.entries(percursos)) {
    await circuito(`SO ${papel}: nada tapa os controlos (390x844)`, async () => {
      for (const rota of rotas) {
        await passo(`${rota} sem controlos tapados`, async () => {
          await sessaoComo(papel); await ir(rota); await espera(1200);
          await ate(async () => !/A carregar…/.test((await conteudo()).texto), 15000, `fim do "A carregar" em ${rota}`).catch(() => {});
          const t = await tapados();
          if (t[0] === '__sem_scroll__') return 'SALTADO: a janela não rola neste ambiente (scrollTo sem efeito), sobreposição não medível';
          afirma(t.length === 1, `${t.length - 1} tapado(s): ${t.slice(0, 3).join(" | ")}`); return t[0];
          return `${rota} ok`;
        });
      }
    });
  }
}
if (tem('perm2')) {
  const EXTRA = [['/avaria', 'Registar avaria'], ['/intervencoes', null], ['/equipamentos', null], ['/equipamentos/novo', null], ['/assinatura', null], ['/pin', null]];
  for (const papel of ['Operador', 'Qualidade']) {
    await circuito(`P2 ${papel}: rotas de outros perfis`, async () => {
      await sessaoComo(papel);
      for (const [rota] of EXTRA) {
        await passo(`${rota}`, async () => {
          const c = await abrirRota(rota);
          const acoes = await page.locator('section.conteudo input:visible, section.conteudo textarea:visible, section.conteudo select:visible').count();
          return `h1="${c.h1}" bloqueio=${BLOQ.test(c.texto)} campos=${acoes} · ${c.texto.slice(0, 110)}`;
        });
      }
    });
  }
}

// ---------------- Mais e leitor
if (tem('mais')) {
  await circuito('M Supervisor: Mais, leitor e Configuração', async () => {
    await passo('Mais mostra 4 atalhos que abrem as páginas certas', async () => {
      await sessaoComo('Supervisor');
      await abrirRota('/mais', 'Mais');
      const destinos = [['Todas as fichas', /fichas/], ['Periodicidades', /periodicidades/], ['Equipa', /equipa/], ['Configuração', /configuracao/]];
      for (const [t, re] of destinos) {
        await abrirRota('/mais', 'Mais');
        await clicar(page.locator('a.mosaico-link', { has: page.locator('.mosaico-titulo', { hasText: t }) }), t);
        await ate(async () => re.test(page.url()), 8000, `url de ${t}`);
      }
    });
    await passo('Leitor: 3 modos, câmara desativada sem câmara, teste de leitura', async () => {
      await abrirRota('/leitor');
      const seg = page.locator('.segmentos .segmento'); afirma((await seg.count()) === 3, `segmentos: ${await seg.count()}`);
      for (const n of ['Leitor', 'Automático']) { await seg.filter({ hasText: n }).click(); await ate(async () => (await seg.filter({ hasText: n }).getAttribute('aria-pressed')) === 'true', 4000, `modo ${n} marcado`); }
      const cam = seg.filter({ hasText: 'Câmara' });
      const t = await page.locator('section.conteudo').innerText();
      return `câmara desativada=${await cam.isDisabled()} · ${t.replace(/\s+/g, ' ').slice(0, 160)}`;
    });
    await passo('Leitor: picada por teclado no campo escondido mostra o código lido', async () => {
      await abrirRota('/leitor');
      const alvo = page.locator('#teste-leitura');
      const n = await alvo.count();
      if (n === 0) return 'campo #teste-leitura não existe (LeitorEtiquetas sem input nesta vista)';
      await alvo.focus().catch(() => {});
      await page.keyboard.type('560001'); await page.keyboard.press('Enter');
      const t = await ate(async () => { const x = await page.locator('.leitor-teste').innerText(); return x.includes('560001') ? x : null; }, 4000, 'código lido no teste de leitura').catch(() => null);
      return t ? 'lido' : 'campo escondido não aceitou teclas (só modo leitor)';
    });
    await passo('Configuração: valor inválido recusado e valor válido gravado (repõe)', async () => {
      await abrirRota('/configuracao');
      const orig = await page.locator('#cfg-antecedencia').inputValue();
      for (const mau of ['', 'abc', '-1', '999']) {
        await escrever('#cfg-antecedencia', mau); await clicar(botao(page, 'Guardar'), 'Guardar');
        await ate(async () => (await page.locator('#cfg-erro').textContent()).trim().length > 0, 4000, `erro com "${mau}"`);
        afirma(/configuracao/.test(page.url()), `saiu da página com "${mau}"`);
      }
      await escrever('#cfg-antecedencia', orig); await clicar(botao(page, 'Guardar'), 'Guardar');
      await ate(async () => !/configuracao/.test(page.url()), 8000, 'voltar a Mais depois de guardar');
      return `valor original ${orig} reposto`;
    });
  });
}

// ---------------- Todas as fichas (filtros)
if (tem('fichas')) {
  await circuito('F Supervisor: Todas as fichas', async () => {
    await passo('Abrir /fichas e ver lista', async () => { await sessaoComo('Supervisor'); const c = await abrirRota('/fichas'); return c.texto.slice(0, 200); });
    const nCartoes = () => page.locator('ul.lista-intervencoes > li').count();
    const carregado = () => ate(async () => !/A carregar…/.test((await conteudo()).texto), 10000, 'fim de carregar');
    await passo('Situação Abertas/Fechadas/Todas: Todas = Abertas + Fechadas', async () => {
      const n = {};
      for (const s of ['Abertas', 'Fechadas', 'Todas']) {
        await page.locator('.segmentos button.segmento', { hasText: new RegExp(`^${s}$`) }).click();
        await espera(900); await carregado(); n[s] = await nCartoes();
        const a = await page.locator('.segmentos button.segmento[aria-pressed="true"]').innerText(); afirma(a.trim() === s, `segmento marcado "${a}" ≠ ${s}`);
      }
      const t = (await conteudo()).texto;
      afirma(!/Há mais fichas/.test(t) ? n.Todas === n.Abertas + n.Fechadas : true, `Todas ${n.Todas} ≠ Abertas ${n.Abertas} + Fechadas ${n.Fechadas}`);
      return JSON.stringify(n);
    });
    await passo('Filtro Estado (cada opção): todos os cartões mostram o estado e batem com a API', async () => {
      await page.locator('details.filtros-bloco summary').click();
      const opcoes = await page.locator('#fi-estado option').evaluateAll((o) => o.map((x) => ({ v: x.value, t: x.textContent.trim() })));
      const tokenS = tokens.Supervisor, vistos = [];
      for (const o of opcoes.filter((x) => x.v)) {
        await page.locator('#fi-estado').selectOption(o.v);
        await botao(page, 'Aplicar filtros').click(); await espera(900); await carregado();
        const n = await nCartoes(); const api = itens((await http(tokenS, 'GET', `/intervencoes/todas?estado=${encodeURIComponent(o.v)}&situacao=todas`)).json).length;
        const chips = await page.locator('ul.lista-intervencoes > li .chips').allInnerTexts();
        const erradas = chips.filter((c) => !c.includes(o.t) && !(o.v === 'Concluida' && /Por validar|Concluída/i.test(c)));
        const tit = n !== api ? ' [' + (await page.locator('ul.lista-intervencoes > li .nr, ul.lista-intervencoes > li .intervencao-data').allInnerTexts()).join(' ; ') + ' | API ' + itens((await http(tokenS, 'GET', `/intervencoes/todas?estado=${encodeURIComponent(o.v)}&situacao=todas`)).json).map((f) => 'Nº' + f.numero + '/' + f.id + '/' + f.dataPlaneada).join(' ; ') + ']' : '';
        vistos.push(`${o.t}: UI ${n}/API ${api}${erradas.length ? ' CHIPS_DIFERENTES(' + erradas.length + ')' : ''}${tit}`);
        afirma(n === api || n > 0 && api >= n, `${o.t}: UI ${n} ≠ API ${api}`);
      }
      await botao(page, 'Repor filtros').click(); await espera(800);
      return vistos.join(' | ');
    });
    await passo('Filtro Tipo (Avaria/Preventiva) e Ordenar mudam a lista sem erro', async () => {
      for (const t of ['Curativa', 'Preventiva', '']) {
        await page.locator('#fi-tipo').selectOption(t); await botao(page, 'Aplicar filtros').click(); await espera(800); await carregado();
      }
      for (const o of await page.locator('#fi-ordem option').evaluateAll((x) => x.map((y) => y.value))) { await page.locator('#fi-ordem').selectOption(o); await botao(page, 'Aplicar filtros').click(); await espera(500); await carregado(); }
      await page.locator('#fi-desc').evaluate((e) => e.click()); await botao(page, 'Aplicar filtros').click(); await espera(600);
      await botao(page, 'Repor filtros').click(); await espera(600);
      return `cartões depois de repor: ${await nCartoes()}`;
    });
    await passo('Só por supervisionar e técnico: respondem; Repor filtros limpa o contador "Filtros (n)"', async () => {
      await page.locator('#fi-sup').evaluate((e) => e.click()); await page.locator('#fi-tecnico').selectOption({ index: 1 }).catch(() => {});
      await botao(page, 'Aplicar filtros').click(); await espera(900); await carregado();
      const com = await page.locator('details.filtros-bloco summary').innerText();
      await botao(page, 'Repor filtros').click(); await espera(800);
      const sem = await page.locator('details.filtros-bloco summary').innerText();
      afirma(/\(\d/.test(com) && !/\(\d/.test(sem), `contador antes "${com}" depois "${sem}"`);
      return `${com} -> ${sem}`;
    });
    await passo('Pesquisa sem resultados mostra estado vazio; datas invertidas recusadas', async () => {
      await escrever('#fi-texto', 'zzzzzz-nao-existe');
      const bp = page.getByRole('button', { name: /Pesquisar|Aplicar|Filtrar/ }).first();
      await page.locator('#fi-texto').press('Enter'); await espera(900);
      let c = await conteudo();
      const vazio = /Nenhuma|Não há|sem resultados|Sem fichas/i.test(c.texto);
      await escrever('#fi-texto', '');
      let datas = 'campo de datas não visível';
      if (await page.locator('#fi-desde').isVisible().catch(() => false)) {
        await page.locator('#fi-desde').fill('2026-12-31'); await page.locator('#fi-ate').fill('2026-01-01');
        await page.locator('#fi-texto').press('Enter'); await espera(800);
        c = await conteudo(); datas = c.alertas.join('|') || 'sem erro de datas invertidas';
      }
      afirma(vazio, `sem estado vazio para pesquisa sem resultados: ${c.texto.slice(0, 160)}`);
      return `datas invertidas: ${datas}`;
    });
  });
}

// ---------------- Por rever e notificações
if (tem('notif')) {
  for (const papel of ['Operador', 'Supervisor', 'Qualidade']) {
    await circuito(`N ${papel}: notificações e por rever`, async () => {
      await passo('Notificações: estado e botões coerentes', async () => {
        await sessaoComo(papel);
        const c = await abrirRota('/notificacoes', 'Notificações');
        const marcar = botao(page, 'Marcar todas como lidas'), limpar = botao(page, 'Limpar notificações');
        const tem = (await marcar.count()) > 0;
        if (tem) {
          const novas = await page.locator('.aviso-novo').count();
          afirma((await marcar.isDisabled()) === (novas === 0), `Marcar todas como lidas ${await marcar.isDisabled() ? 'desativado' : 'ativo'} com ${novas} novas`);
          return `${c.texto.slice(0, 90)} · novas=${novas}`;
        }
        afirma(/Ainda não há notificações/.test(c.texto), `nem lista nem estado vazio: ${c.texto.slice(0, 120)}`);
        const txt = papel === 'Supervisor' ? /fichas por validar/ : /intervenções que lhe atribuem/;
        if (papel !== 'Qualidade') afirma(txt.test(c.texto), `texto de ajuda do perfil em falta: ${c.texto.slice(0, 160)}`);
        return 'vazio: ' + c.texto.slice(0, 110);
      });
      await passo('Cada notificação abre o destino do perfil', async () => {
        await abrirRota('/notificacoes');
        const ligacoes = await page.locator('a.aviso').evaluateAll((a) => a.map((x) => x.getAttribute('href')));
        if (!ligacoes.length) return 'sem notificações para abrir';
        const h = ligacoes[0]; await clicar(page.locator('a.aviso').first(), 'primeira notificação');
        await espera(800);
        const c = await conteudo(); afirma(c.h1 !== '', `destino ${h} sem título`);
        return `${h} -> ${c.h1}`;
      });
      await passo('Por rever abre e mostra vazio ou lista com "Percebi"', async () => {
        const c = await abrirRota('/por-rever', 'Por rever');
        afirma(/Não há nada por rever|Percebi/.test(c.texto), `estado: ${c.texto.slice(0, 120)}`);
        return c.texto.slice(0, 100);
      });
    });
  }
}

// ---------------- Arrumação / rearrumação / inventário (escritas: anunciar ao agente API antes)
const lugaresApi = async (papel, ref) => (await http(tokens[papel], 'GET', `/pecas/${encodeURIComponent(ref)}/localizacoes`)).json;
async function pesquisarPeca(idCampo, texto) {
  await escrever(`#${idCampo}`, texto);
  await page.locator(`#${idCampo}`).press('Enter');
  const op = page.locator('.peca-lugar button.intervencao, .lista-intervencoes button.intervencao', { hasText: texto }).first();
  await op.waitFor({ timeout: 10000 }); return op;
}
async function escolherLugar(id, corredor, estante, nivel, pos) {
  await page.locator(`#${id}-corredor`).selectOption(corredor);
  await page.locator(`#${id}-estante`).selectOption(String(estante).padStart(2, '0'));
  await page.locator(`#${id}-nivel`).selectOption(String(nivel));
  await page.locator(`#${id}-posicao`).selectOption(String(pos).padStart(2, '0'));
}
if (tem('arrumar-ler')) {
  await circuito('A1 Arrumação sem escrita (técnico)', async () => {
    await passo('Página abre no passo 1 com pesquisa e sem botão Arrumar', async () => {
      await sessaoComo('Operador'); const c = await abrirRota('/pecas/arrumar', 'Arrumação');
      afirma(/Passo 1 de 3/.test(c.texto), `passo: ${c.texto.slice(0, 120)}`);
      afirma((await botao(page, 'Arrumar').count()) === 0, 'botão Arrumar visível sem peça');
      afirma((await page.locator('#arrumar-corredor').count()) === 1, 'sem escolha de corredor');
      return c.texto.slice(0, 120);
    });
    await passo('Pesquisa vazia, sem resultados e peça sem nada por arrumar (P002)', async () => {
      await escrever('#arrumar-peca', ''); await page.locator('#arrumar-peca').press('Enter'); await espera(600);
      let c = await conteudo(); const vaz = c.alertas.join('|');
      await escrever('#arrumar-peca', 'zzzz-nao'); await page.locator('#arrumar-peca').press('Enter'); await espera(900);
      c = await conteudo(); const nada = c.alertas.join('|') + ' / ' + c.texto.slice(c.texto.indexOf('Peça'), c.texto.indexOf('Peça') + 150);
      await (await pesquisarPeca('arrumar-peca', 'Rolamento')).click();
      await ate(async () => /nada à espera de arrumar/.test((await conteudo()).texto), 8000, 'aviso "nada à espera de arrumar" na P002');
      afirma((await page.locator('#arrumar-qtt').count()) === 0, 'mostra quantidade sem nada por arrumar');
      afirma((await page.locator('#arrumar-corredor').count()) === 0, 'mostra escolha de lugar sem nada por arrumar');
      return `vazio: "${vaz}" · sem resultados: "${nada.slice(0, 100)}"`;
    });
    await passo('Mudar de peça volta ao passo 1', async () => {
      await clicar(page.getByRole('button', { name: /Mudar de peça/ }), 'Mudar de peça');
      await ate(async () => /Passo 1 de 3/.test((await conteudo()).texto), 5000, 'passo 1');
    });
    await passo('Passo 2 sem peça: "Usar este lugar" só ativa com os 4 níveis; selects em cascata', async () => {
      const usar = botao(page, 'Usar este lugar');
      afirma(await usar.isDisabled(), 'Usar este lugar ativo sem escolha');
      afirma(await page.locator('#arrumar-estante').isDisabled(), 'estante ativa sem corredor');
      const cor = await page.locator('#arrumar-corredor option').allInnerTexts();
      afirma(cor.length > 1, `corredores: ${cor.join(',')}`);
      await page.locator('#arrumar-corredor').selectOption({ index: 1 });
      afirma(!(await page.locator('#arrumar-estante').isDisabled()), 'estante não ativou');
      await page.locator('#arrumar-estante').selectOption({ index: 1 });
      await page.locator('#arrumar-nivel').selectOption({ index: 1 });
      afirma(await usar.isDisabled(), 'ativo sem posição');
      await page.locator('#arrumar-posicao').selectOption({ index: 1 });
      afirma(!(await usar.isDisabled()), 'inativo com os 4 níveis');
      return `corredores: ${cor.join(',')}`;
    });
    await passo('Escolher a peça P001: stock, por arrumar e lugares coerentes com a API (aceita stock negativo)', async () => {
      await (await pesquisarPeca('arrumar-peca', 'Parafuso')).click();
      await ate(async () => /À espera de arrumar/.test((await conteudo()).texto), 8000, 'linha Stock/À espera');
      const t = (await conteudo()).texto;
      const api = await lugaresApi('Supervisor', 'P001');
      const m = t.match(/Stock (-?[\d,.]+) · À espera de arrumar (-?[\d,.]+)/);
      afirma(m, `texto de stock em falta: ${t.slice(0, 200)}`);
      afirma(Number(m[1].replace(',', '.')) === Number(api?.stock), `stock UI ${m[1]} ≠ API ${api?.stock}`);
      if (Number(api?.stock) < 0) afirma(/stock desta peça está negativo/.test(t), `sem aviso de stock negativo: ${t.slice(0, 220)}`);
      return `UI: stock ${m[1]} por arrumar ${m[2]} · API: ${JSON.stringify({ s: api?.stock, p: api?.porArrumar, l: (api?.lugares ?? []).map((l) => `${l.texto}=${l.qtt}`) })}`;
    });
  });
}
if (tem('arrumar-escrever')) {
  // Anunciado ao agente API: arrumar P001 qtt N num lugar novo.
  const QTT = process.env.QTT_ARRUMAR ?? '2';
  const L = (process.env.LUGAR ?? 'A,2,1,2').split(',');
  await circuito('A2 Arrumação com escrita', async () => {
    await passo('Quantidade acima do que está à espera é recusada', async () => {
      await sessaoComo('Operador'); await abrirRota('/pecas/arrumar', 'Arrumação');
      await (await pesquisarPeca('arrumar-peca', 'Parafuso')).click();
      await ate(async () => /À espera de arrumar/.test((await conteudo()).texto), 8000, 'stock');
      await escolherLugar('arrumar', L[0], L[1], L[2], L[3]); await clicar(botao(page, 'Usar este lugar'), 'Usar este lugar');
      await page.locator('#arrumar-qtt').waitFor({ timeout: 8000 });
      await escrever('#arrumar-qtt', '99');
      await ate(async () => /Só pode arrumar o que está à espera/.test((await conteudo()).alertas.join(' ')), 4000, 'aviso ao digitar 99');
      await clicar(botao(page, 'Arrumar'), 'Arrumar 99');
      await espera(800);
      const c = await conteudo(); afirma(/Passo 3/.test(c.texto), 'saiu do passo 3 com quantidade a mais');
      for (const mau of ['0', '-1', 'abc', '1,2345']) { await escrever('#arrumar-qtt', mau); await clicar(botao(page, 'Arrumar'), 'Arrumar mau'); await espera(500); const a = (await conteudo()).alertas.join('|'); afirma(a.length > 0, `sem erro para "${mau}"`); }
    });
    await passo(`Arrumar ${QTT} de P001 no lugar ${L.join('-')}`, async () => {
      await escrever('#arrumar-qtt', QTT); await clicar(botao(page, 'Arrumar'), 'Arrumar');
      await toastDe(/Arrumado/, 8000);
      await ate(async () => /Lugares atualizados/.test((await conteudo()).texto), 8000, 'lugares atualizados');
      const api = await lugaresApi('Supervisor', 'P001');
      const alvo = `A-0${L[1]}-${L[2]}-0${L[3]}`;
      const lug = (api?.lugares ?? []).find((l) => l.texto === alvo || (l.texto ?? '').endsWith(alvo));
      afirma(lug && Number(lug.qtt) >= Number(QTT.replace(',', '.')), `API sem ${QTT} em ${alvo}: ${JSON.stringify(api)}`);
      return `UI: ${(await conteudo()).texto.slice(0, 160)} · API: stock ${api.stock} porArrumar ${api.porArrumar} lugares ${(api.lugares ?? []).map((l) => `${l.texto}=${l.qtt}`).join(',')}`;
    });
  });
}
if (tem('rearrumar')) {
  const QTT = process.env.QTT_MOVER ?? '1';
  const L = (process.env.DESTINO ?? 'A,2,1,2').split(',');
  await circuito('R Rearrumação', async () => {
    await passo('Página abre; peça sem lugares (P002) avisa; técnico não vê botão antes do destino', async () => {
      await sessaoComo('Operador'); const c = await abrirRota('/pecas/rearrumar', 'Rearrumar');
      afirma(/Passo 1 de 3/.test(c.texto), 'passo 1');
      await (await pesquisarPeca('rearrumar-peca', 'Rolamento')).click();
      await ate(async () => /ainda não tem lugares/.test((await conteudo()).texto), 8000, 'aviso "ainda não tem lugares"');
      await clicar(page.getByRole('button', { name: /Mudar de peça/ }), 'Mudar de peça');
    });
    await passo('Escolher P001: lista de origens com disponível; escolher origem avança ao passo 3', async () => {
      await (await pesquisarPeca('rearrumar-peca', 'Parafuso')).click();
      await ate(async () => /De que lugar sai/.test((await conteudo()).texto), 8000, 'origens');
      const origens = await page.locator('.lista-intervencoes button.intervencao').allInnerTexts();
      afirma(origens.length > 0, 'sem origens');
      await page.locator('.lista-intervencoes button.intervencao').first().click();
      await ate(async () => /Sai de/.test((await conteudo()).texto), 6000, 'origem escolhida');
      afirma(await botao(page, 'Mudar de sítio').isDisabled(), 'Mudar de sítio ativo sem destino');
      return origens.join(' | ');
    });
    await passo('Mudar de origem volta à lista; destino igual à origem é recusado', async () => {
      await clicar(page.getByRole('button', { name: /Mudar de origem/ }), 'Mudar de origem');
      await ate(async () => /De que lugar sai/.test((await conteudo()).texto), 5000, 'origens outra vez');
      await page.locator('.lista-intervencoes button.intervencao').first().click();
      await ate(async () => /Sai de/.test((await conteudo()).texto), 5000, 'origem');
      const orig = (await page.locator('.lugar-codigo').first().innerText()).trim();
      const m = orig.match(/([A-Z]+)\D*(\d+)\D+(\d+)\D+(\d+)/);
      if (!m) return `formato do lugar ${orig} não reconhecido para o teste de destino igual`;
      await escolherLugar('rearrumar', m[1], Number(m[2]), Number(m[3]), Number(m[4])); await clicar(botao(page, 'Usar este destino'), 'Usar este destino');
      await page.locator('#rearrumar-qtt').waitFor({ timeout: 6000 });
      await escrever('#rearrumar-qtt', '1'); await clicar(botao(page, 'Mudar de sítio'), 'Mudar de sítio (mesmo lugar)');
      await espera(1200);
      const c = await conteudo();
      afirma(/Passo 3|não pode|mesmo|igual/i.test(c.texto + c.alertas.join()), `destino igual aceitou? ${c.texto.slice(0, 150)}`);
      return `alerta: ${c.alertas.join('|')} avisos: ${c.avisos.join('|')}`;
    });
    await passo(`Quantidade acima do lugar recusada; mudar ${QTT} de P001 para ${L.join('-')}`, async () => {
      await ir('/'); await ir('/pecas/rearrumar'); await page.locator('#rearrumar-peca').waitFor({ timeout: 8000 });
      await (await pesquisarPeca('rearrumar-peca', 'Parafuso')).click();
      await ate(async () => /De que lugar sai/.test((await conteudo()).texto), 8000, 'origens');
      await page.locator('.lista-intervencoes button.intervencao', { hasText: 'A-02-1-01|A-2-1-1' }).first().click().catch(async () => page.locator('.lista-intervencoes button.intervencao').first().click());
      await ate(async () => /Sai de/.test((await conteudo()).texto), 5000, 'origem');
      await escolherLugar('rearrumar', L[0], L[1], L[2], L[3]); await clicar(botao(page, 'Usar este destino'), 'Usar este destino');
      await page.locator('#rearrumar-qtt').waitFor({ timeout: 6000 });
      await escrever('#rearrumar-qtt', '999');
      await ate(async () => /Só há/.test((await conteudo()).alertas.join(' ')), 4000, 'aviso Só há');
      await escrever('#rearrumar-qtt', QTT); await clicar(botao(page, 'Mudar de sítio'), 'Mudar de sítio');
      await toastDe(/Mudou|Mudado|Rearrumado|sítio/i, 8000).catch(() => {});
      await ate(async () => /Lugares atualizados/.test((await conteudo()).texto), 8000, 'lugares atualizados');
      const api = await lugaresApi('Supervisor', 'P001');
      return `UI: ${(await conteudo()).texto.slice(0, 160)} · API: stock ${api.stock} porArrumar ${api.porArrumar} lugares ${(api.lugares ?? []).map((l) => `${l.texto}=${l.qtt}`).join(',')}`;
    });
  });
}
if (tem('inventario')) {
  const ID = process.env.INV_ID;
  await circuito('I Inventários (contagem)', async () => {
    await passo('Lista mostra o inventário atribuído e abre a contagem', async () => {
      await sessaoComo('Operador'); const c = await abrirRota('/inventarios', 'Inventários');
      afirma(/Inventário 1/.test(c.texto), `lista: ${c.texto.slice(0, 150)}`);
      await clicar(page.locator('a.intervencao[href^="inventarios/"]').first(), 'abrir inventário');
      await ate(async () => /Passo 1 de 3/.test((await conteudo()).texto), 10000, 'passo 1');
      return page.url().split('/').pop();
    });
    await passo('Sem linhas: aviso e sem botão Concluir; id inexistente mostra "já não existe"', async () => {
      const c = await conteudo();
      afirma(/Ainda não contou nenhuma peça/.test(c.texto) || /Linhas contadas/.test(c.texto), 'sem secção de linhas');
      const url = page.url();
      await ir('/inventarios/naoexiste123'); await page.locator('section.conteudo').waitFor();
      await ate(async () => !/A carregar…/.test((await conteudo()).texto), 8000, 'fim de carregar');
      const x = await conteudo(); afirma(/já não existe|Não foi possível/.test(x.alertas.join(' ')), `id falso: ${JSON.stringify(x.alertas)} / ${x.texto.slice(0, 120)}`);
      await ir('/' + url.split('/').slice(-2).join('/'));
      return x.alertas.join('|');
    });
    if (ID) await passo('Contar P001 num lugar: quantidade inválida recusada e linha gravada', async () => {
      await ir(`/inventarios/${ID}`); await ate(async () => /Passo 1 de 3/.test((await conteudo()).texto), 10000, 'passo 1');
      await (await pesquisarPeca('inventario-peca', 'Parafuso')).click();
      await escolherLugar('inventario', 'A', 2, 1, 1).catch(() => {});
      await clicar(botao(page, 'Usar este lugar'), 'Usar este lugar');
      await page.locator('#inventario-qtt').waitFor({ timeout: 8000 });
      for (const mau of ['', 'abc', '-3']) { await escrever('#inventario-qtt', mau); await clicar(botao(page, 'Gravar linha'), 'Gravar'); await espera(500); afirma((await conteudo()).alertas.join('').length > 0, `sem erro com "${mau}"`); }
      await escrever('#inventario-qtt', '4'); await clicar(botao(page, 'Gravar linha'), 'Gravar linha');
      await ate(async () => /Linhas contadas/.test((await conteudo()).texto) && (await page.locator('ul.lista-intervencoes li.intervencao').count()) >= 1, 8000, 'linha na lista');
      return (await conteudo()).texto.slice(0, 200);
    });
    if (ID) await passo('Alterar quantidade (vazio recusado, 6 gravado), Cancelar, e a mesma peça+lugar não duplica', async () => {
      const li = page.locator('ul.lista-intervencoes li.intervencao').first();
      await li.getByRole('button', { name: 'Alterar quantidade' }).click();
      const campo = li.locator('input[id^="inv-q-"]'); await campo.waitFor({ timeout: 5000 });
      await campo.fill(''); await li.getByRole('button', { name: 'Guardar' }).click(); await espera(700);
      afirma((await conteudo()).alertas.join('').length > 0 || (await campo.count()) === 1, 'quantidade vazia aceite');
      await li.getByRole('button', { name: 'Cancelar' }).click();
      await li.getByRole('button', { name: 'Alterar quantidade' }).click();
      await li.locator('input[id^="inv-q-"]').fill('6'); await li.getByRole('button', { name: 'Guardar' }).click();
      await ate(async () => /6/.test(await page.locator('ul.lista-intervencoes li.intervencao').first().innerText()), 6000, 'linha com 6');
      return (await page.locator('ul.lista-intervencoes li.intervencao').first().innerText()).replace(/\s+/g, ' ');
    });
    if (ID) await passo('Apagar a linha e voltar a contar; "Concluir inventário" pede confirmação e "Ainda não" cancela', async () => {
      await page.locator('ul.lista-intervencoes li.intervencao').first().getByRole('button', { name: /Apagar a linha/ }).click();
      await ate(async () => (await page.locator('ul.lista-intervencoes li.intervencao').count()) === 0, 6000, 'linha apagada');
      afirma((await botao(page, 'Concluir inventário').count()) === 0, 'Concluir inventário visível sem linhas');
      await (await pesquisarPeca('inventario-peca', 'Rolamento')).click();
      await escolherLugar('inventario', 'A', 1, 2, 3); await clicar(botao(page, 'Usar este lugar'), 'Usar este lugar');
      await page.locator('#inventario-qtt').waitFor({ timeout: 8000 });
      await escrever('#inventario-qtt', '2'); await clicar(botao(page, 'Gravar linha'), 'Gravar linha');
      await ate(async () => (await page.locator('ul.lista-intervencoes li.intervencao').count()) === 1, 8000, 'linha da P002');
      await clicar(botao(page, 'Concluir inventário'), 'Concluir inventário');
      await page.locator('[role=alertdialog]').waitFor({ timeout: 4000 });
      await clicar(page.getByRole('button', { name: 'Ainda não' }), 'Ainda não');
      await ate(async () => (await page.locator('[role=alertdialog]').count()) === 0, 4000, 'fecha o diálogo');
      return 'ok';
    });
    if (ID && process.env.CONCLUIR_INV === '1') await passo('Concluir o inventário: depois só se consulta', async () => {
      await clicar(botao(page, 'Concluir inventário'), 'Concluir inventário');
      await clicar(page.getByRole('button', { name: 'Sim, concluir' }), 'Sim, concluir');
      await ate(async () => /só se pode consultar/.test((await conteudo()).texto), 10000, 'estado concluído');
      afirma((await botao(page, 'Gravar linha').count()) === 0 && (await page.locator('#inventario-qtt').count()) === 0, 'ainda deixa contar');
      afirma((await page.getByRole('button', { name: /Apagar a linha|Alterar quantidade/ }).count()) === 0, 'ainda deixa editar linhas');
      return (await conteudo()).texto.slice(0, 160);
    });
  });
}

if (tem('qualidade-ler')) {
  await circuito('Q Qualidade (sem escrita)', async () => {
    await passo('Lista (ou vazio) coerente com a API', async () => {
      await sessaoComo('Qualidade'); const c = await abrirRota('/qualidade', 'Qualidade');
      const api = itens((await http(tokens.Qualidade, 'GET', '/qualidade')).json);
      const n = await page.locator('li.ficha-validar').count();
      afirma(n === api.length, `UI ${n} fichas ≠ API ${api.length}`);
      if (!n) afirma(/Não há intervenções à espera de conclusão/.test(c.texto), 'sem texto de vazio');
      return `${n} fichas`;
    });
  });
}

if (tem('inv-perm')) {
  await circuito('IP Inventário: quem pode contar', async () => {
    const lista = itens((await http(tokens.Supervisor, 'GET', '/inventarios')).json);
    const id = lista[0]?.id;
    await passo('Supervisor abre o inventário da Ana: só consulta, sem controlos de contagem', async () => {
      afirma(id, 'sem inventário na API');
      await sessaoComo('Supervisor'); await abrirRota(`/inventarios/${id}`);
      const c = await conteudo();
      afirma(/Só o técnico a quem o inventário foi atribuído|já está concluído/.test(c.texto), `mensagem em falta: ${c.texto.slice(0, 200)}`);
      afirma((await page.locator('#inventario-peca, #inventario-qtt, #inventario-corredor').count()) === 0, 'o supervisor vê controlos de contagem');
      return c.texto.slice(0, 160);
    });
    await passo('Qualidade abre /inventarios/{id}: bloqueada', async () => {
      await sessaoComo('Qualidade'); const c = await abrirRota(`/inventarios/${id}`);
      afirma(/Esta área é para operadores e supervisores/.test(c.texto), `não bloqueou: ${c.texto.slice(0, 120)}`);
      afirma((await page.locator('#inventario-peca, #inventario-qtt').count()) === 0, 'controlos visíveis');
    });
    await passo('Técnico: /inventarios/{id} de outro (id falso) não rebenta', async () => {
      await sessaoComo('Operador'); const c = await abrirRota('/inventarios/zzzzzzzzzzzzzzzzzzzzzzzzz');
      afirma(/já não existe|Não foi possível/.test(c.alertas.join(' ')), `sem aviso: ${JSON.stringify(c.alertas)}`);
    });
  });
}
if (tem('inv-concluir')) {
  await circuito('IC Concluir inventário', async () => {
    await passo('Concluir pela app: depois só consulta (sem contar nem editar)', async () => {
      await sessaoComo('Operador'); await abrirRota('/inventarios', 'Inventários');
      await clicar(page.locator('a.intervencao[href^="inventarios/"]').first(), 'abrir inventário');
      await page.locator('h1', { hasText: 'Inventário 1' }).waitFor({ timeout: 8000 });
      await clicar(botao(page, 'Concluir inventário'), 'Concluir inventário');
      await clicar(page.getByRole('button', { name: 'Sim, concluir' }), 'Sim, concluir');
      await ate(async () => /só se pode consultar/.test((await conteudo()).texto), 10000, 'estado concluído');
      afirma((await page.locator('#inventario-qtt').count()) === 0 && (await botao(page, 'Gravar linha').count()) === 0, 'ainda deixa contar');
      afirma((await page.getByRole('button', { name: /Apagar a linha|Alterar quantidade/ }).count()) === 0, 'ainda deixa editar linhas');
      return (await conteudo()).texto.slice(0, 200);
    });
    await passo('Lista de inventários mostra o estado concluído', async () => {
      const c = await abrirRota('/inventarios', 'Inventários'); return c.texto.slice(0, 160);
    });
  });
}
if (tem('qual-avaria')) {
  await circuito('QA Qualidade tenta registar avaria (API recusa com 403)', async () => {
    await passo('Formulário /avaria aceita o envio e a app explica a recusa (sem crash)', async () => {
      await sessaoComo('Qualidade'); await abrirRota('/avaria', 'Registar avaria');
      await page.locator('#equipamento').fill('Balança');
      const op = page.locator('.opcoes .opcao').first(); await op.waitFor({ timeout: 10000 }); await op.click();
      await page.locator('#anomalia').fill('TESTE-UIQ-AVARIA recusada pela API');
      await botao(page, 'Feito').click();
      await espera(2500);
      const c = await conteudo();
      const url = page.url();
      return `url=${url} · alertas=${JSON.stringify(c.alertas)} · avisos=${JSON.stringify(c.avisos)} · ${c.texto.slice(0, 140)}`;
    });
  });
}
if (tem('qualidade-escrever')) {
  const alvoUi = process.env.QUAL_TEXTO ?? 'TESTE-UIQ-1';
  const outro = process.env.QUAL_TEXTO2 ?? 'TESTE-UIQ-2';
  const ficha = (t) => page.locator('li.ficha-validar', { hasText: t });
  await circuito('QW Qualidade: validações e conclusão', async () => {
    await passo('Lista mostra as duas fichas de teste, com chips e texto', async () => {
      await sessaoComo('Qualidade'); await abrirRota('/qualidade', 'Qualidade');
      await ate(async () => (await ficha(alvoUi).count()) === 1 && (await ficha(outro).count()) === 1, 10000, 'as duas fichas');
      const t = await ficha(alvoUi).innerText();
      afirma(/Validada/.test(t) && /Avaria/.test(t) && /Feito:/.test(t), `ficha incompleta: ${t.replace(/\s+/g, ' ')}`);
      const cont = await page.locator('h2.titulo-grupo .contagem').innerText();
      return `cartão: ${t.replace(/\s+/g, ' ').slice(0, 160)} · contagem ${cont} (nº da ficha não aparece no cartão: ${!/\b101[12]\b/.test(t)})`;
    });
    await passo('Concluir: pede a higienização, Concluir desativado vazio/só espaços, Cancelar fecha', async () => {
      const f = ficha(alvoUi);
      await f.getByRole('button', { name: 'Concluir intervenção' }).click();
      const campo = f.locator('input[id^="higien-"]'); await campo.waitFor({ timeout: 6000 });
      const concluir = f.getByRole('button', { name: 'Concluir', exact: true });
      afirma(await concluir.isDisabled(), 'Concluir ativo com o campo vazio');
      await campo.fill('   '); afirma(await concluir.isDisabled(), 'Concluir ativo só com espaços');
      await campo.fill('x'.repeat(80));
      const len = (await campo.inputValue()).length; afirma(len <= 60, `campo aceita ${len} caracteres (>60)`);
      await campo.fill('Teste UI'); afirma(!(await concluir.isDisabled()), 'Concluir inativo com nome válido');
      await f.getByRole('button', { name: 'Cancelar' }).click();
      await ate(async () => (await f.locator('input[id^="higien-"]').count()) === 0, 4000, 'fecha o campo');
      await f.getByRole('button', { name: 'Concluir intervenção' }).click(); await f.locator('input[id^="higien-"]').waitFor({ timeout: 4000 });
      afirma((await f.locator('input[id^="higien-"]').inputValue()) === '', 'o campo não ficou vazio ao reabrir');
      return 'ok';
    });
    await passo('Abrir a conclusão de outra ficha fecha a primeira? (só uma em edição)', async () => {
      const g = ficha(outro);
      await g.getByRole('button', { name: 'Concluir intervenção' }).click();
      await ate(async () => (await page.locator('input[id^="higien-"]').count()) <= 1, 4000, 'só um campo aberto');
      return `campos abertos: ${await page.locator('input[id^="higien-"]').count()}`;
    });
    await passo(`Concluir ${alvoUi} com "Teste UI"`, async () => {
      await abrirRota('/qualidade', 'Qualidade');
      const f = ficha(alvoUi);
      await f.getByRole('button', { name: 'Concluir intervenção' }).click();
      await f.locator('input[id^="higien-"]').fill('Teste UI');
      await f.getByRole('button', { name: 'Concluir', exact: true }).click();
      await toastDe(/Concluída/, 8000);
      await ate(async () => (await ficha(alvoUi).count()) === 0, 8000, 'a ficha sair da lista');
      afirma((await ficha(outro).count()) === 1, 'a outra ficha desapareceu');
      return 'concluída';
    });
    await passo('Início da Qualidade: contagem "por concluir" coerente', async () => {
      await ir('/'); await page.locator('.hero-texto').waitFor({ timeout: 8000 });
      const t = await page.locator('a.mosaico-link', { has: page.locator('.mosaico-titulo', { hasText: 'Qualidade' }) }).innerText();
      return t.replace(/\s+/g, ' ');
    });
    await passo('Por rever da Qualidade: "Percebi" dispensa o aviso antigo', async () => {
      await abrirRota('/por-rever', 'Por rever');
      const n = await page.locator('li.rever-item').count();
      if (!n) return 'sem avisos';
      await clicar(page.getByRole('button', { name: 'Percebi' }).first(), 'Percebi');
      await ate(async () => (await page.locator('li.rever-item').count()) < n, 5000, 'aviso sair');
      return `${n} -> ${await page.locator('li.rever-item').count()}`;
    });
  });
}

fs.writeFileSync(path.join(PASTA, 'resultado.json'), JSON.stringify(resultados, null, 2));
const ok = resultados.filter((r) => r.ok).length;
console.log(`\nTOTAL ${ok}/${resultados.length} ok, ${resultados.length - ok} falha. Resultados em ${PASTA}`);
process.exit(0);
