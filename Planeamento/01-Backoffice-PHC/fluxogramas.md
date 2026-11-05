# Parte 1 (Backoffice e PHC): fluxogramas

Mermaid (abre no VS Code com a extensão Markdown Mermaid, no GitHub e no Azure DevOps). Nomes de tabelas e campos são candidatos até P1-T01.

## 1. Visão da Parte 1 e interfaces com as outras partes

```mermaid
flowchart LR
    WORD[Word P.05-03 do Luis] --> CARGA[Carga inicial do plano]
    INFRA[(Infraestruturas do PHC)] --> EQP[Equipamentos]
    CARGA --> PLANO[Planos de manutencao]
    EQP --> PLANO
    CFG["Configuracao: antecedencia e avancar ordem"] --> GER
    PLANO --> GER[Gerar planeadas]
    GER --> FICHA[(Ficha de Intervencao<br/>estado Planeada)]
    FICHA --> APP["Parte 2: app movel"]
    APP --> FICHA
    FICHA --> VAL[Validacao pela supervisao]
    VAL --> FECH[Ficha Validada e Fechada]
    FICHA --> DASH[Dashboard e alertas]
    FECH --> DASH
    PLANO --> P3["Parte 3: pecas por plano"]
    DASH -. pecas em rutura .-> P3
```

## 2. Estados da intervenção e quem muda cada transição

```mermaid
stateDiagram-v2
    [*] --> Planeada: backoffice gera pelo plano
    [*] --> EmCurso: curativa criada na app ou no backoffice
    Planeada --> EmCurso: tecnico inicia na app
    Planeada --> Cancelada: supervisao cancela ou plano alterado
    EmCurso --> Concluida: tecnico conclui na app
    Concluida --> Validada: supervisao valida e fecha
    Concluida --> EmCurso: supervisao devolve com motivo
    Validada --> [*]
    Cancelada --> [*]
```

Derivados (não gravados): Atrasada = `Planeada` ou `EmCurso` com data planeada anterior a hoje; A vencer = `Planeada` dentro da antecedência.

## 3. Algoritmo de geração das planeadas (BO-03)

```mermaid
flowchart TD
    A[Inicio da geracao<br/>hoje, horizonte, atrasadas max] --> B[Ler planos ativos com equipamento nao obsoleto]
    B --> C{Mais planos?}
    C -- nao --> Z["Devolver relatorio:<br/>criadas, existentes, saltadas, erros"]
    C -- sim --> D{Periodicidade gera planeadas?}
    D -- nao --> C
    D -- sim --> E["desde = maior de ultimadata e maior dataplan das fichas do plano<br/>sem fichas: datainicio menos 1 dia"]
    E --> F["Calcular datas apos desde ate hoje mais horizonte<br/>tipo dias: desde mais N dias, filtrado pelos meses marcados<br/>tipo meses sem marcas: desde mais N meses<br/>com marcas, ou periodicidade sem calculo: dia do mes nos meses marcados"]
    F --> G["Separar: passadas menores que hoje e futuras"]
    G --> H["Das passadas manter so as ultimas atrasadas max;<br/>as outras vao para saltadas"]
    H --> I{Para cada data: existe ficha<br/>do plano nessa data nao cancelada?}
    I -- sim --> J[Contar como existente]
    I -- nao --> K[Inserir ficha estado Planeada<br/>com plano, data, ordem, tipos e fornecedor]
    J --> L[Atualizar proxdata do plano]
    K --> L
    L --> C
```

Regras de datas: dia do mês limitado ao último dia do mês (31 em fevereiro = 28 ou 29); só datas, sem horas; `meses` vazio = todos os meses; no máximo 24 ocorrências por plano e por execução (travão). Um erro num plano é registado (`CrashLogService`), vai para o relatório e a geração continua nos outros.

## 4. Sequência: gerar planeadas a partir do ecrã

