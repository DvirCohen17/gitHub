# Protocol (v2)

Client and server talk over one TCP connection (default port `12345`).

## Framing

Every message, in both directions, is `#` + 8 ASCII digits (payload length in bytes) + payload.
The payload is one UTF-8 JSON object.

```
#00000041{"t":"auth.login","rid":1,"user":"alice"}
```

Two non-JSON payloads remain for installers and old clients: `501` (version check, answered `502<version>`)
and `500` (package download, answered `500<size>` followed by the raw bytes). Unframed `501`/`500` from
builds before framing are answered the same way, without frames.

## Envelope

| Kind | Shape |
|---|---|
| Request | `{"t": "<type>", "rid": <int>, ...fields}` |
| Reply | `{"t": "<type>", "rid": <same>, "ok": true, ...fields}` |
| Error reply | `{"t": "<type>", "rid": <same>, "ok": false, "error": "<message for the user>", "code": "<machine code>"}` |
| Event (server push) | `{"t": "ev.<name>", ...fields}` (no `rid`) |

Error codes: `bad_request`, `not_logged_in`, `forbidden`, `not_found`, `conflict`, `rate_limited`
(with `retryAfter` seconds), `banned`, `update_required`, `unavailable`, `internal`.

Every request gets exactly one reply, except `file.cursor` and `chat.typing`. Requests from one connection are handled in order.
Clients send `ping` every 30 seconds; the server closes connections that stay silent for two minutes.

## Common objects

* **User** `{"id", "name", "avatar"}`. `avatar` is `""` (initials), `"p:<preset>"` (a preset bundled with
  the client, e.g. `p:fox`) or `"u:<sha256>"` (an uploaded picture, fetched with `user.avatar`).
* **Presence** `{"status": "online"|"away"|"offline", "projectId"?, "file"?}`. `projectId`/`file` are only sent
  to members of that project.
* **Role** `owner` (created the project, one per project), `admin`, `member`. `viewer` is used for
  non-members watching a public project.
* **Operation** (live editing) is a JSON array: a positive number retains that many characters, a negative
  number deletes that many, a string inserts. Positions count UTF-16 code units. Same as ot.js.
* Times are Unix milliseconds. Due dates are `yyyy-MM-dd`.

## Requests

### Connection and account
| Type | Fields | Reply |
|---|---|---|
| `hello` | `clientVersion` | `serverVersion`, `updateRequired` |
| `ping` | | |
| `auth.login` | `user` (name or email), `password` (SHA-256 hex) | `user`, `email`, `bio`, `isAdmin` |
| `auth.signup` | `name`, `email`, `password` | same as login |
| `auth.changePassword` | `user`, `oldPassword`, `newPassword` | |
| `auth.logout` | | |

Login failures are rate limited per user name and per IP address (`rate_limited`).

### People
| Type | Fields | Reply |
|---|---|---|
| `user.profile` | `name` | `user`, `bio`, `email` (own only), `createdAt`, `presence`, `relation` (`self`, `friend`, `requestSent`, `requestReceived`, `none`), `projects` [{`id`,`name`,`language`,`role`,`visibility`}] (shared or public only) |
| `user.updateProfile` | `bio` | |
| `user.setAvatar` | `preset` or `png` (base64, square, ≤ 512 KB) or neither (remove) | `avatar` |
| `user.avatar` | `hash` | `png` (base64) |
| `user.search` | `query` | `users` [User + `relation`] |
| `presence.set` | `status` (`online`/`away`), `projectId`?, `fileId`? | |
| `friends.list` | | `friends` [User + `presence`], `incoming` [User], `outgoing` [User] |
| `friends.request` | `name` | `state` (`sent` or `friends` when they had asked you) |
| `friends.respond` | `userId`, `accept` | |
| `friends.remove` | `userId` (friend, or cancels a request) | |

### Inbox and activity
| Type | Fields | Reply |
|---|---|---|
| `inbox.list` | | `items` [{`id`,`kind`,`text`,`from` User?,`projectId`?,`projectName`?,`read`,`createdAt`}], `unread` |
| `inbox.markRead` | `id` or `all: true` | `unread` |
| `inbox.delete` | `id` | `unread` |
| `activity.feed` | `before`? (id) | `items` [{`id`,`user`,`projectId`,`projectName`,`kind`,`text`,`createdAt`}] |

Inbox kinds: `info`, `friendRequest`, `projectInvite`, `joinRequest`, `mention`, `task`.

### Projects
| Type | Fields | Reply |
|---|---|---|
| `project.list` | | `projects` [{`id`,`name`,`language`,`description`,`visibility`,`role`,`members`,`updatedAt`}], `invites` [{`projectId`,`name`,`from`,`role`}] |
| `project.get` | `id` | `project` {`id`,`name`,`language`,`description`,`visibility`,`owner`,`myRole`,`members` [{`user`,`role`,`presence`}],`invites` [{`user`,`role`}],`inviteCode`? (admins), `inviteAutoApprove`} |
| `project.create` | `name`, `language`, `description`, `visibility`, `invites` [{`name`,`role`}] | `project` |
| `project.update` | `id`, `name`?, `description`?, `visibility`?, `invites`?, `roles`? [{`userId`,`role`}] (`owner` hands the project over), `remove`? [userId], `cancelInvites`? [userId] | `project` |
| `project.delete` | `id` | |
| `project.leave` | `id` | |
| `project.respondInvite` | `id`, `accept` | |
| `project.inviteCode` | `id`, `action` (`create`/`revoke`/`settings`), `autoApprove` | `inviteCode` |
| `project.join` | `code` | `status` (`joined`/`requested`), `projectId`, `name` |
| `project.respondJoin` | `projectId`, `userId`, `accept` | |
| `project.public` | `query` | `projects` [... + `owner`] |
| `project.open` | `id` | `project` (as `project.get`), `files` [{`id`,`path`}], `folders` [path], `readOnly`, `presence` [{`userId`,`status`,`file`}] |
| `project.close` | `id` | |
| `project.export` | `id` | `name`, `files` [{`path`,`text`}], `folders` |
| `project.import` | `id`, `files` [{`path`,`text`}], `folders`? | `created`, `skipped` |

