// Testes de cobertura da API (modo memória). Uso: node cobertura-api-testes.mjs [grupo]
// Ajudantes (http, entrar, PINs) em cobertura-api.mjs. Dados criados levam o marcador TESTE-E2E-<run>.
import fs from 'node:fs';
import { http, entrar, PINS } from './cobertura-api.mjs';

const RUN = Math.random().toString(36).slice(2, 8);
const MARCA = `TESTE-E2E-${RUN}`;
const res = []; const bugs = [];
function chk(grupo, nome, cond, detalhe = '') { res.push({ grupo, nome, ok: !!cond, detalhe: cond ? '' : String(detalhe).slice(0, 300) }); console.log(`  ${cond ? 'ok   ' : 'FALHA'} [${grupo}] ${nome}${cond ? '' : ' :: ' + String(detalhe).slice(0, 300)}`); return !!cond; }
const bug = (id, titulo, evidencia) => { bugs.push({ id, titulo, evidencia }); console.log(`  BUG ${id}: ${titulo} :: ${String(evidencia).slice(0, 400)}`); };
const sj = (x) => JSON.stringify(x);
const T = (r, ...codes) => codes.includes(r.s);
const H = http;

async function testes() {
  const tk = await entrar();
  const { t, s, q } = tk;

  console.log('\n== Autenticação e permissões');
  for (const rota of ['/intervencoes', '/pecas', '/inventarios', '/equipamentos', '/qualidade', '/validacao', '/gestao/opcoes', '/utilizadores/', '/configuracao', '/intervencoes/todas', '/planeadas', '/auth/eu']) {
    const r = await H(null, 'GET', rota); chk('auth', `sem token ${rota} -> 401`, r.s === 401, r.s);
  }
  { const r = await H('abc.def.ghi', 'GET', '/pecas'); chk('auth', 'token inválido -> 401', r.s === 401, r.s); }
  for (const [rota, perfis] of [['/validacao', 's'], ['/qualidade', 'q'], ['/gestao/opcoes', 's'], ['/utilizadores/', 's'], ['/configuracao', 's'], ['/intervencoes/todas', 's'], ['/planeadas', 's'], ['/intervencoes/contadores', 's']]) {
    for (const p of ['t', 's', 'q']) {
      const r = await H(tk[p], 'GET', rota); const devia = perfis.includes(p);
      chk('perm', `${rota} perfil ${p} ${devia ? '200' : '403'}`, devia ? r.s === 200 : r.s === 403, `${r.s} ${sj(r.j).slice(0, 120)}`);
    }
  }
  for (const rota of ['/pecas', '/inventarios', '/intervencoes', '/equipamentos']) {
    const r = await H(q, 'GET', rota); chk('perm', `qualidade ${rota}: 200 ou 403 (nunca 500)`, T(r, 200, 403), r.s);
    console.log(`     qualidade ${rota} -> ${r.s}`);
  }
  { const r = await H(t, 'POST', '/validacao/F3'); chk('perm', 'técnico não valida (403)', r.s === 403, r.s); }
  { const r = await H(s, 'POST', '/validacao/F3/devolver', { motivo: '' }); chk('validacao', 'devolver sem motivo -> 400', r.s === 400, r.s); }
  { const r = await H(t, 'POST', '/qualidade/F3/concluir', { higienizacao: 'x' }); chk('perm', 'técnico não conclui qualidade (403)', r.s === 403, r.s); }
  { const r = await H(s, 'POST', '/qualidade/F3/concluir', { higienizacao: 'x' }); chk('perm', 'supervisor não conclui qualidade (403)', r.s === 403, r.s); }
  { const r = await H(t, 'POST', '/planeadas/gerar'); chk('perm', 'técnico não gera planeadas (403)', r.s === 403, r.s); }
  { const r = await H(t, 'PUT', '/configuracao', { antecedencia: 5 }); chk('perm', 'técnico não altera configuração (403)', r.s === 403, r.s); }
  { const r = await H(t, 'POST', '/utilizadores/', { utilizador: 'x', nome: 'x' }); chk('perm', 'técnico não cria utilizadores (403)', r.s === 403, r.s); }
  { const r = await H(t, 'PUT', '/planeadas/F1/tecnico', { tecno: 101 }); chk('perm', 'técnico não atribui planeadas (403)', r.s === 403, r.s); }
  { const r = await H(t, 'POST', '/intervencoes/F1/cancelar', { motivo: 'x' }); chk('perm', 'técnico não cancela (403)', r.s === 403, r.s); }
  { const r = await H(t, 'POST', '/gestao/equipamentos', {}); chk('perm', 'técnico não cria equipamento (403)', r.s === 403, r.s); }

  for (const [nome, corpo] of [['corpo vazio', {}], ['pin curto', { pin: '12' }], ['pin letras', { pin: 'abcd' }], ['pin nulo', { pin: null }], ['pin numérico (tipo errado)', { pin: 4829 }]]) {
    const r = await H(null, 'POST', '/auth/login', corpo); chk('auth', `login ${nome} -> 400/401 (nunca 500)`, T(r, 400, 401, 415), `${r.s} ${sj(r.j)}`);
  }
  { const r = await H(null, 'POST', '/auth/login', 'não é json'); chk('auth', 'login com corpo inválido não dá 500', r.s < 500, r.s); }
  { const r = await H(null, 'POST', '/auth/login', { pin: '0000' }); chk('auth', 'login PIN errado 401 e Cache-Control no-store', r.s === 401 && /no-store/.test(r.h.get('cache-control') ?? ''), `${r.s} ${r.h.get('cache-control')}`); }
  { const r = await H(t, 'GET', '/auth/eu'); chk('auth', '/auth/eu 200', r.s === 200, sj(r.j)); if (r.s === 200) chk('auth', '/auth/eu sem pin/hash', !/pin|hash|pepper/i.test(Object.keys(r.j).join(',')), sj(Object.keys(r.j))); }
  { const r = await H(t, 'POST', '/auth/pin', { pinAtual: PINS.t, pinNovo: '1111' }); chk('auth', 'mudar PIN para fraco -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', '/auth/pin', { pinAtual: '0000', pinNovo: '5927' }); chk('auth', 'mudar PIN com atual errado -> 401', r.s === 401, `${r.s}`); }
  { const r = await H(t, 'POST', '/auth/pin', { pinAtual: PINS.t, pinNovo: PINS.t }); chk('auth', 'mudar PIN igual -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(t, 'POST', '/auth/pin', { pinAtual: PINS.s, pinNovo: '5927' }); chk('auth', 'mudar PIN com PIN de outra pessoa -> 401', r.s === 401, `${r.s}`); }

  console.log('\n== Peças e preços');
  const pt = await H(t, 'GET', '/pecas'); const ps = await H(s, 'GET', '/pecas');
  chk('pecas', 'listar peças técnico/supervisor 200', pt.s === 200 && ps.s === 200, `${pt.s}/${ps.s}`);
  if (pt.s === 200 && pt.j.pecas?.some((p) => Number(p.preco) > 0)) bug('B-PRECO-1', 'GET /pecas devolve "preco" ao técnico (Operador)', `GET /pecas com token do técnico: ${sj(pt.j.pecas[0])}; esperado preco omitido/0`);
  { const r = await H(t, 'GET', '/pecas/codigo/560001'); chk('pecas', 'peça por código 200', r.s === 200, r.s); if (r.s === 200 && Number(r.j.preco) > 0) bug('B-PRECO-2', 'GET /pecas/codigo/{c} devolve preco ao técnico', sj(r.j)); }
  { const r = await H(t, 'GET', '/pecas/codigo/NAOEXISTE'); chk('pecas', 'código inexistente -> 404', r.s === 404, r.s); }
  { const r = await H(t, 'GET', '/pecas?texto=%25%27%3B--'); chk('pecas', 'pesquisa com caracteres especiais -> 200', r.s === 200, r.s); }
  { const r = await H(t, 'GET', '/pecas?texto=' + encodeURIComponent('x'.repeat(5000))); chk('pecas', 'pesquisa com 5000 chars sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'GET', '/pecas/P001/localizacoes'); chk('loc', 'localizações P001 200', r.s === 200, r.s);
    if (r.s === 200) { const somaL = r.j.lugares.reduce((a, l) => a + l.qtt, 0); chk('loc', 'localizado = soma dos lugares', Math.abs(somaL - r.j.localizado) < 1e-9, sj(r.j)); chk('loc', 'porArrumar = max(0, stock - localizado)', r.j.porArrumar === Math.max(0, r.j.stock - r.j.localizado), sj(r.j)); } }
  { const r = await H(t, 'GET', '/pecas/NAO/localizacoes'); chk('loc', 'localizações de peça inexistente 404', r.s === 404, r.s); }
  { const r = await H(t, 'GET', '/pecas/' + 'A'.repeat(40) + '/localizacoes'); chk('loc', 'ref com 40 chars -> 404', r.s === 404, r.s); }

  // arrumar / mover: só validações (os movimentos reais ficam para o agente UI)
  for (const [nome, corpo] of [['sem corpo', {}], ['local incompleto', { ref: 'P001', local: 'A-02', qtt: 1 }], ['qtt 0', { ref: 'P001', local: 'A-03-1-01', qtt: 0 }], ['qtt negativa', { ref: 'P001', local: 'A-03-1-01', qtt: -1 }], ['qtt 4 casas', { ref: 'P001', local: 'A-03-1-01', qtt: 0.0001 }], ['qtt gigante', { ref: 'P001', local: 'A-03-1-01', qtt: 1e12 }], ['ref vazia', { ref: '', local: 'A-03-1-01', qtt: 1 }]]) {
    const r = await H(t, 'POST', '/pecas/localizacoes', corpo); chk('loc', `arrumar ${nome} -> 400`, r.s === 400, `${r.s} ${sj(r.j)}`);
    if (r.s === 400) chk('loc', `arrumar ${nome}: mensagem útil no corpo`, typeof r.j === 'string' ? r.j.length > 3 : !!(r.j?.erro || r.j?.detail || r.j?.title), sj(r.j));
  }
  { const r = await H(t, 'POST', '/pecas/localizacoes', { ref: 'P001', local: 'A-03-1-01', qtt: 999 }); chk('loc', 'arrumar mais do que o buffer -> 400 com mensagem', r.s === 400 && /buffer|arrumar/i.test(sj(r.j)), `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', '/pecas/localizacoes', { ref: 'NAOEXISTE', local: 'A-03-1-01', qtt: 1 }); chk('loc', 'arrumar peça inexistente -> 404', r.s === 404, r.s); }
  { const r = await H(q, 'POST', '/pecas/localizacoes', { ref: 'P001', local: 'A-03-1-01', qtt: 1 }); chk('perm', 'qualidade não arruma (403)', r.s === 403, r.s); }
  { const r = await H(null, 'POST', '/pecas/localizacoes', { ref: 'P001', local: 'A-03-1-01', qtt: 1 }); chk('perm', 'arrumar sem token 401', r.s === 401, r.s); }
  for (const [nome, corpo] of [['mesmo lugar', { origem: 'A-02-1-01', destino: 'A-02-1-01', qtt: 1 }], ['origem inválida', { origem: 'xx', destino: 'A-03-1-01', qtt: 1 }], ['qtt 0', { origem: 'A-02-1-01', destino: 'A-03-1-01', qtt: 0 }], ['acima do disponível', { origem: 'A-02-1-01', destino: 'A-03-1-01', qtt: 99 }], ['origem sem stock', { origem: 'B-09-1-01', destino: 'A-03-1-01', qtt: 1 }]]) {
    const r = await H(t, 'POST', '/pecas/P001/mover', corpo); chk('loc', `mover ${nome} -> 400`, r.s === 400, `${r.s} ${sj(r.j)}`);
  }
  { const r = await H(t, 'GET', '/pecas/no-lugar?local=xx'); chk('loc', 'no-lugar local inválido -> 400', r.s === 400, r.s); }
  { const r = await H(t, 'GET', '/pecas/no-lugar'); chk('loc', 'no-lugar sem local -> 400', r.s === 400, r.s); }

  console.log('\n== Fichas, material e stock');
  const eq = (await H(t, 'GET', '/equipamentos')).j;
  const eqId = eq.find((e) => !e.foraDeServico && !e.obsoleto)?.id;
  for (const [nome, corpo] of [['sem equipamento', { anomalia: MARCA }], ['sem anomalia', { equipamentoId: eqId }], ['anomalia só espaços', { equipamentoId: eqId, anomalia: '   ' }], ['anomalia enorme', { equipamentoId: eqId, anomalia: 'x'.repeat(100000) }], ['equipamento inexistente', { equipamentoId: 'XXXX', anomalia: MARCA }]]) {
    const r = await H(t, 'POST', '/intervencoes/avaria', corpo); chk('ficha', `avaria ${nome} -> 400/404`, T(r, 400, 404, 413), `${r.s} ${sj(r.j).slice(0, 120)}`);
  }
  const av = await H(t, 'POST', '/intervencoes/avaria', { equipamentoId: eqId, anomalia: `${MARCA} avaria` });
  chk('ficha', 'criar avaria 200/201', T(av, 200, 201), `${av.s} ${sj(av.j)}`);
  const fid = av.j?.id; if (!fid) { console.log('sem ficha, a parar'); return fim(); }
  console.log(`  ficha criada ${fid} (${MARCA})`);
  { const r = await H(q, 'POST', '/intervencoes/avaria', { equipamentoId: eqId, anomalia: MARCA }); chk('perm', 'qualidade não cria avaria (403)', r.s === 403, r.s); }
  let d = (await H(t, 'GET', `/intervencoes/${fid}/detalhe`)).j;
  console.log(`  estado inicial: ${d?.estado}`);

  // Uma avaria nasce já EmCurso (a app não a "inicia"): iniciar de novo tem de recusar sem 500.
  chk('estado', 'avaria nasce EmCurso', /curso/i.test(d?.estado), d?.estado);
  { const r = await H(t, 'POST', `/intervencoes/${fid}/iniciar`); chk('idem', 'iniciar ficha já em curso: 200/409 sem 500', T(r, 200, 409), `${r.s} ${sj(r.j)}`); console.log(`  iniciar ficha já em curso: ${r.s}`); }

  for (const [nome, corpo] of [['sem design', { qtt: 1 }], ['qtt 0', { design: MARCA, qtt: 0 }], ['qtt negativa', { design: MARCA, qtt: -3 }], ['epv negativo', { design: MARCA, qtt: 1, epv: -1 }], ['qtt enorme', { design: MARCA, qtt: 1e12 }], ['design só espaços', { design: '  ', qtt: 1 }]]) {
    const r = await H(t, 'POST', `/intervencoes/${fid}/materiais`, corpo); chk('material', `material ${nome} -> 400`, r.s === 400, `${r.s} ${sj(r.j)}`);
  }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/materiais`, { design: MARCA, qtt: 1, ref: 'NAOEXISTE' }); chk('material', 'material com peça inexistente -> 400/404', T(r, 400, 404), `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/materiais`, { design: MARCA, qtt: 1, ref: 'P001', local: 'xx' }); chk('material', 'material com local inválido -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', `/intervencoes/NAOEXISTE/materiais`, { design: MARCA, qtt: 1 }); chk('material', 'material em ficha inexistente -> 404', r.s === 404, r.s); }

  const p0 = (await H(t, 'GET', '/pecas/codigo/560001')).j;
  const m1 = await H(t, 'POST', `/intervencoes/${fid}/materiais`, { design: `${MARCA} parafuso`, qtt: 2, ref: 'P001', local: 'A-02-1-01' });
  chk('material', 'juntar material P001 qtt 2', T(m1, 200, 201), `${m1.s} ${sj(m1.j)}`);
  const mid = m1.j?.id;
  if (mid) {
    chk('material', 'saída dada e lugar gravado', m1.j.saidaDada === true && m1.j.local === 'A-02-1-01', sj(m1.j));
    const p1 = (await H(t, 'GET', '/pecas/codigo/560001')).j;
    chk('stock', 'emConferencia +2, disponível -2, stock intacto', p1.emConferencia === p0.emConferencia + 2 && p1.stock === p0.stock && p1.disponivel === p0.disponivel - 2, `${sj(p0)} -> ${sj(p1)}`);
    const l1 = (await H(t, 'GET', '/pecas/P001/localizacoes')).j;
    chk('stock', 'lugar A-02-1-01 mostra reservado 2', l1.lugares.find((x) => x.texto === 'A-02-1-01')?.reservado === 2, sj(l1));
    { const r = await H(t, 'POST', `/intervencoes/${fid}/materiais/${mid}/saida`, {}); chk('idem', 'repetir saída já dada -> 409', r.s === 409, `${r.s} ${sj(r.j)}`); }
    { const r = await H(t, 'PUT', `/intervencoes/${fid}/materiais/${mid}`, { design: `${MARCA} parafuso`, qtt: 3, ref: 'P001', local: 'A-02-1-01' }); chk('material', 'editar qtt 2 -> 3', r.s === 200, `${r.s} ${sj(r.j)}`);
      const p2 = (await H(t, 'GET', '/pecas/codigo/560001')).j; chk('stock', 'após editar, emConferencia = base + 3', p2.emConferencia === p0.emConferencia + 3, sj(p2)); }
    { const r = await H(t, 'PUT', `/intervencoes/${fid}/materiais/${mid}`, { design: `${MARCA} parafuso`, qtt: 50, ref: 'P001', local: 'A-02-1-01' });
      chk('material', 'qtt acima do stock não bloqueia (200 + avisoStock)', r.s === 200 && !!r.j.avisoStock, `${r.s} ${sj(r.j)}`); console.log(`  aviso: ${r.j?.avisoStock}`);
      console.log(`  P001 com 50 pedidos: ${sj((await H(t, 'GET', '/pecas/codigo/560001')).j)}`); }
    { const r = await H(t, 'PUT', `/intervencoes/${fid}/materiais/${mid}`, { design: `${MARCA} parafuso`, qtt: 2, ref: 'P001', local: 'A-02-1-01' }); chk('material', 'voltar a qtt 2', r.s === 200, `${r.s}`); }
    { const r = await H(t, 'PUT', `/intervencoes/${fid}/materiais/${mid}`, { design: `${MARCA} parafuso`, qtt: 2, ref: 'P001', local: 'Z-09-9-99' }); chk('material', 'lugar sem stock da peça: sem 500', r.s < 500, `${r.s} ${sj(r.j)}`); console.log(`  lugar sem stock: ${r.s} ${sj(r.j).slice(0, 160)}`);
      const p = (await H(t, 'GET', '/pecas/codigo/560001')).j; chk('stock', 'após tentar mudar de lugar a reserva mantém-se 2', p.emConferencia === p0.emConferencia + 2, sj(p)); }
    const ml = (await H(t, 'GET', `/intervencoes/${fid}/materiais`)).j;
    chk('material', 'listar materiais do técnico', Array.isArray(ml) && ml.some((x) => x.id === mid), sj(ml));
    const det = (await H(t, 'GET', `/intervencoes/${fid}/detalhe`)).j;
    if (det.materiais?.some((x) => Number(x.epv) > 0) || Number(det.totRef) > 0 || Number(det.totMo) > 0 || Number(det.matInterno) > 0) bug('B-PRECO-3', 'detalhe da ficha mostra preços/totais ao técnico', sj({ totMo: det.totMo, totRef: det.totRef, matInterno: det.matInterno, moInterna: det.moInterna, epv: det.materiais?.map((x) => x.epv) }));
    else chk('perm', 'detalhe da ficha sem preços/totais ao técnico', true);
    const dets = (await H(s, 'GET', `/intervencoes/${fid}/detalhe`)).j;
    console.log(`  supervisor vê: matInterno=${dets.matInterno} totRef=${dets.totRef} epv=${dets.materiais?.map((x) => x.epv)}`);
    const m2 = await H(t, 'POST', `/intervencoes/${fid}/materiais`, { design: `${MARCA} rolamento`, qtt: 1, ref: 'P002' });
    chk('material', 'material sem stock (P002) permitido, só avisa', T(m2, 200, 201), `${m2.s} ${sj(m2.j)}`); console.log(`  P002: ${sj(m2.j)}`);
    if (m2.j?.id) { const r = await H(t, 'DELETE', `/intervencoes/${fid}/materiais/${m2.j.id}`); chk('material', 'apagar material sem stock 204', r.s === 204, r.s); }
    chk('perm', 'técnico ver ficha de outro (F2): 200/403/404', T(await H(t, 'GET', '/intervencoes/F2/detalhe'), 200, 403, 404), '');
    { const r = await H(t, 'POST', '/intervencoes/F2/materiais', { design: MARCA, qtt: 1 }); chk('perm', 'técnico NÃO junta material a ficha de outro (F2)', T(r, 403, 404, 409), `${r.s} ${sj(r.j)}`); if (T(r, 200, 201)) bug('B-PERM-1', 'técnico juntou material a ficha de outro técnico (F2)', sj(r.j)); }
    { const r = await H(t, 'POST', '/intervencoes/F2/iniciar'); chk('perm', 'técnico NÃO inicia ficha de outro (F2)', T(r, 403, 404, 409), `${r.s} ${sj(r.j)}`); }
  }

  { const r = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: '', inicio: '08:00:00', fim: '09:00:00' }); chk('estado', 'concluir sem texto -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: MARCA, inicio: '10:00:00', fim: '09:00:00' }); chk('estado', 'concluir com fim antes do início -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: MARCA }); chk('estado', 'concluir sem horas -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/pausar`); chk('estado', 'pausar', r.s === 200, `${r.s} ${sj(r.j)}`); const x = await H(t, 'POST', `/intervencoes/${fid}/pausar`); chk('idem', 'pausar 2ª vez sem 500', x.s < 500, x.s); const y = await H(t, 'POST', `/intervencoes/${fid}/retomar`); chk('estado', 'retomar', y.s === 200, `${y.s} ${sj(y.j)}`); }
  const cc = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: `${MARCA} feito`, inicio: '08:00:00', fim: '09:30:00' });
  chk('estado', 'concluir ficha', cc.s === 200, `${cc.s} ${sj(cc.j)}`);
  { const r = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: `${MARCA} feito`, inicio: '08:00:00', fim: '09:30:00' }); chk('idem', 'concluir 2ª vez sem 500', r.s < 500, `${r.s} ${sj(r.j)}`); console.log(`  concluir 2ª vez: ${r.s}`); }
  d = (await H(t, 'GET', `/intervencoes/${fid}/detalhe`)).j; chk('estado', 'estado = Concluida', /conclu/i.test(d.estado), d.estado);
  { const r = await H(t, 'POST', `/intervencoes/${fid}/materiais`, { design: MARCA, qtt: 1 }); chk('estado', 'não se junta material a ficha concluída (409)', r.s === 409, `${r.s} ${sj(r.j)}`); }
  if (mid) { const r = await H(t, 'DELETE', `/intervencoes/${fid}/materiais/${mid}`); chk('estado', 'não se apaga material de ficha concluída (409)', r.s === 409, `${r.s}`); }
  { const r = await H(t, 'POST', `/intervencoes/${fid}/iniciar`); chk('estado', 'não se reinicia ficha concluída', T(r, 409, 400, 403), `${r.s}`); }

  console.log('\n== Validação e Qualidade');
  { const l = (await H(s, 'GET', '/validacao')).j; chk('validacao', `${fid} aparece em /validacao`, l.some((x) => x.id === fid), sj(l.map((x) => x.id))); }
  { const l = (await H(q, 'GET', '/qualidade')).j; chk('qualidade', `${fid} ainda NÃO está em /qualidade`, !l.some((x) => x.id === fid), sj(l.map((x) => x.id))); }
  { const r = await H(s, 'POST', `/validacao/${fid}/devolver`, { motivo: `${MARCA} devolvida` }); chk('validacao', 'devolver ao técnico', r.s === 200, `${r.s} ${sj(r.j)}`);
    const dd = (await H(t, 'GET', `/intervencoes/${fid}/detalhe`)).j; console.log(`  após devolver: ${dd.estado} motivo=${dd.motivo}`); chk('validacao', 'após devolver já não está Concluida/Validada', !/^(conclu|valid)/i.test(dd.estado), dd.estado);
    console.log(`  P001 após devolver: ${sj((await H(t, 'GET', '/pecas/codigo/560001')).j)}`); }
  d = (await H(t, 'GET', `/intervencoes/${fid}/detalhe`)).j;
  if (!/^conclu/i.test(d.estado)) {
    const r1 = await H(t, 'POST', `/intervencoes/${fid}/iniciar`); console.log(`  reiniciar após devolução: ${r1.s}`);
    const c2 = await H(t, 'POST', `/intervencoes/${fid}/concluir`, { feito: `${MARCA} feito 2`, inicio: '08:00:00', fim: '09:45:00' }); chk('estado', 'concluir após devolução', c2.s === 200, `${c2.s} ${sj(c2.j)}`);
  }
  const pAntes = (await H(t, 'GET', '/pecas/codigo/560001')).j;
  const v = await H(s, 'POST', `/validacao/${fid}`); chk('validacao', 'validar ficha', v.s === 200, `${v.s} ${sj(v.j)}`);
  { const pD = (await H(t, 'GET', '/pecas/codigo/560001')).j;
    chk('stock', 'validar: stock -2 e emConferencia -2', pD.stock === pAntes.stock - 2 && pD.emConferencia === pAntes.emConferencia - 2, `${sj(pAntes)} -> ${sj(pD)}`);
    console.log(`  localizações após validar: ${sj((await H(t, 'GET', '/pecas/P001/localizacoes')).j)}`); }
  { const r = await H(s, 'POST', `/validacao/${fid}`); chk('idem', 'validar 2ª vez sem 500', r.s < 500, `${r.s} ${sj(r.j)}`); const p = (await H(t, 'GET', '/pecas/codigo/560001')).j; chk('stock', 'stock não desceu em duplicado', p.stock === pAntes.stock - 2, sj(p)); }
  d = (await H(s, 'GET', `/intervencoes/${fid}/detalhe`)).j; console.log(`  estado após validar: ${d.estado}`);
  { const l = (await H(q, 'GET', '/qualidade')).j; const naQual = l.some((x) => x.id === fid); console.log(`  em /qualidade: ${naQual}`);
    chk('qualidade', 'ficha validada passa à Qualidade', naQual, sj(l.map((x) => x.id)));
    if (naQual) {
      const qd = await H(q, 'GET', `/qualidade/${fid}/detalhe`); chk('qualidade', 'detalhe de qualidade 200', qd.s === 200, `${qd.s}`);
      if (qd.s === 200 && (Number(qd.j.totRef) > 0 || Number(qd.j.totMo) > 0 || qd.j.materiais?.some((x) => Number(x.epv) > 0))) bug('B-PRECO-4', 'detalhe de qualidade expõe preços/totais ao perfil Qualidade', sj({ totRef: qd.j.totRef, totMo: qd.j.totMo, epv: qd.j.materiais?.map((x) => x.epv) }));
      for (const [nome, corpo] of [['sem higienização', {}], ['vazia', { higienizacao: '  ' }], ['gigante', { higienizacao: 'x'.repeat(100000) }]]) { const r = await H(q, 'POST', `/qualidade/${fid}/concluir`, corpo); chk('qualidade', `qualidade ${nome} -> 400/413`, T(r, 400, 413), `${r.s} ${sj(r.j).slice(0, 100)}`); }
      { const r = await H(q, 'POST', '/qualidade/NAOEXISTE/concluir', { higienizacao: MARCA }); chk('qualidade', 'qualidade em ficha inexistente -> 404', r.s === 404, r.s); }
      const qc = await H(q, 'POST', `/qualidade/${fid}/concluir`, { higienizacao: `${MARCA} higienizado` }); chk('qualidade', 'concluir qualidade', qc.s === 200, `${qc.s} ${sj(qc.j)}`);
      { const r = await H(q, 'POST', `/qualidade/${fid}/concluir`, { higienizacao: `${MARCA} higienizado` }); chk('idem', 'concluir qualidade 2ª vez sem 500', r.s < 500, `${r.s} ${sj(r.j)}`); console.log(`  qualidade 2ª vez: ${r.s}`); }
      d = (await H(s, 'GET', `/intervencoes/${fid}/detalhe`)).j; console.log(`  estado após qualidade: ${d.estado} fechada=${d.fechada}`);
      const l2 = (await H(q, 'GET', '/qualidade')).j; chk('qualidade', 'ficha sai da lista de qualidade', !l2.some((x) => x.id === fid), sj(l2.map((x) => x.id)));
    } }
  { const r = await H(s, 'POST', `/intervencoes/${fid}/cancelar`, { motivo: MARCA }); chk('estado', 'ficha validada não se cancela (409/400)', T(r, 409, 400), `${r.s} ${sj(r.j)}`); }

  console.log('\n== Cancelar liberta reserva');
  { const a2 = await H(t, 'POST', '/intervencoes/avaria', { equipamentoId: eqId, anomalia: `${MARCA} cancelar` });
    const f2 = a2.j?.id; if (f2) {
      await H(t, 'POST', `/intervencoes/${f2}/iniciar`);
      const pb = (await H(t, 'GET', '/pecas/codigo/560001')).j;
      await H(t, 'POST', `/intervencoes/${f2}/materiais`, { design: `${MARCA} c`, qtt: 1, ref: 'P001', local: 'A-02-1-01' });
      const pm = (await H(t, 'GET', '/pecas/codigo/560001')).j; chk('stock', 'reserva de 1 na ficha a cancelar', pm.emConferencia === pb.emConferencia + 1, `${sj(pb)} -> ${sj(pm)}`);
      { const r = await H(t, 'POST', `/intervencoes/${f2}/cancelar`, { motivo: MARCA }); chk('perm', 'técnico não cancela (403)', r.s === 403, r.s); }
      { const r = await H(s, 'POST', `/intervencoes/${f2}/cancelar`, { motivo: '' }); chk('estado', 'cancelar sem motivo -> 400', r.s === 400, `${r.s}`); }
      const c = await H(s, 'POST', `/intervencoes/${f2}/cancelar`, { motivo: `${MARCA} cancelada` }); chk('estado', 'cancelar ficha com material', c.s === 200, `${c.s} ${sj(c.j)}`);
      const pc = (await H(t, 'GET', '/pecas/codigo/560001')).j; chk('stock', 'cancelar liberta a reserva sem mexer no stock', pc.emConferencia === pb.emConferencia && pc.stock === pb.stock, `${sj(pb)} -> ${sj(pc)}`);
      { const r = await H(s, 'POST', `/intervencoes/${f2}/cancelar`, { motivo: MARCA }); chk('idem', 'cancelar 2ª vez sem 500', r.s < 500, `${r.s}`); }
    } }

  console.log('\n== Inventários');
  const invs = (await H(t, 'GET', '/inventarios')).j; const inv = invs[0];
  chk('inv', 'listar inventários', Array.isArray(invs) && invs.length > 0, sj(invs));
  if (inv) {
    const iv = inv.id;
    { const r = await H(q, 'GET', '/inventarios'); chk('perm', 'qualidade vs inventários: 403 esperado', r.s === 403, r.s); }
    { const r = await H(t, 'GET', '/inventarios/xx!!'); chk('inv', 'id inválido -> 404', r.s === 404, r.s); }
    { const r = await H(t, 'GET', '/inventarios/' + 'a'.repeat(25)); chk('inv', 'inventário inexistente -> 404', r.s === 404, r.s); }
    for (const [nome, corpo] of [['sem peça', { local: 'A-02-1-01', qtt: 1 }], ['sem local', { ref: 'P001', qtt: 1 }], ['local inválido', { ref: 'P001', local: 'zz', qtt: 1 }], ['qtt negativa', { ref: 'P001', local: 'A-02-1-01', qtt: -1 }], ['qtt 4 casas', { ref: 'P001', local: 'A-02-1-01', qtt: 1.2345 }], ['qtt nula', { ref: 'P001', local: 'A-02-1-01' }], ['peça inexistente', { ref: 'NAOEXISTE', local: 'A-02-1-01', qtt: 1 }]]) {
      const r = await H(t, 'POST', `/inventarios/${iv}/linhas`, corpo); chk('inv', `contagem ${nome} -> 400`, r.s === 400, `${r.s} ${sj(r.j)}`);
    }
    const base = (await H(t, 'GET', `/inventarios/${iv}`)).j;
    const l1 = await H(t, 'POST', `/inventarios/${iv}/linhas`, { ref: 'P001', local: 'A-02-1-01', qtt: 3 });
    chk('inv', 'gravar contagem P001 A-02-1-01 = 3', l1.s === 200, `${l1.s} ${sj(l1.j)}`);
    console.log(`  linhas: ${sj(l1.j?.linhas)}`);
    const l2 = await H(t, 'POST', `/inventarios/${iv}/linhas`, { ref: 'P001', local: 'A-02-1-01', qtt: 4 });
    chk('inv', 'repetir contagem do mesmo lugar sem 500', l2.s < 500, `${l2.s} ${sj(l2.j)}`);
    const nl = (l2.j?.linhas ?? []).filter((x) => /P001/.test(sj(x)) && /A-02-1-01/.test(sj(x)));
    console.log(`  linhas P001/A-02-1-01 após 2 gravações: ${nl.length}`);
    if (nl.length > 1) bug('B-INV-1?', 'gravar 2x a mesma peça+lugar cria linhas duplicadas (confirmar se é intencional)', sj(nl));
    const linId = (l2.j?.linhas ?? []).find((x) => /P001/.test(sj(x)))?.id;
    if (linId) {
      { const r = await H(t, 'PUT', `/inventarios/${iv}/linhas/${linId}`, { qtt: -1 }); chk('inv', 'alterar qtt negativa -> 400', r.s === 400, r.s); }
      { const r = await H(t, 'PUT', `/inventarios/${iv}/linhas/${linId}`, { qtt: 5 }); chk('inv', 'alterar qtt para 5', r.s === 200, `${r.s} ${sj(r.j)}`); }
      { const r = await H(t, 'PUT', `/inventarios/${iv}/linhas/NAOEXISTE`, { qtt: 5 }); chk('inv', 'alterar linha inexistente -> 404', r.s === 404, r.s); }
      { const r = await H(s, 'POST', `/inventarios/${iv}/linhas`, { ref: 'P001', local: 'A-02-1-01', qtt: 1 }); chk('perm', 'supervisor em inventário do técnico -> 403', r.s === 403, `${r.s} ${sj(r.j)}`); if (T(r, 200)) bug('B-INV-PERM', 'outro utilizador gravou linha em inventário alheio', sj(r.j)); }
      { const r = await H(s, 'DELETE', `/inventarios/${iv}/linhas/${linId}`); chk('perm', 'supervisor não apaga linha alheia (403)', r.s === 403, r.s); }
      { const r = await H(s, 'POST', `/inventarios/${iv}/concluir`); chk('perm', 'supervisor não conclui inventário alheio (403)', r.s === 403, r.s); }
    }
    const atual = (await H(t, 'GET', `/inventarios/${iv}`)).j;
    for (const x of atual.linhas ?? []) if (!(base.linhas ?? []).some((b) => b.id === x.id)) { const r = await H(t, 'DELETE', `/inventarios/${iv}/linhas/${x.id}`); chk('inv', `apagar linha ${x.id}`, T(r, 200, 204), `${r.s}`); }
    const f = (await H(t, 'GET', `/inventarios/${iv}`)).j; chk('inv', 'inventário volta às linhas iniciais', (f.linhas ?? []).length === (base.linhas ?? []).length, sj(f.linhas));
  }
  fim();
}

function fim() {
  const ok = res.filter((r) => r.ok).length, falha = res.length - ok;
  console.log(`\nTOTAL: ${ok} ok, ${falha} falhas, ${bugs.length} bugs`);
  for (const r of res.filter((x) => !x.ok)) console.log(` FALHA [${r.grupo}] ${r.nome} :: ${r.detalhe}`);
  fs.mkdirSync('resultados-cobertura', { recursive: true });
  fs.writeFileSync(`resultados-cobertura/cobertura_${RUN}.json`, JSON.stringify({ marca: MARCA, ok, falha, bugs, res }, null, 1));
}
await testes();
