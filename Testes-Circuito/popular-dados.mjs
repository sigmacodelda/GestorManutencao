// Popula a base de testes (testes_land2) pela API com um conjunto completo de dados de teste e deixa-os ficar:
// equipamentos, instruções, tarefas, planeadas, avarias, fichas manuais, material, mão de obra, fotos, devolução e validação.
// Todos os dados levam o marcador TESTE-DADOS-<run> (equipamento, problema, material). Variáveis: API_URL, PIN_TECNICO, PIN_SUPERVISOR (nunca impressos nem gravados).
// Escreve em resultados-dados/<data>/: dados-criados.json (ids para comparar e limpar), snapshot-api.json (o que a API devolve) e relatorio-api.md.
import fs from 'node:fs';
import path from 'node:path';

const API = (process.env.API_URL ?? 'https://localhost:7257').replace(/\/$/, '');
const PIN_T = process.env.PIN_TECNICO;
const PIN_S = process.env.PIN_SUPERVISOR;
if (!PIN_T || !PIN_S) { console.error('Faltam PIN_TECNICO e PIN_SUPERVISOR.'); process.exit(2); }
if (!/^https:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(API)) { console.error('API_URL tem de ser https local.'); process.exit(2); }
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const RUN = Math.random().toString(36).replace(/[^a-z0-9]/g, '').padEnd(6, 'x').slice(0, 6);
const MARCA = `TESTE-DADOS-${RUN}`; // sem parênteses retos, % nem _: no LIKE do SQL seriam especiais
const hoje = new Date();
const pad = (n) => String(n).padStart(2, '0');
const HOJE = `${hoje.getFullYear()}-${pad(hoje.getMonth() + 1)}-${pad(hoje.getDate())}`;
const PASTA = path.resolve('resultados-dados', `${HOJE}_${RUN}`);
fs.mkdirSync(PASTA, { recursive: true });

// ------------------------------------------------------------------ registo
const resultados = [];
const afirma = (cond, msg) => { if (!cond) throw new Error(msg); };
async function passo(nome, fn) {
  const t0 = Date.now();
  try {
    const detalhe = await fn();
    resultados.push({ nome, ok: true, ms: Date.now() - t0, detalhe: detalhe ?? '' });
    console.log(`  ok   ${nome}`);
    return true;
  } catch (e) {
    resultados.push({ nome, ok: false, ms: Date.now() - t0, erro: String(e.message ?? e) });
    console.log(`  FALHA ${nome}: ${e.message ?? e}`);
    return false;
  }
}
const observacoes = []; // coisas que a API recusou ou fez de forma estranha, para o relatório
const nota = (t) => { observacoes.push(t); console.log(`  nota: ${t}`); };

// ------------------------------------------------------------------ API
async function http(token, metodo, rota, corpo, cabecalhos = {}) {
  const r = await fetch(API + rota, {
    method: metodo,
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}), ...(corpo !== undefined && !(corpo instanceof Uint8Array) ? { 'Content-Type': 'application/json' } : {}), ...cabecalhos },
    body: corpo === undefined ? undefined : corpo instanceof Uint8Array ? corpo : JSON.stringify(corpo)
  });
  let json = null;
  try { json = await r.json(); } catch { /* sem corpo */ }
  return { estado: r.status, json };
}
const ok = (r, que) => { afirma(r.estado === 200 || r.estado === 201 || r.estado === 204, `${que}: ${r.estado} ${JSON.stringify(r.json)?.slice(0, 200)}`); return r.json; };
const enc = encodeURIComponent;
const itens = (x) => (Array.isArray(x) ? x : x?.itens ?? []);
async function entrar(pin) {
  const r = await http(null, 'POST', '/auth/login', { pin });
  afirma(r.estado === 200 && r.json?.token, `login recusado (${r.estado})`);
  afirma(!r.json.pinProvisorio, 'o PIN é provisório');
  return r.json;
}

