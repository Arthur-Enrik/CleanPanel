# CleanPanel — Monitor de Hardware, Diagnóstico e Overlay de Desempenho para Windows 11

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-09090B?style=flat-square&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-09090B?style=flat-square&logo=dotnet)
![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20WPF--UI-09090B?style=flat-square)
![Version](https://img.shields.io/badge/Version-v1.1.0-18181B?style=flat-square)
![License](https://img.shields.io/badge/License-MIT-27272A?style=flat-square)

**CleanPanel** é um monitor de hardware, diagnóstico de sistema e overlay de desempenho em tempo real para **Windows 10/11**, desenvolvido em **C# 12**, **.NET 8** e **WPF**. Projetado com estética estritamente **monocromática em Preto e Branco** inspirada em interfaces Web modernas (Vercel / Linear / Shadcn UI), combina um painel completo estilo **Gerenciador de Tarefas** com um **Overlay flutuante** de baixíssimo impacto e captura oficial de FPS via **ETW**, sem injeção de DLLs em jogos.

---

## 📸 Capturas de Tela

| Painel Principal (Visão Geral) | Overlay Flutuante Monocromático |
| :---: | :---: |
| *Espaço reservado para captura do Dashboard (Escuro / Claro)* | *Espaço reservado para captura do Overlay Vertical / Horizontal / Compacto* |

| Processador (P-Cores & E-Cores) | Configurações & Atalhos Globais |
| :---: | :---: |
| *Espaço reservado para captura da Telemetria por Núcleo* | *Espaço reservado para captura da Sub-aba Geral & Sensores* |

---

## ✨ Principais Funcionalidades

### 1. Dashboard Estilo Gerenciador de Tarefas (Design Monocromático Web)
Interface responsiva (`MinWidth="800"`, `MinHeight="560"`) construída sobre o dicionário centralizado [`Resources/MonochromeTheme.xaml`](Resources/MonochromeTheme.xaml), com barra lateral fixa ultra-minimalista (`200px`), micro-rótulos em caixa alta, valores tabulares em `Consolas`, bordas de `1px` e barras de progresso finas (`4px`):

- **Visão Geral:**
  - Grade de 4 cards KPI em tempo real (**Processador**, **Placa de Vídeo**, **Memória RAM** e **Taxa de Quadros**).
  - Resumo completo do **Sistema Operacional** (detecção corrigida do Windows 11 via `CurrentBuild >= 22000`, edição/versão e build), especificações da CPU, lista Multi-GPU, ocupação de RAM e partições de armazenamento.
- **Processador (CPU) & Suporte a Arquitetura Híbrida Intel (P-Cores / E-Cores):**
  - Leituras globais de **Uso Total (%)**, **Temperatura Package/Tdie (°C)**, **Consumo Elétrico (W)**, **Tensão VCore/VID (V)** e **Clock Médio / Máximo (MHz)**.
  - Telemetria individual por núcleo/thread (*Per-Core Telemetry*) em grade de 2 colunas.
  - **Intel Híbrida (12ª Geração Alder Lake e superior):** Detecção automática da topologia via API Win32 `GetLogicalProcessorInformationEx` (`RelationProcessorCore` / `EfficiencyClass`), identificando núcleos de Performance (`P-Core`) e Eficiência (`E-Core`) e permitindo opcionalmente separar **Uso (%)** e **Clock (MHz)** entre P-Cores e E-Cores no Overlay.
- **Placa de Vídeo (Multi-GPU):**
  - Seletor instantâneo entre múltiplas GPUs instaladas (integrada e dedicada — **NVIDIA**, **AMD** e **Intel**).
  - Telemetria de **Uso do Core (%)**, **VRAM (Usada / Total ou Compartilhada)**, **Temperatura Core e Hot Spot (°C)**, **Clock do Core e Memória (MHz)**, **Consumo (W)** e **Ventoinhas (RPM / %)**.
- **Sistema & Discos:**
  - Detalhamento físico de Memória RAM (Instalada, Em Uso e Disponível em GB e %).
  - Leitura híbrida de armazenamento combinando capacidade e espaço livre de partições lógicas (`System.IO.DriveInfo`) com sensores térmicos físicos SMART de SSDs/HDDs (`LibreHardwareMonitor`).

### 2. Motor de FPS via ETW (Sem Injeção de DLL / Anti-Cheat Safe)
O serviço `FpsMonitorService` monitora a apresentação de quadros diretamente pelo kernel do Windows via **Event Tracing for Windows (ETW)** — **100% seguro contra sistemas Anti-Cheat**, sem injeção de código ou hooks gráficos nos executáveis dos jogos:

- **Provedores Diretos & Baixa Latência:** Escuta eventos `Present_Start` de **DXGI** (`EventID 42`) e **D3D9** (`EventID 1`) em uma `TraceEventSession` configurada com buffer reduzido de `4 MB`, limpeza preventiva de sessões residuais (`GetActiveSession`) e thread dedicada (`AboveNormal`) para entrega contínua sem rajadas.
- **Filtro Hot-Path & SwapChain Dominante:** Comparação direta `O(1)` pelo `ProcessID` da janela em primeiro plano (atualizado fora do callback ETW apenas 1x por segundo) e agrupamento por `SwapChainAddress`, isolando a SwapChain principal do motor 3D contra overlays internos, launchers e UIs em segundo plano.
- **Janela Deslizante de Frametime & EMA Adaptativa:** Calcula a média real do tempo entre quadros em janela móvel de `600 ms` (descartando micro-deltas `< 0.5 ms` / > 2000 FPS e aplicando timeout de ociosidade após `1000 ms`), com suavização **EMA** adaptativa para estabilizar leituras cravadas (60/144 FPS) sem perder resposta imediata em transições bruscas (`> 20%`).
- **Fallback Automático via DWM:** Habilita `Microsoft-Windows-Dwm-Core` (`EventID 15` / `46`) como fallback automático quando o aplicativo ativo em foco utiliza **Vulkan**, **OpenGL** ou modos **Borderless** que não emitem eventos DXGI/D3D9 diretos sob o PID do jogo (priorizando sempre os eventos diretos caso passem a chegar).

### 3. Overlay Flutuante Personalizável
Configurado em 3 sub-abas organizadas na página de **Configurações** (`Geral & Sistema`, `Overlay & Layout`, `Sensores & Ordem`):

- **Identidade Monocromática & Translucidez Isolada:** Fundo preto fosco (`#09090B`) com canal Alpha controlado dinamicamente pela opacidade (`10%` a `100%`), borda nítida de `1px` em `#27272A`, títulos/valores tabulares em `#FAFAFA` (`Consolas`) e rótulos em `#A1A1AA`. Blindado contra estilos implícitos e efeitos DWM Mica/Acrylic (`DWMWA_SYSTEMBACKDROP_TYPE`) para manter a transparência por pixel intacta mesmo no tema Automático.
- **Escala Vetorial & Layout Agrupado:** Ajuste proporcional global de tamanho (`70%` a `200%`), cabeçalhos com o nome real simplificado do hardware (`GetShortHardwareName`), bloco de **FPS** em destaque na última posição e ordem padronizada de métricas (**CPU:** Uso → Clock → Temperatura → Consumo; **GPU:** Uso → Clock → Temperatura → Consumo → VRAM), além de reordenação personalizada de blocos (`▲` / `▼`) e toggles individuais por sensor.
- **Modo Compacto Inteligente:** Exibe exclusivamente `CPU %`, `GPU %` e `FPS` (ou `CPU P: XX% | E: XX%` quando P/E-Cores estão separados), ativando automaticamente a orientação horizontal ao ser ligado e retornando à orientação vertical ao ser desligado.
- **Exibição Inteligente (Smart Fullscreen):** Detecção estrita via Win32/DWM que oculta o Overlay na área de trabalho ou sobre janelas comuns maximizadas (navegadores, editores, Explorer), exibindo-o apenas sobre jogos ou aplicativos em **Tela Cheia real** (Exclusive ou Borderless Fullscreen).
- **Click-Through & Alinhamento Rápido:** Modo travado (`WS_EX_TRANSPARENT` / `WS_EX_LAYERED`) que deixa cliques do mouse atravessarem o Overlay sem tirar o foco do jogo, 6 predefinições rápidas de alinhamento na tela e arrasto livre quando destravado.

### 4. Sistema de Temas & Atalhos Globais Personalizáveis
- **Temas Dinâmicos (`Dark`, `Light`, `Auto`):** Alternância imediata entre **Escuro**, **Claro** e **Automático** (acompanhando a chave `AppsUseLightTheme` do Registro do Windows 11), mantendo o Overlay sempre translúcido.
- **Atalhos Globais Personalizáveis em Tempo Real:** Gravador interativo de atalhos via Win32 `RegisterHotKey` com flag `MOD_NOREPEAT` (evita disparos repetidos ao segurar teclas) e detecção de conflitos com o sistema:
  - **Exibir / Ocultar Overlay:** Padrão `Ctrl + Shift + O` (ID `9001`).
  - **Alternar Modo Compacto:** Padrão `Ctrl + Shift + C` (ID `9002`).
- **System Tray & Inicialização com o Windows:** Minimiza silenciosamente para a bandeja do sistema ao fechar no `'X'` e oferece inicialização automática em segundo plano (`--minimized`) como Administrador via Agendador de Tarefas (`CleanPanelAutoStart`).

---

## 🏗️ Arquitetura, Otimizações e Blindagem de Sensores

### Stack Tecnológica

| Camada | Tecnologia / Biblioteca | Versão |
| :--- | :--- | :--- |
| Linguagem & Runtime | **C# 12** / **.NET 8** (`net8.0-windows`) | 8.0 |
| Interface Desktop | **WPF** + **WPF-UI** (`FluentWindow`) | 3.0.5 |
| Arquitetura MVVM | **CommunityToolkit.Mvvm** (`ObservableObject` / `RelayCommand`) | 8.2.2 |
| Sensores de Hardware | **LibreHardwareMonitorLib** + **System.Management (WMI)** | 0.9.3 / 8.0.0 |
| Telemetria de FPS | **Microsoft.Diagnostics.Tracing.TraceEvent** (ETW Kernel Session) | 3.1.8 |
| Empacotamento | **Inno Setup 6** + GitHub Actions CI/CD | — |

### Otimizações de Performance (Zero Desperdício de CPU)
- **Sensor Caching (Zero LINQ no Loop de 1s):** Todos os objetos `ISensor` necessários para CPU, P/E-Cores, Multi-GPU e discos físicos são resolvidos e cacheados uma única vez na inicialização do `HardwareMonitorService`. O `PeriodicTimer` de 1 segundo apenas atualiza os nós de hardware ativos e lê os ponteiros em memória.
- **Dynamic Polling:** Quando a janela principal está minimizada ou oculta na bandeja do sistema, sensores pesados exclusivos do dashboard (como leitura detalhada por núcleo, Multi-GPU secundárias e sensores SMART) são suspensos automaticamente, mantendo apenas os sensores necessários ao Overlay.
- **Brushes Congelados & Debounce em Disco:** Os pincéis monocromáticos do Overlay são congelados (`SolidColorBrush.Freeze()`) fora do loop de renderização, e a persistência de `settings.json` (sempre relativa a `AppDomain.CurrentDomain.BaseDirectory` para compatibilidade no boot via `schtasks`) utiliza debounce assíncrono de `300 ms` com `FlushImmediate()` no encerramento.

### Blindagem Contra Sensores Indisponíveis (Fault Tolerance)
- Validação rigorosa descartando leituras `null`, `NaN`, `Infinity` ou valores espúrios (`IsValidSensorValue`).
- Fallback em cascata por aliases entre fabricantes (ex.: `Core (Tctl/Tdie)` → `CPU Package` → `Package` → `Core Max`).
- Isolamento independente por `try/catch` entre subsistemas (`CPU`, `GPU`, `RAM`, `Storage`, `WMI`, `Registry`) e exibição graciosa de `"N/D"` ou `"Sem sensor"` no lugar de zeros falsos.

### Estrutura do Repositório

```text
HardwareOverlayApp/
├── .github/
│   └── workflows/
│       └── release.yml                # Build e publicação automática de releases via tags
├── Assets/
│   └── app.ico                        # Ícone multi-resolução gerado com cabeçalho ICO real
├── Helpers/
│   ├── IconGeneratorHelper.cs         # Gerador vetorial/binário de ícones e tray icon
│   ├── UpdateVisitor.cs               # Visitor seletivo para árvore do LibreHardwareMonitor
│   └── WindowWin32Helper.cs           # Interop Win32/DWM (Hotkeys, Fullscreen, Click-Through, P/E-Core)
├── Models/
│   ├── AppSettings.cs                 # Schema de configurações persistidas em settings.json
│   ├── HardwareMetrics.cs             # Snapshot em tempo real das métricas e coleções
│   └── HardwareSpecs.cs               # Especificações estáticas (SO, CPU, P/E-Cores, GPUs)
├── Resources/
│   └── MonochromeTheme.xaml           # Design system monocromático centralizado (Dark & Light)
├── Services/
│   ├── FpsMonitorService.cs           # Motor ETW com janela de frametime, SwapChain dominante e DWM fallback
│   ├── HardwareMonitorService.cs      # Coleta cacheada de sensores e polling seletivo
│   ├── SettingsService.cs             # Persistência JSON com debounce assíncrono e FlushImmediate
│   ├── StartupService.cs              # Registro no Agendador de Tarefas (CleanPanelAutoStart)
│   └── TrayIconService.cs             # Ícone e menu de contexto na bandeja do sistema
├── ViewModels/
│   └── MainViewModel.cs               # Estado reativo, temas, atalhos globais e comandos MVVM
├── Views/
│   ├── OverlayWindow.xaml             # Layout monocromático do Overlay (Vertical / Horizontal / Compacto)
│   ├── OverlayWindow.xaml.cs          # Hook Win32 de hotkeys, posicionamento e blindagem Alpha/DWM
│   ├── SettingsWindow.xaml            # Dashboard principal + 3 sub-abas de Configurações
│   └── SettingsWindow.xaml.cs         # Gravador de atalhos (PreviewKeyDown) e controle da bandeja
├── installer/
│   ├── build_installer.ps1            # Automação de publish .NET 8 + compilação Inno Setup
│   └── setup.iss                      # Script do instalador com versionamento dinâmico
├── App.xaml
├── App.xaml.cs
├── app.manifest                       # Elevação Administrator exigida para sensores e sessão ETW
├── CleanPanel.csproj
├── LICENSE
└── README.md
```

---

## 🚀 Instalação e Como Compilar

### Para Usuários Finais (Instalador Pronto)
1. Acesse a aba [**Releases**](https://github.com/Arthur-Enrik/CleanPanel/releases) do repositório.
2. Baixe o instalador mais recente (ex.: `CleanPanel_Setup_v1.1.0.exe`).
3. Execute o instalador e inicie o `CleanPanel.exe` (a elevação como Administrador é solicitada automaticamente via manifesto UAC para leitura direta de sensores de hardware e captura ETW de FPS).

### Para Desenvolvedores (Build Local & Gerar Instalador)

#### Pré-requisitos
- **Windows 10 ou Windows 11 (x64)**
- **.NET 8.0 SDK**
- **Inno Setup 6** (opcional, apenas para compilar o instalador; instalável via `winget install JRSoftware.InnoSetup`)
- **Terminal PowerShell como Administrador** (necessário apenas ao executar o `CleanPanel` via `dotnet run`)

#### Compilar e Executar
```powershell
git clone https://github.com/Arthur-Enrik/CleanPanel.git
cd "CleanPanel"

dotnet clean
dotnet build -c Release
dotnet run -c Release
```

#### Gerar o Executável Publicado e o Instalador (`CleanPanel_Setup_v1.1.0.exe`)
```powershell
powershell -ExecutionPolicy Bypass -File .\installer\build_installer.ps1
```
O instalador compilado será gerado em `installer/Output/CleanPanel_Setup_v<versão>.exe`.

---

## 🗺️ Roadmap Futuro

- **Arquitetura de Plugins para a Comunidade (`CleanPanel Plugin SDK`):**
  - Carregamento dinâmico e isolado de módulos de extensão via **`AssemblyLoadContext`** a partir de arquivos `.dll` colocados na pasta `Plugins/`.
  - Permitirá à comunidade criar novos widgets para o Overlay, provedores de sensores adicionais, perfis customizados e integrações diretas com ecossistemas externos (como controle de iluminação RGB por temperatura/uso via **OpenRGB SDK**, **HWiNFO Shared Memory**, **MSI Afterburner / RTSS** e **Elgato Stream Deck**).

---

## 📄 Licença

Distribuído sob a licença **MIT**. Consulte o arquivo [`LICENSE`](LICENSE) para mais detalhes.
