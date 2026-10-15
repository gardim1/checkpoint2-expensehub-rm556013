# ExpenseHub — Checkpoint 2 de C#

**Vinicius Silva Gardim — RM556013 — 3ESPX — trabalho individual.**

API de reembolsos corporativos com cadastro HTTP, autenticação bearer do ASP.NET Core Identity, roles acumuláveis, rascunhos, aprovação/reprovação, pagamento simulado e histórico persistido. Não há frontend nem movimentação financeira real.

## Ambiente e dependências

- .NET SDK **10.0.x**; validação local com **10.0.402**.
- SQLite, sem servidor externo, via `Microsoft.EntityFrameworkCore.Sqlite` **10.0.13**.
- Identity via `Microsoft.AspNetCore.Identity.EntityFrameworkCore` **10.0.13**; EF Design **10.0.13**; OpenAPI **10.0.12**.
- PowerShell **7+** para os scripts; Gitleaks **8.30.1** para reproduzir a análise completa do CI.
- `dotnet-ef` **10.0.13** para aplicar migrations manualmente (opcional).

Se o SDK estiver instalado fora do PATH, configure `DOTNET_ROOT` e acrescente sua pasta ao PATH. No ambiente usado no desenvolvimento: `$env:DOTNET_ROOT = "$HOME/.dotnet10"; $env:PATH = "$env:DOTNET_ROOT;$HOME/.dotnet/tools;$env:PATH"`.

## Configuração e banco

A conexão `ConnectionStrings:ExpenseHub` usa `Data Source=expensehub.db`. O arquivo SQLite é local e ignorado pelo Git. Para outro caminho, configure `ConnectionStrings__ExpenseHub` no ambiente. Migrations em `sources/ExpenseHub.Api/Data/Migrations` são aplicadas automaticamente na inicialização; as categorias são semeadas com ids 1 a 5.

Configure o e-mail e a senha do único Admin de seed antes da primeira execução. Nenhum usuário funcional é semeado. Não versione credenciais. Em ambiente Development, use User Secrets:

```shell
dotnet user-secrets set "SeedAdmin:Email" "<email-admin>" --project sources/ExpenseHub.Api
dotnet user-secrets set "SeedAdmin:Password" "<senha>" --project sources/ExpenseHub.Api
```

Alternativa em PowerShell, sem senha literal no arquivo ou no histórico do comando:

```powershell
$env:SeedAdmin__Email = Read-Host 'E-mail do Admin'
$env:SeedAdmin__Password = Read-Host 'Senha do Admin' -MaskInput
```

A senha deve atender à política do Identity (ao menos 6 caracteres, maiúscula, minúscula, dígito e caractere não alfanumérico). Sem as configurações necessárias, o seed não cria uma nova conta Admin. Alterar a senha de configuração depois de criar a conta não redefine sua senha persistida.

Se `SeedAdmin:Email` já pertencer a um usuário sem a role Admin, o seed apenas registra um warning e não promove essa conta, mesmo com senha configurada. Para criar o Admin inicial, configure um e-mail ainda não cadastrado. Uma conta Admin já existente é preservada.

Para atualizar o banco manualmente, a partir da raiz:

```shell
dotnet tool install --global dotnet-ef --version 10.0.13
dotnet ef database update --project sources/ExpenseHub.Api
```

Se `dotnet-ef` já estiver instalado, confira a versão e use `dotnet tool update --global dotnet-ef --version 10.0.13` quando necessário. O comando manual aplica o schema; o seed de Identity ocorre ao iniciar a API.

## Executar e validar

```shell
dotnet restore ./sources/ExpenseHub.slnx
dotnet build ./sources/ExpenseHub.slnx
dotnet test ./sources/ExpenseHub.slnx
dotnet format ./sources/ExpenseHub.slnx --verify-no-changes
dotnet run --project ./sources/ExpenseHub.Api/ExpenseHub.Api.csproj
```

O perfil local escuta em `http://localhost:5245`. `GET /health` confirma a inicialização; em Development, o documento OpenAPI fica em `/openapi/v1.json`. Os testes de `ExpenseHub.UnitTests` devem executar sem banco, rede ou serviços externos. O smoke test é uma verificação HTTP adicional e não substitui os testes unitários avaliados.

Para reproduzir as verificações oficiais, sem modificar suas regras:

```powershell
pwsh ./tests/Invoke-CodeQuality.Unit.Tests.ps1
pwsh ./tests/Invoke-CodeQuality.E2E.ps1
pwsh ./scripts/Invoke-CodeQuality.ps1 -GitleaksCommand '<caminho-do-gitleaks>'
```