// ------------------------------------------------------------------ estado
const criados = { run: RUN, marca: MARCA, data: HOJE, equipamentos: [], instrucoes: [], tarefas: [], fichas: [], materiais: [], tempos: [], fotos: [], historicos: [] };
const snapshot = { equipamentos: {}, instrucoes: {}, tarefas: {}, fichas: {}, listaTodas: {}, vistaTecnico: {}, fotos: {}, historico: {} };
let tokenS, tokenT, loginS, loginT;
const eq = {}; // eq1..eq3
const inst = {}; // por equipamento
const fich = {}; // rótulo -> id
const rotuloFicha = (rotulo, id, extra = {}) => { fich[rotulo] = id; criados.fichas.push({ rotulo, id, ...extra }); };
const fotoJpeg = Uint8Array.from(Buffer.from('/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=', 'base64'));

async function planeadasDe(marca) {
  const r = await http(tokenS, 'GET', '/planeadas');
  ok(r, 'planeadas');
  return itens(r.json).filter((p) => (p.equipamento ?? '').includes(marca));
}
let conhecidas = new Set();
async function novaPlaneada() {
  const todas = await planeadasDe(MARCA);
  const novas = todas.filter((p) => !conhecidas.has(p.id));
  todas.forEach((p) => conhecidas.add(p.id));
  return novas;
}

async function material(idFicha, token, design, qtt, epv) {
  const m = ok(await http(token, 'POST', `/intervencoes/${enc(idFicha)}/materiais`, { design: `${MARCA} ${design}`, qtt, epv }), `material ${design}`);
  criados.materiais.push({ id: m.id, ficha: idFicha, design: `${MARCA} ${design}` });
  return m;
}

console.log(`Popular dados ${RUN}: API ${API}`);
const t0 = Date.now();

await passo('Login supervisor e técnico; contadores e configuração antes', async () => {
  loginS = await entrar(PIN_S); loginT = await entrar(PIN_T);
  tokenS = loginS.token; tokenT = loginT.token;
  afirma(loginS.perfil === 'Supervisor' && loginT.perfil === 'Operador', 'perfis inesperados');
  snapshot.contadoresAntes = ok(await http(tokenS, 'GET', '/intervencoes/contadores'), 'contadores');
  snapshot.configuracao = ok(await http(tokenS, 'GET', '/configuracao'), 'configuração');
  snapshot.opcoes = ok(await http(tokenS, 'GET', '/gestao/opcoes'), 'opções');
  snapshot.periodicidades = ok(await http(tokenS, 'GET', '/gestao/periodicidades'), 'periodicidades');
  // O login não devolve o número de técnico do PHC: lê-se da lista de utilizadores pelo código de cada sessão.
  const lista = ok(await http(tokenS, 'GET', '/utilizadores'), 'utilizadores');
  for (const [l, tk] of [[loginS, tokenS], [loginT, tokenT]]) {
    const codigo = ok(await http(tk, 'GET', '/auth/eu'), 'eu').utilizador;
    const u = lista.find((x) => x.codigo === codigo);
    afirma(u, 'utilizador de teste não encontrado na lista');
    l.tecno = u.tecno ?? 0; l.codigo = codigo;
  }
  criados.tecnico = { tecno: loginT.tecno, codigo: loginT.codigo, nome: loginT.nome };
  criados.supervisor = { tecno: loginS.tecno, codigo: loginS.codigo, nome: loginS.nome };
  afirma(loginT.tecno > 0, 'o técnico de teste não tem nº de técnico');
  return `técnico nº ${loginT.tecno}, supervisor nº ${loginS.tecno}`;
});
afirma(tokenS && tokenT, 'sem login');

// ---- 1. Equipamentos
const locais = snapshot.opcoes?.locais ?? []; const tipos = snapshot.opcoes?.tipos ?? [];
const eqDefs = [
  { k: 'eq1', ref: `TD-${RUN}-01`, design: `${MARCA} Compressor`, tipo: 'Compressores', marca: 'Atlas Copco', modelo: 'GA-37', serie: `SN-${RUN}-0001`, ano: 2019, valor: 18500.5, data: '2019-03-15', local: locais[0] ?? 'Oficina', email: 'manutencao@teste.invalid', manualPdf: 'manual-teste.pdf' },
  { k: 'eq2', ref: `TD-${RUN}-02`, design: `${MARCA} Empilhador`, tipo: tipos[0] ?? 'Empilhadores', marca: 'Linde', modelo: 'H25', serie: `SN-${RUN}-0002`, ano: 2021, valor: 32000, data: '2021-06-01', local: locais[1] ?? locais[0] ?? 'Armazém' },
  { k: 'eq3', ref: `TD-${RUN}-03`, design: `${MARCA} Câmara frio`, tipo: 'Frio', marca: 'Kide', modelo: 'CF-10', serie: '', ano: 0, valor: 0, local: 'Zona fria' }
];
await passo('Supervisor cria 3 equipamentos com campos variados', async () => {
  for (const d of eqDefs) {
    const { k, ...pedido } = d;
    const e = ok(await http(tokenS, 'POST', '/gestao/equipamentos', { ...pedido, estado: 'Em Serviço' }), `criar ${k}`);
    eq[k] = e; criados.equipamentos.push({ k, id: e.id, ref: e.ref, design: e.design, noInterno: e.noInterno });
  }
  return criados.equipamentos.map((e) => `${e.ref}(nº ${e.noInterno})`).join(', ');
});
afirma(eq.eq1 && eq.eq2 && eq.eq3, 'equipamentos não criados');

