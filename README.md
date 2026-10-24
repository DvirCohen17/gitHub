# gitHub Demo

A multi-user code editor. Create projects, invite friends, and edit the same files together in real time:
live cursors, presence ("dana is editing main.cpp"), version history, project chat with @mentions and code
blocks, tasks, running code on the server, and an admin panel.

Website: https://dvircohen17.github.io/gitHub/

## Architecture

```
┌───────────────────────────┐   TCP :12345, JSON messages   ┌──────────────────────────────┐
│ client/  WPF, .NET 8, MVVM│ ◄───────────────────────────► │ server/  C++17, Winsock       │
│ one window, pages inside  │   docs/PROTOCOL.md            │ poll thread + worker pool     │
│ AvalonEdit live editor    │                               │ SQLite (prepared statements)  │
└───────────────────────────┘                               │ Docker for "Run"              │
            ▲ MSIX package (packaging/)                     └──────────────────────────────┘
```

* **server/** — `Net.cpp`: one thread polls every socket and hands complete messages to a worker pool;
  each connection's requests are handled in order. `App*.cpp`: the request handlers (auth, people, projects,
  files, chat, tasks, admin, run). Each open project is a *room* with its own lock, so projects never wait for
  each other. `Schema.cpp`: tables and the one-time import of a version 1 database. `CodeRunner.cpp`: runs code
  in throw-away Docker containers.
* **client/** — `Core/`: connection (with automatic reconnect), session, live documents (OT), settings,
  notifications, themes. `ViewModels/` + `Views/`: one page per screen, shown inside the single `MainWindow`
  with a short transition. Only *New / Edit project* is a separate dialog. Look and theming: `Themes/Design.xaml`.
* **docs/PROTOCOL.md** — every request, reply and event.
* **packaging/** — builds the signed MSIX installer.
* **tests/** — end-to-end server tests (Python) and client text-operation tests (C#).
* **installer/** — the version 1 installer, kept only so existing version 1 installs can update.

## Building

Requirements: Windows 10/11, Visual Studio 2022 (*Desktop development with C++* and *.NET desktop development*),
the .NET 8 SDK (or newer), Python 3 for the tests.

| What | How |
|---|---|
| Server | `msbuild server\server.vcxproj /p:Configuration=Release /p:Platform=x64` (or open it in Visual Studio) |
| Client | `dotnet build client\client_side.csproj` |
| Installer (MSIX) | `powershell -ExecutionPolicy Bypass -File packaging\build-msix.ps1` → `packaging\out\` |

## Running the server

Run `server.exe`. On first start it writes `server.json` with defaults:

| Setting | Meaning |
|---|---|
| `port` | TCP port (default 12345). Also `--port N` on the command line. |
| `database` | SQLite file (default `githubdemo.sqlite`, `--db`). |
| `legacyDatabase` | A version 1 `syncDBTemp.sqlite` to import once when `database` doesn't exist yet (the old file is only read). |
| `logFile`, `logLevel` | Log file (rotated at 5 MB) and `debug`/`info`/`warn`/`error` (`--log-level`). |
| `admins` | User names that are server admins (the admin page, the console commands). |
| `saveIntervalSeconds` | How often edited files are written to the database (default 3). |
| `versionIntervalMinutes`, `maxVersionsPerFile` | Automatic version snapshots. Named versions are kept forever. |
| `installerPackage` | The file offered to clients that must update (put the `.msix` here). |
| `clientVersion` | The lowest client version allowed (older clients are asked to update). |
| `rateLimits` | Failed logins before a lockout, sign-ups per hour per address, requests per second, chat messages. |
| `run` | Running code: `enabled`, `docker` command, time/memory/CPU limits, and the image + command per file type. |

**Admin console** (type in the server window): `status`, `sessions`, `users [text]`, `kick <user>`,
`ban <user> [reason]`, `unban <user>`, `admin <user> on|off`, `version [x.y]`, `save`, `exit`.

**Running code** needs [Docker Desktop](https://www.docker.com/products/docker-desktop/) on the server machine.
Each run gets a fresh container with no network, 256 MB of memory, one CPU, a process limit and a 10-second time
limit (all configurable). The first run of each language downloads its image. Supported out of the box: `.py`,
`.js`, `.c`, `.cpp`, `.java`.

## Installing the client

Give people `packaging\out\gitHubDemo-<version>.msix`, `gitHubDemo.cer` and `install.ps1`. Running `install.ps1`
trusts the gitHub Demo certificate once (asks for administrator approval) and installs the app for the current
user. It then appears in the Start menu, opens `githubdemo://` links, shows Windows notifications, and uninstalls
from *Settings > Apps*. The package is self-contained, so no .NET install is needed.

The certificate is self-signed (`packaging\signing.pfx`, created on the first build and never committed). Keep that
file and use it for every release; a purchased code-signing certificate can replace it later.

The client keeps its settings, remembered sign-in (encrypted with Windows DPAPI), editor colours and cached
pictures in `%LOCALAPPDATA%\gitHubDemo`. On first start it reads the server address, theme and colours of a
version 1 install (`C:\githubDemo`) without changing it.

## Live editing

Each open file has one authoritative copy on the server with a revision number. The client turns every editor
change into an operation (retain / insert / delete, the ot.js algorithm) sent with the last revision it has seen,
one at a time. The server *transforms* an edit past the ones it hadn't seen, applies it, acknowledges it and sends
it to everyone else in the file, always in the same order; clients do the mirror-image transform for their own
unconfirmed edits, so every copy ends up identical. Positions are UTF-16 units on both sides, so any language
works. Undo only undoes your own edits (the undo history is transformed past other people's). Cursors and
selections are sent the same way, and edits are saved every few seconds with automatic version snapshots.

## Tests

```
python tests\protocol_test.py server\x64\Release\server.exe [old syncDBTemp.sqlite to import]
dotnet run --project tests\ClientOtTests
```

`protocol_test.py` starts a server on a free port in a temporary folder and runs 17 end-to-end tests: the
version 1 import, accounts and lockouts, friends and presence, roles/invites/invite codes, public projects,
folders, three users and four real OT clients editing at once, cursors, versions, autosave, chat, tasks,
avatars, import/export, run code (with a stand-in for Docker), admin, a 24-user stress test, malformed input and
rate limits. `ClientOtTests` checks the client's compose/transform/undo logic on thousands of random edits.

## Security notes

* Every SQL statement uses bound parameters (`server/Db.h`); no SQL is built from user input.
* Passwords: the client sends SHA-256(password); the server stores it as salted PBKDF2-HMAC-SHA256.
* Failed logins lock the account and the address for a while; sign-ups, requests and chat are rate limited.
* The server checks every action against the user's role (owner / admin / member / viewer).
* Traffic is **not encrypted** (plain TCP). Only run the server on a trusted network until TLS is added.
* Never commit the `.sqlite` database (accounts) or `packaging\signing.pfx` (signing key); both are in `.gitignore`.
