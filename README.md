# CleanPanel — Monitor de Hardware, Diagnóstico e FPS para Windows 11

**CleanPanel** (v1.1.0) é um software completo de monitoramento, diagnóstico de hardware e telemetria de jogos em tempo real desenvolvido em **C# 12**, **.NET 8** e **WPF (WPF-UI)**. Combina um **Painel Principal estilo Gerenciador de Tarefas do Windows 11 / HWiNFO moderno** com um **Overlay flutuante** altamente personalizável e de baixíssimo impacto para jogos.

---

## Principais Funcionalidades

### 1. Interface Principal Monocromática (Design Web Minimalista — Dark & Light)
A janela principal do **CleanPanel** utiliza um sistema de design estritamente monocromático inspirado em interfaces Web modernas (Vercel / Linear / Shadcn UI), definido no dicionário centralizado `Resources/MonochromeTheme.xaml` com suporte dinâmico aos temas **Escuro** (`#09090B`), **Claro** (`#FAFAFA`) e **Automático** (seguindo o Windows):

- **Barra Lateral Fixa Ultra-Minimalista (`200px`)**:
  - **Cabeçalho**: Logo geométrica minimalista do CleanPanel com badge discreta de versão (`v1.1.0`).
  - **Navegação Principal**: 4 abas de telemetria (`Visão Geral`, `Processador`, `Placa de Vídeo` e `Sistema & Discos`).
  - **Rodapé**: Acesso rápido à página de `Configurações` e indicador sutil de status do Overlay (`Overlay Ativo` / `Oculto`) com toggle instantâneo e leitura de FPS tabular.
- **Aba "Visão Geral" (Dashboard Responsivo)**:
  - **Cards KPI de Resumo**: Grade responsiva de 4 colunas (`PROCESSADOR`, `PLACA DE VÍDEO`, `MEMÓRIA RAM`, `TAXA DE QUADROS`) com micro-rótulos em caixa alta, valores tabulares e barras de progresso finas (`4px`) monocromáticas.
  - **Cards Detalhados**: Sistema Operacional (`CurrentBuild >= 22000`), Processador (CPU), lista Multi-GPU, Memória RAM e Unidades de Armazenamento.
- **Aba "Processador" (Detalhes Avançados e Per-Core Telemetry)**:
  - Especificações completas do processador (Nome, Núcleos Físicos e Threads Lógicos).
  - **Suporte Nativo a CPUs Híbridas Intel (12ª geração Alder Lake e superiores)**: Detecção automática da topologia de núcleos via Win32 `GetLogicalProcessorInformationEx` (`RelationProcessorCore`), discriminando contagem de núcleos físicos de Performance (`P-Cores`) e de Eficiência (`E-Cores`), exibindo resumo detalhado (ex: `10 Núcleos (6P + 4E) / 16 Threads Lógicos`).
  - **Métricas Globais**: Uso Total (%), Temperatura (°C), Consumo Elétrico (W), Tensão VCore (V) e Clock Médio/Máximo (MHz).
  - **Grade de Núcleos (Per-Core Telemetry)**: Grid de 2 colunas exibindo cada núcleo/thread individualmente com sua identificação (`Núcleo #N (P-Core)` / `(E-Core)`), barra de Uso (`4px`) e Clock atual (MHz) em tempo real.
- **Aba "Placa de Vídeo" (Suporte Multi-GPU e Sensores Avançados)**:
  - **Seletor Multi-GPU (`ComboBox`)**: Permite alternar instantaneamente entre múltiplas GPUs (ex: GPU Integrada + GPU Dedicada).
  - **Métricas em Tempo Real da GPU Selecionada**: Uso do Core (%), Uso de VRAM (GB / %), Temperatura Core e Hot Spot (°C), Clock do Core e da Memória (MHz), Consumo (W) e Velocidade dos Fans (RPM / %).
- **Aba "Sistema & Discos"**:
  - Uso detalhado de Memória RAM física com barra visual de ocupação (`4px`).
  - Lista de Unidades de Armazenamento combinando partições lógicas (`System.IO.DriveInfo`) e sensores térmicos físicos (`HardwareType.Storage` do LibreHardwareMonitor), exibindo Espaço Ocupado vs. Total e a Temperatura física atual de cada SSD/HDD (com fallback automático para `"Sem sensor"`).