await passo('Supervisor edita o equipamento 1 e passa o 3 a Fora de Serviço e de volta (histórico de estado)', async () => {
  const lido = ok(await http(tokenS, 'GET', `/gestao/equipamentos/${enc(eq.eq1.id)}`), 'ler eq1');
  ok(await http(tokenS, 'PUT', `/gestao/equipamentos/${enc(eq.eq1.id)}`, { marca: 'Atlas Copco (editado)', local: 'Sala de compressores', ano: 2020, valor: 19999.99, versao: lido.versao }), 'editar eq1');
  ok(await http(tokenS, 'PUT', `/gestao/equipamentos/${enc(eq.eq3.id)}`, { estado: 'Fora de Serviço' }), 'eq3 fora de serviço');
  ok(await http(tokenS, 'PUT', `/gestao/equipamentos/${enc(eq.eq3.id)}`, { estado: 'Em Serviço' }), 'eq3 em serviço');
});

// ---- 2. Instruções, tarefas e planeadas
// No testes_land2 todas as periodicidades vêm com unidade 'nenhum' e intervalo 0: sem isto nenhuma tarefa gera planeada.
// Ativam-se só as necessárias (a API edita u_periocid) e repõem-se no fim, com os valores originais gravados em dados-criados.json.
criados.periodicidadesAlteradas = [];
const ativar = { Semanal: ['dias', 7], Quinzenal: ['dias', 14], Mensal: ['meses', 1], Trimestral: ['meses', 3], Anual: ['meses', 12] };
await passo('Periodicidades Semanal, Quinzenal, Mensal, Trimestral e Anual ativadas temporariamente (dias/meses) para gerar planeadas', async () => {
  const lista = ok(await http(tokenS, 'GET', '/gestao/periodicidades'), 'periodicidades');
  for (const [nome, [unidade, intervalo]] of Object.entries(ativar)) {
    const p = lista.filter((x) => x.periodo === nome).sort((a, b) => Number(b.aviso) - Number(a.aviso))[0];
    afirma(p, `periodicidade ${nome} não existe`);
    criados.periodicidadesAlteradas.push({ id: p.id, periodo: nome, antes: { unidade: p.unidade, intervalo: p.intervalo, aviso: p.aviso } });
    const r = ok(await http(tokenS, 'PUT', `/gestao/periodicidades/${enc(p.id)}`, { unidade, intervalo, aviso: p.aviso, concorrencia: p.concorrencia }), `periodicidade ${nome}`);
    afirma(r.unidade === unidade && r.intervalo === intervalo, `periodicidade ${nome} gravada como ${r.unidade}/${r.intervalo}`);
  }
});
async function reporPeriodicidades() {
  for (const p of criados.periodicidadesAlteradas ?? []) {
    const atual = ok(await http(tokenS, 'GET', '/gestao/periodicidades'), 'periodicidades').find((x) => x.id === p.id);
    if (!atual) continue;
    await http(tokenS, 'PUT', `/gestao/periodicidades/${enc(p.id)}`, { unidade: p.antes.unidade, intervalo: p.antes.intervalo, aviso: p.antes.aviso, concorrencia: atual.concorrencia });
  }
}
await passo('Instruções (automáticas e uma versão nova) e tarefas por instrução; cada tarefa gera a sua planeada', async () => {
  for (const k of ['eq1', 'eq2', 'eq3']) {
    const l = ok(await http(tokenS, 'GET', `/gestao/equipamentos/${enc(eq[k].id)}/instrucoes`), `instruções ${k}`);
    afirma(l.length >= 1, `${k} sem instrução automática`);
    inst[k] = l[0];
  }
  async function tarefa(k, instr, periodo, descricao, tipoManut, responsabilidade, responsavel, rotulo) {
    const t = ok(await http(tokenS, 'POST', `/gestao/instrucoes/${enc(instr.id)}/tarefas`, { periodo, descricao: `${MARCA} ${descricao}`, tipoManut, responsabilidade, responsavel }), `tarefa ${rotulo}`);
    criados.tarefas.push({ rotulo, id: t.id, instrucao: instr.id, equipamento: eq[k].id, periodo });
    const novas = await novaPlaneada();
    if (novas.length === 1) rotuloFicha(`plan-${rotulo}`, novas[0].id, { tarefa: t.id, origem: 'tarefa' });
    else nota(`tarefa ${rotulo}: ${novas.length} planeadas novas (esperava 1)${t.aviso ? ' · aviso: ' + t.aviso : ''}`);
    return t;
  }
  await tarefa('eq1', inst.eq1, 'Semanal', 'Verificar nível de óleo e purga de condensados', 'Interna', 'Técnico interno', loginT.nome.slice(0, 25), 'eq1-semanal');
  await tarefa('eq1', inst.eq1, 'Mensal', 'Revisão anual do separador (empresa externa)', 'Externa', 'Empresa Externa', 'Fornecedor', 'eq1-mensal-ext');
  await tarefa('eq2', inst.eq2, 'Mensal', 'Verificar garfos, correntes e travões', 'Interna', 'Técnico interno', '', 'eq2-mensal');
  await tarefa('eq2', inst.eq2, 'Trimestral', 'Trocar filtro hidráulico', 'Interna', 'Técnico interno', '', 'eq2-trimestral');
  // eq3: instrução 1 com tarefa anual (gera planeada); marca-se obsoleta (cancela-a por sistema); instrução V2 nova com a tarefa outra vez.
  await tarefa('eq3', inst.eq3, 'Anual', 'Inspeção anual da câmara (instrução V1)', 'Interna', 'Técnico interno', '', 'eq3-anual-v1');
  const obs = ok(await http(tokenS, 'PUT', `/gestao/instrucoes/${enc(inst.eq3.id)}`, { obsoleta: true, obs: `${MARCA} instrução substituída pela V2` }), 'instrução V1 obsoleta');
  const v2 = ok(await http(tokenS, 'POST', `/gestao/equipamentos/${enc(eq.eq3.id)}/instrucoes`, { versao: 'V2', obs: `${MARCA} nova versão do plano` }), 'instrução V2');
  criados.instrucoes.push({ equipamento: eq.eq3.id, id: v2.id, versao: 'V2' });
  inst.eq3v2 = v2;
  await tarefa('eq3', v2, 'Anual', 'Inspeção anual da câmara (V2)', 'Interna', 'Técnico interno', '', 'eq3-anual-v2');
  // eq1: segunda versão ativa com uma tarefa quinzenal
  const v2b = ok(await http(tokenS, 'POST', `/gestao/equipamentos/${enc(eq.eq1.id)}/instrucoes`, { versao: 'V2', obs: `${MARCA} plano reforçado` }), 'instrução V2 eq1');
  criados.instrucoes.push({ equipamento: eq.eq1.id, id: v2b.id, versao: 'V2' });
  inst.eq1v2 = v2b;
  await tarefa('eq1', v2b, 'Quinzenal', 'Inspeção visual de fugas', 'Interna', 'Técnico interno', '', 'eq1-quinzenal');
  for (const k of ['eq1', 'eq2', 'eq3']) criados.instrucoes.push({ equipamento: eq[k].id, id: inst[k].id, versao: inst[k].versao, automatica: true });
  return `${criados.tarefas.length} tarefas, ${Object.keys(fich).length} planeadas ligadas`;
});