```mermaid
sequenceDiagram
    actor S as Supervisor
    participant E as Ecra Planeadas
    participant P as PlanoManutencaoService
    participant C as ManutencaoConfigService
    participant DB as PHC SQL Server
    S->>E: Gerar planeadas
    E->>C: Obter horizonte e atrasadas max
    C->>DB: SELECT u_manconfig NOLOCK
    E->>P: GerarPlaneadasAsync
    P->>DB: SELECT planos ativos NOLOCK
    loop cada plano
        P->>P: calcular datas, aplicar regras
        P->>DB: INSERT ficha se nao existe plano e data
    end
    P-->>E: Relatorio de geracao
    E-->>S: Resumo e lista atualizada
```

## 5. Validação pela supervisão (BO-06)

```mermaid
flowchart TD
    A[Lista Por validar<br/>fichas Concluida] --> B[Supervisor abre a ficha]
    B --> C{Ficha externa?}
    C -- sim --> D{Fornecedor e doc. externo preenchidos?}
    D -- nao --> E["Aviso: completar antes de validar"]
    D -- sim --> F
    C -- nao --> F{Dados corretos?}
    F -- sim --> G{Ficha mudou desde que abriu?}
    G -- sim --> H[Aviso e recarregar]
    G -- nao --> I["Gravar: estado Validada, Fechada sim,<br/>iniciais e data de validacao"]
    F -- nao --> J[Devolver com motivo]
    J --> K[Estado EmCurso e motivo gravado<br/>a app mostra a devolucao]
    I --> L[Sai da lista e entra no dashboard]
```

## 6. Carga inicial do plano (BO-09)

```mermaid
flowchart TD
    A[Word P.05-03 v1.4] --> B[Converter tabela para CSV UTF-8<br/>Nr, Designacao, Zona, Entidade, 12 meses, periodicidade]
    B --> C[Relatorio de correspondencia<br/>Nr do Word contra infraestruturas]
    C --> D{Divergencias?}
    D -- sim --> E[Luis ou Rui corrigem ou confirmam]
    E --> C
    D -- nao --> F[Gerar script de carga u_manplano<br/>idempotente, so base de testes]
    F --> G["Verificar: 1 plano por linha<br/>e soma de meses igual"]
    G --> H[Utilizador corre em producao]
    H --> I[Gerar planeadas]
```

## 7. Navegação dos ecrãs do backoffice

```mermaid
flowchart LR
    M[Menu Manutencao<br/>por perfil] --> EQ[Equipamentos]
    M --> PL[Planos]
    M --> PA[Planeadas]
    M --> VA[Validacao]
    M --> CF[Configuracao]
    M --> DB[Dashboard]
    EQ --> EQD[Ficha do equipamento]
    EQD --> PL
    EQD --> HI[Historico de intervencoes]
    PL --> PLD[Editar plano e meses]
    PA --> PAD[Detalhe da planeada]
    PA --> GER[Gerar planeadas]
    DB --> ALR[Alertas]
    ALR --> PA
    ALR --> VA
    VA --> VAD["Detalhe: validar ou devolver"]
```

## 8. Sequência de execução das tasks da Parte 1 e dependências externas

```mermaid
flowchart LR
    T01[P1-T01 Levantamento] --> T02[P1-T02 Desenho e contratos]
    T02 --> T03[P1-T03 Servicos]
    T03 --> T04[P1-T04 Ecras]
    T04 --> T05[P1-T05 Dashboard]
    T05 --> T06[P1-T06 Testes e docs]
    T02 -. contratos .-> P2T02[P2-T02]
    T02 -. contratos .-> P3T02[P3-T02]
    T03 -. servicos .-> P2T04[P2-T04]
    TAB[Utilizador cria tabelas e campos] --> T03
    WORD[Word do Luis] --> T01
    BD[Acesso de leitura a TESTES_LAND2] --> T01
    P3T05[P3-T05 alerta de rutura] -. opcional .-> T05
```