- **Aba "Configurações" (Layout Web Centralizado `MaxWidth="780"` em 3 Sub-Abas)**:
  - **Geral & Sistema**: Iniciar com o Windows como Administrador (`CleanPanelAutoStart`), badge do Atalho Global (`Ctrl + Shift + O`), seletor de Tema (`Escuro`, `Claro`, `Automático`) e botão `Encerrar CleanPanel`.
  - **Overlay & Layout**: Ativar/Ocultar Overlay, Modo de Exibição Inteligente (apenas em Tela Cheia), Travar Overlay (*Click-Through*), Modo Compacto (com sincronização automática para Orientação Horizontal), Orientação (`Vertical` / `Horizontal`), Controle de Escala (`70%`–`200%`), Opacidade do Fundo (`10%`–`100%`) e grade rápida de Alinhamento em 6 posições.
  - **Sensores & Ordem**: Reordenação dinâmica dos blocos do Overlay (`▲` / `▼`, mantendo o FPS na última posição por padrão), toggle **Separar Núcleos P-Core e E-Core (Intel Híbrido)** e seleção granular por grupo (`CPU`, `GPU`, `RAM`, `FPS`).
  - **Separação Opcional de P-Cores e E-Cores (Intel)**: Quando ativada em processadores Intel híbridos, exibe no Overlay o Uso (%) e Clock (MHz) divididos (`Uso P-Cores: XX%` / `Uso E-Cores: XX%` e `Clock P-Cores: XXXX MHz` / `Clock E-Cores: XXXX MHz`), e no Modo Compacto horizontal/vertical `CPU P: XX% | E: XX%`. Se desativada ou em CPUs AMD / Intel legadas, o funcionamento permanece no modo padrão unificado com zero regressão.

---

### 2. Overlay Monocromático Padrão Agrupado por Hardware
- **Estética Monocromática Limpa (`Views/OverlayWindow.xaml`)**:
  - Fundo preto fosco profundo (`#09090B`) com canal Alpha controlado dinamicamente pela `OverlayOpacity` configurada, borda nítida de `1px` em `#27272A` (`CornerRadius="6"`), cabeçalhos em branco puro (`#FAFAFA`), rótulos em cinza neutro (`#A1A1AA`) e valores numéricos tabulares em `Consolas` (`#FAFAFA`), com bloco **FPS** destacado em escala maior na última posição.
- **Modelos Reais como Cabeçalhos e Ordem Consistente (`GetShortHardwareName`)**:
  - O Overlay agrupa as métricas abaixo do nome real e limpo do componente (ex: `Intel Core i9-13900K`, `AMD Ryzen 5 5600`, `RTX 5060`, `RX 7600`) mantendo ordem fixa: **CPU** (`Uso` → `Clock` → `Temperatura` → `Consumo`) e **GPU** (`Uso` → `Clock` → `Temperatura` → `Consumo` → `VRAM`).
- **Monitoramento de VRAM no Overlay**:
  - Exibe o consumo de memória de vídeo em tempo real no formato `VRAM: 6.2 GB (78%)` (ou `VRAM: 1.4 GB` em GPUs integradas com memória compartilhada).
- **Nova Logo Exclusiva CleanPanel**:
  - Ícone multi-resolução (`256x256`, `64x64`, `32x32`, `16x16`) com base *Squircle* em azul-noite (`#0F172A` -> `#1E293B`), painéis translúcidos sobrepostos estilo *Glassmorphism* e barras minimalistas de telemetria em gradiente Ciano/Esmeralda (`#38BDF8` -> `#2DD4BF`).

---

### 3. Engenharia de Performance e Baixo Consumo de CPU/Memória (Deep Optimization)
O **CleanPanel** foi arquitetado para ter impacto virtualmente zero durante sessões de jogos competitivos:

- **Cache de Referências Diretas de Sensores (Zero LINQ no Loop de 1s)**:
  - Na inicialização do `HardwareMonitorService`, todas as referências diretas dos objetos `ISensor` (`_cpuTempSensor`, `_cpuLoadSensor`, `_gpuTempSensor`, `_gpuVramUsedSensor`, `_cpuCoreClockSensors`, pares de núcleos `_cachedCpuCores`, GPUs `_cachedGpus` e discos `_cachedStorages`) são mapeadas uma única vez e armazenadas em memória.
  - No loop de 1 segundo do `PeriodicTimer`, o serviço executa apenas `hardware.Update()` nos componentes necessários e lê diretamente a propriedade `.Value` dos ponteiros `ISensor` cacheados.
