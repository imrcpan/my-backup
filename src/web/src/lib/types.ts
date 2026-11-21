// The shapes that the server answers with (see Backup.Server/Api).

export type DeviceKind = 'Local' | 'ExternalDisk' | 'Rclone'

export type Device = {
  id: string
  name: string
  kind: DeviceKind
  uuid?: string
  rcloneRemote?: string
  isBuiltIn: boolean
}

export type DeviceInput = {
  id?: string
  name: string
  kind: DeviceKind
  uuid?: string
  rcloneRemote?: string
}

export type DeviceStatus = {
  state: 'ok' | 'not_connected' | 'not_mounted' | 'mounted' | 'not_configured' | 'unreachable'
  message?: string
  mountPoint?: string
  readOnly?: boolean
}

export type Directory = { name: string; path: string }

export type RetentionPolicy = {
  keepLast?: number
  keepDaily?: number
  keepWeekly?: number
  keepMonthly?: number
  keepYearly?: number
}

export type StorageObject = {
  id: string
  deviceId: string
  path: string
  encrypted: boolean
  hasPassword: boolean
  retention?: RetentionPolicy
}

export type StorageObjectInput = {
  deviceId: string
  path: string
  encrypted: boolean
  password?: string
}

export type Group = { id: string; name: string; objects: StorageObject[] }

export type RunKind = 'Sync' | 'Restore' | 'Forget' | 'Prune' | 'Check'
export type RunState = 'Planned' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Interrupted' | 'Discarded'
export type StepKind =
  'ResticBackup' | 'ResticCopy' | 'FileSync' | 'RestoreToDirectory' | 'Restore' | 'Forget' | 'Prune' | 'Check'
export type StepState =
  'Pending' | 'Blocked' | 'UpToDate' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Skipped' | 'Interrupted'
export type CheckDepth = 'Structure' | 'Subset' | 'Full'

export type PlanLine = { action: string; path: string; size: number }

export type RunStep = {
  id: string
  kind: StepKind
  targetObjectId: string
  state: StepState
  lines: PlanLine[]
  linesTruncated: boolean
  toAdd: number
  toChange: number
  toDelete: number
  bytes: number
  notes: string[]
  progress: number
  bytesDone: number
  errorCode?: string
  errorMessage?: string
  destructive: boolean
}

export type Run = {
  id: string
  kind: RunKind
  groupId: string
  groupName: string
  sourceObjectId: string
  state: RunState
  createdAt: string
  startedAt?: string
  finishedAt?: string
  steps: RunStep[]
  errorMessage?: string
  destructive: boolean
  isFinished: boolean
}

export type Snapshot = {
  id: string
  shortId: string
  time: string
  paths: string[]
  hostname: string
  files?: number
  bytes?: number
  incomplete: boolean
}

export type SnapshotNode = { name: string; type: string; path: string; size?: number; mtime?: string }

export type SystemInfo = {
  restic: { available: boolean; version?: string }
  rclone: { available: boolean; version?: string }
  home: string
}

export type FileEntry = {
  name: string
  type: 'dir' | 'file' | 'symlink'
  path: string
  size?: number
  modified?: string
}

export type FileListing = { entries: FileEntry[]; truncated: boolean }

export type Disk = {
  uuid: string
  device: string
  label?: string
  fileSystem?: string
  sizeBytes?: number
  model?: string
  transport?: string
  removable: boolean
  mountPoint?: string
  deviceId?: string
}
