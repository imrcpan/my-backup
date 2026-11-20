# Backup Manager

A desktop backup manager for Linux. It keeps a group of places (a folder, an
external disk, an encrypted repository on a cloud drive) in step with each
other. [restic](https://restic.net) does the backup, the encryption and the
snapshots, and [rclone](https://rclone.org) reaches Google Drive.

Nothing runs by itself. Every action starts as a **plan** that shows what
would change, like a diff, and happens only after a second, explicit
confirmation.

## Concepts

- **Storage device**: where data can live. This computer and Google Drive are
  built in; an external disk is added with the UUID of its file system and is
  checked by it (connected, mounted, writable) before every use.
- **Backup group**: a set of **storage objects**. A storage object is a path
  on a device and is encrypted or not on its own. An encrypted object is a
  restic repository, and its password is saved. Everything on a cloud drive
  must be encrypted.
- **Run**: one object is chosen as the newest copy, and the others (all by
  default) are brought up to date from it:

  | newest copy | target    | what happens                                    |
  | ----------- | --------- | ----------------------------------------------- |
  | plain       | encrypted | a restic snapshot of the folder                 |
  | encrypted   | encrypted | the latest snapshot is copied between the repos |
  | plain       | plain     | the target is made to look like the source      |
  | encrypted   | plain     | the latest snapshot is restored into an empty folder |

## Safety

- A source that is missing or empty is never backed up, so an unmounted disk
  cannot be recorded as a loss.
- The source is never changed or deleted. A file sync deletes only in the
  target, shows the files in the plan, needs an acknowledgement, and is given
  the number of deletions that was shown as its limit.
- Removing a group, an object or a device only forgets the configuration. No
  repository and no file is deleted.
- A restore goes only into a new or empty folder and never over existing files.
- Removing snapshots and cleaning up a repository need an explicit
  acknowledgement, and remove exactly what the plan listed.
- Passwords and tokens are masked in every log. They reach restic through its
  environment, never through a command line. External programs are started
  with an argument list, never through a shell.
- The server listens on localhost only and refuses other hosts and foreign
  origins.

## Requirements

- Linux with `restic` and `rclone` installed
- .NET SDK 11 (a preview or release candidate is fine) and Node.js with pnpm

For Google Drive, add a cloud device (or use the built-in one) and choose
"Sign in to Google" in its menu; rclone opens a browser for the sign-in.

## Run it (development)

Development and tests use the JIT; nothing needs to be published.

```sh
# the interface
cd src/web
pnpm install
pnpm build            # writes into src/Backup.Server/wwwroot

# the server, on http://localhost:5174
cd ..
dotnet run --project Backup.Server
```

Settings are read from the command line or the environment:

| setting        | meaning                                              | default                            |
| -------------- | ---------------------------------------------------- | ---------------------------------- |
| `Port`         | port of the server                                   | `5174`                             |
| `DataDir`      | state, run history and logs                          | `~/.local/share/backup-manager`    |
| `ResticPath`   | the restic program                                   | `restic`                           |
| `RclonePath`   | the rclone program                                   | `rclone`                           |
| `RcloneConfig` | an rclone config file                                | the default one of rclone          |

For the interface alone, `pnpm dev` in `src/web` serves it with hot reload and
sends `/api` to the server on port 5174.

## Release (Native AOT)

```sh
cd src/web && pnpm build
cd .. && dotnet publish Backup.Server -c Release -r linux-x64 -o out
./out/backup-manager
```

`PublishAot` is set only for the Release configuration. The code stays
AOT-compatible in every configuration: the analyzers report any reflection,
and JSON, request handling and configuration use source generators.

## Tests

```sh
cd src
dotnet test           # the backend; it runs the real restic and rclone on temporary directories
```

The interface has no tests.

## Layout

```
src/Backup.Core       the rules: devices, groups, storage objects, runs
src/Backup.Infra      process runner, restic, rclone, disks, files, logs
src/Backup.App        planning and running the runs, reading repositories
src/Backup.Server     the HTTP API and the host of the interface
src/web               the interface: React, shadcn/ui, Tailwind
```