- **Brushes Congelados (`SolidColorBrush.Freeze()`)**:
  - Todos os pincéis de cores customizadas do Overlay são congelados (`brush.Freeze()`) imediatamente após a conversão do código Hex, reduzindo o overhead de renderização do WPF para zero no loop de telemetria.
- **Otimização Extrema no Capturador de FPS (`CleanPanelFpsSession`)**:
  - O PID da janela ativa é atualizado apenas **1 vez por segundo** em `volatile int _activeForegroundPid`.
  - A primeira instrução do callback `OnEtwEvent` é uma comparação inteira direta em `O(1)` (`if (data.ProcessID != _activeForegroundPid) return;`).
- **Debounce Assíncrono na Persistência (`SettingsService`)**:
  - Alterações contínuas de configuração passam por um *Debounce* assíncrono de **300 ms** com `CancellationTokenSource` e `FlushImmediate()` garantido no encerramento.
- **Polling Seletivo Sob Demanda**:
  - Quando a janela principal está fechada ou minimizada na bandeja do sistema (*System Tray*), os sensores pesados exclusivos da janela principal ficam suspensos, atualizando apenas os sensores leves exigidos pelo Overlay flutuante.

---

### 4. Blindagem Total e Tolerância a Falhas (Fault Tolerance & Sensor Fallback)
- **Validação Estrita de Sensores**: Todo valor `float?` passa pelo validador `IsValidSensorValue`, descartando leituras nulas, `NaN`, `Infinity` ou `<= 0`.
- **Exibição Graciosa na UI (Sem Zeros Falsos)**: Sensores não suportados pelo hardware exibem `"N/D"` ou `"Sem sensor"` em vez de zeros falsos.
- **Isolamento de Exceções (`try/catch` Independentes)**: Leituras de `CPU`, `GPU`, `RAM`, `Storage`, `Motherboard`, `WMI` e `Registry` são isoladas individualmente.

---

## Estrutura do Projeto

```text
HardwareOverlayApp/
├── Assets/
│   └── app.ico
├── Helpers/
│   ├── IconGeneratorHelper.cs
│   ├── UpdateVisitor.cs
│   └── WindowWin32Helper.cs
├── Models/
│   ├── AppSettings.cs
│   ├── HardwareMetrics.cs
│   └── HardwareSpecs.cs
├── Resources/
│   └── MonochromeTheme.xaml
├── Services/
│   ├── FpsMonitorService.cs
│   ├── HardwareMonitorService.cs
│   ├── SettingsService.cs
│   ├── StartupService.cs
│   └── TrayIconService.cs
├── ViewModels/
│   └── MainViewModel.cs
├── Views/
│   ├── OverlayWindow.xaml
│   ├── OverlayWindow.xaml.cs
│   ├── SettingsWindow.xaml
│   └── SettingsWindow.xaml.cs
├── App.xaml
├── App.xaml.cs
├── app.manifest
├── CleanPanel.csproj
├── installer.iss
├── build-installer.ps1
├── .gitignore
├── LICENSE
└── README.md
```

---

## Como Compilar e Executar

### Pré-requisitos
- **Windows 10 ou Windows 11 (x64)**
- **.NET 8.0 SDK** instalado
- **Execução como Administrador** (exigido pelo `app.manifest` para acesso aos sensores físicos de hardware e sessão ETW de FPS)

### Compilação e Execução via Terminal (PowerShell como Administrador)

```powershell
cd HardwareOverlayApp
dotnet clean
dotnet build -c Release
dotnet run -c Release
```

### Como gerar o instalador (`CleanPanel_Setup.exe`)

```powershell
powershell ./build-installer.ps1
```

---

## Roadmap Futuro

- **Arquitetura de Plugins para a Comunidade (`CleanPanel Plugin SDK`)**:
  - Suporte nativo ao carregamento dinâmico de módulos e extensões via arquivos `.dll` externos em uma pasta `Plugins/`.
  - Permitirá que desenvolvedores da comunidade criem **novas abas customizadas**, **widgets para o Overlay** e **integrações diretas com ecossistemas externos**, incluindo:
    - Controle e sincronização de iluminação RGB por temperatura/carga via **OpenRGB SDK**.
    - Integração com controladoras de ventoinhas/watercoolers customizados, **HWiNFO Shared Memory**, **MSI Afterburner / RTSS**, **Elgato Stream Deck** e webhooks/telemetria remota.

---

## Licença

Distribuído sob a licença **MIT**. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
