# Fluxogramas gerais do circuito alvo

Diagramas em Mermaid (abrem no VS Code com a extensão Markdown Mermaid, no GitHub e no Azure DevOps).

## 1. Circuito ponta a ponta (alvo)

```mermaid
flowchart TD
    subgraph BO[Backoffice - Parte 1]
        A[Registar equipamento] --> B[Definir plano e periodicidade<br/>interna ou externa]
        B --> C[Gerar intervenções planeadas<br/>estado Planeada]
        K[Configurar antecedência do alerta<br/>e permissão de avançar ordem]
    end
    subgraph APP[App móvel - Parte 2]
        D[Alerta de planeada<br/>com a antecedência configurada] --> E[Lista de intervenções]
        E --> F{Preventiva planeada<br/>ou curativa nova?}
        F -- planeada --> G[Iniciar e executar]
        F -- curativa --> H[Criar intervenção com +]
        H --> G
        G --> I[Registar anomalia, intervenção,<br/>tempos, foto e peças]
        I --> J[Fila de saída offline]
        J --> L[Sincronizar quando há rede]
    end
    subgraph PEC[Peças e armazém - Parte 3]
        M[Peças necessárias do plano] --> N[Saída por leitura de código]
        N --> O[Stock por localização]
        O --> P{Stock cobre planeadas<br/>no prazo de entrega?}
        P -- não --> Q[Alerta de rutura e compra]
        R[Receção e etiquetas] --> O
        S[Inventário por picagem] --> O
    end
    C --> D
    C --> M
    K --> D
    I --> N
    L --> V[Validação no backoffice<br/>supervisão]
    V --> W[Ficha Fechada]
    W --> X[Dashboard e alertas]
    Q --> X
```

## 2. Estados de uma intervenção

```mermaid
stateDiagram-v2
    [*] --> Planeada: gerada pelo plano
    [*] --> EmCurso: curativa criada no telemóvel (estado inicial por confirmar)
    Planeada --> EmCurso: técnico inicia
    EmCurso --> Concluida: técnico conclui no telemóvel
    Concluida --> Validada: supervisão valida no backoffice
    Concluida --> EmCurso: supervisão devolve
    Validada --> [*]: ficha Fechada
    Planeada --> Cancelada: cancela com motivo
    EmCurso --> Cancelada: cancela com motivo
```

Derivados, nunca gravados (base LandeiroMaui, `base-landeiromaui.md`): **Atrasada** (Planeada/EmCurso com data < hoje) e **A vencer** (Planeada com data até hoje + antecedência). Estado vazio = ficha antiga (legado).

## 3. Criação de uma intervenção (quadro 1)

```mermaid
flowchart TD
    A[Nova intervenção] --> B{Como?}
    B -- Por plano --> C[Filtro: Departamento]
    C --> D[Zona ou Máquina]
    D --> E[Plano de manutenção]
    E --> F[1.ª fase: intervenção criada<br/>2.ª fase: peças necessárias do plano]
    B -- Manual --> G[Departamento]
    G --> H[Máquina]
    H --> I[Detalhe da intervenção]
    I --> J[2.ª fase: enumerar as peças]
```

## 4. Peças: compra, receção, consumo, alerta

```mermaid
flowchart LR
    A[Compra da peça] --> B[Receção<br/>entrada de stock]
    B --> C[Etiqueta da peça e do local]
    C --> D[Armazém virtual<br/>localização]
    D --> E[Intervenção usa a peça<br/>leitura de código de barras ou QR]
    E --> F[Saída de stock]
    F --> G{Stock ≤ necessário<br/>das planeadas + prazo de entrega?}
    G -- sim --> H[Alerta: comprar a peça X]
    H --> A
    I[Inventário por picagem] --> D
```

## 5. Sequência de execução do planeamento

```mermaid
flowchart LR
    P1T01[P1-T01 Levantamento PHC] --> P1T02[P1-T02 Desenho e contratos]
    P1T02 --> P1T03[P1-T03 Serviços e regras]
    P1T03 --> P1T04[P1-T04 Ecrãs backoffice]
    P1T04 --> P1T05[P1-T05 Dashboard e alertas]
    P1T05 --> P1T06[P1-T06 Testes e documentação]
    P1T02 --> P2T02[P2-T02 Desenho app]
    P2T01[P2-T01 Levantamento app] --> P2T02
    P2T02 --> P2T03[P2-T03 Android, login, PIN]
    P2T03 --> P2T04[P2-T04 Offline e sincronização]
    P2T04 --> P2T05[P2-T05 Lista, criar, alerta]
    P2T05 --> P2T06[P2-T06 Testes e documentação]
    P1T03 --> P2T04
    P3T01[P3-T01 Levantamento peças] --> P3T02[P3-T02 Desenho peças]
    P1T02 --> P3T02
    P3T02 --> P3T03[P3-T03 Referências e carga]
    P3T03 --> P3T04[P3-T04 Armazém, etiquetas, receção, inventário]
    P3T04 --> P3T05[P3-T05 Saída, peças do plano, alerta]
    P3T05 --> P3T06[P3-T06 Testes e documentação]
    P2T05 --> P3T05
```
