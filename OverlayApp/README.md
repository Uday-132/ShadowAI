# Productivity Overlay Application (.NET 8 WPF)

A premium Windows desktop overlay application designed for productivity. The application displays useful widgets—such as a persistent quick notes scratchpad, real-time CPU/RAM hardware monitor, and presentation stopwatch timer—while floating on top of other applications. It supports adjustable opacity, always-on-top behavior, and a mouse **click-through mode** with a global hotkey toggle.

---

## Technical Concepts & Architecture

### 1. How Windows Desktop Window Manager (DWM) Composites Overlays
In modern versions of Windows, the **Desktop Window Manager (DWM)** coordinates the rendering of all desktop elements. 
* Rather than windows drawing directly to the monitor's screen buffer, DWM allocates an **off-screen redirect surface** (a texture in GPU memory) for every open window.
* Each application renders its layout to its dedicated texture.
* DWM then composites these surfaces together on the GPU using pixel shaders, projecting the final composite onto the monitor.
* When WPF sets `AllowsTransparency="True"`, it tells the OS to create an alpha-channel backing store for the window. The rendering pipeline then writes transparent pixels (with alpha < 1.0) into the GPU texture. DWM blends these pixels with the textures of the desktop background and windows behind it, creating a high-performance, tear-free transparent blend.

### 2. Window Styles Explained
The overlay dynamically applies specific extended window styles (via `user32.dll` using `GetWindowLong`/`SetWindowLong` indexes):

* **`WS_EX_LAYERED` (0x00080000)**: Enables layering capabilities on the window. Layered windows are composited by the operating system, allowing smooth transparency (alpha channels) and non-rectangular window regions. WPF automatically applies this when `AllowsTransparency` is active.
* **`WS_EX_TRANSPARENT` (0x00000020)**: This style renders the window **mouse-click-through transparent**. The OS hit-test engine (which determines which window received a mouse click) skips this window entirely and dispatches the click to whatever window is behind it.
* **`WS_EX_TOPMOST` (0x00000008)**: Forces the window manager to keep this window placed higher in the Z-order list than all standard windows, preventing it from going behind other apps when it loses focus.

### 3. Click-Through Toggle & Global Hotkey (Ctrl + Shift + C)
When click-through is active (`WS_EX_TRANSPARENT` is applied), the window cannot capture mouse clicks. Therefore, the user cannot click the UI to deactivate click-through.
To solve this, the application registers a global hotkey:
* Calls `RegisterHotKey` (P/Invoke) to bind **`Ctrl + Shift + C`** globally.
* Hooks into the native window message pump (`HwndSource`) to watch for the `WM_HOTKEY` (0x0312) message.
* When pressed, it toggles `IsClickThrough` in the ViewModel, restoring mouse interaction.

### 4. Native Dragging and Resizing
Since the window is borderless (`WindowStyle="None"`), standard title bars and resizing edges are absent:
* **Dragging**: Triggered on the header bar mouse-down event using WPF's native `Window.DragMove()` helper. Dragging is bypassed if the window position is "Locked".
* **Resizing**: A custom drag-grip handle is placed in the bottom-right corner. When dragged, it fires `SendMessage` with `WM_SYSCOMMAND` and `SC_SIZE + 8` (bottom-right resizing parameter). This delegates resizing to the OS, which provides smooth, hardware-accelerated scaling and obeys minimum size limits.

---

## Project Structure

Our application is organized according to clean MVVM (Model-View-ViewModel) architecture principles:

```
OverlayApp/
├── OverlayApp.sln                       # Visual Studio Solution
├── OverlayApp/
│   ├── OverlayApp.csproj                # Target .NET 8 SDK Project file
│   ├── App.xaml                         # App resource dictionary
│   ├── App.xaml.cs                      # Startup code
│   ├── Helpers/
│   │   ├── Win32.cs                     # Win32 API declarations (P/Invokes & Constants)
│   │   └── WindowHelper.cs              # Helper to modify window extended styles
│   ├── Models/
│   │   ├── WidgetSettings.cs            # Persistent application settings
│   │   └── WidgetType.cs                # Enum representing active widgets
│   ├── Services/
│   │   ├── HotkeyService.cs             # Registers/handles Ctrl+Shift+C global hotkey
│   │   ├── SystemMonitorService.cs      # Queries CPU/RAM load using Windows APIs
│   │   ├── WindowStyleService.cs        # Bridge between ViewModel and native Window handle
│   │   └── LlmService.cs                # Calls Groq (Stage 1 Llama 4 Scout OCR & Stage 2 Text Processing)
│   ├── ViewModels/
│   │   ├── ViewModelBase.cs             # PropertyChanged notifier base class
│   │   ├── RelayCommand.cs              # MVVM ICommand implementation
│   │   └── MainViewModel.cs            # Main business logic, updates, and stopwatch timer
│   └── Views/
│       ├── MainWindow.xaml              # Premium Glassmorphic design and control bindings
│       └── MainWindow.xaml.cs           # Source initialization hooks and resize triggers
```

