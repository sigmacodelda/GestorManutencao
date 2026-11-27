// Cobertura extra da API: concorrência, mão de obra e custos, gestão, utilizadores, planeadas, formatos e limite de PIN.
// Uso: node cobertura-api-extra.mjs [--limite-pin]   (o limite de PIN bloqueia logins deste IP durante ~1 min: correr só avisando o agente UI)
import fs from 'node:fs';
import { http, entrar, PINS } from './cobertura-api.mjs';

const RUN = Math.random().toString(36).slice(2, 8);
const MARCA = `TESTE-E2E-${RUN}`;
const res = []; const bugs = [];
function chk(grupo, nome, cond, detalhe = '') { res.push({ grupo, nome, ok: !!cond, detalhe: cond ? '' : String(detalhe).slice(0, 300) }); console.log(`  ${cond ? 'ok   ' : 'FALHA'} [${grupo}] ${nome}${cond ? '' : ' :: ' + String(detalhe).slice(0, 300)}`); return !!cond; }
const bug = (id, titulo, evidencia) => { bugs.push({ id, titulo, evidencia }); console.log(`  BUG ${id}: ${titulo} :: ${String(evidencia).slice(0, 500)}`); };
const sj = (x) => JSON.stringify(x);
const T = (r, ...codes) => codes.includes(r.s);
const H = http;
const peca = async (tok) => (await H(tok, 'GET', '/pecas/codigo/560001')).j;

