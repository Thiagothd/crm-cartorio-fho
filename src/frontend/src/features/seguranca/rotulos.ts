import type { TipoDeAcesso, TipoDeEventoDeSeguranca } from '@/lib/api/tipos'

export const ROTULO_DO_ACESSO: Record<TipoDeAcesso, string> = {
  LeituraDeConversa: 'Abriu a conversa',
  DownloadDeAnexo: 'Baixou anexo',
  LeituraDeFichaDeCliente: 'Abriu a ficha do cliente',
  AcessoTecnicoSolicitado: 'Solicitou acesso técnico',
  AcessoTecnicoAprovado: 'Aprovou acesso técnico',
  AcessoTecnicoNegado: 'Negou acesso técnico',
  AcessoTecnicoRevogado: 'Revogou acesso técnico',
  ChaveDeDadosRotacionada: 'Rotacionou a chave de dados',
  CifraDeLegadoConcluida: 'Concluiu a cifra de dados existentes',
}

export const ROTULO_DO_EVENTO: Record<TipoDeEventoDeSeguranca, string> = {
  MfaAtivado: 'Ativou a verificação em duas etapas',
  MfaDesativado: 'Desativou a verificação em duas etapas',
  CodigoDeRecuperacaoUsado: 'Usou um código de recuperação',
  CodigosDeRecuperacaoRegenerados: 'Gerou novos códigos de recuperação',
  ResetAdministrativo: 'Reset administrativo da verificação em duas etapas',
  ResetDeEmergencia: 'Reset de emergência (break-glass)',
  BloqueioDeMfaPorFalhas: 'Bloqueio por falhas seguidas de código',
  TicketDeMatriculaEmitido: 'Emitiu ticket de matrícula',
  TicketDeBootstrapEmitido: 'Emitiu ticket de bootstrap',
  PoliticaDeMfaAtivada: 'Ativou a política de verificação obrigatória',
  TicketDeRematriculaReemitido: 'Reemitiu ticket de rematrícula',
  PermissoesAlteradas: 'Alterou as permissões de alguém',
  LoginRealizado: 'Login realizado',
}
