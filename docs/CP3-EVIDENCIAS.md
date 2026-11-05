# CP3 — evidências e reprodução

Vinicius Silva Gardim — RM556013 — 3ESPX — individual.

Base CP2: `f3b6721bfeb9c42bd3871b228e8306311234ad60`.

## Fontes, imagens e execução

Repositório: https://github.com/gardim1/checkpoint2-expensehub-rm556013

As sete issues originais foram referenciadas em PRs próprias, sem fechamento das especificações centrais. PRs CP3: #11 a #17 deste repositório (a #16 corresponde à I07; a #17, à I06). Commits de implementação usam exclusivamente a identidade Git do aluno, sem coautoria adicionada.

Versão da aplicação executada e validada: `333d0201f2b50366e8536fdfabd0ba3fd3c65833`. Os commits posteriores de organização desta entrega alteram somente a documentação.

- Imagem web: `ghcr.io/gardim1/expensehub-web:333d0201f2b50366e8536fdfabd0ba3fd3c65833`.
- Imagem worker: `ghcr.io/gardim1/expensehub-worker:333d0201f2b50366e8536fdfabd0ba3fd3c65833`.
- [Release pública com imagens, evidências e fontes](https://github.com/gardim1/checkpoint2-expensehub-rm556013/releases/tag/cp3-333d0201f2b50366e8536fdfabd0ba3fd3c65833).
- [Evidências completas E01–E05, incluindo Excel e logs](https://github.com/gardim1/checkpoint2-expensehub-rm556013/releases/download/cp3-333d0201f2b50366e8536fdfabd0ba3fd3c65833/ExpenseHub-CP3-RM556013-evidencias.zip).

 Imagens `ghcr.io/gardim1/expensehub-web:<SHA>` e `ghcr.io/gardim1/expensehub-worker:<SHA>`, publicadas pelo workflow `cp3-images` **depois** de build sem warnings, testes e verificação de formatação. O label OCI `org.opencontainers.image.revision` registra a versão.

O acesso à correção não depende da visibilidade dos pacotes GHCR: a release pública disponibiliza `expensehub-images.tar.gz`, contendo as mesmas duas imagens publicadas com suas referências originais, e a soma SHA-256. Não há credenciais no arquivo. Carregar imagens publicadas não recompila fontes.

1. Clone este repositório (ou baixe os fontes compactados da release) e baixe o bundle de imagens da versão validada.
2. Copie `.env.example` para `.env`; preencha `IMAGE_TAG` com o SHA entregue e escolha as credenciais locais. Não use credenciais reais de empresa.
3. Execute `docker load -i expensehub-images.tar.gz`.
4. Execute `docker compose pull --policy missing` e `docker compose up -d --wait`.
5. API em `http://localhost:5245`; broker em `http://localhost:15672`. Cadastre usuários, atribua roles pelo Admin e faça novo login conforme README.

O Compose referencia as imagens publicadas, usa um consumidor, compartilha SQLite em `/data/expensehub.db` e preserva os três volumes de dados, chaves do Identity e RabbitMQ. A web aplica migrations antes de o worker iniciar. Os experimentos não removem volumes.

## Testes próprios e qualidade

```shell
dotnet test sources/ExpenseHub.slnx
dotnet build sources/ExpenseHub.slnx --warnaserror
dotnet format sources/ExpenseHub.slnx --verify-no-changes
```

205 testes aprovados, dos quais 25 são novos casos da CP3, sem banco, rede ou RabbitMQ reais. Asserções cobrem cálculo/zeros/limites inclusivos, perfis, propriedade, conflitos de download, falha de publicação, falha de armazenamento, resultado salvo, duplicata com novo processador, retomada de Processing, erro definitivo e estrutura/valores numéricos do Excel. O teste UTC reproduziu a leitura sem Kind e passou após a correção do envelope.

Analisadores, severidades e scripts originais foram preservados. Verificações de referência aprovadas: [qualidade](https://github.com/gardim1/checkpoint2-expensehub-rm556013/actions/runs/41943895168), [publicação](https://github.com/gardim1/checkpoint2-expensehub-rm556013/actions/runs/41943895094). Estes links correspondem à versão validada identificada acima. O relatório completo de qualidade está no pacote de evidências.

## E01–E05 executados em contêineres reais

Execução da versão validada: [GitHub Actions 41944172632](https://github.com/gardim1/checkpoint2-expensehub-rm556013/actions/runs/41944172632), com imagens `333d0201f2b50366e8536fdfabd0ba3fd3c65833`. **99 verificações aprovadas, zero falhas.** O script cria dados fictícios por HTTP, com tokens somente em memória. O pacote de evidências vinculado acima contém os resultados desta execução, que baixou as imagens da release pública sem autenticação no registry.

| Experimento | Evidência observada |
|---|---|
| E01 — normal | Id `6f67fae5-13be-4cf3-a61f-72f703cf2ac7`; Completed; 3 reembolsos, 175,00; Approved: 2/150,00; Paid: 1/25,00; demais estados zero. Excel .xlsx válido, MIME/attachment e células numéricas conferidos contra o resultado salvo. |
| E02 — worker parado | Id `4e9a0049-f4a6-41b6-9a4b-02bf16367a72`; Pending e Excel 409 com worker parado; RabbitMQ Ready=1; retomada concluiu exatamente o mesmo id. |
| E03 — duplicata | Reinício do worker, republicação `{"reportId":"6f67fae5-13be-4cf3-a61f-72f703cf2ac7"}` na fila; log de duplicata; envelope, timestamps e resultado idênticos, Excel fiel. Nenhum novo POST foi usado para simular a duplicata. |
| E04 — isolamento | Duas contas Employee e uma Auditor; anônimo 401; Employee/Global 403; datas inválidas 400; outro Employee e Auditor recebem 404 no detalhe e Excel alheios e não veem o id na listagem. Global inclui 4/375,00; segundo Employee/Mine inclui somente 1/200,00. |
| E05 — persistência | `docker compose up -d --force-recreate web worker`; mesmo id e resultado anteriores e mesmo bearer token continuam válidos. Banco, chaves e broker preservados. Referências das imagens em `containers.json`. |

Arquivos do pacote sanitizado: `E01-result.json`, `E01-report.xlsx`, `E02.json`, `E03.json`, `E04.json`, `E05.json`, `checks.json`, `containers.json`, `worker.log`, `web.log`. O Excel é evidência separada, não binário de build nos fontes.

Para reproduzir em ambiente de estudo inicializado:

```shell
python tools/cp3-experiments.py
```

Requer Python 3, apenas biblioteca padrão, Docker/Compose e `.env` local. Cria novas contas e despesas fictícias com sufixo aleatório; não apaga dados. O workflow manual `cp3-experiments`, com `image_tag=<SHA>`, fornece a mesma execução em Linux e armazena o artefato sanitizado. Os logs reais mantêm `reportId` para correlacionar publicação, início, persistência, duplicata e ack.

Para repetir E03 manualmente: reinicie apenas o worker; no painel RabbitMQ abra `Queues and Streams` → `expensehub.reports` → `Publish message`; configure `delivery_mode=2`, `content_type=application/json` e payload `{"reportId":"<id-já-concluído>"}`. Publique na mesma fila e compare o resultado anterior com o detalhe e os logs. Não altere o banco nem crie outro relatório.

## Diagnóstico e limites exigidos

- **202 é aceitação:** somente depois de registrar a solicitação e receber confirmação da publicação. Não significa Completed.
- **Worker parado:** broker aceita e mantém mensagem persistente; o mesmo id avança quando o consumidor retorna. Ready e Unacked são métricas do broker, não estados da aplicação.
- **Broker indisponível ou confirmação perdida:** retorna 503 com id registrado e erro sanitizado. A publicação pode ter ocorrido ou não; a solicitação continua consultável. O teste unitário cobre a resposta sem confirmação.
- **Janela sem outbox:** queda entre banco e broker pode deixar Pending sem mensagem. Reiniciar o worker não recupera publicação que nunca aconteceu. Novo POST cria outra solicitação. Não há outbox nem garantia universal de recuperação.
- **Reentrega:** Completed/Failed são terminais persistidos; duplicata não altera resultado nem horários. Processing pode ser retomado após interrupção. O ambiente exige um único worker com prefetch 1.
- **Falha definitiva:** dados persistidos inválidos produzem Failed com code/message sanitizados; payload inválido é rejeitado sem requeue infinito. Falhas ao salvar propagam e não são confirmadas como sucesso.

O prazo considerado é o comunicado atual do professor: **04/11/2026, 23h59**; a apostila e as issues contêm um prazo antigo divergente. LinkedIn foi expressamente excluído desta entrega pelo aluno.