async function extra() {
  const { t, s, q } = await entrar();
  const eqId = (await H(t, 'GET', '/equipamentos')).j.find((e) => !e.foraDeServico && !e.obsoleto).id;
  const nova = async (txt) => { const r = await H(t, 'POST', '/intervencoes/avaria', { equipamentoId: eqId, anomalia: `${MARCA} ${txt}` }); return r.j?.id; };

  console.log('\n== Concorrência e idempotência');
  const f1 = await nova('concorrencia');
  chk('conc', 'ficha criada', !!f1, f1);
  const p0 = await peca(t);
  const r10 = await Promise.all(Array.from({ length: 6 }, (_, i) => H(t, 'POST', `/intervencoes/${f1}/materiais`, { design: `${MARCA} par ${i}`, qtt: 1, ref: 'P001', local: 'A-02-1-01' })));
  chk('conc', '6 materiais em paralelo: todos 200/201 sem 500', r10.every((r) => T(r, 200, 201)), sj(r10.map((r) => r.s)));
  const p1 = await peca(t);
  chk('conc', 'emConferencia sobe exatamente 6 (sem perdas/duplicações)', p1.emConferencia === p0.emConferencia + 6, `${p0.emConferencia} -> ${p1.emConferencia}`);
  const det = (await H(t, 'GET', `/intervencoes/${f1}/detalhe`)).j;
  chk('conc', 'detalhe mostra 6 materiais, todos com saída', det.materiais?.length === 6 && det.materiais.every((m) => m.saidaDada), sj(det.materiais?.map((m) => [m.qtt, m.saidaDada])));
  const ids = new Set(det.materiais?.map((m) => m.id)); chk('conc', 'ids dos materiais únicos', ids.size === 6, ids.size);
  // apagar em paralelo o mesmo material
  const mid = det.materiais[0].id;
  const rd = await Promise.all([1, 2, 3].map(() => H(t, 'DELETE', `/intervencoes/${f1}/materiais/${mid}`)));
  chk('conc', 'apagar o mesmo material 3x em paralelo: 1x 204 e resto 404, sem 500', rd.filter((r) => r.s === 204).length === 1 && rd.every((r) => T(r, 204, 404, 409)), sj(rd.map((r) => r.s)));
  const p2 = await peca(t); chk('stock', 'após apagar 1 material a reserva desce exatamente 1', p2.emConferencia === p0.emConferencia + 5, `${p2.emConferencia}`);
  // concluir em paralelo
  const rc = await Promise.all([1, 2, 3].map(() => H(t, 'POST', `/intervencoes/${f1}/concluir`, { feito: `${MARCA} feito`, inicio: '08:00:00', fim: '09:00:00' })));
  chk('conc', 'concluir 3x em paralelo sem 500', rc.every((r) => r.s < 500), sj(rc.map((r) => r.s))); console.log(`  concluir paralelo: ${sj(rc.map((r) => r.s))}`);
  // validar em paralelo: o stock só pode descer 5
  const pv0 = await peca(t);
  const rv = await Promise.all([1, 2, 3, 4].map(() => H(s, 'POST', `/validacao/${f1}`)));
  console.log(`  validar paralelo: ${sj(rv.map((r) => r.s))}`);
  chk('conc', 'validar 4x em paralelo sem 500', rv.every((r) => r.s < 500), sj(rv.map((r) => r.s)));
  const pv1 = await peca(t);
  chk('stock', 'stock desce exatamente 5 (uma só validação efetiva)', pv1.stock === pv0.stock - 5, `${pv0.stock} -> ${pv1.stock}`);
  chk('stock', 'emConferencia volta ao inicial', pv1.emConferencia === p0.emConferencia, `${pv1.emConferencia} vs ${p0.emConferencia}`);
  // devolver ficha validada
  { const r = await H(s, 'POST', `/validacao/${f1}/devolver`, { motivo: MARCA }); chk('estado', 'devolver ficha já validada -> 409/404 (não 200)', T(r, 409, 404, 400), `${r.s} ${sj(r.j)}`); }
  // qualidade em paralelo
  const rq = await Promise.all([1, 2, 3].map(() => H(q, 'POST', `/qualidade/${f1}/concluir`, { higienizacao: `${MARCA} h` })));
  console.log(`  qualidade paralelo: ${sj(rq.map((r) => r.s))}`);
  chk('conc', 'qualidade 3x em paralelo: no máximo 1x 200, sem 500', rq.filter((r) => r.s === 200).length <= 1 && rq.every((r) => r.s < 500), sj(rq.map((r) => r.s)));

  console.log('\n== Mão de obra e custos');
  const f2 = await nova('mao-de-obra');
  for (const [nome, corpo] of [['sem tecno', { horas: 1 }], ['horas 0', { tecno: 101, horas: 0 }], ['horas negativas', { tecno: 101, horas: -1 }], ['horas 1000 (máx 999,99)', { tecno: 101, horas: 1000 }], ['custo negativo', { tecno: 101, horas: 1, custoHora: -5 }], ['técnico inexistente', { tecno: 99999, horas: 1 }]]) {
    const r = await H(t, 'POST', `/intervencoes/${f2}/mao-de-obra`, corpo); chk('mo', `mão de obra ${nome} -> 400/409`, T(r, 400, 409, 404), `${r.s} ${sj(r.j)}`);
  }
  const mo = await H(t, 'POST', `/intervencoes/${f2}/mao-de-obra`, { tecno: 101, horas: 1.5, custoHora: 99 });
  chk('mo', 'técnico regista mão de obra', T(mo, 200, 201), `${mo.s} ${sj(mo.j)}`);
  if (mo.j && Number(mo.j.custoHora) > 0 || Number(mo.j?.custo) > 0) bug('B-PRECO-5', 'resposta da mão de obra expõe custo ao técnico', sj(mo.j));
  const lt = (await H(t, 'GET', `/intervencoes/${f2}/mao-de-obra`)).j; const ls = (await H(s, 'GET', `/intervencoes/${f2}/mao-de-obra`)).j;
  console.log(`  MO técnico: ${sj(lt)}\n  MO supervisor: ${sj(ls)}`);
  if (Array.isArray(lt) && lt.some((x) => Number(x.custoHora) > 0 || Number(x.custo) > 0)) bug('B-PRECO-6', 'GET mão de obra mostra custo ao técnico', sj(lt));
  { const r = await H(t, 'GET', '/tecnicos/101/custo-hora'); chk('perm', 'custo-hora ao técnico: 403 ou sem valor', T(r, 403) || !(Number(r.j?.custoHora ?? r.j) > 0), `${r.s} ${sj(r.j)}`); console.log(`  custo-hora técnico: ${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'GET', `/intervencoes/${f2}/compras`); chk('perm', 'compras ao técnico 403', r.s === 403, r.s); }
  { const r = await H(t, 'GET', '/fornecedores'); console.log(`  /fornecedores técnico: ${r.s}`); chk('perm', '/fornecedores técnico sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'DELETE', `/intervencoes/${f2}/mao-de-obra/NAOEXISTE`); chk('mo', 'apagar tempo inexistente 404', r.s === 404, r.s); }
  if (mo.j?.id) { const r = await H(t, 'DELETE', `/intervencoes/${f2}/mao-de-obra/${mo.j.id}`); chk('mo', 'apagar tempo 204', r.s === 204, r.s); }

  console.log('\n== Ficha manual e edição');
  for (const [nome, corpo] of [['sem equipamento', { tipo: 'Curativa' }], ['equipamento inexistente', { equipamentoId: 'XXX', tipo: 'Curativa', problema: MARCA }]]) {
    const r = await H(t, 'POST', '/intervencoes', corpo); chk('ficha', `ficha manual ${nome} -> 400/404`, T(r, 400, 404), `${r.s} ${sj(r.j)}`);
  }
  { const r = await H(q, 'POST', '/intervencoes', { equipamentoId: eqId, tipo: 'Curativa', problema: MARCA }); chk('perm', 'qualidade não cria ficha manual', r.s === 403, r.s); }
  { const r = await H(t, 'PUT', `/intervencoes/${f2}`, { problema: MARCA }); console.log(`  PUT ficha pelo técnico: ${r.s} ${sj(r.j).slice(0, 150)}`); chk('ficha', 'PUT ficha sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'PUT', `/intervencoes/NAOEXISTE`, { problema: MARCA }); chk('ficha', 'PUT ficha inexistente 404', T(r, 404, 400), r.s); }
  { const r = await H(t, 'GET', '/intervencoes/NAOEXISTE'); chk('ficha', 'GET ficha inexistente 404', r.s === 404, r.s); }
  { const r = await H(t, 'GET', '/intervencoes/NAOEXISTE/detalhe'); chk('ficha', 'detalhe inexistente 404', r.s === 404, r.s); }
  { const r = await H(t, 'GET', "/intervencoes/' OR 1=1 --/detalhe"); chk('ficha', 'id com SQL injection 404 sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'GET', '/intervencoes/recentes?dias=-5'); chk('ficha', 'recentes dias=-5 sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'GET', '/intervencoes/recentes?dias=abc'); chk('ficha', 'recentes dias=abc sem 500', r.s < 500, r.s); }
  { const r = await H(s, 'GET', '/intervencoes/todas?pagina=-1'); chk('ficha', 'todas pagina=-1 sem 500', r.s < 500, r.s); }
  { const r = await H(s, 'GET', '/intervencoes/todas?desde=xx'); chk('ficha', 'todas desde=xx sem 500', r.s < 500, r.s); }
  { const r = await H(s, 'GET', '/intervencoes/todas?estado=Concluida&q=' + encodeURIComponent("'%;")); chk('ficha', 'todas com filtro estranho sem 500', r.s < 500, r.s); }
  { const r = await H(t, 'POST', `/intervencoes/${f2}/assinatura-externa`, { nome: 'x', assinatura: 'nao-e-png' }); chk('ficha', 'assinatura externa inválida -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(t, 'POST', `/intervencoes/${f2}/fotos?nome=x.png`, 'nao-e-imagem'); chk('ficha', 'foto inválida sem 500', r.s < 500, `${r.s} ${sj(r.j).slice(0, 100)}`); }
  { const r = await H(t, 'GET', `/intervencoes/${f2}/fotos`); chk('ficha', 'listar fotos 200', r.s === 200, r.s); }

  console.log('\n== Gestão (supervisor)');
  { const r = await H(s, 'GET', '/gestao/opcoes'); chk('gestao', 'opções 200', r.s === 200, r.s); }
  { const r = await H(s, 'GET', '/gestao/equipamentos/NAOEXISTE'); chk('gestao', 'equipamento inexistente 404', r.s === 404, r.s); }
  { const r = await H(s, 'POST', '/gestao/equipamentos', {}); chk('gestao', 'criar equipamento vazio -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'POST', '/gestao/equipamentos', { design: 'x'.repeat(5000), ref: 'y'.repeat(500) }); chk('gestao', 'criar equipamento com textos gigantes -> 400 (sem 500)', r.s === 400, `${r.s} ${sj(r.j).slice(0, 100)}`); }
  { const r = await H(s, 'PUT', '/gestao/equipamentos/NAOEXISTE', { design: MARCA }); chk('gestao', 'editar equipamento inexistente 404', T(r, 404, 400), r.s); }
  { const r = await H(s, 'GET', '/gestao/periodicidades'); chk('gestao', 'periodicidades 200', r.s === 200, r.s); }
  { const r = await H(s, 'PUT', '/gestao/periodicidades/NAOEXISTE', { intervalo: 1 }); chk('gestao', 'periodicidade inexistente 404/400', T(r, 404, 400), r.s); }
  { const r = await H(s, 'POST', '/gestao/equipamentos/NAOEXISTE/instrucoes', { versao: '1' }); chk('gestao', 'instrução em equipamento inexistente 404/400', T(r, 404, 400), `${r.s}`); }
  { const r = await H(s, 'PUT', '/configuracao', { antecedencia: -3 }); chk('gestao', 'configuração antecedencia negativa -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); const c = (await H(s, 'GET', '/configuracao')).j; chk('gestao', 'configuração não foi alterada por pedido inválido', c.antecedencia === 7, sj(c)); }
  { const r = await H(s, 'PUT', '/configuracao', { antecedencia: 99999 }); chk('gestao', 'configuração antecedencia 99999 -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); const c = (await H(s, 'GET', '/configuracao')).j; if (c.antecedencia === 99999) { bug('B-CFG-1', 'configuração aceita antecedencia absurda', sj(c)); await H(s, 'PUT', '/configuracao', { antecedencia: 7 }); } }

  console.log('\n== Utilizadores e planeadas (só validações e leitura)');
  { const r = await H(s, 'POST', '/utilizadores/', { utilizador: '', nome: 'x' }); chk('util', 'criar utilizador sem código -> 400', r.s === 400, `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'POST', '/utilizadores/', { utilizador: 'ana', nome: 'Duplicada' }); chk('util', 'criar utilizador duplicado -> 409', r.s === 409, `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'POST', '/utilizadores/', { utilizador: 'x_y!', nome: 'x' }); chk('util', 'código com símbolos -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(s, 'POST', '/utilizadores/', { utilizador: 'zzz', nome: 'x', perfil: 99 }); chk('util', 'perfil inválido -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(s, 'PUT', '/utilizadores/chefe/ativo', { ativo: false }); chk('util', 'supervisor não se inativa a si (409)', r.s === 409, `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'PUT', '/utilizadores/naoexiste/ativo', { ativo: false }); chk('util', 'inativar inexistente 404', r.s === 404, r.s); }
  { const r = await H(s, 'PUT', '/utilizadores/ana/tecno', { tecno: 100 }); chk('util', 'tecno de outra pessoa -> 409', r.s === 409, `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'GET', '/utilizadores/'); chk('util', 'lista de utilizadores sem pin/hash', !/"pin"|hash|pepper/i.test(sj(r.j)), sj(r.j).slice(0, 200)); }
  { const r = await H(s, 'GET', '/planeadas'); chk('plan', 'planeadas 200', r.s === 200, r.s); }
  { const r = await H(s, 'PUT', '/planeadas/F1/tecnico', { tecno: 424242 }); chk('plan', 'atribuir técnico inexistente -> 400/404/409', T(r, 400, 404, 409), `${r.s} ${sj(r.j)}`); }
  { const r = await H(s, 'PUT', '/planeadas/NAOEXISTE/tecnico', { tecno: 101 }); chk('plan', 'atribuir em planeada inexistente 404', r.s === 404, r.s); }
  { const r = await H(s, 'PUT', '/planeadas/F1/tecnicos', { tecnos: [101, 101, 101] }); console.log(`  adicionais duplicados: ${r.s} ${sj(r.j).slice(0, 150)}`); chk('plan', 'adicionais duplicados sem 500', r.s < 500, r.s); }

  console.log('\n== Cabeçalhos e formatos');
  { const r = await H(t, 'GET', '/pecas'); chk('fmt', 'content-type JSON', /application\/json/.test(r.h.get('content-type') ?? ''), r.h.get('content-type')); }
  { const r = await H(null, 'GET', '/saude'); chk('fmt', '/saude 200 sem auth', r.s === 200, r.s); }
  { const r = await H(t, 'GET', '/rota-que-nao-existe'); chk('fmt', 'rota inexistente 404 (autenticado)', r.s === 404, r.s); }
  { const r = await H(t, 'POST', '/pecas', { x: 1 }); chk('fmt', 'POST em rota só GET -> 404/405', T(r, 404, 405), r.s); }
  { const r = await H(t, 'POST', `/intervencoes/${f1}/materiais`, '{"design": "x", "qtt": "abc"}', { 'Content-Type': 'application/json' }); chk('fmt', 'JSON com tipo errado -> 400 (não 500)', r.s === 400, `${r.s}`); }
  { const r = await H(t, 'POST', `/intervencoes/${f1}/materiais`, '{"design":', { 'Content-Type': 'application/json' }); chk('fmt', 'JSON truncado -> 400', r.s === 400, `${r.s}`); }
  { const r = await H(t, 'POST', `/intervencoes/${f1}/materiais`, 'design=x', { 'Content-Type': 'text/plain' }); chk('fmt', 'content-type errado -> 415/400', T(r, 415, 400), `${r.s}`); }
  { const r = await H(t, 'GET', '/pecas', undefined, { Origin: 'http://evil.example' }); console.log(`  CORS Allow-Origin: ${r.h.get('access-control-allow-origin')}`); chk('fmt', 'sem CORS aberto a origens externas', (r.h.get('access-control-allow-origin') ?? '') !== '*', r.h.get('access-control-allow-origin')); }
  { const r = await H(t, 'GET', '/pecas'); const hs = ['x-content-type-options', 'strict-transport-security']; console.log(`  cabeçalhos: ${hs.map((k) => `${k}=${r.h.get(k)}`).join(' ')}`); }

  if (process.argv.includes('--limite-pin')) {
    console.log('\n== Limite de tentativas de PIN');
    const cods = []; for (let i = 0; i < 14; i++) cods.push((await H(null, 'POST', '/auth/login', { pin: '0000' })).s);
    console.log(`  14 tentativas erradas: ${sj(cods)}`);
    if (!cods.includes(429)) { console.log('  sem 429 em 14 tentativas: a pilha de teste corre com Limites__LoginPorMinuto=500 (correr-circuito.ps1); limite não testável sem reiniciar a API com o valor por defeito (10)'); } else {
    chk('pin', 'após o limite devolve 429', true, '');
    const bom = await H(null, 'POST', '/auth/login', { pin: PINS.t }); chk('pin', 'PIN certo também é travado dentro da janela (429)', bom.s === 429, bom.s); console.log(`  PIN certo durante bloqueio: ${bom.s}`);
    const t0 = Date.now(); let livre = null; while (Date.now() - t0 < 75000) { const r = await H(null, 'POST', '/auth/login', { pin: PINS.t }); if (r.s === 200) { livre = Date.now() - t0; break; } await new Promise((x) => setTimeout(x, 3000)); }
    chk('pin', 'bloqueio levanta-se em <= 70s', livre !== null, 'sem recuperar'); console.log(`  recuperou após ${livre}ms`); }
  }

  const ok = res.filter((r) => r.ok).length, falha = res.length - ok;
  console.log(`\nTOTAL: ${ok} ok, ${falha} falhas, ${bugs.length} bugs`);
  for (const r of res.filter((x) => !x.ok)) console.log(` FALHA [${r.grupo}] ${r.nome} :: ${r.detalhe}`);
  fs.mkdirSync('resultados-cobertura', { recursive: true });
  fs.writeFileSync(`resultados-cobertura/extra_${RUN}.json`, JSON.stringify({ marca: MARCA, ok, falha, bugs, res }, null, 1));
}
await extra();
