# CleanPanel — Monitor de Hardware, Diagnóstico e FPS para Windows 11

**CleanPanel** é um software completo de monitoramento, diagnóstico de hardware e telemetria de jogos em tempo real desenvolvido em **C# 12**, **.NET 8** e **WPF (WPF-UI)**. Combina um **Painel Principal estilo Gerenciador de Tarefas do Windows 11 / HWiNFO moderno** com um **Overlay flutuante** ultraleve e de baixíssimo impacto para jogos.

---

## Principais Funcionalidades

### 1. Interface Principal Estilo Task Manager (Windows 11 Fluent Design + Mica)
A janela principal do **CleanPanel** conta com navegação lateral moderna dividida em **4 seções de telemetria no topo** e um **ícone de engrenagem no rodapé para Configurações**:

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
- **Rodapé com Ícone de Engrenagem — "Configurações"**:
  - Reúne todas as configurações do aplicativo e do Overlay flutuante: Mostrar/Ocultar Overlay, Modo Compacto, Exibição Inteligente em Tela Cheia, Atalho Global (`Ctrl + Shift + O`), Modo Travado (Click-Through), Iniciar com o Windows como Administrador (`CleanPanelAutoStart`), Opacidade, Orientação Horizontal/Vertical, Alinhamento Rápido em 6 posições e seleção individual de sensores do Overlay.

---

### 2. Engenharia de Performance e Baixo Consumo de CPU/Memória (Deep Optimization)
O **CleanPanel** foi arquitetado para ter impacto virtualmente zero durante sessões de jogos competitivos:

- **Cache de Referências Diretas de Sensores (Zero LINQ no Loop de 1s)**:
  - Na inicialização do `HardwareMonitorService`, todas as referências diretas dos objetos `ISensor` (`_cpuTempSensor`, `_cpuLoadSensor`, `_gpuTempSensor`, `_cpuCoreClockSensors`, pares de núcleos `_cachedCpuCores`, GPUs `_cachedGpus` e discos `_cachedStorages`) são mapeadas uma única vez e armazenadas em memória.
  - No loop de 1 segundo do `PeriodicTimer`, o serviço executa apenas `hardware.Update()` nos componentes necessários e lê diretamente a propriedade `.Value` dos ponteiros `ISensor` cacheados — eliminando buscas de strings, Regex e alocações de LINQ (`.FirstOrDefault`, `.Where`, `.Contains`) que gerariam pressão no *Garbage Collector* (GC).
- **Otimização Extrema no Capturador de FPS (`CleanPanelFpsSession`)**:
  - Eventos ETW (`Present_Start`) disparam centenas de vezes por segundo. Para evitar chamadas P/Invoke (`GetForegroundProcessId()`) a cada quadro renderizado, o PID da janela ativa é atualizado apenas **1 vez por segundo** no loop principal e armazenado em uma variável `volatile int _activeForegroundPid`.
  - A primeira instrução do callback `OnEtwEvent` é uma comparação inteira direta em `O(1)` (`if (data.ProcessID != _activeForegroundPid) return;`), com zero alocações de memória no caminho crítico.
- **Debounce Assíncrono na Persistência (`SettingsService`)**:
  - Alterações contínuas (como arrastar o Slider de Opacidade ou reposicionar o Overlay) passam por um *Debounce* assíncrono de **300 ms** com `CancellationTokenSource`, consolidando dezenas de eventos de UI em uma única gravação em disco (`settings.json`), além de `FlushImmediate()` garantido no encerramento.
- **Redução de Redesenho na UI Thread (`DispatcherPriority.Background`)**:
  - Todas as métricas numéricas são pré-arredondadas (`Math.Round`) em background antes de serem atribuídas às propriedades observáveis. Quando o valor arredondado permanece igual ao segundo anterior, o `CommunityToolkit.Mvvm` ignora notificações redundantes de `PropertyChanged`, poupando ciclos de layout e renderização do WPF.
  - As atualizações da interface utilizam `Dispatcher.InvokeAsync(..., DispatcherPriority.Background)` para nunca interferir na fluidez do mouse ou animações da janela.
- **Polling Seletivo Sob Demanda**:
  - Quando a janela principal está fechada ou minimizada na bandeja do sistema (*System Tray*), os sensores pesados (núcleos individuais da CPU, tensões SuperIO da placa-mãe e SMART/temperatura dos SSDs/HDDs) ficam suspensos, atualizando apenas os sensores leves exigidos pelo Overlay flutuante.

---

### 3. Blindagem Total e Tolerância a Falhas (Fault Tolerance & Sensor Fallback)
- **Validação Estrita de Sensores**: Todo valor `float?` passa pelo validador `IsValidSensorValue`, descartando leituras nulas, `NaN`, `Infinity` ou `<= 0`.
- **Exibição Graciosa na UI (Sem Zeros Falsos)**: Sensores não suportados pelo hardware (ex: placa-mãe sem VCore, iGPU sem Fan RPM/Watts, SSD SATA sem sensor térmico) exibem `"N/D"` ou `"Sem sensor"` em vez de zeros falsos (`0 W`, `0.00 V` ou `0 °C`).
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
