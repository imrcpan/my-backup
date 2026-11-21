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
- Cancelling a run stops the program the way Ctrl+C does, so restic and rclone release their
  locks and remove their half-written files; a program that does not stop in time is killed.
  What earlier steps finished stays as it is. The step that was going leaves nothing that the
  next run cannot finish: a stopped restic backup, copy or prune makes no snapshot, damages
  nothing and is continued by the next run (checked with `check --read-data`), and a stopped
  file sync leaves a half file only under a `.partial` name. A stopped restore is the one
  that could leave a file of full size and wrong content, so its output is removed.
- When restic cannot read some files it still saves a snapshot of the rest. Such a snapshot is
  tagged `incomplete`. It is not taken as the newest copy while a complete snapshot exists, and a
  clean-up is never planned that would leave only incomplete snapshots in place of complete ones.
- Removing snapshots and cleaning up a repository need an explicit
  acknowledgement, and remove exactly what the plan listed.
- Passwords and tokens are masked in every log. They reach restic through its
  environment, never through a command line. External programs are started
  with an argument list, never through a shell.
- The server listens on localhost only and refuses other hosts and foreign
  origins.

## Passwords

The password of an encrypted object is saved in `state.json` in the data directory (readable by
the owner only), so it is not asked again. **Without the password the backups cannot be opened.**
Keep a copy somewhere else. Before each save the version before is kept as `state.json.bak`.

Only one instance can use a data directory; a second one refuses to start.

## Requirements

- Linux with `restic` and `rclone` installed
- .NET SDK 11 (a preview or release candidate is fine) and Node.js with pnpm

For Google Drive, add a cloud device (or use the built-in one) and choose
"Sign in to Google" in its menu; rclone opens a browser for the sign-in.

## Run it (development)

Development and tests use the JIT; nothing needs to be published.

One command builds the interface as static files, as for a release, and starts the
backend (Debug, JIT), which serves them on the same port, http://localhost:1580. Only the
backend is a debug build, and there is no hot reload: after a change in `src/web`, run it
again. Press Ctrl+C to stop:

```sh
./run-debug.sh                      # arguments go to the backend, e.g. -p 8080 --DataDir=/tmp/data
```

Or step by step:

```sh
# the interface
cd src/web
pnpm install
pnpm build            # writes into src/Backup.Server/wwwroot

# the server, with the interface, on http://localhost:1580
cd ..
dotnet run --project Backup.Server
```

Settings are read from the command line or the environment:

| setting        | meaning                                              | default                            |
| -------------- | ---------------------------------------------------- | ---------------------------------- |
| `Port`         | port of the server and of the interface; also `-p 1234` or `--port 1234` | `1580`          |
| `DataDir`      | state, run history and logs                          | `~/.local/share/backup-manager`    |
| `ResticPath`   | the restic program                                   | `restic`                           |
| `RclonePath`   | the rclone program                                   | `rclone`                           |
| `RcloneConfig` | an rclone config file                                | the default one of rclone          |

The program ends with a sentence and an exit code if it cannot start: `2` the port is not a
number, `3` the data directory is used by another instance or cannot be used, or the state file
cannot be read (it is left as it is), `4` the port is in use.

For work on the interface alone, `pnpm dev` in `src/web` serves it with hot reload and
sends `/api` to a server that is already running on port 1580.

## Release (Native AOT)

```sh
cd src/web && pnpm build
cd .. && dotnet publish Backup.Server -c Release -r linux-x64 -o out
./out/backup-manager
```

`PublishAot` is set only for the Release configuration. The code stays
AOT-compatible in every configuration: the analyzers report any reflection,
and JSON, request handling and configuration use source generators.

## Run it as a service (systemd)

`my-backup.service` can be copied as it is. It runs the published program from `/opt/my-backup` on
port 1582 as the user `prc`; change `User=`, `Group=` and the paths in it if yours differ.

```sh
cd src/web && pnpm build && cd ..
sudo dotnet publish Backup.Server -c Release -r linux-x64 -o /opt/my-backup   # the interface is read from ./wwwroot there
sudo cp ../my-backup.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now my-backup          # then open http://localhost:1582
```

The service user owns the data directory (`~/.local/share/backup-manager`) and the rclone config, so
sign in to Google (`rclone config`, or "Sign in" in the interface) as that user. `systemctl stop`
stops a backup that is going the way Ctrl+C does and ends in under a second; the unit allows
60 seconds for it. A program that cannot start (exit code 2, 3 or 4) is not restarted in a loop.
With SELinux, publish into a place such as `/opt`; a service is not allowed to run what is in `/tmp`.

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