### Files and live editing
| Type | Fields | Reply |
|---|---|---|
| `file.create` | `projectId`, `path` | `file` {`id`,`path`} |
| `file.rename` | `fileId`, `path` | |
| `file.delete` | `fileId` | |
| `folder.create` | `projectId`, `path` | |
| `folder.rename` | `projectId`, `path`, `newPath` | |
| `folder.delete` | `projectId`, `path` | |
| `file.open` | `fileId` | `fileId`, `rev`, `text`, `cursors` |
| `file.close` | `fileId` | |
| `file.op` | `fileId`, `rev`, `op` | `rev` (the edit's revision), or `resync: true` with `rev` and `text` when the edit couldn't be applied (start over from that text) |
| `file.cursor` | `fileId`, `rev`, `anchor`, `head` | (no reply) |
| `file.versions` | `fileId` | `versions` [{`id`,`label`,`createdAt`,`user`,`size`}] |
| `file.version` | `versionId` | `text` |
| `file.checkpoint` | `fileId`, `label` | `version` |
| `file.restore` | `versionId` | |

`file.cursor` is the only request without a reply.

### Chat
| Type | Fields | Reply |
|---|---|---|
| `chat.history` | `projectId`, `before`? (message id), `limit`? | `messages` [{`id`,`user`,`text`,`createdAt`,`editedAt`,`deleted`}] (oldest first) |
| `chat.send` | `projectId`, `text` | `message` |
| `chat.edit` | `id`, `text` | |
| `chat.delete` | `id` | |
| `chat.typing` | `projectId` | |

`@name` mentions of project members send them a `mention` inbox item. Fenced code blocks (```` ```cpp ````) are shown highlighted by the client.

### Tasks
| Type | Fields | Reply |
|---|---|---|
| `task.list` | `projectId` | `tasks` [{`id`,`text`,`due`,`done`,`assignees` [User],`createdBy` User,`createdAt`,`completedAt`}] |
| `task.create` | `projectId`, `text`, `due`, `assignees` [userId] | `task` |
| `task.update` | `id`, `text`, `due`, `assignees` | `task` |
| `task.setDone` | `id`, `done` | `task` |
| `task.delete` | `id` | |

### Running code
| Type | Fields | Reply |
|---|---|---|
| `run.start` | `fileId`, `stdin`? | `runId` |
| `run.stop` | `runId` | |

Programs run in a throw-away Docker container on the server: no network, limited memory, CPU and time.

### Server administration (admins only)
| Type | Fields | Reply |
|---|---|---|
| `admin.stats` | | `uptime`, `sessions`, `users`, `projects`, `openFiles`, `requestsPerMinute`, `runs` |
| `admin.users` | `query` | `users` [User + `email`,`isAdmin`,`banned`,`online`,`createdAt`,`lastSeen`] |
| `admin.projects` | `query` | `projects` [{`id`,`name`,`owner`,`members`,`files`,`visibility`,`createdAt`}] |
| `admin.ban` | `userId`, `banned`, `reason`? | |
| `admin.kick` | `userId` | |
| `admin.setAdmin` | `userId`, `isAdmin` | |
| `admin.deleteProject` | `id` | |

## Events

| Type | Fields | Sent to |
|---|---|---|
| `ev.kicked` | `reason` | the session being closed |
| `ev.inbox` | `item`, `unread` | the recipient |
| `ev.friends` | | both users, when friendship state changes |
| `ev.presence` | `userId`, `presence` | friends and project members |
| `ev.user` | `user` | friends and project members, when someone changes their picture |
| `ev.projects` | | users whose project list changed |
| `ev.project` | `project` | everyone with the project open |
| `ev.project.deleted` | `id` | everyone with the project open |
| `ev.project.removed` | `id`, `reason` | a member who was removed |
| `ev.tree` | `projectId`, `files`, `folders` | everyone with the project open |
| `ev.file.op` | `fileId`, `rev`, `op`, `userId` | everyone else with the file open |
| `ev.file.resync` | `fileId`, `rev`, `text` | a client whose edit could not be applied |
| `ev.file.cursor` | `fileId`, `userId`, `sessionId`, `rev`, `anchor`, `head` | everyone else with the file open |
| `ev.file.users` | `fileId`, `users` [User] | everyone with the file open |
| `ev.file.deleted` | `fileId` | everyone with the project open |
| `ev.chat` | `projectId`, `message` | everyone with the project open (new or edited) |
| `ev.chat.typing` | `projectId`, `user` | everyone else with the project open |
| `ev.task` | `projectId`, `task` | everyone with the project open |
| `ev.task.deleted` | `projectId`, `id` | everyone with the project open |
| `ev.activity` | `item` | members of that project who are online |
| `ev.run.output` | `runId`, `stream` (`stdout`/`stderr`), `text` | whoever started the run |
| `ev.run.exit` | `runId`, `exitCode`, `timedOut`, `durationMs`, `error`? | whoever started the run |
