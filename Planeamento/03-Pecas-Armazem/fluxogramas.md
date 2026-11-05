# Fluxogramas da Parte 3: Peças e armazém

Diagramas em Mermaid (abrem no VS Code com a extensão Markdown Mermaid, no GitHub e no Azure DevOps). Coerentes com `00-Comum/fluxogramas-gerais.md` (secções 1 e 4).

## 1. Visão geral da Parte 3

```mermaid
flowchart LR
    A[Manual Marel em PDF] --> B[Carga por máquina<br/>IA e validação humana]
    B --> C[Referências das peças<br/>ref interna e do fabricante]
    C --> D[Peças por plano e por equipamento]
    C --> E[Receção<br/>entrada de stock]
    E --> F[Etiquetas e localização<br/>armazém virtual]
    F --> G[Saída por leitura na intervenção]
    D --> G
    G --> H[Stock por localização]
    I[Inventário por picagem] --> H
    H --> J{Alerta de rutura<br/>planeadas vs stock vs prazo}
    D --> J
    K[Tempo de entrega<br/>artigo e fornecedor] --> J
    J --> L[Painel de alertas e compra]
    L --> M[Compra] --> E
```

## 2. Carga das peças por IA (com portão de autorização)

```mermaid
flowchart TD
    A[PDF Marel] --> B{Autorização D-11<br/>para serviços externos?}
    B -- não --> C[Extração só com ferramentas locais<br/>tradução por glossário e revisão humana]
    B -- sim --> D[Extração local mais IA<br/>só no que foi autorizado]
    C --> E[Dividir por máquina e conjuntos]
    D --> E
    E --> F[Extrair linhas<br/>Item no., Marel code, descrição, qtd]
    F --> G[Normalizar e traduzir EN para PT]
    G --> H[Ficheiro de proposta<br/>estado Proposta, confiança]
    H --> I[Validação humana na grelha]
    I --> J{Linha válida?}
    J -- corrigir --> I
    J -- rejeitar --> K[Estado Rejeitada com motivo]
    J -- aprovar --> L[Estado Validada]
    L --> M[Importar: gerar referências<br/>criar peças e ligações]
    M --> N[Conferir contagens vs manual]
    N --> O{Diferenças explicadas?}
    O -- não --> I
    O -- sim --> P[Máquina carregada]
```

## 3. Geração da referência interna na importação

```mermaid
flowchart TD
    A[Linha Validada] --> B{Código do fabricante<br/>já existe?}
    B -- sim --> C[Não cria artigo]
    C --> D{Ligação máquina e conjunto<br/>já existe?}
    D -- sim --> E[Atualiza quantidade<br/>se não foi editada à mão]
    D -- não --> F[Cria ligação com Item no. e qtd]
    B -- não --> G{Conjunto existe?}
    G -- não --> H[Criar conjunto: próximo CCC da máquina]
    G -- sim --> I[Próximo PPP do conjunto]
    H --> I
    I --> J{PPP maior que 999?}
    J -- sim --> K[Erro claro: não trunca]
    J -- não --> L[Gerar referência M MMM - CCC . PPP]
    L --> M[Criar artigo e extensão da peça]
    M --> F
```

## 4. Receção de peças

```mermaid
flowchart TD
    A[Chega a encomenda] --> B[Ler código da peça<br/>ou pesquisar]
    B --> C{Peça identificada?}
    C -- lista --> D[Escolher da lista]
    C -- não --> E[Procurar por texto<br/>ou criar peça se administrador]
    C -- sim --> F[Quantidade recebida]
    D --> F
    E --> F
    F --> G[Localização<br/>por defeito RECEC ou da peça]
    G --> H{Ligar a encomenda<br/>do PHC?}
    H -- sim --> I[Atualiza pendente da encomenda]
    H -- não --> J[Entrada livre]
    I --> K[Movimento E: stock da localização aumenta]
    J --> K
    K --> L{Peça tem etiqueta?}
    L -- não --> M[Imprimir etiqueta da peça]
    L -- sim --> N[Arrumar na localização]
    M --> N
    N --> O[Entrada no stock PHC<br/>pelo mecanismo nativo]
```

## 5. Saída por leitura (telemóvel, com offline)