// periodicidade: só edição (u_periocid existente); mostra-se sem alterar (a tabela é global, não é de teste)
nota('Periodicidades (u_periocid): a API só edita unidade/intervalo de linhas existentes (não cria); foram ativadas durante o teste e repostas no fim.');
nota('POST /planeadas/gerar não foi chamado: geraria planeadas para todos os equipamentos da base, sem o marcador, e não se conseguiriam limpar. As planeadas nasceram ao gravar cada tarefa.');

// ---- 3. Planeadas: fluxos
// Só a primeira planeada da zona se pode começar, salvo se a configuração deixar avançar fora de ordem. Como a base de testes
// já tem outras planeadas (execuções anteriores), liga-se essa opção só durante as planeadas e repõe-se logo a seguir.
const configAntes = ok(await http(tokenS, 'GET', '/configuracao'), 'configuração antes das planeadas');
const defineAvanca = (v) => http(tokenS, 'PUT', '/configuracao', { ...configAntes, avancaOrdem: v });
if (!configAntes.avancaOrdem) ok(await defineAvanca(true), 'avançar fora de ordem (temporário)');
await passo('Planeada P1 (eq1 semanal): atribuir, começar, 2 materiais, concluir, mão de obra e validar', async () => {
  const id = fich['plan-eq1-semanal']; afirma(id, 'sem planeada P1');
  ok(await http(tokenS, 'PUT', `/planeadas/${enc(id)}/tecnico`, { tecno: loginT.tecno }), 'atribuir');
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/iniciar`), 'começar');
  await material(id, tokenT, 'Óleo 5L', 2, 14.75);
  await material(id, tokenT, 'Filtro ar', 1, 22.5);
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/concluir`, { feito: `${MARCA} nível de óleo reposto, purga feita`, inicio: '08:00:00', fim: '08:45:00' }), 'concluir');
  const mo = ok(await http(tokenS, 'POST', `/intervencoes/${enc(id)}/mao-de-obra`, { tecno: loginT.tecno, horas: 0.75, custoHora: 18.4 }), 'mão de obra');
  criados.tempos.push({ id: mo.id, ficha: id });
  ok(await http(tokenS, 'POST', `/validacao/${enc(id)}`), 'validar');
});

