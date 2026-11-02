# Conversão RGB MoCap opcional

O adaptador converte uma prévia corporal já reconstruída pelo [toyxyz_RGBMocap](https://github.com/toyxyz/toyxyz_RGBMocap) em `.almotion`, reproduzido no avatar Humanoid carregado no VRCStage. Não usa FBX. A reconstrução por IA ocorre no aplicativo externo.

Instale o RGBMoCap separadamente, prepare os modelos conforme seus termos, reconstrua/revise o vídeo e salve o projeto com uma prévia válida. Em **Poses / clipes → Mocap de vídeo • RGB MoCap**, localize a instalação e abra o projeto. Selecione a captura e converta para a biblioteca local. Pausa, velocidade, repetição e extração de pose estão disponíveis.

O adaptador valida a geração/frescor da prévia e não altera os arquivos de entrada. A reprodução usa retargeting Humanoid; proporções diferentes podem causar interseções/deslizamento. Dados de dedos ausentes não são inventados. Ative o bloqueio de tracking da pose se quiser reproduzir a captura sem sobreposição de outros dispositivos.

Compatibilidade de origem: revisão `c372d6d31b0ea259f3334746e017948487cebbfb`. Consulte a documentação upstream antes de preparar projetos de versões diferentes. O código próprio deste adaptador está sob MIT. RGBMoCap, runtimes, pesos de IA e modelos corporais têm termos separados, possivelmente não comerciais. Nenhum peso, vídeo, projeto de usuário ou modelo corporal registrado acompanha o VRCStage. Consulte [THIRD_PARTY_NOTICES](../../THIRD_PARTY_NOTICES.md).
