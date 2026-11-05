# Parte 2: fluxogramas da app de intervenções

Diagramas em Mermaid (abrem no VS Code com a extensão Markdown Mermaid, no GitHub e no Azure DevOps). Coerentes com `00-Comum/fluxogramas-gerais.md`. Nomes de campos e tabelas são provisórios até P1-T02.

## 1. Entrada na app (login, PIN e sessão)

```mermaid
flowchart TD
    A["Abrir a app"] --> B{"Há sessão guardada<br/>e PIN definido?"}
    B -- "não" --> C["Login: utilizador e password"]
    C --> D{"Há rede?"}
    D -- "não" --> E["Aviso: sem ligação.<br/>Sem sessão não há login offline"]
    D -- "sim" --> F{"Credenciais certas?"}
    F -- "não" --> G["Utilizador ou password incorretos"]
    G --> C
    F -- "sim" --> H["Guardar nome, iniciais e perfis<br/>nunca a password"]
    H --> I["Definir PIN de 4 a 6 dígitos<br/>guardar só hash com sal"]
    I --> J["Primeira sincronização<br/>se houver rede"]
    B -- "sim" --> K["Pedir PIN"]
    K --> L{"PIN certo?"}
    L -- "sim" --> M["Contador de falhas a zero"]
    L -- "não" --> N["Contar falha<br/>guardada no armazenamento seguro"]
    N --> O{"Atingiu o máximo<br/>de tentativas?"}
    O -- "não" --> K
    O -- "sim" --> P["Anular PIN<br/>manter a fila de saída"]
    P --> C
    M --> Q{"Há rede?"}
    Q -- "sim" --> R["Recarregar perfis<br/>e verificar utilizador"]
    R --> S{"Utilizador ativo<br/>e com o perfil?"}
    S -- "não" --> T["Terminar sessão<br/>pedir login"]
    T --> C
    S -- "sim" --> U["Lista de intervenções"]
    Q -- "não" --> U
    J --> U
```

## 2. Estados do PIN e da sessão

```mermaid
stateDiagram-v2
    [*] --> SemSessao
    SemSessao --> SessaoSemPin: login com password certo
    SessaoSemPin --> SessaoAtiva: PIN definido
    SessaoAtiva --> Bloqueada: abrir a app ou 5 min em segundo plano
    Bloqueada --> SessaoAtiva: PIN certo
    Bloqueada --> Bloqueada: PIN errado e tentativas por esgotar
    Bloqueada --> SemSessao: tentativas esgotadas
    SessaoAtiva --> SemSessao: logout sem pendentes
    SessaoAtiva --> SemSessao: utilizador desativado ou sem perfil
```

## 3. Navegação entre ecrãs

```mermaid
flowchart LR
    L1["Login"] --> L2["Lista de intervenções"]
    L2 -->|"botão +"| C1["Criar intervenção<br/>ecrã Intr."]
    L2 -->|"tocar numa linha"| D1["Detalhe e edição"]
    L2 -->|"faixa de estado"| P1["Pendentes<br/>Repetir ou Descartar"]
    L2 --> M1["Menu: definições, trocar PIN, sair"]
    C1 -->|"Guardar rascunho"| L2
    C1 -->|"Concluir"| L2
    D1 -->|"Iniciar planeada"| C1
    D1 --> L2
    N1["Notificação"] -->|"tocar"| D1
    M1 -->|"Sair com pendentes"| X1["Bloqueado com aviso"]
    X1 --> P1
```

## 4. Registo de uma intervenção no telemóvel

```mermaid
flowchart TD
    A["Lista"] --> B{"Planeada<br/>ou nova curativa?"}
    B -- "planeada" --> C{"Pode avançar ordem<br/>ou é a primeira?"}
    C -- "não" --> D["Cadeado com a razão"]
    D --> A
    C -- "sim" --> E["Iniciar: estado EmCurso"]
    B -- "curativa" --> F["Botão +: nova curativa<br/>estado EmCurso"]
    F --> G["Escolher equipamento na cache"]
    E --> H["Preencher: início, fim, técnico,<br/>anomalia, intervenção, limpeza"]
    G --> H
    H --> I["Foto opcional"]
    I --> J{"Dados válidos?"}
    J -- "não" --> K["Mensagem junto ao campo"]
    K --> H
    J -- "sim" --> L["Guardar no SQLite<br/>stamp gerado no telemóvel"]
    L --> M["Operação na fila de saída<br/>estado Pendente"]
    M --> N["Sincronizar quando há rede"]
    N --> O["Concluida: espera validação<br/>da supervisão no backoffice"]
```

## 5. Gravação offline e sincronização (sequência)

