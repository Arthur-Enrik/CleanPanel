# HardwareOverlayApp — Monitor de Hardware e FPS para Windows 11

**HardwareOverlayApp** é um aplicativo Desktop Open-Source desenvolvido em **C# 12**, **.NET 8** e **WPF (WPF-UI)** para monitoramento em tempo real de **FPS**, **CPU**, **GPU (NVIDIA, AMD e Intel)** e **Memória RAM** através de um **Overlay flutuante** compacto e uma interface de configurações nativa no estilo **Windows 11 Fluent Design (Mica + Dark Mode)**.

---

## Principais Funcionalidades

- **Captura de FPS via ETW (Seguro contra Anti-Cheat)**:
  - Medição real de quadros por segundo (FPS) escutando eventos oficiais do subsistema gráfico do Windows (`Microsoft-Windows-DXGI` e `Microsoft-Windows-D3D9`) via **Event Tracing for Windows (ETW)**.
  - **Zero injeção de DLLs (No Hooking)**: não modifica processos de jogos e não interfere com sistemas Anti-Cheat (Vanguard, Easy Anti-Cheat, BattlEye, Ricochet).
- **Monitoramento Completo de Hardware em Tempo Real (1s)**:
  - **CPU**: Temperatura (°C — Package, Tctl/Tdie, Core Average), Uso Total (%), Clock Médio dos Núcleos (MHz) e Consumo Elétrico (Watts).
  - **GPU (NVIDIA / AMD / Intel)**: Temperatura do Núcleo (°C), Uso do Núcleo (%), Clock (MHz) e Consumo Elétrico (Watts).
  - **Memória RAM**: Quantidade física utilizada em `GB` e porcentagem de ocupação (`%`).
- **Overlay Flutuante Inteligente**:
  - **Modo Compacto**: exibe exclusivamente as três métricas essenciais (**FPS**, **CPU %** e **GPU %**) com padding reduzido para máxima discrição.
  - **Exibição Inteligente (Apenas em Tela Cheia)**: detecta automaticamente quando um jogo/aplicativo está em Tela Cheia (`GetWindowRect` + `MonitorFromWindow` / `GetMonitorInfo`), ocultando o overlay e suspendendo o polling pesado de sensores quando você está na área de trabalho.
  - **Orientação Vertical ou Horizontal**: alterne dinamicamente entre o layout em coluna ou barra horizontal em linha com separadores discretos.
  - **Alinhamento Rápido em 6 Posições**: botões de 1 clique para posicionar o overlay nos cantos ou centro superior/inferior da tela (`SystemParameters.WorkArea`).
  - **Modo Destravado**: arraste o overlay livremente com o mouse para qualquer posição da tela; as coordenadas `Left` e `Top` são salvas automaticamente ao soltar o clique.
  - **Modo Travado (Click-Through)**: utiliza `WS_EX_TRANSPARENT` da API Win32 (`user32.dll`) para que todos os cliques e movimentos do mouse atravessem o overlay diretamente para o jogo em execução.
  - **Oculto do Alt+Tab**: utiliza `WS_EX_TOOLWINDOW` e `SetWindowPos(..., HWND_TOPMOST, ...)` para permanecer acima de jogos em modo **Janela sem Bordas (Borderless Windowed)** e **DirectX 12 Flip Model**.
- **Atalho Global de Teclado (`Ctrl + Shift + O`)**:
  - Registrado via `RegisterHotKey` no Win32, permitindo mostrar ou ocultar o overlay instantaneamente de dentro de qualquer jogo.
- **Bandeja do Sistema (System Tray)**:
  - Ícone desenhado programaticamente em alta definição com menu de contexto rápido (Abrir Configurações, Mostrar/Ocultar Overlay, Travar/Destravar Posição e Sair Completamente).
  - Fechar a janela de configurações no botão `"X"` minimiza o aplicativo para a bandeja do sistema, mantendo o overlay ativo.
- **Inicialização Automática com o Windows como Administrador**:
  - Configura automaticamente uma tarefa no **Agendador de Tarefas do Windows (`schtasks.exe`)** com privilégios elevados (`/RL HIGHEST`) e argumento `--minimized`, iniciando silenciosamente na bandeja ao fazer logon.
- **Persistência Instantânea (`settings.json`)**:
  - Todas as preferências, posição do overlay e visibilidade individual de cada métrica são salvas instantaneamente em JSON nativo (`System.Text.Json`).

---

## Arquitetura e Explicação Técnica

### 1. Leitura de Sensores (`HardwareMonitorService`)
Utiliza a biblioteca `LibreHardwareMonitorLib` executando um loop assíncrono com `PeriodicTimer` de **1000 ms** em background. O visitante `UpdateVisitor` percorre os sensores físicos de CPU, GPU e RAM sem bloquear a Thread de UI do WPF.

### 2. Captura de FPS via ETW (`FpsMonitorService`)
Utiliza `Microsoft.Diagnostics.Tracing.TraceEvent` para abrir uma sessão de eventos do kernel/sistema (`HardwareOverlayFpsSession`) em uma thread dedicada:
- **Provedor DXGI (`CA11C036-0102-4A2D-A6AD-F03CFED5D3C9`)**: captura o evento `Present_Start` (`EventID 42`), utilizado por jogos DirectX 10, 11, 12 e Vulkan/OpenGL em DXGI SwapChain.
- **Provedor D3D9 (`783ACA0A-790E-4D7F-8451-AA850511C6B9`)**: captura o evento `Present_Start` (`EventID 1`), utilizado por jogos DirectX 9.
- O serviço identifica dinamicamente o `ProcessID` (PID) da janela em foco através de `GetForegroundWindow()` + `GetWindowThreadProcessId()`, contabilizando os timestamps (`Stopwatch.GetTimestamp()`) dos quadros entregues nos últimos 1000 ms.

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
│   └── HardwareMetrics.cs
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
- **Execução como Administrador** (exigido pelo `app.manifest` para acesso aos sensores de hardware de baixo nível e sessão ETW de FPS)

### Compilação e Execução via Terminal (PowerShell como Administrador)

```powershell
cd HardwareOverlayApp
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

### Como gerar o instalador (.exe)

O projeto conta com empacotamento automatizado via **Inno Setup 6** (publicação *Self-Contained* `win-x64`, sem exigir .NET instalado na máquina do usuário final).

Para compilar a aplicação e gerar o instalador `Output\HardwareOverlayApp_Setup.exe` com um único comando, basta rodar no terminal:

```powershell
powershell ./build-installer.ps1
```

> Caso o Inno Setup ainda não esteja instalado na máquina de build, instale-o rapidamente com:
> ```powershell
> winget install JRSoftware.InnoSetup
> ```

---

## Licença

Distribuído sob a licença **MIT**. Consulte o arquivo [LICENSE](LICENSE) para mais detalhes.
