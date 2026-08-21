import { formatarHoraDoDia, formatarNumero } from '@/lib/formatacao'

/**
 * 24 barras verticais — "horários de maior demanda". Mesma linguagem de barra
 * de `fluxo-de-atendimentos.tsx`: canto superior arredondado, realce da coluna
 * inteira no hover (inclusive o vazio acima da barra, para hora zerada também
 * responder) e crescimento escalonado a partir da base.
 *
 * TEM EIXO Y, e não é enfeite: antes o valor de cada hora existia só no
 * atributo `title`, ou seja só para quem tem mouse. Num celular o gráfico
 * ficava sem nenhuma magnitude legível — dava a forma da distribuição e
 * nenhum número. Duas marcas (0 e o pico) resolvem para todo mundo, sem
 * depender de hover nem de toque.
 */
export function MiniHistograma({ valoresPorHora }: { valoresPorHora: readonly number[] }) {
  const maximo = Math.max(1, ...valoresPorHora)
  const semDados = valoresPorHora.every((v) => v === 0)

  if (semDados) {
    return (
      <div className="flex h-32 items-center justify-center text-sm text-tinta-fraca">
        Nenhum atendimento aberto no período.
      </div>
    )
  }

  return (
    <div className="flex gap-2">
      {/* `pb-5` reserva a faixa dos rótulos de hora, para o zero do eixo cair
          exatamente na base das barras. */}
      <div className="flex h-32 w-5 shrink-0 flex-col-reverse justify-between pb-5 text-right">
        <span className="font-dado text-[10px] leading-none text-tinta-fraca">0</span>
        <span className="font-dado text-[10px] leading-none text-tinta-fraca">
          {formatarNumero(maximo)}
        </span>
      </div>

      <div className="relative min-w-0 flex-1">
        <div aria-hidden className="absolute inset-x-0 top-0 flex h-32 flex-col-reverse justify-between pb-5">
          <div className="h-px w-full bg-linha-forte" />
          <div className="h-px w-full bg-linha" />
        </div>

        <div className="relative flex h-32 items-stretch gap-px">
          {valoresPorHora.map((valor, hora) => (
            <div
              key={hora}
              className="group relative flex min-w-0 flex-1 flex-col justify-end pb-5"
              title={`${formatarHoraDoDia(hora)} — ${formatarNumero(valor)} atendimento(s)`}
            >
              <span
                aria-hidden
                className="pointer-events-none absolute inset-x-0 bottom-5 top-0 rounded-sm transition-colors duration-150 group-hover:bg-superficie"
              />

              {valor > 0 ? (
                <span
                  className="relative w-full origin-bottom rounded-t-[3px] bg-tinta/80 transition-colors duration-150 group-hover:bg-tinta"
                  style={{
                    height: `${(valor / maximo) * 100}%`,
                    animation: `crm-crescer-barra-vertical 420ms cubic-bezier(0.16, 1, 0.3, 1) ${hora * 12}ms both`,
                  }}
                />
              ) : null}

              {/* Rótulo a cada 6 horas: 00h, 06h, 12h, 18h — o suficiente para
                  situar o turno sem escrever 24 números lado a lado. */}
              {hora % 6 === 0 ? (
                <span className="absolute bottom-0 left-0 whitespace-nowrap text-[10px] font-medium leading-5 text-tinta-fraca">
                  {formatarHoraDoDia(hora)}
                </span>
              ) : null}
            </div>
          ))}
        </div>
      </div>

      {/* Tabela equivalente para leitor de tela — é a resposta honesta de
          acessibilidade para gráfico, e não depende de hover nem de toque. */}
      <table className="sr-only">
        <caption>Atendimentos abertos por hora do dia</caption>
        <thead>
          <tr>
            <th scope="col">Hora</th>
            <th scope="col">Atendimentos</th>
          </tr>
        </thead>
        <tbody>
          {valoresPorHora.map((valor, hora) => (
            <tr key={hora}>
              <td>{formatarHoraDoDia(hora)}</td>
              <td>{valor}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
