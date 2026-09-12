[CmdletBinding()]
param(
    [string] $HostDoBanco = 'localhost',
    [string] $UsuarioDoBanco = 'postgres',
    [string] $NomeDoBanco = 'evolution_db',
    [string] $CaminhoDoPsql = 'C:\Program Files\PostgreSQL\17\bin\psql.exe',
    [string] $ArquivoPgPass = 'C:\Scripts\pgpass-crm-backup.conf',
    [int] $DiasDeRetencao = 7,
    [switch] $Confirmar
)

# =============================================================================
# CRM-Cartorio — expurgo periodico da copia de mensagens que a Evolution API
# guarda por conta propria no Postgres dela (evolution_db).
# -----------------------------------------------------------------------------
# POR QUE ISTO EXISTE
#     DATABASE_SAVE_DATA_NEW_MESSAGE=true e obrigatorio (sem ele o tique de
#     entrega/leitura nunca correlaciona — ver infra/whatsapp/docker-compose.yml
#     e docs/DEPLOY-WHATSAPP.md, secao 7). Isso significa que a Evolution
#     guarda uma SEGUNDA copia do texto de toda mensagem, fora do SQL Server —
#     o historico de verdade, que nunca e apagado (RF-015/RNF-008), continua
#     so no SQL Server. Esta copia da Evolution e um efeito colateral tecnico,
#     nao um requisito de produto, e por isso pode e deve ser podada.
#
# JANELA DE RETENCAO
#     7 dias por padrao: cobre folgadamente o tempo que um DELIVERY_ACK/READ
#     leva para voltar da Meta (minutos a poucas horas na pratica observada).
#
# SEGURANCA DE EXECUCAO — MODO SEGURO POR PADRAO
#     Sem -Confirmar, o script so CONTA quantas linhas seriam apagadas e
#     IMPRIME o resultado — nao apaga nada. So com -Confirmar ele executa o
#     DELETE de verdade. Antes de apagar, confirma que a tabela e a coluna de
#     data esperadas existem no schema atual — se o schema da versao instalada
#     da Evolution divergir do esperado, o script para com um erro claro em
#     vez de arriscar um DELETE contra a coluna errada.
#
# Ver docs/PROTECAO-DE-DADOS.md, secao 6, passo 5, para a instalacao como
# Tarefa Agendada (ExpurgoEvolutionCrm, diaria, 03:30, como SYSTEM).
# =============================================================================

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$env:PGPASSFILE = $ArquivoPgPass

function Invoke-Psql {
    param([Parameter(Mandatory)][string] $Sql)

    $saida = & $CaminhoDoPsql -h $HostDoBanco -U $UsuarioDoBanco -d $NomeDoBanco -t -A -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql saiu com codigo $LASTEXITCODE executando: $Sql"
    }
    return $saida
}

# ------------------------------------------------------- confirmar schema

$tabelaExiste = Invoke-Psql -Sql @"
SELECT COUNT(*) FROM information_schema.tables
WHERE table_name = 'Message' AND table_schema = 'public';
"@

if ($tabelaExiste.Trim() -ne '1') {
    throw "Tabela ""Message"" nao encontrada em $NomeDoBanco. O schema desta " +
          "versao da Evolution API pode ser diferente do esperado — confira " +
          "com '\dt' no psql antes de ajustar este script. Nada foi apagado."
}

$colunaExiste = Invoke-Psql -Sql @"
SELECT COUNT(*) FROM information_schema.columns
WHERE table_name = 'Message' AND column_name = 'messageTimestamp' AND table_schema = 'public';
"@

if ($colunaExiste.Trim() -ne '1') {
    throw "Coluna ""messageTimestamp"" nao encontrada em ""Message"". Confira " +
          'o schema real (comando \d "Message" no psql) antes de ajustar este ' +
          'script. Nada foi apagado.'
}

# ------------------------------------------------------------- contagem

# messageTimestamp e epoch em segundos (padrao Baileys/Prisma nesta versao).
$limiteEpoch = [DateTimeOffset]::UtcNow.AddDays(-$DiasDeRetencao).ToUnixTimeSeconds()

$totalAApagar = Invoke-Psql -Sql "SELECT COUNT(*) FROM ""Message"" WHERE ""messageTimestamp"" < $limiteEpoch;"
Write-Output "Mensagens com mais de $DiasDeRetencao dias na copia da Evolution: $($totalAApagar.Trim())"

if (-not $Confirmar) {
    Write-Output 'Modo seguro (sem -Confirmar): nada foi apagado. Rode novamente com -Confirmar para executar o expurgo.'
    return
}

if ($totalAApagar.Trim() -eq '0') {
    Write-Output 'Nada a apagar.'
    return
}

Invoke-Psql -Sql "DELETE FROM ""Message"" WHERE ""messageTimestamp"" < $limiteEpoch;" | Out-Null

$restante = Invoke-Psql -Sql "SELECT COUNT(*) FROM ""Message"" WHERE ""messageTimestamp"" < $limiteEpoch;"
Write-Output "Expurgo concluido. Linhas restantes fora da janela (esperado 0): $($restante.Trim())"
