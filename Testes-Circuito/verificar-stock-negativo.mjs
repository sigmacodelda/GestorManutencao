// Deixa P001 com stock negativo pela API (ficha com material validada) e corre o passo da Arrumação que confere o aviso na app.
import { spawnSync } from 'node:child_process';
import { http, entrar, itens } from './cobertura-api.mjs';
const tk = await entrar();
const MARCA = `TESTE-E2E-NEG-${Math.random().toString(36).slice(2, 6)}`;
const eq = itens((await http(tk.t, 'GET', '/equipamentos')).j);
const eqId = eq.find((e) => !e.foraDeServico && !e.obsoleto)?.id;
const av = await http(tk.t, 'POST', '/intervencoes/avaria', { equipamentoId: eqId, anomalia: `${MARCA} avaria` });
const fid = av.j?.id; if (!fid) { console.log('FALHA avaria', av.s, JSON.stringify(av.j)); process.exit(1); }
const m = await http(tk.t, 'POST', `/intervencoes/${fid}/materiais`, { design: `${MARCA} parafuso`, qtt: 15, ref: 'P001' });
const c = await http(tk.t, 'POST', `/intervencoes/${fid}/concluir`, { feito: `${MARCA} feito`, inicio: '08:00:00', fim: '09:00:00' });
const v = await http(tk.s, 'POST', `/validacao/${fid}`);
const p = (await http(tk.t, 'GET', '/pecas?q=P001')).j.pecas.find((x) => x.ref === 'P001');
console.log(`material ${m.s}, concluir ${c.s}, validar ${v.s}, stock P001 = ${p.stock}`);
if (!(p.stock < 0)) { console.log('FALHA: o stock não ficou negativo'); process.exit(1); }
const r = spawnSync('node', ['cobertura-ui.mjs'], { env: { ...process.env, SEG: 'arrumar-ler' }, stdio: 'inherit' });
process.exit(r.status ?? 1);
