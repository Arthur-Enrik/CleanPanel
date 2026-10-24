# CleanPanel — Monitor de Hardware, Diagnóstico e FPS para Windows 11

**CleanPanel** (v1.1.0) é um software completo de monitoramento, diagnóstico de hardware e telemetria de jogos em tempo real desenvolvido em **C# 12**, **.NET 8** e **WPF (WPF-UI)**. Combina um **Painel Principal estilo Gerenciador de Tarefas do Windows 11 / HWiNFO moderno** com um **Overlay flutuante** altamente personalizável e de baixíssimo impacto para jogos.

---

## Principais Funcionalidades

### 1. Interface Principal Estilo Task Manager (Windows 11 Fluent Design + Mica)
A janela principal do **CleanPanel** conta com navegação lateral moderna dividida em **4 seções de telemetria**, **1 seção dedicada de Personalização Visual** e um **ícone de engrenagem no rodapé para Configurações**:

- **Aba "Visão Geral" (Resumo do Sistema)**:
  - **Card do Sistema Operacional**: Nome corrigido para Windows 11 (detecção via `CurrentBuild >= 22000` no Registro), Edição/DisplayVersion e Build completa.
  - **Card do Processador (CPU)**: Modelo completo, contagem física de Núcleos e Threads Lógicos, Uso Total (%) com barra visual e Temperatura (°C).
  - **Card(s) de Placa de Vídeo (Multi-GPU)**: Lista todas as GPUs instaladas (dedicadas e integradas — NVIDIA, AMD e Intel), exibindo Modelo, VRAM Total (ou `"Compartilhada / Integrada"`), Uso (%) e Temperatura (°C).
  - **Card de Memória RAM**: Total instalado, memória usada, memória livre (em GB) e porcentagem de ocupação.
  - **Card de Armazenamento**: Resumo de todas as partições fixas, capacidade total, espaço usado/livre e temperatura física atual de cada disco.
- **Aba "Processador (CPU)" (Detalhes Avançados e Per-Core Telemetry)**:
  - Especificações completas do processador (Nome, Núcleos Físicos e Threads Lógicos).
  - **Métricas Globais**: Uso Total (%), Temperatura (°C), Consumo Elétrico (W), Tensão VCore (V) e Clock Médio/Máximo (MHz).
  - **Grade de Núcleos (Per-Core Telemetry)**: Grid exibindo cada núcleo/thread individualmente com sua barra de Uso (%) e Clock atual (MHz) em tempo real.
- **Aba "Placa de Vídeo (GPU)" (Suporte Multi-GPU e Sensores Avançados)**:
  - **Seletor Multi-GPU (`ComboBox`)**: Permite alternar instantaneamente entre múltiplas GPUs (ex: GPU Integrada + GPU Dedicada).
  - **Métricas em Tempo Real da GPU Selecionada**: Uso do Core (%), Temperatura Core e Hot Spot (°C), Clock do Core e da Memória (MHz), Uso de VRAM (Usado / Total em GB), Consumo (W) e Velocidade dos Fans (RPM / %).
- **Aba "Sistema & Armazenamento"**:
  - Uso detalhado de Memória RAM com barra visual de ocupação.
  - Lista de Unidades de Armazenamento combinando partições lógicas (`System.IO.DriveInfo`) e sensores térmicos físicos (`HardwareType.Storage` do LibreHardwareMonitor), exibindo Espaço Ocupado vs. Total e a Temperatura física atual de cada SSD/HDD (com fallback automático para `"Sem sensor"`).
- **Aba "Personalização" (Temas do App, Cores e Ordem do Overlay)**:
  - **Tema do Aplicativo (`Dark`, `Light` e `Auto`)**: Alterne entre `Escuro (Dark)`, `Claro (Light)` e `Automático (Seguir o Windows)` com sincronização dinâmica via `ApplicationThemeManager` e monitoramento do Registro do Windows (`AppsUseLightTheme`). Todos os cards e textos usam recursos dinâmicos (`DynamicResource`) para legibilidade perfeita em ambos os modos.
  - **Personalização de Cores do Overlay em Tempo Real**: Customize a **Cor do Fundo** (`#141418`), **Cor da Borda** (`#33FFFFFF`), **Cor de Destaque dos Títulos de Hardware** (`#00E5FF`) e **Cor do Texto/Valores das Métricas** (`#FFFFFF`) usando códigos Hex (`#RRGGBB` / `#AARRGGBB`), amostras rápidas ou 6 temas predefinidos (`Ciano Clean`, `Verde Esmeralda`, `Âmbar Dourado`, `Rubi Carmesim`, `Roxo Synthwave`, `Claro Minimalista`), com botão `"Restaurar Cores Padrão"`.
  - **Ordenação Dinâmica dos Blocos no Overlay**: Reorganize em tempo real a ordem de exibição de `FPS`, `CPU`, `GPU` e `RAM` através dos botões `"Subir (▲)"` e `"Descer (▼)"`.
- **Rodapé com Ícone de Engrenagem — "Configurações"**:
  - Reúne todas as configurações do aplicativo e do Overlay flutuante: Mostrar/Ocultar Overlay, Modo Compacto, Exibição Inteligente em Tela Cheia, Atalho Global (`Ctrl + Shift + O`), Modo Travado (Click-Through), Iniciar com o Windows como Administrador (`CleanPanelAutoStart`), Opacidade, Orientação Horizontal/Vertical, Alinhamento Rápido em 6 posições e seleção individual de sensores (incluindo o novo toggle de **GPU Uso de VRAM (GB / %)**).

---

### 2. Novo Layout do Overlay Agrupado por Hardware + Uso de VRAM
- **Modelos Reais como Cabeçalhos de Grupo (`GetShortHardwareName`)**:
  - O Overlay agrupa as métricas abaixo do nome real e limpo do componente do usuário (ex: `Intel Core i9-13900K`, `AMD Ryzen 5 5600`, `RTX 5060`, `RX 7600`), formatado e cacheado uma única vez na inicialização.
- **Monitoramento de VRAM no Overlay**:
  - Exibe o consumo de memória de vídeo em tempo real no formato `VRAM: 6.2 / 8.0 GB (78%)` (ou `VRAM: 1.4 GB` em GPUs integradas com memória compartilhada).
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
