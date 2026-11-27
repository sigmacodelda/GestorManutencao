// Mantém a pilha (API + app em memória) de pé para os agentes de cobertura; termina quando existir coordenacao/parar.txt (ou ao fim de 90 min).
import fs from 'node:fs';
const fim = Date.now() + 90 * 60 * 1000;
console.log(`Pilha pronta: API ${process.env.API_URL}, CDP ${process.env.CDP_URL}`);
while (Date.now() < fim && !fs.existsSync('coordenacao/parar.txt')) await new Promise((r) => setTimeout(r, 2000));
console.log('A terminar a pilha.');
