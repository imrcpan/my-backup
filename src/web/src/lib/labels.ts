import { useQuery } from '@tanstack/react-query'
import { api } from '@/lib/api'

/** Names a storage object of a group the way a person would: "Google Drive · /backups/docs". */
export function useObjectLabels() {
  const groups = useQuery({ queryKey: ['groups'], queryFn: api.groups.list })
  const devices = useQuery({ queryKey: ['devices'], queryFn: api.devices.list })

  return (groupId: string, objectId: string): string => {
    const object = groups.data?.find((group) => group.id === groupId)?.objects.find((item) => item.id === objectId)
    if (!object) return objectId
    const device = devices.data?.find((item) => item.id === object.deviceId)
    return `${device?.name ?? object.deviceId} · ${object.path}`
  }
}
