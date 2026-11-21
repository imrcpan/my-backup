import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { EyeIcon, EyeOffIcon } from 'lucide-react'
import { DirectoryPicker } from '@/components/directory-picker'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldContent, FieldDescription, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { InputGroup, InputGroupAddon, InputGroupButton, InputGroupInput } from '@/components/ui/input-group'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Device, StorageObject } from '@/lib/types'

/**
 * Adds a storage object (a device and a path) to a group, or changes one. Encryption is chosen
 * for this object alone; a password is asked for only when it is on, and it is saved.
 */
export function ObjectDialog({
  open,
  onOpenChange,
  groupId,
  devices,
  object,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  groupId: string
  devices: Device[]
  object?: StorageObject
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const queryClient = useQueryClient()
  const [deviceId, setDeviceId] = useState('')
  const [path, setPath] = useState('')
  const [encrypted, setEncrypted] = useState(false)
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)

  useEffect(() => {
    if (!open) return
    setDeviceId(object?.deviceId ?? devices[0]?.id ?? '')
    setPath(object?.path ?? '')
    setEncrypted(object?.encrypted ?? false)
    setPassword('')
    setShowPassword(false)
  }, [open, object, devices])

  const device = devices.find((item) => item.id === deviceId)
  // Data on a cloud drive is always encrypted.
  const forced = device?.kind === 'Rclone'
  const isEncrypted = forced || encrypted
  const keepsPassword = object?.encrypted === true && object.hasPassword

  const save = useMutation({
    mutationFn: () => {
      const input = { deviceId, path, encrypted: isEncrypted, password: isEncrypted ? password : undefined }
      return object ? api.groups.updateObject(groupId, object.id, input) : api.groups.addObject(groupId, input)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['groups'] })
      notify.success('object.saved')
      onOpenChange(false)
    },
    onError: notify.error,
  })

  const items = devices.map((item) => ({ value: item.id, label: `${item.name} (${item.id})` }))

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{object ? t('object.edit') : t('object.add.title')}</DialogTitle>
          <DialogDescription>{t('object.add.description')}</DialogDescription>
        </DialogHeader>
        <form
          id="object-form"
          // The fields scroll when the window is too low; the title and the buttons stay in view.
          className="-mx-4 max-h-[60vh] overflow-y-auto px-4"
          onSubmit={(event) => {
            event.preventDefault()
            save.mutate()
          }}
        >
          <FieldGroup>
            <Field>
              <FieldLabel>{t('object.field.device')}</FieldLabel>
              <Select items={items} value={deviceId} onValueChange={(value) => setDeviceId(value ?? '')}>
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
            <Field>
              <FieldLabel htmlFor="object-path">{t('object.field.path')}</FieldLabel>
              <div className="flex gap-2">
                <Input
                  id="object-path"
                  className="font-mono"
                  value={path}
                  placeholder="/path/to/folder"
                  autoComplete="off"
                  onChange={(event) => setPath(event.target.value)}
                />
                <DirectoryPicker deviceId={deviceId} start={path} onPick={setPath} />
              </div>
              <FieldDescription>{t('object.field.path.hint')}</FieldDescription>
            </Field>
            <Field orientation="horizontal">
              <FieldContent>
                <FieldLabel htmlFor="object-encrypted">{t('object.field.encrypted')}</FieldLabel>
                <FieldDescription>
                  {forced ? t('object.field.encrypted.cloud') : t('object.field.encrypted.hint')}
                </FieldDescription>
              </FieldContent>
              <Switch id="object-encrypted" checked={isEncrypted} disabled={forced} onCheckedChange={setEncrypted} />
            </Field>
            {isEncrypted && (
              <Field>
                <FieldLabel htmlFor="object-password">{t('object.field.password')}</FieldLabel>
                <InputGroup>
                  <InputGroupInput
                    id="object-password"
                    type={showPassword ? 'text' : 'password'}
                    value={password}
                    autoComplete="new-password"
                    placeholder={keepsPassword ? t('object.field.password.keep') : undefined}
                    onChange={(event) => setPassword(event.target.value)}
                  />
                  <InputGroupAddon align="inline-end">
                    <InputGroupButton
                      aria-label={showPassword ? t('object.password.hide') : t('object.password.show')}
                      onClick={() => setShowPassword(!showPassword)}
                    >
                      {showPassword ? <EyeOffIcon /> : <EyeIcon />}
                    </InputGroupButton>
                  </InputGroupAddon>
                </InputGroup>
                <FieldDescription>{t('object.field.password.hint')}</FieldDescription>
              </Field>
            )}
          </FieldGroup>
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" form="object-form" disabled={save.isPending}>
            {t('common.save')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