await passo('Planeada P2 (eq2 mensal): atribuir, começar, concluir, supervisor devolve com motivo, técnico material e reconclui (fica por validar)', async () => {
  const id = fich['plan-eq2-mensal']; afirma(id, 'sem planeada P2');
  ok(await http(tokenS, 'PUT', `/planeadas/${enc(id)}/tecnico`, { tecno: loginT.tecno }), 'atribuir');
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/iniciar`), 'começar');
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/concluir`, { feito: `${MARCA} garfos e correntes verificados`, inicio: '09:00:00', fim: '09:30:00' }), 'concluir 1');
  ok(await http(tokenS, 'POST', `/validacao/${enc(id)}/devolver`, { motivo: `${MARCA} faltou verificar o travão de mão` }), 'devolver');
  snapshot.p2Devolvida = ok(await http(tokenT, 'GET', `/intervencoes/${enc(id)}`), 'técnico vê a devolvida');
  await material(id, tokenT, 'Pastilha trav.', 4, 6.2);
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/concluir`, { feito: `${MARCA} travão de mão verificado e ajustado`, inicio: '09:00:00', fim: '10:10:00' }), 'concluir 2');
});

await passo('Planeada P3 (eq2 trimestral): atribuir, começar e deixar em curso com técnico adicional (supervisor)', async () => {
  const id = fich['plan-eq2-trimestral']; afirma(id, 'sem planeada P3');
  ok(await http(tokenS, 'PUT', `/planeadas/${enc(id)}/tecnico`, { tecno: loginT.tecno }), 'atribuir');
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(id)}/iniciar`), 'começar');
  await material(id, tokenT, 'Filtro hidr.', 1, 48);
  if (loginS.tecno > 0) {
    const r = await http(tokenS, 'PUT', `/planeadas/${enc(id)}/tecnicos`, { tecnos: [loginS.tecno] });
    if (r.estado !== 200) nota(`técnico adicional recusado: ${r.estado} ${JSON.stringify(r.json)}`);
  } else nota('o supervisor de teste não tem nº de técnico: sem técnico adicional');
});

await passo('Planeadas P4 (eq3 anual V2, atribuída e por começar) e P5 (eq1 mensal externa, sem técnico) ficam Planeadas', async () => {
  const id4 = fich['plan-eq3-anual-v2']; afirma(id4, 'sem planeada P4');
  ok(await http(tokenS, 'PUT', `/planeadas/${enc(id4)}/tecnico`, { tecno: loginT.tecno }), 'atribuir P4');
});