Os relatórios JSON/Markdown e logs ficam em `artifacts/code-quality/`, ignorado pelo Git. A meta de I10 é 100/100, sem warnings ou findings com desconto; score não comprova sozinho as regras de negócio.

## Cadastro, roles e tokens

1. Cadastre cada usuário com `POST /register`, corpo `{ "email": "<email>", "password": "<senha>" }`. O cadastro não concede roles; campos extras de role são ignorados.
2. Entre como Admin em `POST /login` com o mesmo formato. Use o `accessToken` retornado no cabeçalho `Authorization: Bearer <token>`; o token é opaco do Identity, não um JWT.
3. Consulte `GET /api/admin/users` para obter o id de cada usuário.
4. Como Admin, envie `PUT /api/admin/users/{id}/roles`, corpo `{ "roles": ["Employee"] }` (ou outra combinação de roles conhecidas). A lista substitui as roles atuais; `[]` remove todas. O Admin não pode remover sua própria role Admin.
5. **Faça novo login de cada usuário depois de alterar roles.** O token emitido anteriormente mantém as claims antigas até expirar.

Admin sozinho administra usuários e não recebe acesso funcional aos reembolsos. Roles funcionais podem ser acumuladas, mas ninguém pode aprovar nem pagar o próprio reembolso.

## Estados, permissões e validações

| Origem | Operação | Quem pode executar | Destino |
|---|---|---|---|
| inexistente | criar | Employee | Draft |
| Draft | editar | Employee proprietário | Draft |
| Draft | enviar | Employee proprietário | Submitted |
| Submitted | aprovar | Approver diferente do proprietário | Approved |
| Submitted | reprovar com justificativa | Approver diferente do proprietário | Rejected |
| Approved | pagar | Finance diferente do proprietário | Paid |

`Rejected` e `Paid` são finais. Não existem exclusão, cancelamento, reabertura ou reenvio. Cada alteração efetiva registra histórico; repetir uma transição retorna conflito e não duplica seus efeitos. O pagamento simulado registra valor, ator e horário na mesma operação lógica da mudança de estado e do histórico.

| Role | Escopo de leitura de despesas e histórico | Escrita |
|---|---|---|
| Employee | próprias, em qualquer estado | criar, editar/enviar Draft próprio |
| Approver | Submitted | aprovar/reprovar Submitted alheio |
| Finance | Approved e Paid | pagar Approved alheio |
| Auditor | todas | nenhuma |
| Admin | nenhum apenas por ser Admin | administrar roles |

A leitura de usuários com várias roles reúne os escopos concedidos. Um Draft alheio permanece privado nas decisões. Descrição: 10 a 500 caracteres após trim; valor decimal: R$ 0,01 a R$ 2.147.483.647,00; data: `yyyy-MM-dd`, não futura em UTC; categoria existente; justificativa de reprovação: 10 a 500 caracteres. Id, dono, estado, atores e horários são definidos pelo servidor.

| HTTP | Significado |
|---|---|
| 400 | entrada inválida, role desconhecida ou tentativa de remover a própria Admin |
| 401 | autenticação ausente ou inválida |
| 403 | autenticado sem permissão; inclui autoaprovação/autopagamento |
| 404 | recurso inexistente ou fora do escopo do ator |
| 409 | estado incompatível, transição repetida ou conflito de concorrência |

Erros usam ProblemDetails ou resposta de validação equivalente.

## Requisições de exemplo e smoke test

O arquivo [ExpenseHub.Api.http](sources/ExpenseHub.Api/ExpenseHub.Api.http) contém todos os endpoints obrigatórios, além de saúde e categorias, com caminhos separados até Paid e Rejected. Abra-o com a extensão **REST Client do VS Code**, que suporta `{{$processEnv ...}}` e referências às respostas nomeadas; clientes HTTP de outros editores podem exigir adaptação dessa sintaxe.

Defina no ambiente que inicia o editor `EXPENSEHUB_ADMIN_EMAIL/PASSWORD` e os pares `EXPENSEHUB_EMPLOYEE_EMAIL/PASSWORD`, `EXPENSEHUB_APPROVER_EMAIL/PASSWORD`, `EXPENSEHUB_FINANCE_EMAIL/PASSWORD` e `EXPENSEHUB_AUDITOR_EMAIL/PASSWORD`. Os nomes com `/` nesta lista significam duas variáveis, por exemplo `EXPENSEHUB_EMPLOYEE_EMAIL` e `EXPENSEHUB_EMPLOYEE_PASSWORD`. Use e-mails ainda não cadastrados. Defina a data:

