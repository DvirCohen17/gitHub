# gitHub

A multi-user client/server code editor. Create projects, invite friends, and edit the same files together in real time — with project chat, a to-do/issue list per project, friend requests and in-app messages.

Website: https://dvircohen17.github.io/gitHub/

## Architecture

```
┌──────────────────────┐      TCP :12345       ┌───────────────────────────┐
│ client/  (WPF, C#)   │ ◄───────────────────► │ server/  (C++, Winsock)   │
│ AvalonEdit editor    │   length-prefixed     │ one thread per client     │
│ one window per page  │   text protocol       │ SQLite (syncDBTemp.sqlite)│
└──────────────────────┘                       └───────────────────────────┘
          ▲
          │ installs / updates
┌──────────────────────┐
│ installer/ (WPF, C#) │
└──────────────────────┘
```

* **server/** — `Communicator` accepts connections and runs one thread per client. Each message starts with a 3-digit code (see `MessageCodes` in `server/Helper.h` / `client/Communicator.cs`) followed by 5-digit-length-prefixed fields. Live edits (insert / delete / replace) are index-adjusted against concurrent edits (`adjustIndexForSync`) and broadcast to everyone in the same file. Files are flushed to the database every 60 s.
* **client/** — WPF app (.NET 6). Login → Home → Project → editor, plus To-Do, Messages and Settings windows.
* **installer/** — WPF app (.NET 8) that installs the client under `C:\githubDemo\`.

## Building

Requirements: Windows, Visual Studio 2022 with the **Desktop development with C++** and **.NET desktop development** workloads.

| Project | How to build |
|---|---|
| Server | Open `server/server.vcxproj` → build **x64**. Or: `msbuild server\server.vcxproj /p:Configuration=Release /p:Platform=x64` |
| Client | `dotnet build client\client_side.csproj` |
| Installer | Build in Visual Studio (it uses a COM reference, so `dotnet build` can't build it; VS's `msbuild -restore` can) |

## Running

1. **Server** — run the server executable. It listens on port `12345` and creates `syncDBTemp.sqlite` (with all tables) in its working directory on first start. Type `EXIT` to stop it.
   Optional files next to the server: `gitHubDemo.zip` (served to the installer) and a `ClientVersion` row in the database (clients with a different version are told to update).
2. **Client** — create `config.txt` next to the client executable containing the server address:
   ```
   127.0.0.1,12345
   ```
   The client stores its settings in `C:\githubDemo\settings\appsettings.json` and reads syntax-highlighting files from `C:\githubDemo\data\codeStyles\`.

## Security notes

* Every SQL query escapes user input (`sqlStr()` in `server/SqliteDataBase.cpp`). Any new query that includes a string **must** use it.
* Passwords: the client sends SHA-256(password); the server stores that value as salted **PBKDF2-HMAC-SHA256** (`server/PasswordHasher.cpp`). Accounts created before this change are upgraded automatically on their next successful login.
* "Remember me" is encrypted with Windows DPAPI (current user only).
* Traffic is **not encrypted** (plain TCP). Only run the server on a trusted network until TLS is added.
* Never commit the `.sqlite` database — it contains user accounts (it is in `.gitignore`).