// ---- 4. Avarias do técnico
await passo('Configuração reposta (avançar fora de ordem como estava)', async () => {
  if (!configAntes.avancaOrdem) ok(await defineAvanca(false), 'repor configuração');
  const c = ok(await http(tokenS, 'GET', '/configuracao'), 'configuração');
  afirma(c.avancaOrdem === configAntes.avancaOrdem, 'a configuração não foi reposta');
});

await passo('Avaria A1 (eq1): técnico regista, junta material, conclui; supervisor valida', async () => {
  const r = ok(await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: eq.eq1.id, anomalia: `${MARCA} avaria A1 compressor não arranca` }), 'avaria A1');
  rotuloFicha('avaria-A1', r.id, { origem: 'tecnico' });
  await material(r.id, tokenT, 'Contactor', 1, 35);
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(r.id)}/concluir`, { feito: `${MARCA} contactor substituído`, inicio: '10:00:00', fim: '10:40:00' }), 'concluir A1');
  ok(await http(tokenS, 'POST', `/validacao/${enc(r.id)}`), 'validar A1');
});
await passo('Avaria A2 (eq2): em curso, com material, foto, horas e observações do técnico', async () => {
  const r = ok(await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: eq.eq2.id, anomalia: `${MARCA} avaria A2 fuga de óleo hidráulico` }), 'avaria A2');
  rotuloFicha('avaria-A2', r.id, { origem: 'tecnico' });
  await material(r.id, tokenT, 'Vedante 40mm', 3, 1.2);
  const f = await http(tokenT, 'POST', `/intervencoes/${enc(r.id)}/fotos?nome=${enc(`foto-${MARCA}`)}`, fotoJpeg, { 'Content-Type': 'image/jpeg' });
  if (f.estado === 200 && f.json?.id) criados.fotos.push({ id: f.json.id, ficha: r.id }); else nota(`foto recusada: ${f.estado} ${JSON.stringify(f.json)}`);
  const p = await http(tokenT, 'PUT', `/intervencoes/${enc(r.id)}`, { obs: `${MARCA} observação do técnico`, horasConf: 0.5, versao: undefined });
  if (p.estado !== 200) nota(`edição da A2 pelo técnico: ${p.estado} ${JSON.stringify(p.json)}`);
});
await passo('Avaria A3 (eq3): concluída sem material, por validar', async () => {
  const r = ok(await http(tokenT, 'POST', '/intervencoes/avaria', { equipamentoId: eq.eq3.id, anomalia: `${MARCA} avaria A3 temperatura alta` }), 'avaria A3');
  rotuloFicha('avaria-A3', r.id, { origem: 'tecnico' });
  ok(await http(tokenT, 'POST', `/intervencoes/${enc(r.id)}/concluir`, { feito: `${MARCA} termostato recalibrado`, inicio: '14:00:00', fim: '14:20:00' }), 'concluir A3');
});

// ---- 5. Fichas manuais do supervisor
await passo('Ficha manual corretiva M1 (eq1) completada pelo supervisor: horas, frio, resolução, mão de obra, material, observações', async () => {
  const r = await http(tokenS, 'POST', '/intervencoes', { equipamentoId: eq.eq1.id, tipo: 'Curativa', tipoManut: 'Interna', problema: `${MARCA} ficha manual corretiva M1` });
  if (r.estado !== 200) { nota(`ficha manual do supervisor recusada: ${r.estado} ${JSON.stringify(r.json)}`); throw new Error('M1 não criada'); }
  rotuloFicha('manual-M1', r.json.id, { origem: 'supervisor' });
  const det = ok(await http(tokenS, 'GET', `/intervencoes/${enc(r.json.id)}/detalhe`), 'detalhe M1');
  const ini = `${HOJE}T08:00:00`, fim = `${HOJE}T09:30:00`;
  const p = await http(tokenS, 'PUT', `/intervencoes/${enc(r.json.id)}`, { horaInicio: ini, horaFim: fim, horasFrio: 1.5, horasConf: 1, resolucao: `${MARCA} resolução escrita pelo supervisor`, obs: `${MARCA} observações da ficha M1`, versao: det.versao });
  if (p.estado !== 200) nota(`PUT ficha M1: ${p.estado} ${JSON.stringify(p.json)}`);
  const mo = await http(tokenS, 'POST', `/intervencoes/${enc(r.json.id)}/mao-de-obra`, { tecno: loginT.tecno, horas: 1.5, custoHora: 20 });
  if (mo.estado === 200) criados.tempos.push({ id: mo.json.id, ficha: r.json.id }); else nota(`mão de obra M1: ${mo.estado} ${JSON.stringify(mo.json)}`);
  await material(r.json.id, tokenS, 'Correia trap.', 2, 9.99);
});
await passo('Ficha manual preventiva M2 (eq2, externa, mensal): fornecedor e documento externo, fica em curso', async () => {
  const r = await http(tokenS, 'POST', '/intervencoes', { equipamentoId: eq.eq2.id, tipo: 'Preventiva', tipoManut: 'Externa', periodicidade: 'Mensal', problema: `${MARCA} ficha manual preventiva M2 externa` });
  if (r.estado !== 200) { nota(`ficha manual preventiva recusada: ${r.estado} ${JSON.stringify(r.json)}`); throw new Error('M2 não criada'); }
  rotuloFicha('manual-M2', r.json.id, { origem: 'supervisor' });
  const forn = await http(tokenS, 'GET', '/fornecedores?q=sa');
  const f1 = (Array.isArray(forn.json) ? forn.json : [])[0];
  const det = ok(await http(tokenS, 'GET', `/intervencoes/${enc(r.json.id)}/detalhe`), 'detalhe M2');
  const corpo = { docExterno: `DOC-${RUN}`, moExterna: 120, matExterno: 45.5, versao: det.versao };
  if (f1) { corpo.noFornecedor = f1.no; criados.fornecedorUsado = f1.no; } else nota('sem fornecedores encontrados: M2 sem nº de fornecedor');
  const p = await http(tokenS, 'PUT', `/intervencoes/${enc(r.json.id)}`, corpo);
  if (p.estado !== 200) nota(`PUT ficha M2: ${p.estado} ${JSON.stringify(p.json)}`);
});
await passo('Ficha manual M3 (eq3) criada e cancelada pelo supervisor com motivo', async () => {
  const r = await http(tokenS, 'POST', '/intervencoes', { equipamentoId: eq.eq3.id, tipo: 'Curativa', tipoManut: 'Interna', problema: `${MARCA} ficha manual M3 a cancelar` });
  if (r.estado !== 200) throw new Error(`M3: ${r.estado}`);
  rotuloFicha('manual-M3-cancelada', r.json.id, { origem: 'supervisor' });
  ok(await http(tokenS, 'POST', `/intervencoes/${enc(r.json.id)}/cancelar`, { motivo: `${MARCA} criada por engano` }), 'cancelar M3');
});

// ---- 6. Fotografia final do que a API devolve
await passo('Snapshot da API: equipamentos, instruções, tarefas, fichas (detalhe supervisor, lista, vista do técnico), contadores depois', async () => {
  for (const e of criados.equipamentos) {
    snapshot.equipamentos[e.id] = ok(await http(tokenS, 'GET', `/gestao/equipamentos/${enc(e.id)}`), 'eq');
    snapshot.historico[e.id] = ok(await http(tokenS, 'GET', `/gestao/equipamentos/${enc(e.id)}/historico-estado`), 'hist');
    const l = ok(await http(tokenS, 'GET', `/gestao/equipamentos/${enc(e.id)}/instrucoes`), 'instruções');
    for (const i of l) {
      snapshot.instrucoes[i.id] = i;
      const ts = ok(await http(tokenS, 'GET', `/gestao/instrucoes/${enc(i.id)}/tarefas`), 'tarefas');
      ts.forEach((t) => { snapshot.tarefas[t.id] = t; });
    }
  }
  let pagina = 1, mais = true;
  while (mais && pagina <= 20) {
    const r = ok(await http(tokenS, 'GET', `/intervencoes/todas?q=${enc(MARCA)}&pagina=${pagina}`), 'todas');
    for (const it of itens(r)) snapshot.listaTodas[it.id] = it;
    mais = !!r.temMais; pagina++;
  }
  // As planeadas/em curso sem problema com o marcador (nascem da tarefa) não aparecem na pesquisa por texto: lista por equipamento.
  const pl = itens(ok(await http(tokenS, 'GET', '/planeadas'), 'planeadas')).filter((p) => (p.equipamento ?? '').includes(MARCA));
  for (const it of pl) snapshot.listaTodas[it.id] = snapshot.listaTodas[it.id] ?? it;
  // Planeadas seguintes geradas ao concluir (não foram criadas por nenhum pedido directo): entram no registo para o detalhe, a comparação e a limpeza.
  for (const it of pl) if (!criados.fichas.some((f) => f.id === it.id)) rotuloFicha(`plan-seguinte-${it.numero}`, it.id, { origem: 'gerada ao concluir' });
  for (const f of criados.fichas) {
    const d = await http(tokenS, 'GET', `/intervencoes/${enc(f.id)}/detalhe`);
    snapshot.fichas[f.id] = d.json;
    const c = await http(tokenS, 'GET', `/intervencoes/${enc(f.id)}/compras`);
    if (c.estado === 200 && Array.isArray(c.json) && c.json.length) snapshot.fichas[f.id].compras = c.json;
    const v = await http(tokenT, 'GET', `/intervencoes/${enc(f.id)}`);
    snapshot.vistaTecnico[f.id] = { estado: v.estado, dto: v.json };
    const fo = await http(tokenS, 'GET', `/intervencoes/${enc(f.id)}/fotos`);
    if (fo.estado === 200 && Array.isArray(fo.json) && fo.json.length) snapshot.fotos[f.id] = fo.json;
  }
  snapshot.vistaTecnicoLista = ok(await http(tokenT, 'GET', '/intervencoes'), 'lista do técnico');
  snapshot.contadoresDepois = ok(await http(tokenS, 'GET', '/intervencoes/contadores'), 'contadores depois');
  snapshot.validacao = itens(ok(await http(tokenS, 'GET', '/validacao'), 'validação')).filter((x) => (x.equipamento ?? '').includes(MARCA)).map((x) => x.id);
});

await passo('Periodicidades repostas aos valores originais (as planeadas já geradas ficam)', async () => {
  await reporPeriodicidades();
  const depois = ok(await http(tokenS, 'GET', '/gestao/periodicidades'), 'periodicidades');
  for (const p of criados.periodicidadesAlteradas) {
    const a = depois.find((x) => x.id === p.id);
    afirma(a && a.unidade === p.antes.unidade && a.intervalo === p.antes.intervalo, `periodicidade ${p.periodo} não foi reposta`);
  }
});

// ------------------------------------------------------------------ saída
fs.writeFileSync(path.join(PASTA, 'dados-criados.json'), JSON.stringify(criados, null, 2));
fs.writeFileSync(path.join(PASTA, 'snapshot-api.json'), JSON.stringify(snapshot, null, 2));
fs.writeFileSync(path.join(PASTA, 'resultados.json'), JSON.stringify({ resultados, observacoes }, null, 2));
const falhas = resultados.filter((r) => !r.ok);
const md = [`# Popular dados de teste ${MARCA}`, '', `Duração: ${Math.round((Date.now() - t0) / 1000)} s · passos: ${resultados.length} · falhas: ${falhas.length}`, '',
  `Criados: ${criados.equipamentos.length} equipamentos, ${criados.instrucoes.length} instruções, ${criados.tarefas.length} tarefas, ${criados.fichas.length} fichas, ${criados.materiais.length} materiais, ${criados.tempos.length} mão de obra, ${criados.fotos.length} fotos.`, '',
  '| Passo | Resultado | ms |', '|---|---|---|',
  ...resultados.map((r) => `| ${r.nome} | ${r.ok ? 'ok' + (r.detalhe ? ' (' + r.detalhe + ')' : '') : 'FALHA: ' + String(r.erro).replace(/\|/g, '/').replace(/\n/g, ' ')} | ${r.ms} |`),
  '', '## Notas', '', ...(observacoes.length ? observacoes.map((o) => `- ${o}`) : ['- nenhuma'])].join('\n');
fs.writeFileSync(path.join(PASTA, 'relatorio-api.md'), md);
console.log(`\n${resultados.length - falhas.length}/${resultados.length} passos ok. Pasta: ${PASTA}`);
process.exit(falhas.length ? 1 : 0);
