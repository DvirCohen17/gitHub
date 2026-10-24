# gitHub

A multi-user client/server code editor. Create projects, invite friends, and edit the same files together in real time — with project chat, a to-do/issue list per project, friend requests and in-app messages.

Website: https://dvircohen17.github.io/gitHub/

## Architecture

```
┌──────────────────────┐      TCP :12345       ┌───────────────────────────┐
│ client/  (WPF, C#)   │ ◄───────────────────► │ server/  (C++, Winsock)   │
│ AvalonEdit editor    │   length-prefixed     │ one thread per client     │
│ shared window frame  │   text protocol       │ SQLite (syncDBTemp.sqlite)│
└──────────────────────┘                       └───────────────────────────┘
          ▲
          │ installs / updates
┌──────────────────────┐
│ installer/ (WPF, C#) │
└──────────────────────┘
```

* **server/** — `Communicator` accepts connections and runs one thread per client (shared state is guarded by one lock). Each message starts with a 3-digit code (see `MessageCodes` in `server/Helper.h` / `client/Communicator.cs`) followed by length-prefixed fields. Request handlers are split by feature (`CommunicatorAuth/Files/Projects/Social/Issues/Navigation/LiveEdit.cpp`). Files are flushed to the database every 60 s.
* **client/** — WPF app (.NET 6). Screens: sign-in → Home → Project (files, live editor, members, chat) → To-do list, plus Messages and Settings. All screens share one window frame (`MainFrame` in `App.xaml.cs`), so navigating looks like one window changing pages; only *New / Edit project* opens as a dialog. Look and theming live in `App.xaml` (styles read `Theme.*` brushes that `Communicator.ModifyTheme` updates).
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
2. **Installer** — put the client package as `gitHubDemo.zip` next to the server executable, then run the installer, enter the server address and click **Install**. It downloads the package, installs it to `C:\githubDemo\data` (the previous install is only replaced once the new one is complete), creates a desktop shortcut and starts the client. When the server reports a newer version, the client runs the installer with `--update` automatically.
3. **Client** — reads the server address (`IP,PORT`, e.g. `127.0.0.1,12345`) from `config.txt` next to the executable, or from `C:\githubDemo\data\config.txt`. If there is none, or the server can't be reached, it asks for the address and saves it.
   The client stores its settings in `C:\githubDemo\settings\appsettings.json` and reads syntax-highlighting files from `C:\githubDemo\data\codeStyles\`.

## Live editing

Each open file has one authoritative copy on the server with a revision number (`server/CommunicatorLiveEdit.cpp`). The client turns every editor change into an operation (`TextOperation` — retain / insert / delete, same algorithm as ot.js) and sends it with the last revision it has seen; one edit is in flight at a time. If others edited in between, the server *transforms* the incoming edit past theirs, applies it, acknowledges it and broadcasts it; clients transform their own unconfirmed edits the same way, so every copy ends up identical. Positions are UTF-16 code units on both sides, so any language/keyboard works.

## Protocol

Every message, in both directions, is framed as `#` + 8-digit payload length in bytes + payload. The payload starts with the 3-digit message code. The server still answers unframed `501` (version) and `500` (download) requests from older clients and installers, so they can update themselves; any other unframed request is refused.

Fields inside a payload are length-prefixed with their **UTF-8 byte count** (`Wire` / `WireReader` in the client), so Hebrew and other non-ASCII text works in project names, files, chat and tasks. Usernames are limited to ASCII letters, digits and `. _ -`.

## Security notes

* Every SQL query escapes user input (`sqlStr()` in `server/SqliteDataBase.cpp`). Any new query that includes a string **must** use it.
* Passwords: the client sends SHA-256(password); the server stores that value as salted **PBKDF2-HMAC-SHA256** (`server/PasswordHasher.cpp`). Accounts created before this change are upgraded automatically on their next successful login.
* "Remember me" is encrypted with Windows DPAPI (current user only).
* Traffic is **not encrypted** (plain TCP). Only run the server on a trusted network until TLS is added.
* The server checks permissions for every project, file, task, friend and message action (creator/admin/participant roles); it never trusts a user name sent by the client.
* Never commit the `.sqlite` database — it contains user accounts (it is in `.gitignore`).
