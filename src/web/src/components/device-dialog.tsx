import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Device, DeviceKind } from '@/lib/types'

/** Adds a device, or changes one. A built-in device keeps its id and its kind. */
export function DeviceDialog({
  open,
  onOpenChange,
  device,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  device?: Device
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const queryClient = useQueryClient()
  const [id, setId] = useState('')
  const [name, setName] = useState('')
  const [kind, setKind] = useState<DeviceKind>('ExternalDisk')
  const [uuid, setUuid] = useState('')
  const [remote, setRemote] = useState('')

  useEffect(() => {
    if (!open) return
    setId(device?.id ?? '')
    setName(device?.name ?? '')
    setKind(device?.kind ?? 'ExternalDisk')
    setUuid(device?.uuid ?? '')
    setRemote(device?.rcloneRemote ?? '')
  }, [open, device])

  const save = useMutation({
    mutationFn: () => {
      const input = {
        id,
        name,
        kind,
        uuid: kind === 'ExternalDisk' ? uuid : undefined,
        rcloneRemote: kind === 'Rclone' ? remote : undefined,
      }
      return device ? api.devices.update(device.id, input) : api.devices.create(input)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
      notify.success('devices.saved')
      onOpenChange(false)
    },
    onError: notify.error,
  })

  const kinds: { value: DeviceKind; label: string }[] = [
    { value: 'ExternalDisk', label: t('devices.kind.ExternalDisk') },
    { value: 'Rclone', label: t('devices.kind.Rclone') },
  ]
  const fixedKind = device != null && (device.isBuiltIn || device.kind === 'Local')
  const items = fixedKind ? [{ value: kind, label: t(`devices.kind.${kind}`) }] : kinds

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{device ? t('devices.edit') : t('devices.add')}</DialogTitle>
          <DialogDescription>{t('page.devices.description')}</DialogDescription>
        </DialogHeader>
        <form
          id="device-form"
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
        >
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="device-id">{t('devices.field.id')}</FieldLabel>
              <Input
                id="device-id"
                value={id}
                disabled={device != null}
                autoComplete="off"
                onChange={(event) => setId(event.target.value)}
              />
              <FieldDescription>{t('devices.field.id.hint')}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel htmlFor="device-name">{t('devices.field.name')}</FieldLabel>
              <Input
                id="device-name"
                value={name}
                autoComplete="off"
                onChange={(event) => setName(event.target.value)}
              />
            </Field>
            <Field>
              <FieldLabel>{t('devices.field.kind')}</FieldLabel>
              <Select
                items={items}
                value={kind}
                disabled={fixedKind}
                onValueChange={(value) => setKind(value as DeviceKind)}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {items.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>
            {kind === 'ExternalDisk' && (
              <Field>
                <FieldLabel htmlFor="device-uuid">{t('devices.field.uuid')}</FieldLabel>
                <Input
                  id="device-uuid"
                  value={uuid}
                  autoComplete="off"
                  placeholder="1234-ABCD"
                  onChange={(event) => setUuid(event.target.value)}
                />
                <FieldDescription>{t('devices.field.uuid.hint')}</FieldDescription>
              </Field>
            )}
            {kind === 'Rclone' && (
              <Field>
                <FieldLabel htmlFor="device-remote">{t('devices.field.remote')}</FieldLabel>
                <Input
                  id="device-remote"
                  value={remote}
                  autoComplete="off"
                  placeholder="gdrive"
                  onChange={(event) => setRemote(event.target.value)}
                />
                <FieldDescription>{t('devices.field.remote.hint')}</FieldDescription>
              </Field>
            )}
          </FieldGroup>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" form="device-form" disabled={save.isPending}>
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