---

## Building and Running in Visual Studio 2022

Follow these instructions to compile and launch the overlay:

### Prerequisites
* **Visual Studio 2022** (Community, Professional, or Enterprise).
* **.NET Desktop Development** Workload checked inside the Visual Studio Installer.
* **.NET 8.0 SDK** (usually bundled with VS 2022 17.8+).

### Step-by-Step Instructions

1. **Open the Solution**:
   * Open Visual Studio 2022.
   * Select **Open a project or solution**.
   * Browse to the project workspace and select `OverlayApp.sln`.

2. **Select the Build Configuration**:
   * In the top toolbar, ensure the Solution Configuration dropdown is set to **Debug** (for debugging) or **Release** (for optimized speed).
   * Ensure the Target Platform is set to **Any CPU**.

3. **Restore Dependencies**:
   * Visual Studio will restore implicit packages on load. (The project has been optimized to use zero NuGet packages, so there are no package source downloading failures!).

4. **Build and Run**:
   * Press **F5** (or click the green play button **"OverlayApp"** in the toolbar) to compile and start debugging.
   * To build without debugging, press **Ctrl + F5**.

---

## Operating instructions

1. **Repositioning**: Left-click and drag the top header bar to move the overlay.
2. **Resizing**: Left-click and drag the bottom-right corner grip indicator (diagonal lines) to change the overlay dimensions.
3. **Switching Widgets**: Click on **NOTES**, **MONITOR**, or **TIMER** in the tab bar.
   * **Notes**: Click inside the textarea to write text.
   * **Monitor**: View live updates of your system's global CPU Load and RAM utilization.
   * **Timer**: Track timing. Click **START** to run, **PAUSE** to halt, and **RESET** to reset the stopwatch.
4. **Overlay Configurations**: Click the **Gear Icon** on the right side of the header to open settings:
   * **Opacity**: Slide to adjust overlay transparency from 10% to 100%.
   * **Always on Top**: Keeps the window floating above all other open windows.
   * **Lock Position**: Disables dragging and corner resizing to prevent accidental movements.
   * **Theme**: Choose from Onyx (Cyan), Neon (Pink), Emerald (Mint), or Sunset (Orange) themes.
5. **Mouse Click-Through**: 
   * Click the **Pin Icon** in the header bar. The overlay becomes mouse-transparent. Click actions will pass through the overlay onto files, desktop icons, or apps behind it.
   * To regain control, press the global hotkey shortcut: **`Ctrl + Shift + C`**.
6. **AI Screen Scan (Google Scan-like feature)**:
   * Click **AI SCAN** in the tab bar.
   * Input your **Groq API Key** in the text box.
   * Click **SCAN SCREEN**. The screen will darken slightly, indicating selection mode.
   * Click and drag your cursor to select any area of your screen (e.g. text, equations, code, charts).
   * Release the mouse button. The app will crop the desktop region, display a thumbnail preview, and initiate the pipeline:
     * **Stage 1 (Groq - Llama 4 Scout)**: Invokes the `"meta-llama/llama-4-scout-17b-16e-instruct"` vision model to extract and transcribe the screen contents.
     * **Stage 2 (Groq)**: The transcribed text is sent to the `"llama-3.1-8b-instant"` model to explain, solve, or format the extracted text.
   * The final response will render inside the scrollable output box.
   * **Privacy & Stealth**: Since `SetWindowDisplayAffinity` is applied to both the overlay window and the selection window, **neither the overlay nor the selection mask will show up in screen shares, recordings, or screenshots**.