```mermaid
sequenceDiagram
    participant U as Operador
    participant UI as Ecrã
    participant DB as SQLite local
    participant F as Fila de saída
    participant S as Sincronizador
    participant R as Servidor PHC
    U->>UI: Concluir intervenção
    UI->>DB: Guardar intervenção (Pendente)
    UI->>F: Enfileirar operação com o stamp
    UI-->>U: Mostrar de imediato com relógio
    Note over S,R: Sem rede: a fila espera
    S->>S: Rede voltou ou botão Enviar agora
    S->>F: Ler a próxima operação (FIFO)
    S->>R: Gravar idempotente por stamp
    alt Gravou
        R-->>S: Confirmação
        S->>DB: Estado Sincronizado
        S->>F: Marcar Enviado
    else Já existe o stamp
        R-->>S: Já existe
        S->>DB: Sincronizado sem duplicar
    else Ficha Validada ou Cancelada
        R-->>S: Conflito
        S->>DB: Guardar como proposta
        S-->>UI: Avisar o técnico
    else Erro de ligação
        S->>F: Backoff e tentar mais tarde
    else Erro de regra
        S->>F: Estado Erro, seguem as independentes
    end
    S->>R: Ler o que mudou (assinaturas)
    R-->>S: Novas, alteradas, removidas
    S->>DB: Atualizar cache em lote
```

## 6. Conflitos (ganha a validação do backoffice)

```mermaid
flowchart TD
    A["Operação chega ao servidor"] --> B{"Estado da ficha<br/>no servidor"}
    B -- "não existe" --> C["Criar com o stamp do telemóvel"]
    B -- "Planeada" --> D{"Operação"}
    D -- "iniciar" --> E["Iniciar e registar o técnico"]
    D -- "outra" --> F["Gravar como EmCurso"]
    B -- "EmCurso" --> G{"É o mesmo técnico?"}
    G -- "sim" --> H["Atualizar a ficha<br/>inclui o caso de ficha devolvida<br/>pela supervisão"]
    G -- "não" --> I["Conflito: iniciada por outro<br/>avisar o segundo técnico"]
    B -- "Concluida" --> J["Se a operação é igual: sem efeito<br/>senão: conflito, proposta"]
    B -- "Validada ou Cancelada" --> K["Conflito: guardar como proposta<br/>a supervisão decide"]
```

## 7. Alerta de intervenções planeadas

```mermaid
flowchart TD
    A["Sincronização termina<br/>ou configuração muda<br/>ou telemóvel reinicia"] --> B["Ler da cache: planeadas,<br/>antecedência, hora do aviso"]
    B --> C["Planeador: calcular avisos<br/>data planeada menos antecedência,<br/>no dia, uma por dia se atrasada"]
    C --> D["Cancelar os agendados antigos"]
    D --> E["Agendar notificações locais"]
    E --> F{"Permissão de notificações?"}
    F -- "não" --> G["Só a faixa dentro da app<br/>e aviso para ativar"]
    F -- "sim" --> H["Avisos disparam sem rede"]
    H --> I["Tocar abre a intervenção"]
    J["Planeada iniciada, concluída<br/>ou cancelada"] --> D
```

## 8. Avançar ordem (APP-06)

```mermaid
flowchart TD
    A["Tocar numa planeada"] --> B{"Curativa?"}
    B -- "sim" --> Z["Permitido"]
    B -- "não" --> C["Ler AVANCAR_ORDEM do perfil na cache"]
    C --> D{"Configuração<br/>existe?"}
    D -- "não" --> E["Assumir: sem permissão"]
    D -- "sim" --> F{"Permite avançar?"}
    E --> G
    F -- "sim" --> Z
    F -- "não" --> G{"É a primeira por executar<br/>por data e ordem?"}
    G -- "sim" --> Z
    G -- "não" --> H["Bloquear com cadeado<br/>e indicar a anterior"]
```

## 9. Dependências da Parte 2 com as outras partes e marcos

```mermaid
flowchart LR
    P1T02["P1-T02 Contratos e tabelas"] --> P2T02["P2-T02 Desenho"]
    P2T01["P2-T01 Levantamento"] --> P2T02
    P2T02 --> P2T03["P2-T03 Android, login, PIN"]
    P2T03 --> P2T04["P2-T04 Offline"]
    P1T03["P1-T03 Serviços"] --> P2T04
    P2T04 --> P2T05["P2-T05 Lista, alerta, criar"]
    P2T05 --> P2T06["P2-T06 Testes e documentação"]
    P2T05 --> P3T05["P3-T05 Peças e saída"]
    P2T02 -.-> M0["M0 Protótipo 11/11"]
    P2T03 -.-> M1["M1 Login e lista online"]
    P2T04 -.-> M2["M2 Offline"]
    P2T05 -.-> M3["M3 Alertas e fotos"]
```