```mermaid
flowchart TD
    A[Intervenção aberta<br/>lista de peças do plano] --> B[Ler código da peça]
    B --> C[Resolver leitura<br/>PEC:, código do fabricante]
    C --> D{Resultado}
    D -- desconhecido --> E[Aviso e pesquisa à mão]
    D -- lista --> F[Escolher peça]
    D -- peça --> G[Quantidade]
    F --> G
    G --> H[Localização de origem<br/>sugere a de maior stock]
    H --> I{Está na lista<br/>do plano?}
    I -- não --> J[Aceita com aviso: extra ao plano]
    I -- sim --> K[Criar movimento S com opid]
    J --> K
    K --> L{Há rede?}
    L -- sim --> M[Gravar já]
    L -- não --> N[Fila de saída offline]
    N --> O[Sincronizar quando há rede]
    O --> M
    M --> P{Stock ficou negativo?}
    P -- sim --> Q[Gravar e assinalar divergência<br/>para inventário]
    P -- não --> R[Stock atualizado]
    Q --> R
    R --> S[Reavaliar alerta de rutura]
```

## 6. Estados de uma sessão de inventário

```mermaid
stateDiagram-v2
    [*] --> Aberta: criar sessão
    Aberta --> EmContagem: primeira picagem
    EmContagem --> EmContagem: picar localização e peças
    EmContagem --> EmRevisao: terminar contagem
    EmRevisao --> EmContagem: recontar localização
    EmRevisao --> Fechada: supervisão aprova ajustes
    Aberta --> Cancelada: cancelar
    EmContagem --> Cancelada: cancelar
    EmRevisao --> Cancelada: cancelar
    Fechada --> [*]
    Cancelada --> [*]
```

## 7. Inventário por picagem (passos)

```mermaid
flowchart TD
    A[Abrir sessão<br/>armazém, zona ou localização] --> B[Picar a localização]
    B --> C[Picar cada peça<br/>cada leitura soma 1 ou indicar quantidade]
    C --> D{Peça esperada<br/>nesta localização?}
    D -- não --> E[Aviso: não esperada aqui<br/>contar na localização atual]
    D -- sim --> F[Registar contagem com opid]
    E --> F
    F --> G{Mais peças?}
    G -- sim --> C
    G -- não --> H{Mais localizações?}
    H -- sim --> B
    H -- não --> I[Rever diferenças<br/>contado vs sistema]
    I --> J{Supervisão aprova?}
    J -- recontar --> B
    J -- sim --> K[Movimentos de ajuste<br/>só nas diferenças aprovadas]
    K --> L[Sessão Fechada]
```

## 8. Etiquetas (criar e imprimir)

```mermaid
flowchart LR
    A[Escolher contexto<br/>Peça ou Localização] --> B[Modelo ativo<br/>do editor de etiquetas]
    B --> C[Escolher objetos<br/>peça, conjunto, máquina, localização, receção]
    C --> D[Pré-visualização<br/>com dados reais]
    D --> E{Ajustar desenho?}
    E -- sim --> F[Editar modelo<br/>campos e tamanho] --> D
    E -- não --> G[Gerar payload<br/>PEC: ou LOC:]
    G --> H[Gerar ZPL e imprimir<br/>ImpressaoEtiquetasService]
    H --> I{Impressão OK?}
    I -- não --> J[Aviso e registo de erro]
    I -- sim --> K[Fim]
```

## 9. Cálculo do alerta de rutura (sequência)

```mermaid
flowchart TD
    A[Para cada peça ativa] --> B[Prazo efetivo L<br/>mais longo de par, artigo e fornecedor<br/>ou prazo por defeito]
    B --> C[Janela J igual a L mais margem M]
    C --> D[N: soma das planeadas na janela<br/>atrasadas contam com data de hoje]
    D --> E[S menos R: stock por localização<br/>menos reservas das EmCurso]
    E --> F{Disponível D menor<br/>que necessidade N?}
    F -- não --> G[Sem alerta]
    F -- sim --> H[Falta F igual a N menos D<br/>data da falha: acumular por data]
    H --> I[Data limite de compra<br/>falha menos L]
    I --> J{DLC menor que hoje?}
    J -- sim --> K[Crítico: já não chega a tempo]
    J -- não --> L[Aviso: comprar até DLC]
    K --> M[Painel ordenado por DLC]
    L --> M
```

## 10. Sequência de ecrãs do backoffice (Windows) e do telemóvel

```mermaid
flowchart LR
    subgraph BO[Backoffice Windows]
        B1[Peças por máquina<br/>árvore e pesquisa] --> B2[Ficha da peça<br/>stock, prazo, ligações]
        B3[Receção] --> B4[Etiquetas]
        B5[Armazém: localizações] --> B6[Inventário]
        B7[Validação da carga IA]
        B8[Painel de alertas de rutura]
    end
    subgraph TEL[Telemóvel]
        T1[Intervenção<br/>peças do plano] --> T2[Ler código]
        T2 --> T3[Quantidade e localização]
        T3 --> T4[Saída registada]
        T5[Inventário por picagem]
    end
    B2 -. consulta .-> T1
    T4 -. sincroniza .-> B8
```
