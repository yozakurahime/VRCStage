# VRCStage

**Seu avatar do VRChat na live, sem converter para VRM.**

O VRCStage é um estúdio Windows independente para VTubers. Você exporta o avatar uma vez pelo projeto Unity, abre o arquivo no programa e configura tracking, expressões, cenário e câmera. Depois transmite pelo OBS, sem precisar manter o Unity ou VRChat abertos.

Versão **0.10.1 alpha**, anteriormente AvatarLab Offline. Projeto independente, sem vínculo com a VRChat Inc. A compatibilidade é parcial: o programa usa implementações próprias de física e outros sistemas.

## Primeira transmissão

1. Baixe o ZIP Windows em [Releases](https://github.com/yozakurahime/VRCStage/releases), extraia tudo e abra `VRCStage.exe`.
2. No projeto Unity do avatar, importe `VRCStage-Exporter.unitypackage`. Editor testado: Unity **2022.3.22f1**.
3. Selecione a raiz do avatar e abra **Tools → VRCStage → Exportar avatar local**. O exportador tenta obter automaticamente controllers, menus, parâmetros e outras configurações do avatar. Leia o relatório ao exportar.
4. Exporte um `.alavatar` ou envie para a Inbox local. A exportação não publica nada na internet ou no VRChat.
5. No VRCStage, a aba **Começar** leva à importação, rosto/voz, mãos/VR opcionais, enquadramento e transmissão.
6. Em **Transmissão**, inicie a saída local e capture a janela limpa no OBS. Também é possível usar Spout ou a câmera virtual do OBS já instalado. Iniciar a saída local não inicia uma live na internet.

## Tracking e criação

iFacialMocap, mapeamento ARKit/Unified Expressions, voz por microfone, Leap/Ultraleap, webcam, OpenXR, trackers SteamVR e integrações OSC/VMC estão disponíveis, com limites dependentes do avatar e dos dispositivos. Sinais UE que o iPhone não mede não podem ser recuperados integralmente por um mapeamento.

O Leap sobre a mesa acompanha a altura do corpo ao sentar/agachar e mantém a calibração por avatar. Nas poses, deixe **Fixar corpo e mãos durante a prévia** desmarcado para combinar com o tracking.

Câmeras cinematográficas, fotos PNG, luzes, mundos/objetos locais e pós-processamento estão disponíveis. No modo Jogar: **F2** entra/sai, mouse gira, roda dá zoom, WASD move, Shift corre, Espaço pula e Ctrl/C agacha. **Esc** libera o controle. Em **Movimento**, ajuste aceleração, frenagem e ritmo dos passos. A versão 0.10.1 melhora as transições, o pulo em movimento e a retomada após aterrissar. Veja [locomoção](LOCOMOTION.md).

## Dados e compatibilidade

Os formatos `.alavatar`, `.alworld` e a pasta `%USERPROFILE%\AppData\LocalLow\AvatarLab\AvatarLab Offline` foram mantidos para preservar instalações anteriores. Os nomes internos de canais Spout/OBS ainda podem aparecer como AvatarLab.

Avatares, mundos, calibrações, fotos e configurações pessoais não entram no código público. O pacote só inclui exemplos gerados pelo projeto. GoGo Loco, avatares comerciais e modelos externos de mocap não acompanham a distribuição. Respeite os termos dos criadores para uso em lives e em aplicativos externos.

O programa não executa Udon, recursos de rede ou todos os sistemas do cliente VRChat. PhysBones e constraints podem se comportar de forma diferente. Veja o [README principal](../README.md), o [guia inicial](QUICKSTART.md), o [roadmap](../ROADMAP.md) e as [licenças de terceiros](../THIRD_PARTY_NOTICES.md).
