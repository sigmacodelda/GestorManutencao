# Intervenção de 11/11/2026: Gestor de Manutenção

- Reunião com Osvaldo Carlos e Rui.
- PIN de 4 números, regras de geração de fichas do Landeiro e circuitos funcionais Playwright (auditoria de UX, popular e limpar dados de teste, comparação de esquema e de dados com o PHC), com relatório em `docs/Relatorio-Circuitos-Funcionais.md`.
- Publicação da API no IIS e do APK para 192.168.1.71, com script de ativação da API neste PC e guia `docs/Instalacao-IIS-e-APK.md`.
- A app lê o `ApiUrl` do `appsettings`; a API ganhou o PIN de configuração (`Pin:Dev`) e passou a haver um só `appsettings.json`, fora do git. Acrescentei testes (`PinConfiguracaoTests`) e o script para criar o supervisor no telemóvel.
- Rede interna: a API aceita http com `Seguranca:PermitirHttp`, a app só aceita http para IP privado e o Android permite http para 192.168.10.98. Dois PRs (#5 e #6) fundidos em `main`.
- Persistência do `OpId` em separado e filtro de equipamentos (limpeza de 27 ficheiros, com menos 95 linhas no saldo).
- Por enviar (em stage, sem commit): modo escuro e claro (componente `ModoVisual`, tema e `app.css`), ajustes ao ecrã de detalhe da intervenção e ao ecrã do equipamento.
- Por enviar: sugestão do próximo nº de infraestrutura (maior número puro + 1), com teste; o formulário novo não conta o número sugerido como alteração.
- Por enviar: a lista de periodicidades passa a mostrar só as de manutenção (`u_periocid.manutencao = 1`).
- Por enviar, fora do git: `docs/Manual-Utilizador.docx`, `docs/Plano-Testes-QA.docx` e `publicar-api.zip`.
- Por fechar: não corri build nem testes ao trabalho em stage, por isso não está verificado; o `publicar-api.zip` não deve ir para o git.