```powershell
$env:EXPENSEHUB_EXPENSE_DATE = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
```

Execute os blocos na ordem. Depois de listar usuários, preencha as quatro variáveis `@employeeId`, `@approverId`, `@financeId` e `@auditorId` no editor com os ids retornados (ou forneça as correspondentes `EXPENSEHUB_*_ID` no ambiente antes de abrir o editor). Tokens e ids das despesas vêm das respostas nomeadas. Ajuste `@host` e `@categoryId` se necessário.

Com a API rodando, execute em outro terminal PowerShell 7:

```powershell
$env:EXPENSEHUB_ADMIN_EMAIL = Read-Host 'E-mail do Admin já criado'
$env:EXPENSEHUB_ADMIN_PASSWORD = Read-Host 'Senha do Admin' -MaskInput
pwsh ./tools/smoke-test.ps1 -BaseUrl http://localhost:5245
```

[tools/smoke-test.ps1](tools/smoke-test.ps1) registra por HTTP oito usuários com sufixo aleatório e senhas geradas em runtime, atribui roles como Admin e faz novo login. Verifica os fluxos Paid e Rejected, edição, histórico, filtros de leitura, 400/401/403/404/409, roles acumuladas, cadastro com tentativa de injetar roles, role inválida e proteção da própria Admin. Imprime tabela PASS/FAIL e sai com código 1 se houver falha. Não imprime credenciais/tokens. O script cria dados persistidos; use um banco local de teste. Novas execuções geram novos usuários e despesas.

## Guia do código

| Responsabilidade | Local |
|---|---|
| inicialização, DI, endpoints e configuração | `sources/ExpenseHub.Api/Program.cs` |
| login e cadastro | `sources/ExpenseHub.Api/Auth/` |
| administração de usuários e roles | `sources/ExpenseHub.Api/Admin/` |
| roles, ator autenticado e seed Admin | `sources/ExpenseHub.Api/Security/` |
| autorização contextual, filtros e transições | `sources/ExpenseHub.Api/Expenses/` |
| validação de entrada / respostas | `sources/ExpenseHub.Api/Dtos/` / `Responses/` |
| entidades e estados | `sources/ExpenseHub.Api/Domain/` |
| EF Core, mapeamentos e migrations SQLite | `sources/ExpenseHub.Api/Data/` |
| conversão de erros em HTTP | `sources/ExpenseHub.Api/Common/` |
| testes unitários com fakes | `sources/ExpenseHub.UnitTests/` |

## Limitações e autoria

O pagamento é simulado. Não há interface gráfica, notificações nem integração bancária. Tokens já emitidos mantêm as roles antigas até expirar: refaça login depois de alterar permissões. SQLite é a opção documentada para execução local; outro provider exigiria configuração e migrations compatíveis.

O código foi gerado e assistido por agentes de IA sob coordenação do aluno. Este registro não afirma uma revisão humana integral que não foi comprovada. A responsabilidade pela entrega e pela compreensão do código é do aluno. As verificações automatizadas e seus resultados devem ser conferidos nos testes, logs e CI; consulte também a [política de uso de IA](docs/USO-DE-IA.md).

## Entrega e referência do template

- Repositório público: https://github.com/gardim1/checkpoint2-expensehub-rm556013
- SHA final, link da execução de CI desse SHA e score do relatório `code-quality`: registrados na PR da I10 e no texto de entrega (o README não pode conter o SHA do próprio commit final).
- Execuções do workflow: https://github.com/gardim1/checkpoint2-expensehub-rm556013/actions/workflows/build.yml

O README original do professor informa prazo de **13/10/2026**. Esta entrega foi preparada em **14/10/2026**; a divergência fica registrada, sem presumir prorrogação ou aceite.

O template orienta criar repositório público por **Use this template**, sem fork; manter branches e PRs por issue; referenciar `Racass/checkpoint-csharpracass-expensehub#N` sem fechar a issue original; e demonstrar evolução por commits. A entrega é individual e o histórico deve refletir isso. Não versione senhas, tokens, dados de execução ou binários. Somente testes unitários próprios sem dependências externas entram na avaliação de I09; o projeto deve compilar sem warnings para a pontuação integral de I10.

Especificações: [enunciado](docs/ENUNCIADO.md), [requisitos](docs/REQUISITOS.md), [matriz de autorização](docs/MATRIZ-AUTORIZACAO.md), [rubrica](docs/RUBRICA.md), [processo GitHub](docs/PROCESSO-GITHUB.md) e [regras de qualidade](docs/code-quality-rules.md).
