# HardwareOverlayApp — Monitor de Hardware, Diagnóstico e FPS para Windows 11

**HardwareOverlayApp** é um software completo de monitoramento, diagnóstico de hardware e telemetria de jogos em tempo real desenvolvido em **C# 12**, **.NET 8** e **WPF (WPF-UI)**. Combina um **Painel Principal estilo Gerenciador de Tarefas do Windows 11 / HWiNFO moderno** com um **Overlay flutuante** ultraleve para jogos.

---

## Principais Funcionalidades

### 1. Interface Principal Estilo Task Manager (Windows 11 Fluent Design + Mica)
A janela principal conta com navegação lateral moderna dividida em **4 seções de telemetria no topo** e um **ícone de engrenagem no rodapé para Configurações**:

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
  - Reúne todas as configurações do aplicativo e do Overlay flutuante: Mostrar/Ocultar Overlay, Modo Compacto, Exibição Inteligente em Tela Cheia, Atalho Global (`Ctrl + Shift + O`), Modo Travado (Click-Through), Iniciar com o Windows como Administrador, Opacidade, Orientação Horizontal/Vertical, Alinhamento Rápido em 6 posições e seleção individual de sensores do Overlay.

---

### 2. Blindagem Total e Tolerância a Falhas (Fault Tolerance & Sensor Fallback)
Projetado para funcionar com estabilidade em qualquer combinação de hardware (CPUs Intel vs. AMD, desktops vs. notebooks, GPUs dedicadas vs. integradas, SSDs NVMe com sensor térmico vs. SSDs SATA sem sensor):

- **Validação Estrita de Sensores**:
  - Todo valor `float?` lido do `LibreHardwareMonitor` passa pelo validador `IsValidSensorValue`, descartando leituras nulas, `NaN`, `Infinity` ou valores inválidos (`<= 0`).
- **Busca por Múltiplos Aliases com Ordem de Prioridade**:
  - **CPU Temperatura**: `"Core (Tctl/Tdie)"` → `"CPU Package"` → `"Package"` → `"Core Average"` → `"Core Max"` → primeiro sensor térmico da CPU → fallback no chip SuperIO da Placa-Mãe.
  - **CPU VCore (Tensão)**: `"Core (SVI2 TFN)"` → `"Vcore"` → `"CPU Core"` → `"VID"` → fallback nos subHardwares (`SuperIO`) da Placa-Mãe.
  - **CPU Consumo (Watts)**: `"Package"` → `"CPU Package"` → `"Cores (SMU)"` → `"CPU Power"`.
  - **GPU Temperatura e Consumo**: `"GPU Core"`, `"GPU"`, `"Hot Spot"` para temperatura; `"GPU Package"`, `"GPU Power"`, `"GPU Core"`, `"Board Power"` para consumo.
  - **GPU VRAM Total e Usada**: Sensores `SmallData` (`"GPU Memory Total"`, `"D3D Dedicated Memory Total"`, `"GPU Memory Used"`, `"D3D Dedicated Memory Used"`) convertidos de MB para GB, com fallback para Registro do Windows (`HardwareInformation.qwMemorySize`), WMI (`Win32_VideoController`) ou `"Compartilhada / Integrada"`.
- **Exibição Graciosa na UI (Sem Zeros Falsos)**:
  - Quando um sensor não é suportado pelo hardware do usuário (ex: placa-mãe sem leitura de VCore, iGPU sem Fan RPM ou Watts, SSD sem sensor térmico), a interface exibe `"N/D"` ou `"Sem sensor"` em vez de exibir valores falsos como `0 W`, `0.00 V` ou `0 °C`.
- **Isolamento de Exceções (`try/catch` Independentes)**:
  - A leitura de cada subsistema (`CPU`, `GPU`, `RAM`, `Storage`, `Motherboard`, `WMI`, `Registry`) é encapsulada em blocos `try/catch` isolados.

---

### 3. Otimização Inteligente de Leitura (Zero Desperdício de CPU)
- **Cache Estático na Inicialização**: Especificações fixas (Nome da CPU, Núcleos/Threads via WMI, Versão/Build do Windows via Registro, Capacidade Total das Partições e Mapeamento Físico de Discos) são lidas **apenas uma vez** na abertura do aplicativo e mantidas em cache (`HardwareSpecs`).
- **Polling Dinâmico Sob Demanda**:
  - Sensores pesados (uso/clock individual por núcleo da CPU, tensões SuperIO da placa-mãe e SMART/temperatura dos SSDs/HDDs) **só são atualizados quando a janela principal está aberta e visível na tela**.
  - Quando a janela principal está fechada/minimizada na bandeja do sistema (*System Tray*), o loop atualiza exclusivamente os sensores leves necessários para o Overlay flutuante (ou suspende 100% do polling se o Overlay também estiver oculto).

---

### 4. Captura de FPS via ETW (Seguro contra Anti-Cheat)
- Medição oficial de quadros por segundo (FPS) escutando eventos do subsistema gráfico do Windows (`Microsoft-Windows-DXGI` e `Microsoft-Windows-D3D9`) via **Event Tracing for Windows (ETW)** sem injeção de DLLs (*No Hooking*).

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
├── HardwareOverlayApp.csproj
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
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

### Como gerar o instalador (.exe)

```powershell
powershell ./build-installer.ps1
```

---

## Licença

Distribuído sob a licença **MIT**. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
