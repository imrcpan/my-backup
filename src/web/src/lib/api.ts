import type {
  CheckDepth,
  Device,
  DeviceInput,
  DeviceStatus,
  Disk,
  Directory,
  FileListing,
  Group,
  RetentionPolicy,
  Run,
  Snapshot,
  SnapshotNode,
  StorageObject,
  StorageObjectInput,
  SystemInfo,
} from '@/lib/types'

/** An answer of the server that says no. The code is stable and is what the interface translates. */
export class ApiError extends Error {
  readonly code: string
  readonly status: number

  constructor(code: string, message: string, status: number) {
    super(message)
    this.code = code
    this.status = status
  }
}

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError('network', 'The server cannot be reached.', 0)
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  let data: unknown
  try {
    data = text ? JSON.parse(text) : undefined
  } catch {
    // Not JSON, for example the page of a proxy that could not reach the server: show what it said.
    if (!response.ok)
      throw new ApiError('unexpected', text.trim().slice(0, 300) || response.statusText, response.status)
    throw new ApiError('unexpected', 'The server sent something that is not JSON.', response.status)
  }
  if (!response.ok) {
    const error = (data ?? {}) as { code?: string; message?: string }
    throw new ApiError(error.code ?? 'unexpected', error.message ?? response.statusText, response.status)
  }
  return data as T
}

const enc = encodeURIComponent

export const api = {
  system: () => request<SystemInfo>('GET', '/api/system'),
  disks: () => request<Disk[]>('GET', '/api/disks'),

  devices: {
    list: () => request<Device[]>('GET', '/api/devices'),
    create: (input: DeviceInput) => request<Device>('POST', '/api/devices', input),
    update: (id: string, input: DeviceInput) => request<Device>('PUT', `/api/devices/${enc(id)}`, input),
    remove: (id: string) => request<void>('DELETE', `/api/devices/${enc(id)}`),
    status: (id: string) => request<DeviceStatus>('GET', `/api/devices/${enc(id)}/status`),
    authorize: (id: string) => request<void>('POST', `/api/devices/${enc(id)}/authorize`),
    directories: (id: string, path: string) =>
      request<Directory[]>('GET', `/api/devices/${enc(id)}/directories?path=${enc(path)}`),
  },

  groups: {
    list: () => request<Group[]>('GET', '/api/groups'),
    create: (name: string) => request<Group>('POST', '/api/groups', { name }),
    rename: (id: string, name: string) => request<Group>('PUT', `/api/groups/${enc(id)}`, { name }),
    remove: (id: string) => request<void>('DELETE', `/api/groups/${enc(id)}`),
    addObject: (groupId: string, input: StorageObjectInput) =>
      request<StorageObject>('POST', `/api/groups/${enc(groupId)}/objects`, input),
    updateObject: (groupId: string, objectId: string, input: StorageObjectInput) =>
      request<StorageObject>('PUT', `/api/groups/${enc(groupId)}/objects/${enc(objectId)}`, input),
    removeObject: (groupId: string, objectId: string) =>
      request<void>('DELETE', `/api/groups/${enc(groupId)}/objects/${enc(objectId)}`),
    setRetention: (groupId: string, objectId: string, policy: RetentionPolicy | null) =>
      request<StorageObject>('PUT', `/api/groups/${enc(groupId)}/objects/${enc(objectId)}/retention`, { policy }),
    objectFiles: (groupId: string, objectId: string, path: string) =>
      request<FileListing>('GET', `/api/groups/${enc(groupId)}/objects/${enc(objectId)}/files?path=${enc(path)}`),
    snapshots: (groupId: string, objectId: string) =>
      request<Snapshot[]>('GET', `/api/groups/${enc(groupId)}/objects/${enc(objectId)}/snapshots`),
    files: (groupId: string, objectId: string, snapshotId: string, path: string) =>
      request<SnapshotNode[]>(
        'GET',
        `/api/groups/${enc(groupId)}/objects/${enc(objectId)}/snapshots/${enc(snapshotId)}/files?path=${enc(path)}`,
      ),
  },

  runs: {
    list: () => request<Run[]>('GET', '/api/runs'),
    get: (id: string) => request<Run>('GET', `/api/runs/${enc(id)}`),
    log: (id: string) => request<{ lines: string[] }>('GET', `/api/runs/${enc(id)}/log`),
    planSync: (groupId: string, sourceObjectId: string, targetObjectIds?: string[]) =>
      request<Run>('POST', '/api/runs/sync', { groupId, sourceObjectId, targetObjectIds }),
    planRestore: (groupId: string, objectId: string, snapshotId: string, targetDirectory: string, paths?: string[]) =>
      request<Run>('POST', '/api/runs/restore', { groupId, objectId, snapshotId, targetDirectory, paths }),
    planForget: (groupId: string, objectId: string, snapshotIds: string[] | undefined, prune: boolean) =>
      request<Run>('POST', '/api/runs/forget', { groupId, objectId, snapshotIds, prune }),
    planPrune: (groupId: string, objectId: string) => request<Run>('POST', '/api/runs/prune', { groupId, objectId }),
    planCheck: (groupId: string, objectId: string, depth: CheckDepth, subsetPercent?: number) =>
      request<Run>('POST', '/api/runs/check', { groupId, objectId, depth, subsetPercent }),
    start: (id: string, acknowledgeDestructive: boolean) =>
      request<Run>('POST', `/api/runs/${enc(id)}/start`, { acknowledgeDestructive }),
    cancel: (id: string) => request<Run>('POST', `/api/runs/${enc(id)}/cancel`),
    discard: (id: string) => request<Run>('POST', `/api/runs/${enc(id)}/discard`),
  },
}
