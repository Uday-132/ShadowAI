# Shadow AI 🚀

Shadow AI is an AI-powered desktop overlay application built with C# WPF (.NET 8) accompanied by a web portal and serverless backend hosted on Vercel with Neon PostgreSQL. It offers real-time screen text extraction (OCR), voice interaction, seamless Groq API integration, custom desktop click-through overlay controls, and secure multi-tenant user authentication.

---

## 🌟 Key Features

### 🖥️ Desktop Application (WPF .NET 8)
- **Click-Through Desktop Overlay**: Frameless glassmorphic window designed to sit unobtrusively on your desktop.
- **Pin & Lock Controls**: Mouse-hook enabled pin button to lock/unlock click-through mode for uninterrupted work.
- **Screen OCR & Snip Tool**: Capture screen region text instantly and pass context to Groq LLMs.
- **Voice AI Assistant**: Real-time voice recording and automated transcription/response workflow powered by NAudio.
- **Groq API Key Synchronization**: Custom user key validation, persistent local settings storage, and automated cloud sync.
- **User Profile & Status**: Live session status tracking, trial duration countdowns, and profile customization.

### 🌐 Web Landing Page & Serverless API Backend
- **Landing Page**: Modern web interface for project demonstration and setup installer download.
- **Serverless API (Vercel Functions)**:
  - `/api/auth/signup` - User registration with password hashing (bcrypt) & trial generation.
  - `/api/auth/login` - Secure JWT token authentication.
  - `/api/session/status` - Synchronize session metadata, active status, system key fallback, and user custom Groq keys.
  - `/api/user/save-key` - Secure server-side validation & SHA-256 hashed storage of Groq API keys into PostgreSQL (`user_api_keys`).
  - `/api/pay/verify` - Payment processing & subscription extension routes.
- **Database**: Neon PostgreSQL server with indexed tables (`users`, `user_api_keys`, `app_config`).

---

## 🛠️ Technology Stack

| Domain | Technologies |
| :--- | :--- |
| **Desktop Application** | C#, .NET 8.0 WPF, NAudio, Win32 Low-Level Hooks, System.Text.Json |
| **Installer & Packaging** | Inno Setup 6 (`shadow_ai_setup.iss`) |
| **Backend & Cloud Services** | Node.js, Vercel Serverless Functions, Neon PostgreSQL (`pg`), JWT, bcryptjs |
| **Web Frontend** | HTML5, Vanilla CSS3, JavaScript (ES6+), FontAwesome |
| **Browser Extension / Scripts** | Tampermonkey Anti-Detection Script (`anti_detection_tampermonkey.js`) |

---

## 📁 Repository Structure

```
Overlays/
├── OverlayApp/                   # C# WPF Desktop Overlay Solution
│   ├── Models/                   # Data models (WidgetSettings, UserSession, etc.)
│   ├── Services/                 # Services (LlmService, HotkeyService, WindowStyleService, etc.)
│   ├── ViewModels/               # MVVM ViewModel (MainViewModel.cs)
│   ├── Views/                    # XAML UI Windows & Overlays (MainWindow.xaml)
│   └── OverlayApp.csproj         # .NET 8 WPF Project file
├── Website/                      # Web Application & Vercel Root Directory
│   ├── api/                      # Vercel Serverless Functions (auth, session, user, pay)
│   ├── index.html                # Landing Page UI
│   ├── style.css                 # Custom Styling
│   ├── app.js                    # Client-side JavaScript
│   └── setup.exe                 # Production Windows Installer Executable
├── api/                          # Root API directory backup
├── shadow_ai_setup.iss           # Inno Setup compilation script for setup.exe
├── anti_detection_tampermonkey.js# Custom browser automation script
├── vercel.json                   # Vercel deployment & routing configuration
└── README.md                     # Project documentation
```

---

## 🚀 Getting Started

### Prerequisites
- **For Desktop App**:
  - [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
  - Windows 10 / 11 OS (Required for Win32 WPF interop APIs)
  - [Inno Setup 6](https://jrsoftware.org/isinfo.php) (Optional, required only for building `setup.exe`)
- **For Backend / Web**:
  - [Node.js 18+](https://nodejs.org/)
  - [Vercel CLI](https://vercel.com/cli) (Optional, for local serverless development)
  - Neon PostgreSQL Database account

---

## 🔧 Building & Packaging

### 1. Compile & Publish the Desktop Application
Run `dotnet publish` in Release mode targeting `win-x64`:

```powershell
dotnet publish OverlayApp/OverlayApp.csproj -c Release -r win-x64 --self-contained false
```

The output binaries will be generated under:
`OverlayApp\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`

### 2. Compile the Windows Installer Setup (`setup.exe`)
Build the installer package using Inno Setup compiler (`ISCC.exe`):

```powershell
& "C:\Users\<YourUsername>\AppData\Local\Programs\Inno Setup 6\ISCC.exe" shadow_ai_setup.iss
```

This compiles `Website/setup.exe` containing all application binaries and dependency verification logic.

---

## ⚙️ Environment Variables (Backend)

The serverless API expects the following environment variables configured on Vercel:

| Variable | Description |
| :--- | :--- |
| `POSTGRES_URL` | Neon PostgreSQL Connection URI |
| `JWT_SECRET` | Secret key for signing and verifying user session tokens |
| `ADMIN_SECRET_KEY` | Admin authorization key for management APIs |

---

## 🔒 Security & Privacy

- **Groq API Keys**: Hashed using SHA-256 (`key_hash`) and associated with `user_id` in the cloud PostgreSQL database.
- **Privacy Protections**: Local key memory and input variables are automatically purged upon user logout to prevent key leakage on shared hardware.

---

## 📜 License

This project is proprietary and all rights are reserved by the author.